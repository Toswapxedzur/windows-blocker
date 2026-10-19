using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using WindowsBlocker.Core;
using WindowsBlocker.WebUI;

namespace WindowsBlocker.Bridge;

// Protocol-v4 authenticated loopback hub. Links pin explicit group IDs; no
// display-name pairing and no credentials enter the browser editor.
public sealed partial class ConnectionHub
{
    public const int ProtocolVersion = 4;
    public const string LocalProgram = "windowsapp";
    private readonly object _gate = new();
    private readonly Dictionary<Guid, Peer> _peers = new();
    private readonly Dictionary<string, JsonArray> _rosters = new();
    private readonly Dictionary<string, Cluster> _clusters = new();
    private readonly Dictionary<string, LocalDefinition> _seenDefinitions = new();
    private readonly Dictionary<string, PendingBrowserRequest> _browserRequests = new();
    private readonly HashSet<string> _unlinkedLocal = new();
    private readonly Dictionary<(Guid Peer,string Request),Guid> _classifierRequests = new();
    private WebApplication? _listener;
    private string _error = "";
    private JsonObject? _rejection;
    private WebStore? _localStore;
    public ConnectionHub(WebStore? localStore = null)
    {
        if (localStore != null) AttachLocalStore(localStore);
    }
    private sealed record LocalDefinition(JsonObject Scalars, JsonArray Scopes, JsonObject Lock)
    {
        public static LocalDefinition Read(JsonObject group)
        {
            var scalars = new JsonObject();
            foreach (var field in ScalarFields) if (group.ContainsKey(field)) scalars[field] = group[field]?.DeepClone();
            var unit = new JsonObject();
            if (group.ContainsKey("lockVersion")) foreach (var field in LockFields) unit[field] = group[field]?.DeepClone();
            return new(scalars, (JsonArray)(group["scopes"]?.DeepClone() ?? new JsonArray()), unit);
        }
    }
    private void AttachLocalStore(WebStore store)
    {
        // Install ownership and its read-only intent baseline in one file -> hub
        // section, before any producer can observe the newly attached owner.
        try
        {
            store.WithLockedDocument((root, _) => { lock (_gate)
            {
                if (_localStore != null) { if (!ReferenceEquals(_localStore, store)) throw new InvalidOperationException("Canonical local store already attached."); return; }
                if (root["blockedGroups"] is JsonArray groups)
                    foreach (var group in groups.OfType<JsonObject>()) _seenDefinitions[Text(group["id"])] = LocalDefinition.Read(group);
                _localStore = store;
            }});
        }
        catch (Exception ex) when (ex is InvalidDataException or JsonException or IOException or UnauthorizedAccessException)
        {
            // Missing/unsupported startup bytes supply no baseline. Reconciliation
            // still validates and refuses them before changing any hub state.
            lock (_gate) { if (_localStore == null) _localStore = store; else if (!ReferenceEquals(_localStore, store)) throw new InvalidOperationException("Canonical local store already attached."); }
        }
    }
    public Func<string, string, string, JsonObject, Task<JsonObject>>? ClassifierRequest { get; set; }

    private sealed class Peer(WebSocket socket)
    {
        public readonly Guid Id = Guid.NewGuid();
        public readonly WebSocket Socket = socket;
        public readonly object SendGate = new();
        public Task SendTail = Task.CompletedTask;
        public readonly string Challenge = LocalHubAuthentication.Challenge();
        public string Program = "";
        public bool Connected;
        public bool ClosingForWorker;
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
        [JsonPropertyName("scopeOrigins")]
        public Dictionary<string,string> ScopeOrigins { get; set; } = new();
        public JsonObject Lock { get; set; } = new();
        public double Timestamp { get; set; }
        public double Usage { get; set; }
        public double Anchor { get; set; }
        public bool UsageSeeded { get; set; }
        public Dictionary<string,double> UsageTransferReceipts { get; set; } = new();
        public JsonObject Buckets { get; set; } = new();
        public JsonObject? SharedRuleTime { get; set; }
        public Dictionary<string,JsonObject> DetachedRuleTime { get; set; } = new();
        public JsonArray TimeSeeds { get; set; } = new();
        public bool BucketsSeeded { get; set; }
        public double? JoiningUsageBase { get; set; }
        public double JoiningUsageDelta { get; set; }
        public JsonObject? JoiningBucketBase { get; set; }
        public JsonObject JoiningBucketDeltas { get; set; } = new();
        public double JoiningSeedFloor { get; set; }
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
#if VAULT_CONTRACT_TESTS
    internal Func<Task, Task>? ListenerStopCompletionForTests;
#endif
    public async Task StopAsync()
    {
        var listener=_listener; _listener=null;
        List<Peer> peers; lock (_gate) peers = _peers.Values.ToList();
        foreach (var peer in peers) Remove(peer);
        var stopped = LoopbackServer.StopAsync(listener);
#if VAULT_CONTRACT_TESTS
        stopped = ListenerStopCompletionForTests?.Invoke(stopped) ?? stopped;
#endif
        // Stop can be called synchronously by the native UI bridge.
        await stopped.ConfigureAwait(false);
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
                Task? workerClose;
                lock (_gate) workerClose = peer.ClosingForWorker ? peer.SendTail : null;
                if (workerClose != null) { await workerClose; return; }
                if (JsonNode.Parse(data.ToArray()) is not JsonObject message) { await Reject(peer, "invalid-message"); return; }
                var kind = Text(message["kind"]);
                if (!peer.Connected)
                {
                    var reason = kind == "hello" ? HelloRejectionReason(message, peer.Challenge, LocalHubAuthentication.Secret()) : "authentication-required";
                    Task? initial=null;
                    bool workerClosing;
                    lock(_gate)
                    {
                        workerClosing=peer.ClosingForWorker;
                        if(workerClosing) initial=peer.SendTail;
                        else
                        {
                            if(reason==null && _peers.Values.Any(p=>p.Id!=peer.Id && p.Connected && p.Program==Text(message["program"]))) reason="duplicate-program";
                            if(reason==null) { peer.Program=Text(message["program"]);peer.Connected=true;initial=QueueWelcome(peer); }
                        }
                    }
                    if(workerClosing) { await initial!; return; }
                    if (reason != null) { await Reject(peer, reason); return; }
                    await initial!;
                    BroadcastPeers(); continue;
                }
                switch (kind)
                {
                    case "hello": await Reject(peer, "already-authenticated"); return;
                    case "groups-announce": SetRoster(peer.Program, message["groups"] as JsonArray ?? new()); break;
                    case "group-link": case "group-unlink":
                        var refusal = kind == "group-link" ? Link(peer.Program, Text(message["groupId"]), Text(message["targetProgram"]), Text(message["targetGroupId"])) : Unlink(peer.Program, Text(message["groupId"]));
                        if (refusal != null) await SendAsync(peer, new() { ["kind"] = "link-refused", ["reason"] = refusal, ["groupId"] = Text(message["groupId"]) }); break;
                    case "group-sync": ApplySync(peer.Program, Text(message["groupId"]), message); break;
                    case "rule-time-request": _ = HandleRuleTimeAsync(peer, message); break;
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
    private Task SendAsync(Peer peer, JsonObject message)
    {
        var bytes=Encoding.UTF8.GetBytes(message.ToJsonString());
        // Never execute the write/remove callback inline while SendGate is held:
        // removing a failed peer acquires the hub gate in the opposite direction.
        lock(peer.SendGate)
            return peer.SendTail=peer.SendTail.ContinueWith(async _ => {
                try { if(peer.Socket.State==WebSocketState.Open) await peer.Socket.SendAsync(bytes,WebSocketMessageType.Text,true,CancellationToken.None); }
                catch { Remove(peer); }
            },CancellationToken.None,TaskContinuationOptions.None,TaskScheduler.Default).Unwrap();
    }
    private Task QueueWelcome(Peer peer)
    {
        lock(_gate)
        {
            var welcome=SendAsync(peer,new() { ["kind"]="welcome",["v"]=ProtocolVersion,["hubProgram"]=LocalProgram,["peers"]=PeerList(),["usageTransferReceipts"]=true,
                ["activitySettingsNotifications"]=ActivitySettingsSupported(),["ruleTimeVersion"]=1 });
            var frame=ClustersFrame();
#if VAULT_CONTRACT_TESTS
            ClusterSnapshotCapturedForTests?.Invoke("welcome");
#endif
            return Task.WhenAll(welcome,SendAsync(peer,frame));
        }
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
        peer.Socket.Dispose(); BroadcastPeers(); BroadcastRosters(); BroadcastClusters();
    }
    private void Broadcast(JsonObject frame)
    {
        lock (_gate)
        {
            var recipients = _peers.Values.Where(p => p.Connected).ToArray();
            if (_editorStage != null) { _editorStage.Frames.Add((recipients, (JsonObject)frame.DeepClone())); return; }
            foreach (var peer in recipients) _ = SendAsync(peer, frame);
        }
    }
    private void BroadcastPeers()
    {
        lock(_gate)
        {
            var frame=new JsonObject { ["kind"]="peers",["peers"]=PeerList() };
#if VAULT_CONTRACT_TESTS
            ClusterSnapshotCapturedForTests?.Invoke("peers");
#endif
            Broadcast(frame);
        }
    }
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
    public int ActiveClusterCount() { lock (_gate) return _clusters.Values.Count(c => c.Members.Count >= 2); }
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
        WithLocalStore((root, persistLocal) => { lock (_gate)
        {
            var membershipChanged = SetRosterLocked(program, groups, root, persistLocal);
            BroadcastRosters();
            // Shared-definition adoption announces the roster back to the hub.
            // Echoing clusters for that unchanged membership recursively starts
            // another adoption and can starve rule-event state commits.
            if (membershipChanged) BroadcastClusters();
        }});
    }
    private bool SetRosterLocked(string program, JsonArray groups, JsonObject? root, Action persistLocal)
    {
        var affected = _clusters.Values.Where(c => c.Members.ContainsKey(program) &&
            !groups.OfType<JsonObject>().Any(g => Text(g["id"]) == c.Members[program])).ToList();
        foreach (var c in affected) RemoveMember(c, program, root, persistLocal, false);
        _rosters[program] = (JsonArray)groups.DeepClone();
        var prefix=program+"/";
        var ids=groups.OfType<JsonObject>().Select(g=>Text(g["id"])).ToHashSet();
        foreach(var retained in _clusters.Values.ToList()) {
            foreach(var member in retained.DetachedRuleTime.Keys.Where(m=>m.StartsWith(prefix,StringComparison.Ordinal) && !ids.Contains(m[prefix.Length..])).ToList()) retained.DetachedRuleTime.Remove(member);
            if(retained.Members.Count==0 && retained.DetachedRuleTime.Count==0) _clusters.Remove(retained.Id);
        }
        Persist();
        return affected.Count > 0;
    }
    private void BroadcastRosters()
    {
        lock(_gate)
        {
            var r=new JsonObject();foreach(var (p,groups) in _rosters)r[p]=groups.DeepClone();
            var frame=new JsonObject { ["kind"]="rosters",["rosters"]=r };
#if VAULT_CONTRACT_TESTS
            ClusterSnapshotCapturedForTests?.Invoke("rosters");
#endif
            Broadcast(frame);
        }
    }
    private Cluster? Find(string program, string groupId) => _clusters.Values.FirstOrDefault(c => c.Members.GetValueOrDefault(program) == groupId && groupId.Length > 0);
    public string? Link(string program, string groupId, string targetProgram, string targetGroupId)
    {
        lock (_gate)
        {
            if (_editorStorageHeld) return "editor-storage-partial-failure; storage repair required";
            if (!_clusterStorageWritable) return "unsupported-storage";
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
            var original = EditorStateNow(); var registry = ReadEditorRegistry();
            InstallEditorState(CloneEditorState(original)); _editorStage = new EditorStage();
            try
            {
                c = Find(program, groupId) ?? Find(targetProgram, targetGroupId) ?? new Cluster { Name = Text(a["name"]), Initiator = program };
                if (!PrepareTimeLink(c, program, groupId, a["timeState"] as JsonObject, targetProgram, targetGroupId, b["timeState"] as JsonObject))
                { InstallEditorState(original); return "incompatible-custom-time"; }
                c.Members[program] = groupId; c.Members[targetProgram] = targetGroupId;
                if (!c.JoiningUsageBase.HasValue) BeginJoiningUsage(c); _clusters[c.Id] = c;
                var bytes = StorageSchema.Wrap(JsonSerializer.SerializeToNode(_clusters.Values)!, "hub.clusters", 2).ToJsonString();
#if VAULT_CONTRACT_TESTS
                LinkDiskPhaseForTests?.Invoke("registry-save");
#endif
                StorageSchema.AtomicWrite(Storage.ClustersPath, bytes);
                if (File.ReadAllText(Storage.ClustersPath) != bytes) throw new IOException("Linked shared state was not saved");
            }
            catch (Exception commitError)
            {
                InstallEditorState(original);
                try
                {
                    if (!SameBytes(File.Exists(Storage.ClustersPath) ? File.ReadAllBytes(Storage.ClustersPath) : null, registry))
                    { if (registry == null) File.Delete(Storage.ClustersPath); else AtomicRegistryBytes(registry); }
                }
                catch (Exception rollbackError)
                {
                    _clusterStorageWritable = false;
                    throw new AggregateException("link-storage-save-failed; storage repair required", commitError, rollbackError);
                }
                throw;
            }
            finally { _editorStage = null; }
        }
        BroadcastClusters(); return null;
    }
    public string? Unlink(string program, string groupId)
    {
        string? refusal = null;
        WithLocalStore((root, persistLocal) => { lock (_gate) {
                if (!_clusterStorageWritable) { refusal = "unsupported-storage"; return; }
                var c = Find(program, groupId); if (c == null) { refusal = "not-linked"; return; }
                var localGroup = c.Members.TryGetValue(LocalProgram, out var localId)
                    ? (root?["blockedGroups"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(g => Text(g["id"]) == localId) : null;
                if (IsLocked(c.Lock) || localGroup != null && Number(localGroup["lockVersion"]) > Number(c.Lock["lockVersion"]) && IsLocked(localGroup)) { refusal = "group-locked"; return; }
                // Stage memory/notifications as the native editor does. A final
                // snapshot is retained only after both normal writes succeed;
                // reported IO failure restores the original link and bytes.
                var original = EditorStateNow();
                var canonical = root != null && _localStore != null ? File.ReadAllBytes(_localStore.FilePath) : null;
                var registry = ReadEditorRegistry();
                var stage = new EditorStage();
                InstallEditorState(CloneEditorState(original)); _editorStage = stage;
                try
                {
                    RemoveMember(Find(program, groupId)!, program, root, persistLocal, true);
                    var bytes = StorageSchema.Wrap(JsonSerializer.SerializeToNode(_clusters.Values)!, "hub.clusters", 2).ToJsonString();
#if VAULT_CONTRACT_TESTS
                    UnlinkDiskPhaseForTests?.Invoke("registry-save");
#endif
                    StorageSchema.AtomicWrite(Storage.ClustersPath, bytes);
                    if (File.ReadAllText(Storage.ClustersPath) != bytes) throw new IOException("Final shared snapshot was not saved");
                    _editorStage = null;
                    foreach (var frame in stage.Frames) foreach (var peer in frame.Recipients) _ = SendAsync(peer, frame.Frame);
                }
                catch (Exception commitError)
                {
                    var rollbackErrors = new List<Exception>();
                    try
                    {
                        if (canonical != null && _localStore != null && !SameBytes(File.ReadAllBytes(_localStore.FilePath), canonical))
                            _localStore.RestoreValidatedBytes(canonical);
                    }
                    catch (Exception ex) { rollbackErrors.Add(ex); }
                    try
                    {
                        if (!SameBytes(File.Exists(Storage.ClustersPath) ? File.ReadAllBytes(Storage.ClustersPath) : null, registry))
                        {
                            if (registry == null) File.Delete(Storage.ClustersPath); else AtomicRegistryBytes(registry);
                        }
                    }
                    catch (Exception ex) { rollbackErrors.Add(ex); }
                    InstallEditorState(original);
                    if (rollbackErrors.Count > 0)
                    {
                        _editorStorageHeld = true; _localStore?.HoldEditorWrites();
                        throw new AggregateException("unlink-storage-partial-failure; storage repair required", new[] { commitError }.Concat(rollbackErrors));
                    }
                    throw; // Preserve the existing writer's concrete failure contract.
                }
                finally { _editorStage = null; }
            }});
        if (refusal != null) return refusal;
        BroadcastClusters(); return null;
    }
#if VAULT_CONTRACT_TESTS
    internal Action<string>? UnlinkDiskPhaseForTests;
    internal Action<string>? LinkDiskPhaseForTests;
#endif
    // Keep store -> hub lock ordering, matching ReconcileLocal's adoption.
    // Snapshot the authoritative budget while the member is still attached.
    private void WithLocalStore(Action<JsonObject?, Action> action)
    {
        var store = _localStore;
        if (store == null) action(null, () => { });
        else store.WithLockedDocument((root, save) => { lock (_gate) RequireNoPartialStorage(); action(root, save); save(); });
    }
    private void RemoveMember(Cluster c, string program, JsonObject? root, Action persistLocal, bool requireLocalSnapshot)
    {
        var retainedBefore = c.DetachedRuleTime;
        RetainDetachedRuleTime(c, program);
        try {
        if (c.Members.TryGetValue(LocalProgram, out var localId)) {
            var localGroup = (root?["blockedGroups"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(g => Text(g["id"]) == localId);
            if (root != null && localGroup == null && requireLocalSnapshot) throw new InvalidDataException("Missing local group snapshot; link is preserved.");
            if (localGroup != null) {
                if(c.Contributed.Contains(LocalProgram) && root!["groupSnoozes"]?[localId] is JsonObject localSnooze && SnoozeChanged(localSnooze)>c.SnoozeTimestamp)
                    ApplySync(LocalProgram,localId,new() { ["snooze"]=localSnooze.DeepClone(),["snoozeTs"]=SnoozeChanged(localSnooze),["ts"]=0 });
                var detached = program == LocalProgram || c.Members.Count == 2;
                AdoptSharedBudget(root!, localId, c, true);
                AdoptSharedConfiguration(localGroup, c, detached);
                persistLocal();
            }
            _unlinkedLocal.Add(localId); _seenDefinitions.Remove(localId);
        }
        if(c.SharedRuleTime?["confirmed"] is JsonObject confirmed && c.Members.TryGetValue(program,out var oldId)) confirmed.Remove(program+"/"+oldId);
        c.Members.Remove(program); c.Contributed.Remove(program);
        if (c.Members.Keys.All(c.Contributed.Contains)) ClearJoiningUsage(c);
        if (c.Members.Count < 2) { c.Members.Clear(); if(c.DetachedRuleTime.Count == 0) _clusters.Remove(c.Id); }
        } catch { c.DetachedRuleTime = retainedBefore; throw; }
    }
    public static bool IsLocked(JsonObject obj) => obj["lockedAtMs"]?.GetValueKind() == JsonValueKind.Number;
    public void ApplySync(string program, string groupId, JsonObject frame)
    {
        lock (_gate) { RequireNoPartialStorage(); ApplySyncLocked(program, groupId, frame); BroadcastClusters(); }
    }
    // Caller holds the same gate from intent rebasing through commit/adoption.
    private void ApplySyncLocked(string program, string groupId, JsonObject frame)
    {
        var c = Find(program, groupId); if (c == null) return;
        var first = !c.Contributed.Contains(program); var carriesConfig = frame.ContainsKey("scalars") || frame.ContainsKey("scopes");
        var firstConfig = first && carriesConfig; var ts = Number(frame["ts"]); var priority = firstConfig && program == c.Initiator;
        // A repeated zero-timestamp original is a replay, not an acknowledged edit.
        var wins = !(!first && carriesConfig && ts == 0) && (priority || (firstConfig ? !c.Contributed.Contains(c.Initiator) : ts >= c.Timestamp));
        if (frame["scalars"] is JsonObject scalars && wins)
        {
            var budgetChanged = new[] { "mode", "allowedMinutes", "resetIntervalHours", "resetAtMidnight", "rollingLimit" }.Any(k => c.Scalars.ContainsKey(k) && c.Scalars[k]?.ToJsonString() != scalars[k]?.ToJsonString());
            c.Scalars = (JsonObject)scalars.DeepClone(); c.Timestamp = priority ? Math.Max(ts, c.Timestamp) + 1 : ts; c.Name = Text(c.Scalars["name"]);
            if (budgetChanged && !firstConfig) { c.Usage = 0; c.Anchor = DateTimeOffset.Now.ToUnixTimeMilliseconds(); c.Buckets.Clear(); c.UsageSeeded = c.BucketsSeeded = true; ResetJoiningUsage(c); }
        }
        if (frame["scopes"] is JsonArray scopes && (first || wins))
        {
            MergeScopes(c, program, groupId, scopes, first, priority);
        }
        if (frame["lock"] is JsonObject incoming && (c.Lock.Count == 0 || !first && Number(frame["lockBase"]) == Number(c.Lock["lockVersion"]) && Number(incoming["lockVersion"]) > Number(c.Lock["lockVersion"]))) c.Lock = (JsonObject)incoming.DeepClone();
        if (c.Anchor <= 0 && Number(frame["usageResetAtMs"]) > 0) c.Anchor = Number(frame["usageResetAtMs"]);
        Roll(c);
        var now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        var transferId=Text(frame["usageTransferId"]);
        var transferKey=transferId.Length is >0 and <=128 ? program+":"+transferId : null;
        var receiptLifetime=Math.Max(86_400_000,Math.Max(1,Number(c.Scalars["resetIntervalHours"]))*10_800_000);
        foreach(var expired in c.UsageTransferReceipts.Where(p=>p.Value<now-receiptLifetime).Select(p=>p.Key).ToList()) c.UsageTransferReceipts.Remove(expired);
        var duplicateTransfer=transferKey!=null && c.UsageTransferReceipts.ContainsKey(transferKey);
        var delta = Number(frame["usageDeltaMs"]);
        if (!duplicateTransfer && firstConfig && frame.ContainsKey("usageMs") && (c.JoiningSeedFloor <= 0 || Number(frame["usageResetAtMs"]) >= c.JoiningSeedFloor))
        {
            c.JoiningUsageBase = Math.Max(c.JoiningUsageBase ?? Math.Max(0,c.Usage-c.JoiningUsageDelta),Number(frame["usageMs"]));
            c.Usage = Math.Max(0,c.JoiningUsageBase.Value+c.JoiningUsageDelta);
            c.UsageSeeded=true;
        }
        if (!duplicateTransfer && delta != 0 && (!frame.ContainsKey("usageDeltaAnchorMs") || Number(frame["usageDeltaAnchorMs"]) == c.Anchor)) { if(c.Scalars["rollingLimit"]?.GetValueKind()!=JsonValueKind.True) CountBudgetSnooze(c,c.Usage,delta,now); c.Usage = Math.Max(0, c.Usage + delta); if(c.JoiningUsageBase.HasValue) c.JoiningUsageDelta+=delta; c.UsageSeeded = true; }
        else if (!duplicateTransfer && !c.UsageSeeded) c.Usage = Math.Max(c.Usage, Number(frame["usageMs"]));
        var rollingPolicy=new BlockGroup { ResetIntervalHours=Number(c.Scalars["resetIntervalHours"])>0 ? Number(c.Scalars["resetIntervalHours"]) : 24,ResetAtMidnight=c.Scalars["resetAtMidnight"]?.GetValueKind()==JsonValueKind.True };
        var rollingBefore=UsageBudget.UsedMs(UsageBudget.PruneBuckets(BucketValues(c.Buckets),rollingPolicy,now));
        if (!duplicateTransfer && frame["usageBuckets"] is JsonObject buckets)
        {
            if(c.Scalars["rollingLimit"]?.GetValueKind()==JsonValueKind.True)
                CountBudgetSnooze(c,rollingBefore,UsageBudget.UsedMs(UsageBudget.PruneBuckets(BucketValues(buckets),rollingPolicy,now)),now);
            foreach (var (k, v) in buckets) { c.Buckets[k] = Math.Max(0, Number(c.Buckets[k]) + Number(v)); if(c.JoiningBucketBase!=null) c.JoiningBucketDeltas[k]=Number(c.JoiningBucketDeltas[k])+Number(v); } c.BucketsSeeded = true;
        }
        if (!duplicateTransfer && frame["usageBucketsSeed"] is JsonObject seed)
        {
            if(firstConfig && (c.JoiningSeedFloor<=0 || Number(frame["usageResetAtMs"])>=c.JoiningSeedFloor))
            {
                c.JoiningBucketBase ??= (JsonObject)c.Buckets.DeepClone();
                foreach(var (k,v) in seed) { c.JoiningBucketBase[k]=Math.Max(Number(c.JoiningBucketBase[k]),Number(v)); c.Buckets[k]=Math.Max(0,Number(c.JoiningBucketBase[k])+Number(c.JoiningBucketDeltas[k])); }
                c.BucketsSeeded=true;
            }
            else if(!c.BucketsSeeded) foreach (var (k, v) in seed) c.Buckets[k] = Math.Max(Number(c.Buckets[k]), Number(v));
        }
        var cutoff = now - (Math.Max(Number(c.Scalars["resetIntervalHours"]), 24) * 3_600_000 + 60_000);
        foreach (var key in c.Buckets.Select(k => k.Key).Where(k => !double.TryParse(k, out var n) || n <= cutoff).ToList()) c.Buckets.Remove(key);
        foreach(var values in new[]{c.JoiningBucketBase,c.JoiningBucketDeltas}) if(values!=null) foreach(var key in values.Select(k=>k.Key).Where(k=>!double.TryParse(k,out var n) || n<=cutoff).ToList()) values.Remove(key);
        if (Number(frame["snoozeTs"]) > c.SnoozeTimestamp) { CountSnooze(c, now, true); c.SnoozeTimestamp = Number(frame["snoozeTs"]); c.Snooze = frame["snooze"] is JsonObject snooze ? (JsonObject)snooze.DeepClone() : new(); }
        CountSnooze(c, now, false);
        if(transferKey!=null) c.UsageTransferReceipts[transferKey]=now;
        if(carriesConfig) c.Contributed.Add(program);
        if(c.Members.Keys.All(c.Contributed.Contains)) ClearJoiningUsage(c);
        Persist();
    }

    private static double SnoozeChanged(JsonNode? snooze) => Number(snooze?["changedAtMs"])>0 ? Number(snooze?["changedAtMs"]) : Number(snooze?["startsAtMs"]);
    private static string ScopeKey(JsonObject line) => Text(line["entryID"]) is { Length: > 0 } entry ? entry : Text(line["surface"]) == "apps" ? "apps" : Text(line["platform"]) is { Length: > 0 } p ? p : "site";
    private static JsonObject? UnionOriginalTargets(JsonObject existing, JsonObject incoming)
    {
        var surface = Text(incoming["surface"]);
        if(surface != Text(existing["surface"]) || surface is not ("site" or "apps")) return null;
        var field = surface == "site" ? "sites" : "apps"; var except = field + "Except";
        if(existing[except]?.GetValueKind() == JsonValueKind.True || incoming[except]?.GetValueKind() == JsonValueKind.True) return null;
        if(existing[field] is not JsonArray previous || incoming[field] is not JsonArray added) return null;
        JsonObject? Metadata(JsonObject line) {
            if(Text(line["action"]) is not ("" or "block")) return null;
            var metadata=(JsonObject)line.DeepClone();metadata.Remove("id");metadata.Remove(field);metadata.Remove(except);metadata.Remove("action");
            if(surface=="site") metadata.Remove("entryID");
            if(metadata["platform"] == null) metadata.Remove("platform");return metadata;
        }
        var before=Metadata(existing);var after=Metadata(incoming);
        if(before == null || after == null || !JsonNode.DeepEquals(before, after)) return null;
        var result=(JsonObject)incoming.DeepClone();var targets=new JsonArray();var seen=new HashSet<string>();
        foreach(var target in previous.Concat(added)) {
            var key=field == "apps" && target is JsonObject app && Text(app["id"]).Length > 0 ? Text(app["id"]) : target?.ToJsonString() ?? "null";
            if(seen.Add(key)) targets.Add(target?.DeepClone());
        }
        result[field]=targets;return result;
    }
    // Only Website entries receive independent identities on an incompatible
    // first contribution. Platform and Apps entry ownership is unchanged.
    private static bool Website(JsonObject line) => Text(line["surface"]) == "site" && Text(line["platform"]).Length == 0 &&
        (ScopeKey(line) == "site" || Regex.IsMatch(ScopeKey(line), @"^site:[A-Za-z0-9_-]{1,80}$"));
    private static string Origin(string program, string groupId, string key) => program + "\0" + groupId + "\0" + key;
    private static JsonObject ComparableWebsite(JsonObject line)
    {
        var copy=(JsonObject)line.DeepClone(); copy.Remove("id"); copy.Remove("entryID");
        if(copy["platform"] == null) copy.Remove("platform");
        if(Text(copy["action"]).Length == 0) copy["action"]="block";
        if(copy["sitesExcept"] == null) copy["sitesExcept"]=false;
        if(copy["sites"] is JsonArray sites) copy["sites"]=new JsonArray(sites.Select(n=>n?.ToJsonString() ?? "null").Distinct().OrderBy(v=>v,StringComparer.Ordinal).Select(v=>JsonNode.Parse(v)).ToArray());
        return copy;
    }
    private static bool EquivalentWebsite(JsonObject a, JsonObject b) => JsonNode.DeepEquals(ComparableWebsite(a), ComparableWebsite(b));
    private static string WebsiteAlias(string seed, JsonObject entry, IEnumerable<JsonObject> lines, HashSet<string> reserved)
    {
        var prefix="site:linked_"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(seed))).ToLowerInvariant()[..24];
        var alias=prefix; var suffix=2;
        while(reserved.Contains(alias)) {
            var existing=lines.Where(s=>Website(s) && ScopeKey(s)==alias).ToList();
            if(existing.Count==1 && EquivalentWebsite(existing[0],entry)) return alias;
            alias=prefix+"_"+suffix++;
        }
        reserved.Add(alias); return alias;
    }
    private static void PruneOrigins(Cluster c)
    {
        var keys=c.Scopes.OfType<JsonObject>().Where(Website).Select(ScopeKey).ToHashSet();
        foreach(var key in c.ScopeOrigins.Keys.Where(k=>!keys.Contains(k)).ToList()) c.ScopeOrigins.Remove(key);
    }
    private static void MergeScopes(Cluster c, string program, string groupId, JsonArray scopes, bool first, bool priority)
    {
        var desktop=program==LocalProgram;
        var own=scopes.OfType<JsonObject>().Where(s=>(Text(s["surface"])=="apps")==desktop).Select(s=>(JsonObject)s.DeepClone()).ToList();
        var previous=c.Scopes.OfType<JsonObject>().Select(s=>(JsonObject)s.DeepClone()).ToList();
        var origins=new Dictionary<string,string>(c.ScopeOrigins);
        var reserved=previous.Concat(own).Select(ScopeKey).ToHashSet();
        var merged=previous.Where(s=>(Text(s["surface"])=="apps")!=desktop || first && !own.Any(i=>ScopeKey(i)==ScopeKey(s))).ToList();
        if(first) {
            // Restore Website collisions removed by the original one-owner merge.
            foreach(var old in previous.Where(Website)) if(!desktop && !merged.Contains(old)) merged.Add(old);
            foreach(var incoming in own) {
                if(!Website(incoming) || desktop) {
                    var matches=previous.Where(s=>ScopeKey(s)==ScopeKey(incoming)).ToList();
                    merged.Add(matches.Count==1 && own.Count(s=>ScopeKey(s)==ScopeKey(incoming))==1 ? UnionOriginalTargets(matches[0],incoming) ?? incoming : incoming);
                    continue;
                }
                var key=ScopeKey(incoming); var existing=merged.Where(s=>Website(s) && ScopeKey(s)==key).ToList();
                var seed=Origin(program,groupId,key);
                if(existing.Count==0) { merged.Add(incoming); if(!origins.ContainsKey(key)) origins[key]=seed; continue; }
                var retained=existing[0];
                var union=existing.Count==1 ? UnionOriginalTargets(retained,incoming) : null;
                if(existing.Count==1 && (EquivalentWebsite(retained,incoming) || union!=null)) {
                    merged.Remove(retained); merged.Add(union ?? retained);
                    if(!origins.ContainsKey(key) || priority && key=="site") origins[key]=seed;
                    continue;
                }
                if(priority && key=="site") {
                    foreach(var old in existing) {
                        var known=origins.GetValueOrDefault(key);
                        var alias=WebsiteAlias(known ?? "retained\0"+c.Id+"\0"+key,old,merged,reserved);
                        if(merged.Any(s=>ScopeKey(s)==alias)) merged.Remove(old); else old["entryID"]=alias;
                        if(known!=null && !origins.ContainsKey(alias)) origins[alias]=known;
                    }
                    origins.Remove(key); merged.Add(incoming); origins[key]=seed;
                } else {
                    var alias=WebsiteAlias(seed,incoming,merged,reserved);
                    if(!merged.Any(s=>ScopeKey(s)==alias)) { incoming["entryID"]=alias; merged.Add(incoming); origins[alias]=seed; }
                }
            }
        } else {
            merged.AddRange(own);
            foreach(var line in own.Where(Website)) { var key=ScopeKey(line); if(!origins.ContainsKey(key)) origins[key]=Origin(program,groupId,key); }
        }
        var counts=new Dictionary<string,int>(); var result=new JsonArray();
        foreach(var line in merged) { var surface=Text(line["surface"]);counts[surface]=counts.GetValueOrDefault(surface)+1;line["id"]=$"{surface}-{counts[surface]}";result.Add(line); }
        c.Scopes=result; c.ScopeOrigins=origins; PruneOrigins(c);
    }
    private static bool ValidScopeOrigin(string key, JsonNode? node)
    {
        if(key!="site" && !Regex.IsMatch(key,@"^site:[A-Za-z0-9_-]{1,80}$") || node?.GetValueKind()!=JsonValueKind.String) return false;
        var parts=node!.GetValue<string>().Split('\0');
        return parts.Length==3 && parts.All(p=>p.Length is >0 and <=128) && parts[0] is "chrome" or "edge" or "safari";
    }
    private static JsonNode RegistryPayload(JsonNode document)
    {
        var payload=StorageSchema.Payload(document,"hub.clusters",2);
        if(payload is not JsonArray entries) throw new InvalidDataException("Invalid cluster registry.");
        foreach(var entry in entries.OfType<JsonObject>()) if(entry.ContainsKey("scopeOrigins")) {
            if(entry["scopeOrigins"] is not JsonObject origins || origins.Any(p=>!ValidScopeOrigin(p.Key,p.Value)))
                throw new InvalidDataException("Invalid Website origin map; registry is preserved.");
        }
        return payload;
    }
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
        if (start == c.Anchor) return; c.Anchor = start; c.Usage = 0; c.UsageSeeded = true; ResetJoiningUsage(c);
    }
    private static void BeginJoiningUsage(Cluster c) { c.JoiningUsageBase=c.Usage; c.JoiningUsageDelta=0; c.JoiningBucketBase=(JsonObject)c.Buckets.DeepClone(); c.JoiningBucketDeltas=new(); c.JoiningSeedFloor=0; }
    private static void ClearJoiningUsage(Cluster c) { c.JoiningUsageBase=null; c.JoiningUsageDelta=0; c.JoiningBucketBase=null; c.JoiningBucketDeltas=new(); c.JoiningSeedFloor=0; }
    private static void ResetJoiningUsage(Cluster c) { if(c.JoiningUsageBase.HasValue) { BeginJoiningUsage(c); c.JoiningSeedFloor=c.Anchor; } }
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
        AttachLocalStore(store);
        store.WithLockedDocument((document, save) => { lock (_gate) ReconcileLocalLocked(document, save); });
    }
    private void ReconcileLocalLocked(JsonObject document, Action save)
    {
            RequireNoPartialStorage();
            var groups = document["blockedGroups"] as JsonArray ?? new();
            var roster = new JsonArray(groups.OfType<JsonObject>().Select(g => (JsonNode)new JsonObject { ["id"] = Text(g["id"]), ["name"] = Text(g["name"]), ["frozen"] = IsLocked(g),
                ["timeState"] = document["cbRuleState"]?[Text(g["id"])]?["$vaultTime"]?.DeepClone() }).ToArray());
            if (!_rosters.TryGetValue(LocalProgram, out var oldRoster) || !JsonNode.DeepEquals(oldRoster, roster))
                SetRosterLocked(LocalProgram, roster, document, save);
            foreach (var group in groups.OfType<JsonObject>())
            {
                var gid = Text(group["id"]); var c = Find(LocalProgram, gid); if (c == null) continue;
                var joined = c.Contributed.Contains(LocalProgram);
                if (joined && document["groupSnoozes"]?[gid] is JsonObject snooze && SnoozeChanged(snooze) > c.SnoozeTimestamp)
                    ApplySyncLocked(LocalProgram, gid, new() { ["snooze"] = snooze.DeepClone(), ["snoozeTs"] = SnoozeChanged(snooze), ["ts"] = 0 });
                var raw = LocalDefinition.Read(group);
                var previous = _seenDefinitions.GetValueOrDefault(gid);
                // Record only actual observed bytes here. An adoption acknowledgment
                // is separate and occurs strictly after the throwing save below.
                _seenDefinitions[gid] = raw;
                if (joined && previous == null) continue;
                var scalars = joined ? (JsonObject)c.Scalars.DeepClone() : (JsonObject)raw.Scalars.DeepClone();
                var changed = !joined;
                if (joined) foreach (var field in ScalarFields)
                {
                    if (raw.Scalars.ContainsKey(field) == previous!.Scalars.ContainsKey(field) && JsonNode.DeepEquals(raw.Scalars[field], previous.Scalars[field])) continue;
                    changed = true;
                    if (raw.Scalars.ContainsKey(field)) scalars[field] = raw.Scalars[field]?.DeepClone(); else scalars.Remove(field);
                }
                JsonArray Apps(JsonArray lines) => new(lines.OfType<JsonObject>().Where(l => Text(l["surface"]) == "apps").Select(l => l.DeepClone()).ToArray());
                var apps = Apps(raw.Scopes);
                if (joined && JsonNode.DeepEquals(apps, Apps(previous!.Scopes))) apps = Apps(c.Scopes); else changed = true;
                var lockChanged = !joined || !JsonNode.DeepEquals(raw.Lock, previous!.Lock);
                if (lockChanged && raw.Lock.Count > 0) changed = true;
                if (!changed) continue;
                var frame = new JsonObject { ["scalars"] = scalars, ["scopes"] = apps, ["ts"] = joined ? DateTimeOffset.Now.ToUnixTimeMilliseconds() : 0 };
                if (!joined)
                {
                    frame["usageMs"] = document["usageTimersMs"]?[gid]?.DeepClone() ?? JsonValue.Create(0);
                    frame["usageResetAtMs"] = document["usageResetAtMs"]?[gid]?.DeepClone() ?? JsonValue.Create(DateTimeOffset.Now.ToUnixTimeMilliseconds());
                    if (document["usageBucketsMs"]?[gid] is JsonObject originalBuckets) frame["usageBucketsSeed"] = originalBuckets.DeepClone();
                }
                if (lockChanged && raw.Lock.Count > 0) { frame["lock"] = raw.Lock.DeepClone(); frame["lockBase"] = Number(group["lockSyncedVersion"]); }
                ApplySyncLocked(LocalProgram, gid, frame);
            }
#if VAULT_CONTRACT_TESTS
            LocalReconciliationPhaseForTests?.Invoke("before-adoption");
#endif
            var detached = new List<string>();
            foreach (var group in groups.OfType<JsonObject>())
            {
                var gid = Text(group["id"]); var c = Find(LocalProgram, gid);
                if (c == null)
                {
                    if (_unlinkedLocal.Contains(gid)) { detached.Add(gid); group["scopes"] = new JsonArray((group["scopes"] as JsonArray ?? new()).OfType<JsonObject>().Where(l => Text(l["surface"]) == "apps").Select(l => l.DeepClone()).ToArray()); }
                    continue;
                }
                if (c.Contributed.Contains(LocalProgram))
                {
                    AdoptSharedBudget(document, gid, c);
                    foreach (var field in ScalarFields) group.Remove(field);
                    foreach (var (key, value) in c.Scalars) group[key] = value?.DeepClone();
                    if (c.Lock.Count > 0) { foreach (var field in LockFields) group.Remove(field); foreach (var (key, value) in c.Lock) group[key] = value?.DeepClone(); group["lockSyncedVersion"] = c.Lock["lockVersion"]?.DeepClone(); }
                }
                if (c.Members.Keys.All(c.Contributed.Contains)) group["scopes"] = c.Scopes.DeepClone();
            }
            save();
            foreach (var gid in detached) _unlinkedLocal.Remove(gid);
            foreach (var group in groups.OfType<JsonObject>())
                if (Find(LocalProgram, Text(group["id"]))?.Contributed.Contains(LocalProgram) == true) _seenDefinitions[Text(group["id"])] = LocalDefinition.Read(group);
            BroadcastRosters(); BroadcastClusters();
    }
    private static void AdoptSharedConfiguration(JsonObject group, Cluster c, bool detached)
    {
        foreach(var (key, value) in c.Scalars) group[key] = value?.DeepClone();
        if(c.Lock.Count > 0 && Number(c.Lock["lockVersion"]) >= Number(group["lockVersion"])) {
            foreach(var field in LockFields) group.Remove(field);
            foreach(var (key, value) in c.Lock) group[key] = value?.DeepClone();
            group["lockSyncedVersion"] = c.Lock["lockVersion"]?.DeepClone();
        }
        if(c.Members.Keys.All(c.Contributed.Contains)) group["scopes"] = c.Scopes.DeepClone();
        if(detached && group["scopes"] is JsonArray scopes) group["scopes"] = new JsonArray(scopes.OfType<JsonObject>().Where(scope => Text(scope["surface"]) == "apps").Select(scope => scope.DeepClone()).ToArray());
    }
    private static void AdoptSharedBudget(JsonObject root, string gid, Cluster c, bool final = false)
    {
        Roll(c);
        var rolling = c.Scalars["rollingLimit"]?.GetValueKind() == JsonValueKind.True;
        var policy = new BlockGroup { ResetIntervalHours = Number(c.Scalars["resetIntervalHours"]), ResetAtMidnight = c.Scalars["resetAtMidnight"]?.GetValueKind() == JsonValueKind.True };
        var history = rolling ? UsageBudget.PruneBuckets(BucketValues(c.Buckets), policy, DateTimeOffset.Now.ToUnixTimeMilliseconds()) : BucketValues(c.Buckets);
        var timers = root["usageTimersMs"] as JsonObject ?? new(); root["usageTimersMs"] = timers; timers[gid] = rolling ? UsageBudget.UsedMs(history) : c.Usage;
        if (c.Anchor > 0) { var resets = root["usageResetAtMs"] as JsonObject ?? new(); root["usageResetAtMs"] = resets; resets[gid] = c.Anchor; }
        var buckets = root["usageBucketsMs"] as JsonObject ?? new(); root["usageBucketsMs"] = buckets; buckets[gid] = JsonSerializer.SerializeToNode(WebStore.BucketJson(history));
        if(final || c.SnoozeTimestamp>0 && c.SnoozeTimestamp>=SnoozeChanged(root["groupSnoozes"]?[gid])) {
            var snoozes=root["groupSnoozes"] as JsonObject ?? new(); root["groupSnoozes"]=snoozes;
            if(c.Snooze.Count>0) snoozes[gid]=c.Snooze.DeepClone(); else snoozes.Remove(gid);
            var totals=root["groupSnoozeTotalsMs"] as JsonObject ?? new(); root["groupSnoozeTotalsMs"]=totals; totals[gid]=c.SnoozeTotal;
        }
    }
    private JsonObject Snapshot(Cluster c)
    {
        var peers = _peers.Values.Where(p => p.Connected).Select(p => p.Program).Append(LocalProgram).ToHashSet();
        var snapshot = new JsonObject { ["id"] = c.Id, ["groupName"] = c.Name, ["allOnline"] = c.Members.Keys.All(peers.Contains), ["members"] = new JsonArray(c.Members.Select(m => (JsonNode)new JsonObject { ["program"] = m.Key, ["groupId"] = m.Value, ["groupName"] = c.Name, ["online"] = peers.Contains(m.Key), ["contributed"] = c.Contributed.Contains(m.Key) }).ToArray()), ["shared"] = new JsonObject { ["scalars"] = c.Scalars.DeepClone(), ["scopes"] = c.Scopes.DeepClone(), ["lock"] = c.Lock.DeepClone(), ["ts"] = c.Timestamp, ["usageMs"] = c.Usage, ["usageResetAtMs"] = c.Anchor, ["usageBuckets"] = c.Buckets.DeepClone(), ["snooze"] = c.Snooze.DeepClone(), ["snoozeTs"] = c.SnoozeTimestamp, ["snoozeTotalMs"] = c.SnoozeTotal } };
        // Joining participants must contribute their original owned lines before
        // anybody adopts the union; publishing Apps-only here erases Websites.
        if(!c.Members.Keys.All(c.Contributed.Contains)) ((JsonObject)snapshot["shared"]!).Remove("scopes");
        ((JsonObject)snapshot["shared"]!)["usageTransferReceipts"]=JsonSerializer.SerializeToNode(c.UsageTransferReceipts);
        ((JsonObject)snapshot["shared"]!)["ruleTime"]=c.SharedRuleTime?.DeepClone();
        return snapshot;
    }
    private JsonObject ClustersFrame() => new() { ["kind"]="clusters",["clusters"]=new JsonArray(_clusters.Values.Where(c => c.Members.Count >= 2).Select(c => (JsonNode)Snapshot(c)).ToArray()),["rosters"]=new JsonObject(_rosters.Select(r=>new KeyValuePair<string,JsonNode?>(r.Key,r.Value.DeepClone()))) };
    public string ClustersJson() { lock (_gate) { var frame=ClustersFrame();frame.Remove("kind");return frame.ToJsonString(); } }
#if VAULT_CONTRACT_TESTS
    internal Action<string>? ClusterSnapshotCapturedForTests;
    internal Action<string>? LocalReconciliationPhaseForTests;
#endif
    private void BroadcastClusters()
    {
        lock(_gate)
        {
            foreach(var c in _clusters.Values.Where(c => c.Members.Count >= 2)) Broadcast(new() { ["kind"]="cluster-updated",["cluster"]=Snapshot(c) });
            var frame=ClustersFrame();
#if VAULT_CONTRACT_TESTS
            ClusterSnapshotCapturedForTests?.Invoke("broadcast");
#endif
            Broadcast(frame);
        }
    }
    private bool _clusterStorageWritable = true;
    private void Persist()
    {
        if (_editorStage != null || _editorStorageHeld) return;
        if (!_clusterStorageWritable) return;
        try {
            if (File.Exists(Storage.ClustersPath)) RegistryPayload(JsonNode.Parse(File.ReadAllText(Storage.ClustersPath))!);
            StorageSchema.AtomicWrite(Storage.ClustersPath, StorageSchema.Wrap(JsonSerializer.SerializeToNode(_clusters.Values)!, "hub.clusters",2).ToJsonString());
        } catch { _clusterStorageWritable = false; }
    }
    private void Restore()
    {
        lock (_gate)
        {
            if (_clusters.Count > 0 || !File.Exists(Storage.ClustersPath)) return;
            try
            {
                var rows = RegistryPayload(JsonNode.Parse(File.ReadAllText(Storage.ClustersPath))!).Deserialize<List<Cluster>>() ?? [];
                // A receipt must never acknowledge/discard an unsupported final
                // namespace. Validate retained envelopes before installing any
                // restored row; unknown bytes remain untouched and read-only.
                if (rows.Sum(c => c.DetachedRuleTime.Count) > 1024 ||
                    Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(rows.Select(c => c.DetachedRuleTime))) > 8 * 1024 * 1024 ||
                    rows.Any(c => c.DetachedRuleTime.Count > 16 || c.DetachedRuleTime.Values.Any(record =>
                        Number(record["version"]) != 1 || record["state"] is not JsonObject state || Number(state["version"]) != 1 || state["objects"] is not JsonObject ||
                        Text(record["token"]).Length == 0 || Text(record["initiator"]).Length == 0)))
                    throw new InvalidDataException("Unsupported final time snapshots");
                foreach (var c in rows)
                {
                    if ((c.Members.Count < 2 && c.DetachedRuleTime.Count == 0) ||
                        !c.Members.All(m => m.Value.Length > 0 && LocalHubAuthentication.Programs.Contains(m.Key))) continue;
                    PruneOrigins(c);
                    if (c.Members.Keys.All(c.Contributed.Contains)) ClearJoiningUsage(c);
                    else if (!c.JoiningUsageBase.HasValue) BeginJoiningUsage(c);
                    _clusters[c.Id] = c;
                }
            }
            catch { _clusterStorageWritable = false; }
        }
    }
    private static double Number(JsonNode? node) => node?.GetValueKind() == JsonValueKind.Number && double.TryParse(node.ToJsonString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? value : 0;
    private static string Text(JsonNode? node) => node?.GetValueKind() == JsonValueKind.String ? node.GetValue<string>() : "";
}
