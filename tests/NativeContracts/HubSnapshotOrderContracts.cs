using System.Collections;
using System.Net.WebSockets;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json.Nodes;
using WindowsBlocker.Bridge;

static class HubSnapshotOrderContracts
{
    static readonly BindingFlags Private=BindingFlags.NonPublic|BindingFlags.Instance;
    static void Check(bool value,string label) { if(!value)throw new Exception(label);Console.WriteLine("PASS "+label); }
    class RecordingSocket : WebSocket
    {
        readonly object gate=new();readonly List<JsonObject> frames=[];
        public Task? BlockFirst;
        public WebSocketCloseStatus? NormalClose;
        public override WebSocketState State=>WebSocketState.Open;
        public override WebSocketCloseStatus? CloseStatus=>null;
        public override string? CloseStatusDescription=>null;
        public override string? SubProtocol=>null;
        public override void Abort() {}
        public override void Dispose() {}
        public override Task CloseAsync(WebSocketCloseStatus status,string? description,CancellationToken token)=>Task.CompletedTask;
        public override Task CloseOutputAsync(WebSocketCloseStatus status,string? description,CancellationToken token){NormalClose=status;return Task.CompletedTask;}
        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer,CancellationToken token)=>throw new NotSupportedException();
        public override async Task SendAsync(ArraySegment<byte> buffer,WebSocketMessageType type,bool end,CancellationToken token)
        {
            if(BlockFirst is { } blocked){BlockFirst=null;await blocked;}
            lock(gate)frames.Add((JsonObject)JsonNode.Parse(Encoding.UTF8.GetString(buffer.AsSpan()))!);
        }
        public JsonObject[] Read() { lock(gate)return frames.Select(f=>(JsonObject)f.DeepClone()).ToArray(); }
    }
    sealed class SynchronousFailureSocket : RecordingSocket
    {
        public override Task SendAsync(ArraySegment<byte> buffer,WebSocketMessageType type,bool end,CancellationToken token)=>throw new WebSocketException("Controlled synchronous socket failure");
    }
    static object AddPeer(ConnectionHub hub,RecordingSocket socket,string program="chrome")
    {
        var type=typeof(ConnectionHub).GetNestedType("Peer",BindingFlags.NonPublic)!;
        var peer=Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,[socket],null)!;
        type.GetField("Program")!.SetValue(peer,program);type.GetField("Connected")!.SetValue(peer,true);
        ((IDictionary)typeof(ConnectionHub).GetField("_peers",Private)!.GetValue(hub)!).Add(type.GetField("Id")!.GetValue(peer)!,peer);
        return peer;
    }
    static JsonObject Site(string target,bool except=false,string? key=null)
    {
        var line=new JsonObject{["surface"]="site",["sites"]=new JsonArray(target),["sitesExcept"]=except,["action"]="block"};
        if(key!=null)line["entryID"]=key;return line;
    }
    static JsonObject Frame(JsonArray scopes,double timestamp)=>new(){["scopes"]=scopes,["scalars"]=new JsonObject{["name"]="Ordering fixture",["enabled"]=true,["mode"]="instant"},["ts"]=timestamp};
    static ConnectionHub Seed()
    {
        var hub=new ConnectionHub();
        foreach(var (p,g) in new[]{("chrome","c"),("edge","e"),("windowsapp","w")})hub.SetRoster(p,new JsonArray(new JsonObject{["id"]=g,["frozen"]=false}));
        hub.Link("chrome","c","edge","e");hub.Link("chrome","c","windowsapp","w");
        hub.ApplySync("chrome","c",Frame(new JsonArray(Site("safe.example")),1));
        hub.ApplySync("edge","e",Frame(new JsonArray(Site("safe.example")),1));
        hub.ApplySync("windowsapp","w",Frame(new JsonArray(new JsonObject{["surface"]="apps",["apps"]=new JsonArray()}),1));
        return hub;
    }
    static async Task Drain(object peer)
    {
        if(peer.GetType().GetField("SendTail")?.GetValue(peer) is Task tail)await tail.WaitAsync(TimeSpan.FromSeconds(5));
        else await Task.Delay(300);
    }
    public static async Task RunActivitySettings()
    {
        var hub = new ConnectionHub();
        var chrome = new RecordingSocket(); var chromePeer = AddPeer(hub, chrome);
        var edge = new RecordingSocket(); var edgePeer = AddPeer(hub, edge, "edge");
        var safari = new RecordingSocket(); var safariPeer = AddPeer(hub, safari, "safari");
        var classifier = new RecordingSocket(); var classifierPeer = AddPeer(hub, classifier, "classifier");
        var waiting = new RecordingSocket(); var waitingPeer = AddPeer(hub, waiting);
        waitingPeer.GetType().GetField("Connected")!.SetValue(waitingPeer, false);
        var epoch = Guid.NewGuid().ToString();
        JsonObject Commit(long revision) => new() { ["event"] = "activity-settings-committed", ["value"] = new JsonObject {
            ["settings"] = new JsonObject { ["byCategory"] = new JsonObject { ["web-visit"] = new JsonObject { ["enabled"] = false } }, ["idleThresholdSeconds"] = 60 },
            ["epoch"] = epoch, ["revision"] = revision, ["changedAtMs"] = 1_750_000_000_000.0 } };
        var getResponse = (JsonObject)Commit(0)["value"]!.DeepClone(); getResponse["kind"] = "settings";
        var getBody = ConnectionHub.ActivitySettingsResponse(getResponse);
        Check(getBody["epoch"]?.GetValue<string>() == epoch && getBody["revision"]?.GetValue<long>() == 0 && getBody["kind"] == null,
            "Settings GET preserves authoritative metadata without the private response wrapper");
        getResponse["settings"]!["byCategory"]!["web-visit"]!["enabled"] = true;
        Check(getBody["settings"]?["byCategory"]?["web-visit"]?["enabled"]?.GetValue<bool>() == false, "Settings reply captures worker snapshot rather than mutable intent");
        getResponse.Remove("revision");
        var invalidReply = false;
        try { ConnectionHub.ActivitySettingsResponse(getResponse); } catch(InvalidDataException) { invalidReply = true; }
        Check(invalidReply, "Missing worker response metadata is explicit failure rather than empty settings success");
        var recordResponse = new JsonObject { ["kind"] = "recorded", ["accepted"] = 1L,
            ["acceptedIDs"] = new JsonArray("new", "persisted-duplicate"), ["rejectedIDs"] = new JsonArray("expired"), ["pendingIDs"] = new JsonArray("retry-write") };
        var recordBody = ConnectionHub.ActivityRecordResponse(recordResponse);
        recordResponse["pendingIDs"]!.AsArray().Clear();
        Check(recordBody["stored"]?.GetValue<long>() == 1 && recordBody["acceptedIDs"]?.AsArray().Count == 2 &&
            recordBody["rejectedIDs"]?[0]?.GetValue<string>() == "expired" && recordBody["pendingIDs"]?[0]?.GetValue<string>() == "retry-write",
            "Record route preserves stored count and all exact durable/rejected/pending receipts");
        recordResponse.Remove("acceptedIDs");
        var invalidRecord = false;
        try { ConnectionHub.ActivityRecordResponse(recordResponse); } catch(InvalidDataException) { invalidRecord = true; }
        Check(invalidRecord, "Missing record receipt cannot become a successful zero-count response");
        Check(!hub.PublishWorkerActivitySettings(Commit(1)), "Unqualified worker cannot publish settings or advertise support");
        hub.ActivitySettingsNotificationsSupported = () => true;
        var commit = Commit(1);
        Check(hub.PublishWorkerActivitySettings(commit), "Successful own worker commit admitted");
        commit["value"]!["settings"]!["byCategory"]!["web-visit"]!["enabled"] = true;
        await Task.WhenAll(Drain(chromePeer), Drain(edgePeer), Drain(safariPeer), Drain(classifierPeer), Drain(waitingPeer));
        Check(new[] { chrome, edge, safari }.All(s => s.Read().Single()["kind"]?.GetValue<string>() == "activity-settings-updated" &&
            s.Read().Single()["body"]?["settings"]?["byCategory"]?["web-visit"]?["enabled"]?.GetValue<bool>() == false),
            "All authenticated browsers including origin receive captured committed snapshot");
        Check(classifier.Read().Length == 0 && waiting.Read().Length == 0, "Classifier and unauthenticated peers receive no Activity settings");
        Check(!hub.PublishWorkerActivitySettings(Commit(0)) && !hub.PublishWorkerActivitySettings(Commit(1)), "Older and duplicate same-epoch commits cannot overwrite current settings");
        foreach (var field in new[] { "epoch", "revision", "changedAtMs", "settings" })
        {
            var invalid = Commit(2); invalid["value"]!.AsObject().Remove(field);
            Check(!hub.PublishWorkerActivitySettings(invalid), "Missing " + field + " refuses native settings event");
        }
        foreach (var invalidRevision in new JsonNode?[] { JsonValue.Create(-1), JsonValue.Create(9_007_199_254_740_992L), JsonValue.Create(1.5), JsonValue.Create("2") })
        {
            var invalid = Commit(2); invalid["value"]!["revision"] = invalidRevision;
            Check(!hub.PublishWorkerActivitySettings(invalid), "Revision must be a nonnegative JavaScript-safe integer");
        }
        var wrongEpoch = Commit(2); wrongEpoch["value"]!["epoch"] = "not-a-store-uuid";
        var wrongTime = Commit(2); wrongTime["value"]!["changedAtMs"] = -1.0;
        var oversized = Commit(2); oversized["value"]!["settings"]!["unexpected"] = new string('x', 88_001);
        Check(!hub.PublishWorkerActivitySettings(wrongEpoch) && !hub.PublishWorkerActivitySettings(wrongTime) && !hub.PublishWorkerActivitySettings(oversized), "Malformed epoch, negative time and oversized snapshots fail closed");
        var client = Commit(2); client.Remove("event"); client["kind"] = "activity-settings-updated";
        Check(!hub.PublishWorkerActivitySettings(client), "Client-originated settings kind cannot enter worker-only publisher");
        var classifierEvent = Commit(2); classifierEvent["event"] = "broadcast"; classifierEvent["operation"] = "activity-settings-updated";
        hub.BroadcastClassifier(classifierEvent);
        Check(!hub.PublishWorkerActivitySettings(classifierEvent), "Classifier whitelist does not authorize Activity settings commits");
        Check(hub.PublishWorkerActivitySettings(Commit(2)), "Newer commit follows older snapshot in peer FIFO");
        var restarted = Commit(0); restarted["value"]!["epoch"] = Guid.NewGuid().ToString();
        Check(hub.PublishWorkerActivitySettings(restarted), "New store epoch may begin at revision zero");
        await Drain(chromePeer);
        Check(chrome.Read().Select(f => f["body"]!["revision"]!.GetValue<long>()).SequenceEqual(new long[] { 1, 2, 0 }), "Native commit generation enqueue preserves pipe order");
        var welcomeSocket = new RecordingSocket(); var welcomePeer = AddPeer(hub, welcomeSocket, "edge");
        await (Task)typeof(ConnectionHub).GetMethod("QueueWelcome", Private)!.Invoke(hub, [welcomePeer])!;
        Check(welcomeSocket.Read().First()["activitySettingsNotifications"]?.GetValue<bool>() == true, "Supporting worker advertises additive v4 welcome capability");
        hub.ActivitySettingsNotificationsSupported = () => false;
        var unsupported = new RecordingSocket(); var unsupportedPeer = AddPeer(hub, unsupported, "safari");
        await (Task)typeof(ConnectionHub).GetMethod("QueueWelcome", Private)!.Invoke(hub, [unsupportedPeer])!;
        Check(unsupported.Read().First()["activitySettingsNotifications"]?.GetValue<bool>() == false && !hub.PublishWorkerActivitySettings(Commit(3)), "Unsupported worker neither advertises nor publishes notifications");
        var failing = new ConnectionHub { ActivitySettingsNotificationsSupported = () => true };
        var failedPeer = AddPeer(failing, new SynchronousFailureSocket());
        Check(failing.PublishWorkerActivitySettings(Commit(1)), "Native enqueue accepts committed snapshot before asynchronous socket failure");
        await Drain(failedPeer);
        Check(((IDictionary)typeof(ConnectionHub).GetField("_peers", Private)!.GetValue(failing)!).Count == 0, "Failed Activity send removes peer without lock inversion");
        var reconnectHub = new ConnectionHub { ActivitySettingsNotificationsSupported = () => true };
        var reconnectSocket = new RecordingSocket(); var reconnectPeer = AddPeer(reconnectHub, reconnectSocket);
        var retainedSocket = new RecordingSocket(); var retainedPeer = AddPeer(reconnectHub, retainedSocket, "classifier");
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        reconnectSocket.BlockFirst = release.Task;
        reconnectHub.PublishWorkerActivitySettings(Commit(1));
        var disconnected = reconnectHub.DisconnectActivityBrowsersAsync();
        Check(reconnectSocket.NormalClose == null && !disconnected.IsCompleted, "Worker reconnect normal close waits behind admitted snapshot send");
        Check(!reconnectPeer.GetType().GetField("Connected")!.GetValue(reconnectPeer)!.Equals(true), "Reconnect immediately prevents stale authenticated peer delivery");
        release.SetResult(); await disconnected.WaitAsync(TimeSpan.FromSeconds(5)); await Drain(retainedPeer);
        Check(reconnectSocket.NormalClose == WebSocketCloseStatus.NormalClosure && retainedSocket.NormalClose == null &&
            ((IDictionary)typeof(ConnectionHub).GetField("_peers", Private)!.GetValue(reconnectHub)!).Count == 1,
            "Worker change normally closes only browser peers and keeps Hub/nonbrowser ownership");
        var freshSocket = new RecordingSocket(); var freshPeer = AddPeer(reconnectHub, freshSocket);
        await (Task)typeof(ConnectionHub).GetMethod("QueueWelcome", Private)!.Invoke(reconnectHub, [freshPeer])!;
        Check(freshSocket.Read().First()["activitySettingsNotifications"]?.GetValue<bool>() == true, "Reauthenticated browser receives current capability in fresh welcome");
    }
    public static async Task Run(string? appAssembly=null)
    {
        foreach(var phase in new[]{"broadcast","welcome"})
        {
            Environment.SetEnvironmentVariable("VAULT_STORAGE_ROOT",Path.Combine(Path.GetTempPath(),"vault-order-"+Guid.NewGuid()));
            var hub=Seed();var socket=new RecordingSocket();var peer=AddPeer(hub,socket);
            var observer=new RecordingSocket();var observerPeer=AddPeer(hub,observer,"edge");
            using var captured=new ManualResetEventSlim();using var release=new ManualResetEventSlim();var entered=0;
            hub.ClusterSnapshotCapturedForTests=kind=>{
                if(kind==phase && Interlocked.Increment(ref entered)==1){captured.Set();if(!release.Wait(TimeSpan.FromSeconds(5)))throw new Exception("Snapshot barrier timed out");}
            };
            var old=Task.Run(async()=>{
                var method=typeof(ConnectionHub).GetMethod(phase=="broadcast"?"BroadcastClusters":"QueueWelcome",Private)!;
                if(method.Invoke(hub,phase=="welcome"?[peer]:null) is Task task)await task;
            });
            Check(captured.Wait(TimeSpan.FromSeconds(5)),"Actual "+phase+" snapshot reaches deterministic capture barrier");
            // Avoid reading the hub while its capture worker holds the gate.
            var scopes=new JsonArray(new JsonObject{["surface"]="apps",["apps"]=new JsonArray()},Site("safe.example"),Site("also-safe.example",true,"site:linked_fixture"));
            using var started=new ManualResetEventSlim();
            var update=Task.Run(()=>{started.Set();hub.ApplySync("chrome","c",Frame(scopes,2));});
            started.Wait();var overtook=await Task.WhenAny(update,Task.Delay(150))==update;
            release.Set();await Task.WhenAll(old,update).WaitAsync(TimeSpan.FromSeconds(5));await Drain(peer);
            var frames=socket.Read().Where(f=>f["kind"]?.GetValue<string>()=="clusters").ToArray();
            Check(frames.Length>=2 && frames[^1]["clusters"]![0]!["shared"]!["scopes"]!.AsArray().Count==3,
                "Captured older "+phase+" aggregate cannot overwrite newer independent Website entries");
            Check(!overtook,"Newer policy mutation cannot overtake "+phase+" capture and enqueue");
            if(phase=="welcome")Check(socket.Read()[0]["kind"]!.GetValue<string>()=="welcome","Welcome is queued before initial and later cluster frames");
            hub.ClusterSnapshotCapturedForTests=null;
            var before=frames[^1]["clusters"]![0]!["shared"]!["ts"]!.GetValue<double>();
            hub.ApplySync("chrome","c",new(){["usageDeltaMs"]=100});
            hub.SetRoster("edge",new JsonArray(new JsonObject{["id"]="e",["frozen"]=false},new JsonObject{["id"]="new-roster-item"}));await Drain(peer);
            var latest=socket.Read().Last(f=>f["kind"]?.GetValue<string>()=="clusters");
            Check(latest["clusters"]![0]!["shared"]!["ts"]!.GetValue<double>()==before &&
                latest["clusters"]![0]!["shared"]!["usageMs"]!.GetValue<double>()>=100 &&
                latest["rosters"]!["edge"]!.AsArray().Count==2,"Usage and roster changes survive at unchanged policy timestamp");
            typeof(ConnectionHub).GetMethod("Remove",Private)!.Invoke(hub,[peer]);await Drain(observerPeer);
            var removed=observer.Read().Last(f=>f["kind"]?.GetValue<string>()=="clusters")["clusters"]![0]!;
            Check(removed["shared"]!["ts"]!.GetValue<double>()==before && removed["shared"]!["scopes"]!.AsArray().Count==3 &&
                !removed["members"]!.AsArray().Any(m=>m!["program"]!.GetValue<string>()=="chrome" && m["online"]!.GetValue<bool>()),
                "Member disconnection remains visible at unchanged policy timestamp without losing Website entries");
        }
        foreach(var phase in new[]{"rosters","peers"})
        {
            Environment.SetEnvironmentVariable("VAULT_STORAGE_ROOT",Path.Combine(Path.GetTempPath(),"vault-order-"+Guid.NewGuid()));
            var hub=Seed();var socket=new RecordingSocket();var peer=AddPeer(hub,socket);
            using var captured=new ManualResetEventSlim();using var release=new ManualResetEventSlim();var entered=0;
            hub.ClusterSnapshotCapturedForTests=kind=>{
                if(kind==phase && Interlocked.Increment(ref entered)==1){captured.Set();if(!release.Wait(TimeSpan.FromSeconds(5)))throw new Exception("Discovery barrier timed out");}
            };
            var discoveryBroadcast=typeof(ConnectionHub).GetMethod(phase=="rosters"?"BroadcastRosters":"BroadcastPeers",Private)!;
            var old=Task.Run(()=>discoveryBroadcast.Invoke(hub,null));
            Check(captured.Wait(TimeSpan.FromSeconds(5)),"Actual "+phase+" discovery snapshot reaches deterministic capture barrier");
            using var started=new ManualResetEventSlim();
            var update=Task.Run(()=>{
                started.Set();
                if(phase=="rosters")hub.SetRoster("edge",new JsonArray(new JsonObject{["id"]="e"},new JsonObject{["id"]="latest"}));
                else
                {
                    lock(typeof(ConnectionHub).GetField("_gate",Private)!.GetValue(hub)!)AddPeer(hub,new RecordingSocket(),"edge");
                    discoveryBroadcast.Invoke(hub,null);
                }
            });
            started.Wait();var overtook=await Task.WhenAny(update,Task.Delay(150))==update;
            release.Set();await Task.WhenAll(old,update).WaitAsync(TimeSpan.FromSeconds(5));await Drain(peer);
            var latest=socket.Read().Last(f=>f["kind"]!.GetValue<string>()==phase);
            Check(phase=="rosters"?latest["rosters"]!["edge"]!.AsArray().Count==2:latest["peers"]!.AsArray().Count==2,
                "Captured older "+phase+" discovery cannot overwrite newer state");
            Check(!overtook,"Newer discovery mutation cannot overtake "+phase+" capture and enqueue");
        }
        var fifoHub=new ConnectionHub();var fifoSocket=new RecordingSocket();var fifoPeer=AddPeer(fifoHub,fifoSocket);
        var unblock=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);fifoSocket.BlockFirst=unblock.Task;
        var send=typeof(ConnectionHub).GetMethod("SendAsync",Private)!;
        var requests=Enumerable.Range(0,64).Select(n=>(Task)send.Invoke(fifoHub,[fifoPeer,new JsonObject{["sequence"]=n}])!).ToArray();
        unblock.SetResult();await Task.WhenAll(requests).WaitAsync(TimeSpan.FromSeconds(5));
        Check(fifoSocket.Read().Select(f=>f["sequence"]!.GetValue<int>()).SequenceEqual(Enumerable.Range(0,64)),"Peer sends preserve FIFO while the first socket write is stalled");
        var failedHub=new ConnectionHub();var failedPeer=AddPeer(failedHub,new SynchronousFailureSocket());
        var broadcast=typeof(ConnectionHub).GetMethod("BroadcastClusters",Private)!;
        await Task.WhenAll(Task.Run(()=>broadcast.Invoke(failedHub,null)),Task.Run(()=>failedHub.SetRoster("edge",new JsonArray()))).WaitAsync(TimeSpan.FromSeconds(5));
        await Drain(failedPeer);
        Check(((IDictionary)typeof(ConnectionHub).GetField("_peers",Private)!.GetValue(failedHub)!).Count==0,"Synchronous failed socket removal and concurrent state enqueue complete without lock inversion");
        if(appAssembly!=null)
        {
            using var stream=File.OpenRead(appAssembly);using var pe=new PEReader(stream);var metadata=pe.GetMetadataReader();
            var type=metadata.TypeDefinitions.Select(metadata.GetTypeDefinition).Single(t=>metadata.GetString(t.Name)=="ConnectionHub");
            Check(!type.GetFields().Any(f=>metadata.GetString(metadata.GetFieldDefinition(f).Name)=="ClusterSnapshotCapturedForTests"),"Runnable native assembly contains no contract-test snapshot hook");
        }
    }
}
