using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WindowsBlocker.Core;
using WindowsBlocker.WebUI;

namespace WindowsBlocker.Bridge;

// Protocol-v4 authenticated loopback hub. Links pin explicit group IDs; no
// display-name pairing and no credentials enter the browser editor.
public sealed class ConnectionHub
{
    public const int ProtocolVersion = 4;
    public const string LocalProgram = "windowsapp";
    private readonly object _gate = new();
    private readonly Dictionary<Guid, Peer> _peers = new();
    private readonly Dictionary<string, JsonArray> _rosters = new();
    private readonly Dictionary<string, Cluster> _clusters = new();
    private readonly Dictionary<string, string> _seenDefinitions = new();
    private readonly Dictionary<string, PendingBrowserRequest> _browserRequests = new();
    private readonly HashSet<string> _unlinkedLocal = new();
    private readonly Dictionary<(Guid Peer,string Request),Guid> _classifierRequests = new();
    private WebApplication? _listener;
    private string _error = "";
    private JsonObject? _rejection;
    public Func<string, string, string, JsonObject, Task<JsonObject>>? ClassifierRequest { get; set; }

    private sealed class Peer(WebSocket socket)
    {
        public readonly Guid Id = Guid.NewGuid();
        public readonly WebSocket Socket = socket;
        public readonly SemaphoreSlim SendLock = new(1);
        public readonly string Challenge = LocalHubAuthentication.Challenge();
        public string Program = "";
        public bool Connected;
    }
    private sealed record PendingBrowserRequest(Guid PeerId, string Operation, TaskCompletionSource<JsonObject> Reply);
    private sealed class Cluster
    {
        public Cluster() { }
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "";
        public string Initiator { get; set; } = "";
        public Dictionary<string, string> Members { get; set; } = new();
        public HashSet<string> Contributed { get; set; } = new();
        public JsonObject Scalars { get; set; } = new();
        public JsonArray Scopes { get; set; } = new();
        public JsonObject Lock { get; set; } = new();
        public double Timestamp { get; set; }
        public double Usage { get; set; }
        public double Anchor { get; set; }
        public bool UsageSeeded { get; set; }
        public JsonObject Buckets { get; set; } = new();
        public bool BucketsSeeded { get; set; }
        public JsonObject Snooze { get; set; } = new();
        public double SnoozeTimestamp { get; set; }
        public double SnoozeTotal { get; set; }
    }
    public static readonly string[] ScalarFields = ["name", "enabled", "mode", "allowedMinutes", "resetIntervalHours", "resetAtMidnight", "rollingLimit", "allowSnooze", "snoozeKind", "snoozeMinutes", "snoozeActivationDelayMinutes", "snoozeCooldownMinutes", "snoozeConfirmations", "activeDays", "timeWindowsText", "fallbackUrl", "pauseSeconds"];
    public static readonly string[] LockFields = ["lockedAtMs", "lockWaitHours", "parentalPasswordHash", "parentalPasswordSalt", "lockVersion"];

    public void Start()
    {
        if (_listener != null) return;
        try
        {
            _ = LocalHubAuthentication.Secret();
            Restore();
            _listener=LoopbackServer.Start(Storage.HubPort,AcceptAsync,true); _error="";
        }
        catch (Exception ex) { _error = ex.Message; }
    }
    public void Stop() => StopAsync().GetAwaiter().GetResult();
    public async Task StopAsync()
    {
        var listener=_listener; _listener=null;
        List<Peer> peers; lock (_gate) peers = _peers.Values.ToList();
        foreach (var peer in peers) Remove(peer);
        await LoopbackServer.StopAsync(listener);
    }
    private async Task AcceptAsync(HttpContext context)
    {
        if(!context.WebSockets.IsWebSocketRequest) { context.Response.StatusCode=403; return; }
        using var socket=await context.WebSockets.AcceptWebSocketAsync();
        var peer=new Peer(socket); lock(_gate) _peers[peer.Id]=peer;
        await ReceiveAsync(peer);
    }
    internal static string? HelloRejectionReason(JsonObject message, string challenge, byte[] secret)
    {
        if (Number(message["v"]) != ProtocolVersion) return "protocol-mismatch";
        var program = Text(message["program"]);
        if (!LocalHubAuthentication.BrowserPrograms.Contains(program)) return "invalid-program";
        return Text(message["challenge"]) == challenge && LocalHubAuthentication.Verify(program, challenge, Text(message["proof"]), secret) ? null : "authentication-failed";
    }
    private async Task ReceiveAsync(Peer peer)
    {
        try
        {
            await SendAsync(peer, new() { ["kind"] = "challenge", ["v"] = ProtocolVersion, ["challenge"] = peer.Challenge });
            using var authenticationDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var buffer = new byte[8192];
            while (peer.Socket.State == WebSocketState.Open)
            {
                using var data = new MemoryStream();
                WebSocketReceiveResult part;
                do
                {
                    part = await peer.Socket.ReceiveAsync(new ArraySegment<byte>(buffer), peer.Connected ? CancellationToken.None : authenticationDeadline.Token);
                    if (part.MessageType != WebSocketMessageType.Text) return;
                    data.Write(buffer, 0, part.Count);
                    if (data.Length > 1_048_576) { await Reject(peer, "message-too-large"); return; }
                } while (!part.EndOfMessage);
                if (JsonNode.Parse(data.ToArray()) is not JsonObject message) { await Reject(peer, "invalid-message"); return; }
                var kind = Text(message["kind"]);
                if (!peer.Connected)
                {
                    var reason = kind == "hello" ? HelloRejectionReason(message, peer.Challenge, LocalHubAuthentication.Secret()) : "authentication-required";
                    lock (_gate) if (reason == null && _peers.Values.Any(p => p.Id != peer.Id && p.Connected && p.Program == Text(message["program"]))) reason = "duplicate-program";
                    if (reason != null) { await Reject(peer, reason); return; }
                    peer.Program = Text(message["program"]); peer.Connected = true;
                    await SendAsync(peer, new() { ["kind"] = "welcome", ["v"] = ProtocolVersion, ["hubProgram"] = LocalProgram, ["peers"] = PeerList() });
                    await SendAsync(peer, new() { ["kind"] = "clusters", ["clusters"] = JsonNode.Parse(ClustersJson())?["clusters"]?.DeepClone(), ["rosters"] = JsonNode.Parse(ClustersJson())?["rosters"]?.DeepClone() });
                    Broadcast(new() { ["kind"] = "peers", ["peers"] = PeerList() }); continue;
                }
                switch (kind)
                {
                    case "hello": await Reject(peer, "already-authenticated"); return;
                    case "groups-announce": SetRoster(peer.Program, message["groups"] as JsonArray ?? new()); break;
                    case "group-link": case "group-unlink":
                        var refusal = kind == "group-link" ? Link(peer.Program, Text(message["groupId"]), Text(message["targetProgram"]), Text(message["targetGroupId"])) : Unlink(peer.Program, Text(message["groupId"]));
                        if (refusal != null) await SendAsync(peer, new() { ["kind"] = "link-refused", ["reason"] = refusal, ["groupId"] = Text(message["groupId"]) }); break;
                    case "group-sync": ApplySync(peer.Program, Text(message["groupId"]), message); break;
                    case "classifier-request": _ = RouteClassifierAsync(peer, message); break;
                    case "browser-response": ResolveBrowser(peer, message); break;
                    case "ping": await SendAsync(peer, new() { ["kind"] = "pong", ["t"] = Number(message["t"]) }); break;
                }
            }
        }
        catch { }
        finally { Remove(peer); }
    }
    internal static readonly HashSet<string> ClassifierOperations = new() { "bridge-info","collection-info","diagnostic","collect","video-tags","video-tags-batch","classifier-taxonomy","submit-correction","dev-log","activity-record","activity-settings" };
    internal static bool ValidClassifierRequest(string requestId,string operation,JsonNode? body) => requestId.Length is >0 and <=128 && requestId.All(c=>c is >= '!' and <= '~') && ClassifierOperations.Contains(operation) && body is JsonObject && Encoding.UTF8.GetByteCount(body.ToJsonString())<=88_000;
    internal string? BeginClassifierRequest(Guid peerId,string requestId,out Guid lease)
    {
        lock(_gate)
        {
            lease=Guid.Empty;
            if(_classifierRequests.ContainsKey((peerId,requestId))) return "duplicate-classifier-request";
            if(_classifierRequests.Count>=32) return "classifier-busy";
            lease=Guid.NewGuid();_classifierRequests[(peerId,requestId)]=lease;
            return null;
        }
    }
    internal void EndClassifierRequest(Guid peerId,string requestId,Guid lease)
    {
        lock(_gate) if(_classifierRequests.TryGetValue((peerId,requestId),out var current) && current==lease) _classifierRequests.Remove((peerId,requestId));
    }
    internal void RemoveClassifierRequests(Guid peerId)
    {
        lock(_gate) foreach(var key in _classifierRequests.Keys.Where(k=>k.Peer==peerId).ToArray()) _classifierRequests.Remove(key);
    }
    private async Task RouteClassifierAsync(Peer peer, JsonObject message)
    {
        var requestId = Text(message["requestID"]); var operation = Text(message["operation"]); var lease=Guid.Empty;
        var reply = new JsonObject { ["kind"] = "classifier-response", ["requestID"] = requestId, ["operation"] = operation };
        try
        {
            if (!ValidClassifierRequest(requestId,operation,message["body"])) throw new InvalidDataException("invalid-classifier-request");
            if(BeginClassifierRequest(peer.Id,requestId,out lease) is { } refusal) throw new InvalidOperationException(refusal);
            reply["body"] = ClassifierRequest != null ? await ClassifierRequest(peer.Program, requestId, operation, (JsonObject)message["body"]!).WaitAsync(TimeSpan.FromSeconds(30)) : throw new InvalidOperationException("classifier-unavailable");
        }
        catch (Exception ex) { reply["error"] = ex is TimeoutException ? "classifier-timeout" : ex.Message; }
        finally { if(lease!=Guid.Empty) EndClassifierRequest(peer.Id,requestId,lease); }
        await SendAsync(peer, reply);
    }
    public async Task<JsonObject> SendBrowserRequest(string operation, JsonObject body, string? target = null)
    {
        Peer peer; string id = Guid.NewGuid().ToString(); var completion = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            var candidates = _peers.Values.Where(p => p.Connected && (target == null || target == p.Program || target == p.Id.ToString())).ToList();
            if (candidates.Count != 1) throw new InvalidOperationException(candidates.Count == 0 ? "browser-unavailable" : "browser-ambiguous");
            if (_browserRequests.Count >= 64) throw new InvalidOperationException("browser-busy");
            peer = candidates[0]; _browserRequests[id] = new(peer.Id, operation, completion);
        }
        try
        {
            await SendAsync(peer, new() { ["kind"] = "browser-request", ["requestID"] = id, ["operation"] = operation, ["body"] = body.DeepClone() });
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(20));
        }
        finally { lock (_gate) _browserRequests.Remove(id); }
    }
    private void ResolveBrowser(Peer peer, JsonObject message)
    {
        lock (_gate)
        {
            if (!_browserRequests.TryGetValue(Text(message["requestID"]), out var pending)) return;
            if (pending.PeerId != peer.Id || pending.Operation != Text(message["operation"])) { _ = Reject(peer, "unmatched-browser-response"); return; }
            if (message["body"] is JsonObject body) pending.Reply.TrySetResult((JsonObject)body.DeepClone()); else pending.Reply.TrySetException(new InvalidOperationException(Text(message["error"])));
        }
    }
    private async Task SendAsync(Peer peer, JsonObject message)
    {
        await peer.SendLock.WaitAsync();
        try { if (peer.Socket.State == WebSocketState.Open) await peer.Socket.SendAsync(Encoding.UTF8.GetBytes(message.ToJsonString()), WebSocketMessageType.Text, true, CancellationToken.None); }
        catch { Remove(peer); }
        finally { peer.SendLock.Release(); }
    }
    private async Task Reject(Peer peer, string reason)
    {
        await SendAsync(peer, new() { ["kind"] = "rejected", ["reason"] = reason });
        try { await peer.Socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, reason, CancellationToken.None); } catch { }
    }
    private void Remove(Peer peer)
    {
        lock (_gate)
        {
            if (!_peers.Remove(peer.Id)) return;
            RemoveClassifierRequests(peer.Id);
            _rosters.Remove(peer.Program);
            foreach (var pending in _browserRequests.Values.Where(p => p.PeerId == peer.Id)) pending.Reply.TrySetException(new InvalidOperationException("browser-unavailable"));
        }
        peer.Socket.Dispose(); Broadcast(new() { ["kind"] = "peers", ["peers"] = PeerList() }); BroadcastRosters(); BroadcastClusters();
    }
    private void Broadcast(JsonObject frame) { List<Peer> peers; lock (_gate) peers = _peers.Values.Where(p => p.Connected).ToList(); foreach (var p in peers) _ = SendAsync(p, frame); }
    private JsonArray PeerList()
    {
        lock (_gate)
        {
            var peers=new JsonArray(_peers.Values.Where(p=>p.Connected).Select(p=>(JsonNode)new JsonObject { ["id"]=p.Id.ToString(),["program"]=p.Program,["connected"]=true }).ToArray());
            // The shared worker is embedded behind this authenticated hub;
            // browsers still discover its route through the canonical roster.
            if(ClassifierRequest!=null) peers.Add(new JsonObject { ["id"]="windows-vault-classifier",["program"]="classifier",["connected"]=true });
            return peers;
        }
    }
    public string CurrentStatusJson() => new JsonObject { ["running"] = _listener != null, ["state"] = _error.Length > 0 ? "error" : _listener != null ? "running" : "off", ["address"] = $"ws://127.0.0.1:{Storage.HubPort}", ["peers"] = PeerList(), ["error"] = _error, ["hubProgram"] = LocalProgram }.ToJsonString();
    public void BroadcastClassifier(JsonObject evt) { if(Text(evt["operation"]) is not ("video-tags-updated" or "classifier-state-updated") || evt["body"] is not JsonObject body || Encoding.UTF8.GetByteCount(body.ToJsonString())>88_000) return; Broadcast(new JsonObject { ["kind"] = "classifier-broadcast", ["operation"] = evt["operation"]?.DeepClone(), ["body"] = body.DeepClone() }); }
    public int ActiveClusterCount() { lock (_gate) return _clusters.Count; }
    public string? TakeLocalRejectionJson() { var r = _rejection?.ToJsonString(); _rejection = null; return r; }
    public void AnnounceFromBridge(string json) { if (JsonNode.Parse(json) is JsonObject m) SetRoster(LocalProgram, m["groups"] as JsonArray ?? new()); }
    public void ConnectFromBridge(string json) => LinkFromBridge("group-link", json);
    public void DisconnectFromBridge(string json) => LinkFromBridge("group-unlink", json);
    public void LinkFromBridge(string kind, string json)
    {
        if (JsonNode.Parse(json) is not JsonObject m) return;
        var gid = Text(m["groupId"]); var reason = kind == "group-link" ? Link(LocalProgram, gid, Text(m["targetProgram"]), Text(m["targetGroupId"])) : Unlink(LocalProgram, gid);
        if (reason != null) _rejection = new() { ["reason"] = reason, ["groupId"] = gid };
    }
    public void SyncFromBridge(string json) { if (JsonNode.Parse(json) is JsonObject m) ApplySync(LocalProgram, Text(m["groupId"]), m); }
    public void SetRoster(string program, JsonArray groups)
    {
        lock (_gate)
        {
            _rosters[program] = (JsonArray)groups.DeepClone();
            foreach (var c in _clusters.Values.Where(c => c.Members.ContainsKey(program)).ToList())
                if (!groups.OfType<JsonObject>().Any(g => Text(g["id"]) == c.Members[program])) RemoveMember(c, program);
            Persist();
        }
        BroadcastRosters(); BroadcastClusters();
    }
    private void BroadcastRosters() { var r = new JsonObject(); lock (_gate) foreach (var (p, groups) in _rosters) r[p] = groups.DeepClone(); Broadcast(new() { ["kind"] = "rosters", ["rosters"] = r }); }
    private Cluster? Find(string program, string groupId) => _clusters.Values.FirstOrDefault(c => c.Members.GetValueOrDefault(program) == groupId && groupId.Length > 0);
    public string? Link(string program, string groupId, string targetProgram, string targetGroupId)
    {
        lock (_gate)
        {
            if (program == targetProgram || groupId.Length == 0 || targetGroupId.Length == 0) return "invalid-link";
            var a = _rosters.GetValueOrDefault(program)?.OfType<JsonObject>().FirstOrDefault(g => Text(g["id"]) == groupId);
            var b = _rosters.GetValueOrDefault(targetProgram)?.OfType<JsonObject>().FirstOrDefault(g => Text(g["id"]) == targetGroupId);
            if (a == null || b == null) return "group-not-found";
            if (a["frozen"]?.GetValueKind() == JsonValueKind.True || b["frozen"]?.GetValueKind() == JsonValueKind.True) return "group-locked";
            var own = Find(program, groupId); var other = Find(targetProgram, targetGroupId);
            if (own != null && other != null) return own == other ? "already-linked" : "linked-elsewhere";
            var c = own ?? other ?? new Cluster { Name = Text(a["name"]), Initiator = program };
            if (IsLocked(c.Lock)) return "group-locked";
            if (c.Members.ContainsKey(program) && c.Members[program] != groupId || c.Members.ContainsKey(targetProgram) && c.Members[targetProgram] != targetGroupId) return "program-already-linked";
            c.Members[program] = groupId; c.Members[targetProgram] = targetGroupId; _clusters[c.Id] = c; Persist();
        }
        BroadcastClusters(); return null;
    }
    public string? Unlink(string program, string groupId)
    {
        lock (_gate) { var c = Find(program, groupId); if (c == null) return "not-linked"; if (IsLocked(c.Lock)) return "group-locked"; RemoveMember(c, program); Persist(); }
        BroadcastClusters(); return null;
    }
    private void RemoveMember(Cluster c, string program)
    {
        if (c.Members.TryGetValue(LocalProgram, out var localId)) { _unlinkedLocal.Add(localId); _seenDefinitions.Remove(localId); }
        c.Members.Remove(program); c.Contributed.Remove(program);
        if (c.Members.Count < 2) _clusters.Remove(c.Id);
    }
    public static bool IsLocked(JsonObject obj) => obj["lockedAtMs"]?.GetValueKind() == JsonValueKind.Number;
    public void ApplySync(string program, string groupId, JsonObject frame)
    {
        lock (_gate)
        {
            var c = Find(program, groupId); if (c == null) return;
            var first = !c.Contributed.Contains(program); var ts = Number(frame["ts"]); var priority = first && program == c.Initiator; var wins = priority || ts >= c.Timestamp;
            if (frame["scalars"] is JsonObject scalars && wins)
            {
                var budgetChanged = new[] { "mode", "allowedMinutes", "resetIntervalHours", "resetAtMidnight", "rollingLimit" }.Any(k => c.Scalars.ContainsKey(k) && c.Scalars[k]?.ToJsonString() != scalars[k]?.ToJsonString());
                c.Scalars = (JsonObject)scalars.DeepClone(); c.Timestamp = priority ? Math.Max(ts, c.Timestamp) + 1 : ts; c.Name = Text(c.Scalars["name"]);
                if (budgetChanged) { c.Usage = 0; c.Anchor = DateTimeOffset.Now.ToUnixTimeMilliseconds(); c.Buckets.Clear(); c.UsageSeeded = c.BucketsSeeded = true; }
            }
            if (frame["scopes"] is JsonArray scopes && (first || wins))
            {
                var desktop = program == LocalProgram;
                var own = scopes.OfType<JsonObject>().Where(s => (Text(s["surface"]) == "apps") == desktop).ToList();
                var keep = c.Scopes.OfType<JsonObject>().Where(s => (Text(s["surface"]) == "apps") != desktop || first && !own.Any(i => ScopeKey(i) == ScopeKey(s))).ToList();
                var counts = new Dictionary<string, int>(); var merged = new JsonArray();
                foreach (var s in keep.Concat(own)) { var copy = (JsonObject)s.DeepClone(); var surface = Text(s["surface"]); counts[surface] = counts.GetValueOrDefault(surface) + 1; copy["id"] = $"{surface}-{counts[surface]}"; merged.Add(copy); }
                c.Scopes = merged;
            }
            if (frame["lock"] is JsonObject incoming && (c.Lock.Count == 0 || !first && Number(frame["lockBase"]) == Number(c.Lock["lockVersion"]) && Number(incoming["lockVersion"]) > Number(c.Lock["lockVersion"]))) c.Lock = (JsonObject)incoming.DeepClone();
            if (frame.ContainsKey("scalars") || frame.ContainsKey("scopes")) c.Contributed.Add(program);
            if (c.Anchor <= 0 && Number(frame["usageResetAtMs"]) > 0) c.Anchor = Number(frame["usageResetAtMs"]);
            Roll(c);
            var now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            var delta = Number(frame["usageDeltaMs"]);
            if (delta != 0 && (!frame.ContainsKey("usageDeltaAnchorMs") || Number(frame["usageDeltaAnchorMs"]) == c.Anchor)) { if(c.Scalars["rollingLimit"]?.GetValueKind()!=JsonValueKind.True) CountBudgetSnooze(c,c.Usage,delta,now); c.Usage = Math.Max(0, c.Usage + delta); c.UsageSeeded = true; }
            else if (!c.UsageSeeded) c.Usage = Math.Max(c.Usage, Number(frame["usageMs"]));
            var rollingPolicy=new BlockGroup { ResetIntervalHours=Number(c.Scalars["resetIntervalHours"])>0 ? Number(c.Scalars["resetIntervalHours"]) : 24,ResetAtMidnight=c.Scalars["resetAtMidnight"]?.GetValueKind()==JsonValueKind.True };
            var rollingBefore=UsageBudget.UsedMs(UsageBudget.PruneBuckets(BucketValues(c.Buckets),rollingPolicy,now));
            if (frame["usageBuckets"] is JsonObject buckets)
            {
                if(c.Scalars["rollingLimit"]?.GetValueKind()==JsonValueKind.True)
                    CountBudgetSnooze(c,rollingBefore,UsageBudget.UsedMs(UsageBudget.PruneBuckets(BucketValues(buckets),rollingPolicy,now)),now);
                foreach (var (k, v) in buckets) c.Buckets[k] = Math.Max(0, Number(c.Buckets[k]) + Number(v)); c.BucketsSeeded = true;
            }
            else if (!c.BucketsSeeded && frame["usageBucketsSeed"] is JsonObject seed) foreach (var (k, v) in seed) c.Buckets[k] = Math.Max(Number(c.Buckets[k]), Number(v));
            var cutoff = now - (Math.Max(Number(c.Scalars["resetIntervalHours"]), 24) * 3_600_000 + 60_000);
            foreach (var key in c.Buckets.Select(k => k.Key).Where(k => !double.TryParse(k, out var n) || n <= cutoff).ToList()) c.Buckets.Remove(key);
            if (Number(frame["snoozeTs"]) > c.SnoozeTimestamp) { CountSnooze(c, now, true); c.SnoozeTimestamp = Number(frame["snoozeTs"]); c.Snooze = frame["snooze"] is JsonObject snooze ? (JsonObject)snooze.DeepClone() : new(); }
            CountSnooze(c, now, false);
            Persist();
        }
        BroadcastClusters();
    }
    private static double SnoozeChanged(JsonNode? snooze) => Number(snooze?["changedAtMs"])>0 ? Number(snooze?["changedAtMs"]) : Number(snooze?["startsAtMs"]);
    private static string ScopeKey(JsonObject line) => Text(line["entryID"]) is { Length: > 0 } entry ? entry : Text(line["surface"]) == "apps" ? "apps" : Text(line["platform"]) is { Length: > 0 } p ? p : "site";
    private static Dictionary<double,double> BucketValues(JsonObject buckets) => buckets.Where(b=>double.TryParse(b.Key,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var start) && double.IsFinite(start) && Number(b.Value)>0).ToDictionary(b=>double.Parse(b.Key,System.Globalization.CultureInfo.InvariantCulture),b=>Number(b.Value));
    private static void CountBudgetSnooze(Cluster c,double before,double added,double now)
    {
        if(added<=0 || Text(c.Snooze["kind"])!="budget" || Number(c.Snooze["startsAtMs"])>now || Number(c.Snooze["untilMs"])<=now || Number(c.Snooze["extraMs"])<=0) return;
        var allowance=Math.Max(0,Number(c.Scalars["allowedMinutes"]))*60_000;
        c.SnoozeTotal+=Math.Max(0,before+added-Math.Max(before,allowance));
    }
    private static void CountSnooze(Cluster c, double now, bool replacing)
    {
        if (Text(c.Snooze["kind"])=="budget") return;
        var start = Number(c.Snooze["startsAtMs"]); var end = Number(c.Snooze["untilMs"]);
        if (start <= 0 || end <= start || c.Snooze["activeMsApplied"]?.GetValueKind() == JsonValueKind.True || !replacing && now < end) return;
        c.SnoozeTotal += Math.Max(0, Math.Min(now, end) - start); c.Snooze["activeMsApplied"] = true;
    }
    private static void Roll(Cluster c)
    {
        if (c.Anchor <= 0 || c.Scalars["rollingLimit"]?.GetValueKind() == JsonValueKind.True) return;
        var group = new BlockGroup { ResetIntervalHours = Number(c.Scalars["resetIntervalHours"]), ResetAtMidnight = c.Scalars["resetAtMidnight"]?.GetValueKind() == JsonValueKind.True };
        var start = UsageBudget.PeriodStartMs(c.Anchor, group, DateTimeOffset.Now.ToUnixTimeMilliseconds());
        if (start == c.Anchor) return; c.Anchor = start; c.Usage = 0; c.UsageSeeded = true;
    }
    public void ReportLocalUsage(string groupId, double deltaMs, double resetAtMs, double? seedMs = null, IReadOnlyDictionary<double, double>? bucketDeltas = null, IReadOnlyDictionary<double, double>? seedBuckets = null)
    {
        var frame = new JsonObject { ["usageResetAtMs"] = resetAtMs, ["usageDeltaMs"] = deltaMs };
        if (seedMs.HasValue) frame["usageMs"] = seedMs.Value;
        if (bucketDeltas != null) frame["usageBuckets"] = JsonSerializer.SerializeToNode(WebStore.BucketJson(bucketDeltas));
        if (seedBuckets != null) frame["usageBucketsSeed"] = JsonSerializer.SerializeToNode(WebStore.BucketJson(seedBuckets));
        ApplySync(LocalProgram, groupId, frame);
    }
    public (double Ms, double ResetAtMs, Dictionary<double, double> Buckets)? SharedUsage(string groupId)
    {
        lock (_gate) { var c = Find(LocalProgram, groupId); if (c == null) return null; Roll(c); return (c.Usage, c.Anchor, c.Buckets.ToDictionary(k => double.Parse(k.Key, System.Globalization.CultureInfo.InvariantCulture), k => Number(k.Value))); }
    }
    public void ReconcileLocal(WebStore store)
    {
        if (JsonNode.Parse(store.LoadRawJson() ?? "{}") is not JsonObject document) return;
        var groups = document["blockedGroups"] as JsonArray ?? new();
        var roster = new JsonArray(groups.OfType<JsonObject>().Select(g => (JsonNode)new JsonObject { ["id"] = Text(g["id"]), ["name"] = Text(g["name"]), ["frozen"] = IsLocked(g) }).ToArray());
        var key = roster.ToJsonString();
        lock (_gate) { if (!_rosters.TryGetValue(LocalProgram, out var previous) || previous.ToJsonString() != key) SetRoster(LocalProgram, roster); }
        foreach (var g in groups.OfType<JsonObject>())
        {
            var gid = Text(g["id"]); Cluster? c; lock (_gate) c = Find(LocalProgram, gid); if (c == null) continue;
            if(document["groupSnoozes"]?[gid] is JsonObject snooze && SnoozeChanged(snooze)>c.SnoozeTimestamp)
                ApplySync(LocalProgram,gid,new() { ["snooze"]=snooze.DeepClone(),["snoozeTs"]=SnoozeChanged(snooze),["ts"]=0 });
            var scalars = new JsonObject(); foreach (var field in ScalarFields) if (g.ContainsKey(field)) scalars[field] = g[field]?.DeepClone();
            var lockUnit = new JsonObject(); if(g.ContainsKey("lockVersion")) foreach (var field in LockFields) lockUnit[field]=g[field]?.DeepClone();
            var definition = scalars.ToJsonString() + g["scopes"]?.ToJsonString() + lockUnit.ToJsonString();
            var previous = _seenDefinitions.GetValueOrDefault(gid); if(previous==definition) continue; _seenDefinitions[gid]=definition;
            var joined=c.Contributed.Contains(LocalProgram);
            if(joined && previous==null) continue;
            if(joined && scalars.ToJsonString()==c.Scalars.ToJsonString() && g["scopes"]?.ToJsonString()==c.Scopes.ToJsonString() && (lockUnit.Count==0 || lockUnit.ToJsonString()==c.Lock.ToJsonString())) continue;
            var frame = new JsonObject { ["scalars"]=scalars,["scopes"]=new JsonArray((g["scopes"] as JsonArray ?? new()).OfType<JsonObject>().Where(l=>Text(l["surface"])=="apps").Select(l=>l.DeepClone()).ToArray()),["ts"]=joined ? DateTimeOffset.Now.ToUnixTimeMilliseconds() : 0 };
            if(lockUnit.Count>0) { frame["lock"]=lockUnit; frame["lockBase"]=Number(g["lockSyncedVersion"]); }
            ApplySync(LocalProgram,gid,frame);
        }
        store.Update(root =>
        {
            if (root["blockedGroups"] is not JsonArray nativeGroups) return;
            lock (_gate) foreach (var g in nativeGroups.OfType<JsonObject>())
            {
                var gid = Text(g["id"]); var c = Find(LocalProgram, gid);
                if (c == null) { if (_unlinkedLocal.Remove(gid) && g["scopes"] is JsonArray s) g["scopes"] = new JsonArray(s.OfType<JsonObject>().Where(l => Text(l["surface"]) == "apps").Select(l => l.DeepClone()).ToArray()); continue; }
                foreach (var (k, v) in c.Scalars) g[k] = v?.DeepClone();
                if (c.Contributed.Count > 0) g["scopes"] = c.Scopes.DeepClone();
                if(c.SnoozeTimestamp>0 && c.Snooze.Count>0 && c.SnoozeTimestamp>SnoozeChanged(root["groupSnoozes"]?[gid])) { var snoozes=root["groupSnoozes"] as JsonObject ?? new(); root["groupSnoozes"]=snoozes; snoozes[gid]=c.Snooze.DeepClone(); }
                if(c.SnoozeTotal>0) { var totals=root["groupSnoozeTotalsMs"] as JsonObject ?? new(); root["groupSnoozeTotalsMs"]=totals; totals[gid]=c.SnoozeTotal; }
                foreach (var (k, v) in c.Lock) g[k] = v?.DeepClone();
                if (c.Lock.Count > 0) g["lockSyncedVersion"] = c.Lock["lockVersion"]?.DeepClone();
                _seenDefinitions[gid] = new JsonObject(ScalarFields.Where(g.ContainsKey).Select(k => KeyValuePair.Create(k, g[k]?.DeepClone()))).ToJsonString() + g["scopes"]?.ToJsonString();
            }
        });
    }
    private JsonObject Snapshot(Cluster c)
    {
        var peers = _peers.Values.Where(p => p.Connected).Select(p => p.Program).Append(LocalProgram).ToHashSet();
        var snapshot = new JsonObject { ["id"] = c.Id, ["groupName"] = c.Name, ["allOnline"] = c.Members.Keys.All(peers.Contains), ["members"] = new JsonArray(c.Members.Select(m => (JsonNode)new JsonObject { ["program"] = m.Key, ["groupId"] = m.Value, ["groupName"] = c.Name, ["online"] = peers.Contains(m.Key) }).ToArray()), ["shared"] = new JsonObject { ["scalars"] = c.Scalars.DeepClone(), ["scopes"] = c.Scopes.DeepClone(), ["lock"] = c.Lock.DeepClone(), ["ts"] = c.Timestamp, ["usageMs"] = c.Usage, ["usageResetAtMs"] = c.Anchor, ["usageBuckets"] = c.Buckets.DeepClone(), ["snooze"] = c.Snooze.DeepClone(), ["snoozeTs"] = c.SnoozeTimestamp, ["snoozeTotalMs"] = c.SnoozeTotal } };
        if(c.Contributed.Count==0) ((JsonObject)snapshot["shared"]!).Remove("scopes");
        return snapshot;
    }
    public string ClustersJson() { lock (_gate) return new JsonObject { ["clusters"]=new JsonArray(_clusters.Values.Select(c => (JsonNode)Snapshot(c)).ToArray()),["rosters"]=new JsonObject(_rosters.Select(r=>new KeyValuePair<string,JsonNode?>(r.Key,r.Value.DeepClone()))) }.ToJsonString(); }
    private void BroadcastClusters() { lock (_gate) foreach (var c in _clusters.Values) Broadcast(new() { ["kind"] = "cluster-updated", ["cluster"] = Snapshot(c) }); Broadcast(new() { ["kind"] = "clusters", ["clusters"] = JsonNode.Parse(ClustersJson())?["clusters"]?.DeepClone(), ["rosters"] = JsonNode.Parse(ClustersJson())?["rosters"]?.DeepClone() }); }
    private void Persist() { var temp = Storage.ClustersPath + ".tmp"; File.WriteAllText(temp, JsonSerializer.Serialize(_clusters.Values)); File.Move(temp, Storage.ClustersPath, true); }
    private void Restore() { lock (_gate) { if (_clusters.Count > 0 || !File.Exists(Storage.ClustersPath)) return; try { foreach (var c in JsonSerializer.Deserialize<List<Cluster>>(File.ReadAllText(Storage.ClustersPath)) ?? []) if (c.Members.Count >= 2 && c.Members.All(m => m.Value.Length > 0 && LocalHubAuthentication.Programs.Contains(m.Key))) _clusters[c.Id] = c; } catch { } } }
    private static double Number(JsonNode? node) => node?.GetValueKind() == JsonValueKind.Number && double.TryParse(node.ToJsonString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? value : 0;
    private static string Text(JsonNode? node) => node?.GetValueKind() == JsonValueKind.String ? node.GetValue<string>() : "";
}
