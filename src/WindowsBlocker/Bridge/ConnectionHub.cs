using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using WindowsBlocker.WebUI;

namespace WindowsBlocker.Bridge;

// Loopback WebSocket hub for the web-app bridge — the Windows analog of
// MacBlockerAppFeature/ConnectionHub.swift.
//
// The desktop app is the only endpoint that can listen on a socket (a browser
// extension cannot), so it hosts this server on a fixed loopback port. The
// customBlocker browser extension connects OUT to it and authenticates with a
// per-install pairing key. The protocol is byte-for-byte the same one the
// macOS hub speaks, so the unmodified extension client connects on Windows too.
//
// Scope: accept loopback WebSocket connections, track connected peers, route
// cluster/group sync messages, and expose JSON status/clusters strings for the
// embedded web editor (pushed each second + on demand).
public sealed class ConnectionHub
{
    internal const int ProtocolVersion = 2;
    internal const string LocalProgram = "windowsapp";
    private const int Port = 8787;
    private const int MaxMessageBytes = 1_048_576;
    private static readonly HashSet<string> RemotePrograms = new(StringComparer.Ordinal)
    {
        "chrome", "edge", "firefox", "safari", "opera", "browser"
    };

    private sealed class Peer
    {
        public Guid Id { get; } = Guid.NewGuid();
        public string Program { get; set; } = "";
        public bool Connected { get; set; }
        public WebSocket Socket { get; }
        public SemaphoreSlim SendLock { get; } = new(1, 1);
        public Peer(WebSocket socket) => Socket = socket;
    }

    private readonly record struct GroupInfo(string Id, string Name, string Type, bool Frozen);

    private sealed class ClusterState
    {
        public string Id = "";
        public string GroupName = "";
        public string GroupType = "";
        public HashSet<string> Members = new();
        // Per-program local group id: the specific group *instance* that program
        // linked. Membership is pinned to this id so deleting a group and later
        // re-creating one with the same name does NOT silently re-join the old
        // cluster. Empty for clusters formed before id pinning; those fall back to
        // name+type matching and are backfilled from the roster on next announce.
        public Dictionary<string, string> MemberGroupIds = new();
        public Dictionary<string, JsonObject> Contributions = new();
        public JsonObject SharedScalars = new();
        public double SharedTs;
        public double SharedUsageMs;
        public double SharedUsageResetAtMs;
        public bool UsageSeeded;
        public JsonObject SharedSnooze = new();
        public double SharedSnoozeTs;
        public double SharedSnoozeTotalMs;
    }

    private readonly object _lock = new();
    private readonly Dictionary<Guid, Peer> _peers = new();
    private readonly Dictionary<string, List<GroupInfo>> _rosters = new();
    private readonly Dictionary<string, ClusterState> _clusters = new();
    private string? _pendingLocalRejection;

    private HttpListener? _listener;
    private bool _running;
    private string _lastError = "";
    private readonly string _pairingKey;

    public ConnectionHub()
    {
        _pairingKey = LoadOrCreatePairingKey();
    }

    private static string LoadOrCreatePairingKey()
    {
        try
        {
            if (File.Exists(Storage.BridgePairingKeyPath))
            {
                var existing = File.ReadAllText(Storage.BridgePairingKeyPath).Trim().ToLowerInvariant();
                if (existing.Length == 64 && existing.All(Uri.IsHexDigit)) return existing;
            }
        }
        catch { }
        var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        try
        {
            var tmp = Storage.BridgePairingKeyPath + ".tmp";
            File.WriteAllText(tmp, key);
            File.Move(tmp, Storage.BridgePairingKeyPath, overwrite: true);
        }
        catch { }
        return key;
    }

    private static bool SecureEquals(string supplied, string expected)
    {
        var a = Encoding.UTF8.GetBytes(supplied);
        var b = Encoding.UTF8.GetBytes(expected);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    internal static string? HelloRejectionReason(JsonObject obj, string pairingKey)
    {
        if (obj["v"]?.GetValueKind() != JsonValueKind.Number || obj["v"]!.GetValue<int>() != ProtocolVersion)
            return "protocol-mismatch";
        var program = Str(obj["program"]);
        if (!RemotePrograms.Contains(program)) return "invalid-program";
        var supplied = Str(obj["pairingKey"]).Trim().ToLowerInvariant();
        return SecureEquals(supplied, pairingKey) ? null : "pairing-key-rejected";
    }

    // MARK: Lifecycle ------------------------------------------------------

    public void Start()
    {
        lock (_lock)
        {
            if (_listener != null) return;
        }
        Stop();
        lock (_lock)
        {
            _lastError = "";
            // Restore persisted clusters once so links survive a restart (members
            // show offline until they reconnect). Only when empty so a live
            // registry is never clobbered.
            if (_clusters.Count == 0) RestoreClustersLocked();
        }

        try
        {
            var listener = new HttpListener();
            // Loopback only — never expose the hub on the LAN. A literal 127.0.0.1
            // prefix needs no urlacl reservation / admin rights.
            listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
            listener.Start();
            lock (_lock)
            {
                _listener = listener;
                _running = true;
            }
            _ = AcceptLoopAsync(listener);
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
        }
    }

    public void Stop()
    {
        HttpListener? listener;
        List<Peer> conns;
        lock (_lock)
        {
            listener = _listener;
            _listener = null;
            conns = _peers.Values.ToList();
            _peers.Clear();
            _running = false;
        }
        try { listener?.Stop(); listener?.Close(); } catch { }
        foreach (var p in conns)
        {
            try { p.Socket.Abort(); p.Socket.Dispose(); } catch { }
        }
    }

    /// Number of live clusters (≥2 members). Used to decide whether quitting
    /// would break any active bridge link.
    public int ActiveClusterCount()
    {
        lock (_lock) return _clusters.Values.Count(c => c.Members.Count >= 2);
    }

    // MARK: Accept / receive ----------------------------------------------

    private async Task AcceptLoopAsync(HttpListener listener)
    {
        while (true)
        {
            HttpListenerContext ctx;
            try { ctx = await listener.GetContextAsync(); }
            catch { break; } // listener stopped / disposed
            if (!ctx.Request.IsWebSocketRequest)
            {
                try { ctx.Response.StatusCode = 400; ctx.Response.Close(); } catch { }
                continue;
            }
            _ = HandleClientAsync(ctx);
        }
    }

    private async Task HandleClientAsync(HttpListenerContext ctx)
    {
        WebSocket socket;
        try
        {
            var wsCtx = await ctx.AcceptWebSocketAsync(null);
            socket = wsCtx.WebSocket;
        }
        catch
        {
            try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { }
            return;
        }

        var peer = new Peer(socket);
        lock (_lock) _peers[peer.Id] = peer;
        _ = EnforceHandshakeTimeoutAsync(peer);
        try { await ReceiveLoopAsync(peer); }
        catch { }
        finally { RemovePeer(peer.Id); }
    }

    private async Task EnforceHandshakeTimeoutAsync(Peer peer)
    {
        await Task.Delay(TimeSpan.FromSeconds(5));
        bool pending;
        lock (_lock) pending = _peers.ContainsKey(peer.Id) && !peer.Connected;
        if (pending) await RejectAndCloseAsync(peer, "authentication-timeout");
    }

    private async Task ReceiveLoopAsync(Peer peer)
    {
        var buffer = new byte[8192];
        using var ms = new MemoryStream();
        while (peer.Socket.State == WebSocketState.Open)
        {
            ms.SetLength(0);
            WebSocketReceiveResult result;
            do
            {
                result = await peer.Socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    try
                    {
                        await peer.Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
                    }
                    catch { }
                    return;
                }
                if (ms.Length + result.Count > MaxMessageBytes)
                {
                    try
                    {
                        await peer.Socket.CloseAsync(WebSocketCloseStatus.MessageTooBig, "message-too-big", CancellationToken.None);
                    }
                    catch { }
                    return;
                }
                ms.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);

            if (result.MessageType != WebSocketMessageType.Text) continue;
            var text = Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
            await HandleIncomingAsync(peer, text);
        }
    }

    private async Task HandleIncomingAsync(Peer peer, string text)
    {
        var obj = Parse(text);
        if (obj is null)
        {
            await RejectAndCloseAsync(peer, "invalid-message");
            return;
        }
        var kind = Str(obj["kind"]);
        bool authenticated;
        lock (_lock) authenticated = peer.Connected;
        if (kind != "hello" && !authenticated)
        {
            await RejectAndCloseAsync(peer, "authentication-required");
            return;
        }
        if (kind == "hello" && authenticated)
        {
            await RejectAndCloseAsync(peer, "already-authenticated");
            return;
        }
        switch (kind)
        {
            case "hello":
                var rejection = HelloRejectionReason(obj, _pairingKey);
                if (rejection != null)
                {
                    await RejectAndCloseAsync(peer, rejection);
                    return;
                }
                var program = Str(obj["program"]);
                lock (_lock)
                {
                    peer.Program = program;
                    peer.Connected = true;
                }
                await SendTextAsync(peer, new JsonObject
                {
                    ["kind"] = "welcome",
                    ["v"] = ProtocolVersion,
                    ["hubProgram"] = LocalProgram,
                    ["peers"] = PeerListJson()
                }.ToJsonString());
                BroadcastPeers();
                await SendClustersSnapshotAsync(peer);
                break;
            case "groups-announce":
                SetRoster(ProgramOf(peer, obj, "program"), obj["groups"] as JsonArray);
                break;
            case "connect-group":
                ConnectGroup(ProgramOf(peer, obj, "fromProgram"), Str(obj["toProgram"]), Str(obj["groupName"]), Str(obj["groupType"]));
                break;
            case "disconnect-group":
                DisconnectGroup(Str(obj["clusterId"]), Str(obj["groupName"]), ProgramOf(peer, obj, "program"));
                break;
            case "group-sync":
                ApplySync(ProgramOf(peer, obj, "program"), Str(obj["groupName"]), Str(obj["groupType"]), obj, Num(obj["ts"]));
                break;
            case "ping":
                await SendTextAsync(peer, new JsonObject { ["kind"] = "pong", ["t"] = Num(obj["t"]) }.ToJsonString());
                break;
        }
    }

    private async Task RejectAndCloseAsync(Peer peer, string reason)
    {
        await SendTextAsync(peer, new JsonObject { ["kind"] = "rejected", ["reason"] = reason }.ToJsonString());
        try
        {
            await peer.Socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, reason, CancellationToken.None);
        }
        catch { }
    }

    private string ProgramOf(Peer peer, JsonObject obj, string fallbackKey)
    {
        lock (_lock)
        {
            if (peer.Program != "") return peer.Program;
        }
        return Str(obj[fallbackKey]);
    }

    // MARK: Send -----------------------------------------------------------

    private async Task SendTextAsync(Peer peer, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        await peer.SendLock.WaitAsync();
        try
        {
            if (peer.Socket.State == WebSocketState.Open)
            {
                await peer.Socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            }
        }
        catch
        {
            RemovePeer(peer.Id);
        }
        finally
        {
            peer.SendLock.Release();
        }
    }

    private void RemovePeer(Guid key)
    {
        string? program;
        bool existed;
        var affected = new List<string>();
        lock (_lock)
        {
            if (!_peers.TryGetValue(key, out var peer)) return;
            program = peer.Program;
            existed = true;
            _peers.Remove(key);
            try { peer.Socket.Dispose(); } catch { }
            if (!string.IsNullOrEmpty(program))
            {
                // A dropped socket means the program went OFFLINE, not that it
                // left its clusters. Keep cluster membership + last contribution
                // so shared memory survives a brief disconnect; drop the roster
                // (re-announced on reconnect, so stale groups can't validate links).
                _rosters.Remove(program);
                foreach (var c in _clusters.Values.Where(c => c.Members.Contains(program!)))
                {
                    affected.Add(ClusterJsonObject(c).ToJsonString());
                }
            }
        }
        foreach (var snap in affected) BroadcastClusterText(snap);
        if (existed) BroadcastPeers();
    }

    private void BroadcastPeers()
    {
        var payload = new JsonObject { ["kind"] = "peers", ["peers"] = PeerListJson() }.ToJsonString();
        List<Peer> conns;
        lock (_lock) conns = _peers.Values.Where(p => p.Connected).ToList();
        foreach (var p in conns) _ = SendTextAsync(p, payload);
    }

    private void BroadcastClusterText(string snapshotJson)
    {
        var payload = new JsonObject { ["kind"] = "cluster-updated", ["cluster"] = JsonNode.Parse(snapshotJson) }.ToJsonString();
        List<Peer> conns;
        lock (_lock) conns = _peers.Values.Where(p => p.Connected).ToList();
        foreach (var p in conns) _ = SendTextAsync(p, payload);
        // The app's own web editor is refreshed by the per-tick clusters push.
    }

    private async Task SendClustersSnapshotAsync(Peer peer)
    {
        var arr = new JsonArray();
        lock (_lock)
        {
            foreach (var c in _clusters.Values) arr.Add(ClusterJsonObject(c));
        }
        await SendTextAsync(peer, new JsonObject { ["kind"] = "clusters", ["clusters"] = arr }.ToJsonString());
    }

    private void RejectTo(string program, string reason)
    {
        if (program == LocalProgram)
        {
            lock (_lock) _pendingLocalRejection = reason;
            return;
        }
        Peer? target;
        lock (_lock) target = _peers.Values.FirstOrDefault(p => p.Connected && p.Program == program);
        if (target != null)
        {
            _ = SendTextAsync(target, new JsonObject { ["kind"] = "connect-group-rejected", ["reason"] = reason }.ToJsonString());
        }
    }

    // MARK: Status ---------------------------------------------------------

    private JsonArray PeerListJson()
    {
        var arr = new JsonArray();
        lock (_lock)
        {
            foreach (var p in _peers.Values.Where(p => p.Connected))
            {
                arr.Add(new JsonObject { ["id"] = p.Id.ToString(), ["program"] = p.Program, ["connected"] = p.Connected });
            }
        }
        return arr;
    }

    /// JSON status string in the shape the web editor's `__cbConnectionState`
    /// receiver expects.
    public string CurrentStatusJson()
    {
        bool running;
        string err;
        var peerList = new JsonArray();
        lock (_lock)
        {
            running = _running;
            err = _lastError;
            foreach (var p in _peers.Values.Where(p => p.Connected))
            {
                peerList.Add(new JsonObject { ["id"] = p.Id.ToString(), ["program"] = p.Program, ["connected"] = p.Connected });
            }
        }
        var status = new JsonObject
        {
            ["running"] = running,
            ["state"] = err.Length == 0 ? (running ? "running" : "off") : "error",
            ["address"] = $"ws://127.0.0.1:{Port}",
            ["peers"] = peerList,
            ["error"] = err,
            ["pairingKey"] = _pairingKey,
            ["hubProgram"] = LocalProgram
        };
        return status.ToJsonString();
    }

    /// JSON array of all clusters, pushed to the app's web editor each tick.
    public string ClustersJson()
    {
        var arr = new JsonArray();
        lock (_lock)
        {
            foreach (var c in _clusters.Values) arr.Add(ClusterJsonObject(c));
        }
        return arr.ToJsonString();
    }

    /// Returns and clears any pending app-initiated rejection, as JSON.
    public string? TakeLocalRejectionJson()
    {
        string? reason;
        lock (_lock)
        {
            reason = _pendingLocalRejection;
            _pendingLocalRejection = null;
        }
        if (reason is null) return null;
        return new JsonObject { ["reason"] = reason }.ToJsonString();
    }

    private void SetError(string message)
    {
        lock (_lock)
        {
            _lastError = message;
            _running = false;
        }
    }

    // MARK: Cluster registry — bridge entry points (called for "windowsapp") -

    public void AnnounceFromBridge(string json)
    {
        var o = Parse(json);
        if (o is null) return;
        SetRoster(StrOr(o["program"], LocalProgram), o["groups"] as JsonArray);
    }

    public void ConnectFromBridge(string json)
    {
        var o = Parse(json);
        if (o is null) return;
        ConnectGroup(StrOr(o["fromProgram"], LocalProgram), Str(o["toProgram"]), Str(o["groupName"]), Str(o["groupType"]));
    }

    public void DisconnectFromBridge(string json)
    {
        var o = Parse(json);
        if (o is null) return;
        DisconnectGroup(Str(o["clusterId"]), Str(o["groupName"]), StrOr(o["program"], LocalProgram));
    }

    public void SyncFromBridge(string json)
    {
        var o = Parse(json);
        if (o is null) return;
        ApplySync(StrOr(o["program"], LocalProgram), Str(o["groupName"]), Str(o["groupType"]), o, Num(o["ts"]));
    }

    // MARK: Cluster registry API ------------------------------------------

    /// Records an endpoint's eligible groups. `program` "windowsapp" is this app.
    ///
    /// A roster announcement is the full current set of bridge-eligible groups for
    /// that program (re-sent after every edit, delete, and reconnect), so it
    /// doubles as the authoritative "decouple on delete" signal. Membership is
    /// pinned to the linked group *instance* (its id), so a delete decouples, a
    /// rename decouples (name changes), and a same-named group created later can't
    /// silently re-join (new id). Works even when the peer is offline — the peer
    /// reconciles from the clusters snapshot on its next reconnect.
    public void SetRoster(string program, JsonArray? groups)
    {
        if (string.IsNullOrEmpty(program)) return;
        var infos = new List<GroupInfo>();
        if (groups != null)
        {
            foreach (var g in groups)
            {
                if (g is JsonObject go)
                {
                    infos.Add(new GroupInfo(Str(go["id"]), Str(go["name"]), Str(go["type"]), Bool(go["frozen"])));
                }
            }
        }
        var snapshots = new List<string>();
        lock (_lock)
        {
            _rosters[program] = infos;
            var affected = _clusters.Values.Where(c => c.Members.Contains(program)).ToList();
            var changed = false;
            foreach (var cluster in affected)
            {
                // Backfill the pinned id for clusters formed before id pinning (or
                // restored from disk) by matching name+type once; auto-migrates.
                cluster.MemberGroupIds.TryGetValue(program, out var pinnedId);
                pinnedId ??= "";
                if (pinnedId == "")
                {
                    var legacy = infos.FirstOrDefault(i => i.Name == cluster.GroupName && i.Type == cluster.GroupType);
                    if (!string.IsNullOrEmpty(legacy.Id))
                    {
                        pinnedId = legacy.Id;
                        cluster.MemberGroupIds[program] = pinnedId;
                        changed = true;
                    }
                }
                // Keep the member only if its pinned instance is still present under
                // the same name+type.
                bool stillPresent = pinnedId == ""
                    ? infos.Any(i => i.Name == cluster.GroupName && i.Type == cluster.GroupType)
                    : infos.Any(i => i.Id == pinnedId && i.Name == cluster.GroupName && i.Type == cluster.GroupType);
                if (stillPresent) continue;

                cluster.Members.Remove(program);
                cluster.MemberGroupIds.Remove(program);
                cluster.Contributions.Remove(program);
                if (cluster.Members.Count < 2)
                {
                    _clusters.Remove(cluster.Id);
                    cluster.Members.Clear();
                }
                changed = true;
                snapshots.Add(ClusterJsonObject(cluster).ToJsonString());
            }
            if (changed) PersistClustersLocked();
        }
        foreach (var snap in snapshots) BroadcastClusterText(snap);
    }

    /// Links the same-named group on two programs into one cluster. Requires both
    /// to have a matching, unfrozen group of the same type; otherwise rejects.
    public void ConnectGroup(string from, string to, string groupName, string groupType)
    {
        if (from == "" || to == "" || groupName == "" || from == to) return;

        string? rejectProgram = null;
        string? rejectReason = null;
        string? snapshot = null;
        lock (_lock)
        {
            var fromOk = RosterHasEligibleLocked(from, groupName, groupType);
            var toOk = RosterHasEligibleLocked(to, groupName, groupType);
            if (!(fromOk && toOk))
            {
                rejectProgram = from;
                rejectReason = !fromOk
                    ? "this group must be unfrozen to connect"
                    : $"no matching unfrozen \"{groupName}\" group on {to}";
            }
            else
            {
                var cluster = _clusters.Values.FirstOrDefault(c => c.GroupName == groupName && c.GroupType == groupType);
                if (cluster is null)
                {
                    cluster = new ClusterState { Id = Guid.NewGuid().ToString(), GroupName = groupName, GroupType = groupType };
                    _clusters[cluster.Id] = cluster;
                }
                cluster.Members.Add(from);
                cluster.Members.Add(to);
                // Pin each member to the specific local group instance it linked,
                // so a later delete + same-name re-create can't re-join this cluster.
                var fromId = RosterGroupIdLocked(from, groupName, groupType);
                if (!string.IsNullOrEmpty(fromId)) cluster.MemberGroupIds[from] = fromId!;
                var toId = RosterGroupIdLocked(to, groupName, groupType);
                if (!string.IsNullOrEmpty(toId)) cluster.MemberGroupIds[to] = toId!;
                PersistClustersLocked();
                snapshot = ClusterJsonObject(cluster).ToJsonString();
            }
        }
        if (rejectReason != null) RejectTo(rejectProgram!, rejectReason);
        else if (snapshot != null) BroadcastClusterText(snapshot);
    }

    public void DisconnectGroup(string clusterId, string groupName, string program)
    {
        string? snapshot = null;
        lock (_lock)
        {
            ClusterState? target = null;
            if (clusterId != "" && _clusters.TryGetValue(clusterId, out var byId)) target = byId;
            target ??= _clusters.Values.FirstOrDefault(c => c.GroupName == groupName);
            if (target is null) return;

            target.Members.Remove(program);
            target.MemberGroupIds.Remove(program);
            target.Contributions.Remove(program);
            if (target.Members.Count < 2)
            {
                _clusters.Remove(target.Id);
                target.Members.Clear();
            }
            PersistClustersLocked();
            snapshot = ClusterJsonObject(target).ToJsonString();
        }
        if (snapshot != null) BroadcastClusterText(snapshot);
    }

    /// Folds one member's contribution into the cluster's shared state and, if the
    /// shared snapshot changed, broadcasts it. Scalars are last-writer-wins, lists
    /// are a union of owned lists, and the usage counter is a delta accumulator.
    public void ApplySync(string program, string groupName, string groupType, JsonObject contribution, double ts)
    {
        if (program == "" || groupName == "") return;

        string? before = null;
        string? after = null;
        lock (_lock)
        {
            var cluster = _clusters.Values.FirstOrDefault(c => c.GroupName == groupName && c.Members.Contains(program));
            if (cluster is null) return;

            before = ClusterJsonObject(cluster).ToJsonString();

            // Block-list / scalar contributions only update when the message
            // actually carries them. Lightweight usage-only pings must NOT clobber
            // the member's stored sites/apps/scalars contribution.
            var scalarsPayload = contribution["scalars"] as JsonObject;
            var carriesConfig = scalarsPayload != null || contribution.ContainsKey("sites") || contribution.ContainsKey("apps");
            if (carriesConfig)
            {
                var stored = new JsonObject { ["scalars"] = scalarsPayload != null ? scalarsPayload.DeepClone() : new JsonObject() };
                if (contribution["sites"] is JsonArray sites) stored["sites"] = sites.DeepClone();
                if (contribution.ContainsKey("apps") && contribution["apps"] is JsonNode apps) stored["apps"] = apps.DeepClone();
                cluster.Contributions[program] = stored;

                var priority = Bool(contribution["priority"]);
                if (scalarsPayload != null)
                {
                    if (priority)
                    {
                        cluster.SharedScalars = (JsonObject)scalarsPayload.DeepClone();
                        cluster.SharedTs = Math.Max(cluster.SharedTs, ts) + 1;
                    }
                    else if (ts >= cluster.SharedTs)
                    {
                        cluster.SharedScalars = (JsonObject)scalarsPayload.DeepClone();
                        cluster.SharedTs = ts;
                    }
                }

                // Freeze is NOT last-writer-wins: take the MOST RESTRICTIVE freeze
                // across members so coupling/decoupling can't flip it at random.
                MergeFreezeLocked(cluster);
            }

            // Usage: shared budget via delta accrual. A newer reset anchor rolls
            // the whole budget over.
            if (contribution["usageResetAtMs"] is JsonNode anchorNode && anchorNode.GetValueKind() == JsonValueKind.Number)
            {
                var anchor = anchorNode.GetValue<double>();
                if (anchor > cluster.SharedUsageResetAtMs)
                {
                    cluster.SharedUsageMs = 0;
                    cluster.SharedUsageResetAtMs = anchor;
                    cluster.UsageSeeded = false;
                }
            }
            if (contribution["usageDeltaMs"] is JsonNode deltaNode && deltaNode.GetValueKind() == JsonValueKind.Number && deltaNode.GetValue<double>() != 0)
            {
                cluster.SharedUsageMs = Math.Max(0, cluster.SharedUsageMs + deltaNode.GetValue<double>());
                cluster.UsageSeeded = true;
            }
            else if (!cluster.UsageSeeded && contribution["usageMs"] is JsonNode seedNode && seedNode.GetValueKind() == JsonValueKind.Number)
            {
                var seed = seedNode.GetValue<double>();
                if (seed > cluster.SharedUsageMs) cluster.SharedUsageMs = seed;
            }

            // Active snooze: newest start wins.
            if (contribution["snoozeTs"] is JsonNode snoozeTsNode && snoozeTsNode.GetValueKind() == JsonValueKind.Number)
            {
                var snoozeTs = snoozeTsNode.GetValue<double>();
                if (snoozeTs > 0 && snoozeTs > cluster.SharedSnoozeTs)
                {
                    cluster.SharedSnoozeTs = snoozeTs;
                    cluster.SharedSnooze = contribution["snooze"] is JsonObject sn ? (JsonObject)sn.DeepClone() : new JsonObject();
                }
            }

            // Cumulative snooze total: keep the max across members (display only).
            if (contribution["snoozeTotalMs"] is JsonNode totalNode && totalNode.GetValueKind() == JsonValueKind.Number)
            {
                var total = totalNode.GetValue<double>();
                if (total > cluster.SharedSnoozeTotalMs) cluster.SharedSnoozeTotalMs = total;
            }

            if (carriesConfig) PersistClustersLocked();
            after = ClusterJsonObject(cluster).ToJsonString();
        }

        if (after != null && before != after) BroadcastClusterText(after);
    }

    /// Called by the in-process Windows enforcer when it accrues (or rolls over)
    /// usage for one of its groups. A no-op unless that group is actually
    /// clustered (ApplySync ignores unknown clusters).
    public void ReportLocalUsage(string groupName, double deltaMs, double resetAtMs, double? seedMs = null)
    {
        var contribution = new JsonObject { ["usageResetAtMs"] = resetAtMs };
        if (deltaMs != 0) contribution["usageDeltaMs"] = deltaMs;
        if (seedMs.HasValue) contribution["usageMs"] = seedMs.Value;
        ApplySync(LocalProgram, groupName, "site", contribution, 0);
    }

    /// The hub-authoritative shared usage budget for a clustered group involving
    /// this app, or null when the group isn't in any cluster.
    public (double Ms, double ResetAtMs)? SharedUsage(string groupName)
    {
        if (string.IsNullOrEmpty(groupName)) return null;
        lock (_lock)
        {
            var cluster = _clusters.Values.FirstOrDefault(c => c.GroupName == groupName && c.Members.Contains(LocalProgram));
            if (cluster is null) return null;
            return (cluster.SharedUsageMs, cluster.SharedUsageResetAtMs);
        }
    }

    // MARK: Cluster registry — internals ----------------------------------

    /// Restrictiveness rank for a freeze mode. Higher wins the cluster merge.
    private static int FreezeRank(string? mode) => mode switch
    {
        "strict" => 3,
        "parental" => 2,
        "frozen" => 1,
        _ => 0
    };

    /// Caller must hold `_lock`. Overwrites the freeze fields in SharedScalars with
    /// the most-restrictive freeze tuple found across all member contributions.
    private void MergeFreezeLocked(ClusterState cluster)
    {
        string[] freezeFields = { "freezeMode", "freezeModeChoice", "strictFreezeHours", "frozenAtMs" };
        var bestRank = -1;
        JsonObject? bestFreeze = null;
        foreach (var contribution in cluster.Contributions.Values)
        {
            if (contribution["scalars"] is not JsonObject scalars) continue;
            var rank = FreezeRank(Str(scalars["freezeMode"]));
            if (rank > bestRank)
            {
                bestRank = rank;
                var tuple = new JsonObject();
                foreach (var field in freezeFields)
                {
                    if (scalars[field] is JsonNode v) tuple[field] = v.DeepClone();
                }
                bestFreeze = tuple;
            }
        }
        if (bestFreeze is null) return;
        foreach (var field in freezeFields)
        {
            if (bestFreeze[field] is JsonNode v) cluster.SharedScalars[field] = v.DeepClone();
            else cluster.SharedScalars.Remove(field);
        }
    }

    /// Caller must hold `_lock`.
    private bool RosterHasEligibleLocked(string program, string name, string type)
    {
        if (!_rosters.TryGetValue(program, out var infos)) return false;
        return infos.Any(i => i.Name == name && i.Type == type && !i.Frozen);
    }

    /// Caller must hold `_lock`. The local group id for an eligible (unfrozen)
    /// same-named group, used to pin cluster membership to a specific instance.
    private string? RosterGroupIdLocked(string program, string name, string type)
    {
        if (!_rosters.TryGetValue(program, out var infos)) return null;
        foreach (var i in infos)
        {
            if (i.Name == name && i.Type == type && !i.Frozen) return i.Id;
        }
        return null;
    }

    /// Caller must hold `_lock`. Programs with a live connected peer, plus this
    /// app itself (always online while the hub runs).
    private HashSet<string> OnlineProgramsLocked()
    {
        var set = new HashSet<string>();
        foreach (var p in _peers.Values)
        {
            if (p.Connected && p.Program != "") set.Add(p.Program);
        }
        set.Add(LocalProgram);
        return set;
    }

    /// Caller must hold `_lock`. Serializes a cluster including the shared state
    /// and the union of owned lists (sites from browsers, apps from this app).
    private JsonObject ClusterJsonObject(ClusterState cluster)
    {
        var sites = new SortedSet<string>(StringComparer.Ordinal);
        // App pool keyed by display name → icon data URL. A non-empty icon from
        // any member wins so a name contributed without an icon still picks one up.
        var appIcons = new Dictionary<string, string>();
        foreach (var contribution in cluster.Contributions.Values)
        {
            if (contribution["sites"] is JsonArray siteList)
            {
                foreach (var s in siteList)
                {
                    if (s?.GetValueKind() == JsonValueKind.String) sites.Add(s.GetValue<string>());
                }
            }
            if (contribution["apps"] is JsonArray appList)
            {
                foreach (var entry in appList)
                {
                    if (entry is JsonObject eo)
                    {
                        var name = Str(eo["name"]);
                        if (name == "") continue;
                        var icon = Str(eo["icon"]);
                        if (!appIcons.TryGetValue(name, out var existing) || string.IsNullOrEmpty(existing))
                        {
                            appIcons[name] = icon;
                        }
                    }
                    else if (entry?.GetValueKind() == JsonValueKind.String)
                    {
                        var name = entry.GetValue<string>();
                        if (name == "") continue;
                        if (!appIcons.ContainsKey(name)) appIcons[name] = "";
                    }
                }
            }
        }

        var appsArray = new JsonArray();
        foreach (var name in appIcons.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            appsArray.Add(new JsonObject { ["name"] = name, ["icon"] = appIcons[name] });
        }

        var online = OnlineProgramsLocked();
        var allOnline = cluster.Members.All(m => online.Contains(m));
        var hasShared = cluster.SharedScalars.Count > 0 || cluster.SharedTs > 0;

        var members = new JsonArray();
        foreach (var m in cluster.Members.OrderBy(x => x, StringComparer.Ordinal))
        {
            cluster.MemberGroupIds.TryGetValue(m, out var memberGroupId);
            members.Add(new JsonObject
            {
                ["program"] = m,
                ["groupName"] = cluster.GroupName,
                ["groupId"] = memberGroupId ?? "",
                ["online"] = online.Contains(m)
            });
        }

        var dict = new JsonObject
        {
            ["id"] = cluster.Id,
            ["groupName"] = cluster.GroupName,
            ["groupType"] = cluster.GroupType,
            ["allOnline"] = allOnline,
            ["members"] = members
        };

        if (hasShared || sites.Count > 0 || appsArray.Count > 0 || cluster.SharedUsageMs > 0
            || cluster.SharedSnoozeTs > 0 || cluster.SharedSnoozeTotalMs > 0)
        {
            var sitesArray = new JsonArray();
            foreach (var s in sites) sitesArray.Add(s);
            dict["shared"] = new JsonObject
            {
                ["scalars"] = cluster.SharedScalars.DeepClone(),
                ["ts"] = cluster.SharedTs,
                ["sites"] = sitesArray,
                ["apps"] = appsArray,
                ["usageMs"] = cluster.SharedUsageMs,
                ["usageResetAtMs"] = cluster.SharedUsageResetAtMs,
                ["snooze"] = cluster.SharedSnooze.DeepClone(),
                ["snoozeTs"] = cluster.SharedSnoozeTs,
                ["snoozeTotalMs"] = cluster.SharedSnoozeTotalMs
            };
        }
        return dict;
    }

    // MARK: Persistence ----------------------------------------------------

    /// Caller must hold `_lock`. Persists the cluster registry so links survive a
    /// restart. Live usage is intentionally NOT persisted (re-seeded on reconnect).
    private void PersistClustersLocked()
    {
        var arr = new JsonArray();
        foreach (var c in _clusters.Values)
        {
            var contribs = new JsonObject();
            foreach (var (k, v) in c.Contributions) contribs[k] = v.DeepClone();
            var members = new JsonArray();
            foreach (var m in c.Members) members.Add(m);
            var memberGroupIds = new JsonObject();
            foreach (var (k, v) in c.MemberGroupIds) memberGroupIds[k] = v;
            arr.Add(new JsonObject
            {
                ["id"] = c.Id,
                ["groupName"] = c.GroupName,
                ["groupType"] = c.GroupType,
                ["members"] = members,
                ["memberGroupIds"] = memberGroupIds,
                ["contributions"] = contribs,
                ["sharedScalars"] = c.SharedScalars.DeepClone(),
                ["sharedTs"] = c.SharedTs,
                ["sharedSnooze"] = c.SharedSnooze.DeepClone(),
                ["sharedSnoozeTs"] = c.SharedSnoozeTs,
                ["sharedSnoozeTotalMs"] = c.SharedSnoozeTotalMs
            });
        }
        try
        {
            var tmp = Storage.ClustersPath + ".tmp";
            File.WriteAllText(tmp, arr.ToJsonString());
            File.Move(tmp, Storage.ClustersPath, overwrite: true);
        }
        catch { }
    }

    /// Caller must hold `_lock`. Rebuilds the cluster registry from disk on boot.
    private void RestoreClustersLocked()
    {
        try
        {
            if (!File.Exists(Storage.ClustersPath)) return;
            if (JsonNode.Parse(File.ReadAllText(Storage.ClustersPath)) is not JsonArray arr) return;
            foreach (var node in arr)
            {
                if (node is not JsonObject o) continue;
                var id = Str(o["id"]);
                var groupName = Str(o["groupName"]);
                var groupType = Str(o["groupType"]);
                if (id == "" || groupName == "" || groupType == "") continue;

                var cluster = new ClusterState { Id = id, GroupName = groupName, GroupType = groupType };
                if (o["members"] is JsonArray members)
                {
                    foreach (var m in members)
                    {
                        if (m?.GetValueKind() == JsonValueKind.String)
                        {
                            var program = m.GetValue<string>();
                            cluster.Members.Add(program == "macapp" ? LocalProgram : program);
                        }
                    }
                }
                if (o["memberGroupIds"] is JsonObject memberGroupIds)
                {
                    foreach (var (k, v) in memberGroupIds)
                    {
                        if (v?.GetValueKind() == JsonValueKind.String)
                        {
                            cluster.MemberGroupIds[k == "macapp" ? LocalProgram : k] = v.GetValue<string>();
                        }
                    }
                }
                if (o["contributions"] is JsonObject contribs)
                {
                    foreach (var (k, v) in contribs)
                    {
                        if (v is JsonObject vo)
                        {
                            cluster.Contributions[k == "macapp" ? LocalProgram : k] = (JsonObject)vo.DeepClone();
                        }
                    }
                }
                if (o["sharedScalars"] is JsonObject scalars) cluster.SharedScalars = (JsonObject)scalars.DeepClone();
                cluster.SharedTs = Num(o["sharedTs"]);
                if (o["sharedSnooze"] is JsonObject snooze) cluster.SharedSnooze = (JsonObject)snooze.DeepClone();
                cluster.SharedSnoozeTs = Num(o["sharedSnoozeTs"]);
                cluster.SharedSnoozeTotalMs = Num(o["sharedSnoozeTotalMs"]);

                // Only restore clusters that still have ≥2 members.
                if (cluster.Members.Count >= 2) _clusters[id] = cluster;
            }
        }
        catch { }
    }

    // MARK: JSON helpers ---------------------------------------------------

    private static JsonObject? Parse(string json)
    {
        try { return JsonNode.Parse(json) as JsonObject; }
        catch { return null; }
    }

    private static double Num(JsonNode? node) =>
        node is not null && node.GetValueKind() == JsonValueKind.Number ? node.GetValue<double>() : 0;

    private static string Str(JsonNode? node) =>
        node is not null && node.GetValueKind() == JsonValueKind.String ? node.GetValue<string>() : "";

    private static string StrOr(JsonNode? node, string fallback) =>
        node is not null && node.GetValueKind() == JsonValueKind.String ? node.GetValue<string>() : fallback;

    private static bool Bool(JsonNode? node) =>
        node is not null && node.GetValueKind() == JsonValueKind.True;
}
