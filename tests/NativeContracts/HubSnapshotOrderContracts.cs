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
    public static async Task RunActivitySettings(string? fixturePath = null)
    {
        var authority = (Generation: 1L, Supported: true);
        var hub = new ConnectionHub { ActivitySettingsAuthority = () => authority };
        await hub.ResetActivitySettingsAuthority(1);
        var chrome = new RecordingSocket(); var chromePeer = AddPeer(hub, chrome);
        var edge = new RecordingSocket(); var edgePeer = AddPeer(hub, edge, "edge");
        var safari = new RecordingSocket(); var safariPeer = AddPeer(hub, safari, "safari");
        var classifier = new RecordingSocket(); var classifierPeer = AddPeer(hub, classifier, "classifier");
        var waiting = new RecordingSocket(); var waitingPeer = AddPeer(hub, waiting);
        waitingPeer.GetType().GetField("Connected")!.SetValue(waitingPeer, false);
        var epoch = Guid.NewGuid().ToString();
        JsonObject Commit(long revision) => new() {
            ["settings"] = new JsonObject { ["byCategory"] = new JsonObject { ["web-visit"] = new JsonObject { ["enabled"] = false } }, ["idleThresholdSeconds"] = 60 },
            ["epoch"] = epoch, ["revision"] = revision, ["changedAtMs"] = 1_750_000_000_000.0 };
        var getResponse = (JsonObject)Commit(0).DeepClone(); getResponse["kind"] = "settings";
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
        Check(!new ConnectionHub().PublishActivitySettings(1, Commit(1)), "Unqualified worker cannot publish settings or advertise support");
        var commit = Commit(1);
        Check(hub.PublishActivitySettings(1, commit), "Successful own worker commit admitted");
        commit["settings"]!["byCategory"]!["web-visit"]!["enabled"] = true;
        await Task.WhenAll(Drain(chromePeer), Drain(edgePeer), Drain(safariPeer), Drain(classifierPeer), Drain(waitingPeer));
        Check(new[] { chrome, edge, safari }.All(s => s.Read().Single()["kind"]?.GetValue<string>() == "activity-settings-updated" &&
            s.Read().Single()["body"]?["settings"]?["byCategory"]?["web-visit"]?["enabled"]?.GetValue<bool>() == false),
            "All authenticated browsers including origin receive captured committed snapshot");
        Check(classifier.Read().Length == 0 && waiting.Read().Length == 0, "Classifier and unauthenticated peers receive no Activity settings");
        Check(!hub.PublishActivitySettings(1, Commit(0)) && !hub.PublishActivitySettings(1, Commit(1)), "Older and duplicate same-epoch commits cannot overwrite current settings");
        foreach (var field in new[] { "epoch", "revision", "changedAtMs", "settings" })
        {
            var invalid = Commit(2); invalid.AsObject().Remove(field);
            Check(!hub.PublishActivitySettings(1, invalid), "Missing " + field + " refuses native settings event");
        }
        foreach (var invalidRevision in new JsonNode?[] { JsonValue.Create(-1), JsonValue.Create(9_007_199_254_740_992L), JsonValue.Create(1.5), JsonValue.Create("2") })
        {
            var invalid = Commit(2); invalid["revision"] = invalidRevision;
            Check(!hub.PublishActivitySettings(1, invalid), "Revision must be a nonnegative JavaScript-safe integer");
        }
        var wrongEpoch = Commit(2); wrongEpoch["epoch"] = "not-a-store-uuid";
        var wrongTime = Commit(2); wrongTime["changedAtMs"] = -1.0;
        var oversized = Commit(2); oversized["settings"]!["unexpected"] = new string('x', 88_001);
        Check(!hub.PublishActivitySettings(1, wrongEpoch) && !hub.PublishActivitySettings(1, wrongTime) && !hub.PublishActivitySettings(1, oversized), "Malformed epoch, negative time and oversized snapshots fail closed");
        var client = new JsonObject { ["kind"] = "activity-settings-updated", ["body"] = Commit(2) };
        Check(!hub.PublishActivitySettings(1, client), "Client-originated settings kind cannot enter worker-only publisher");
        var classifierEvent = new JsonObject { ["event"] = "broadcast", ["operation"] = "activity-settings-updated", ["body"] = Commit(2) };
        hub.BroadcastClassifier(classifierEvent);
        Check(!hub.PublishActivitySettings(1, classifierEvent), "Classifier whitelist does not authorize Activity settings commits");
        Check(hub.PublishActivitySettings(1, Commit(2)), "Newer commit follows older snapshot in peer FIFO");
        var restarted = Commit(0); restarted["epoch"] = Guid.NewGuid().ToString();
        Check(hub.PublishActivitySettings(1, restarted), "New store epoch may begin at revision zero");
        await Drain(chromePeer);
        Check(chrome.Read().Select(f => f["body"]!["revision"]!.GetValue<long>()).SequenceEqual(new long[] { 1, 2, 0 }), "Native commit generation enqueue preserves pipe order");
        var welcomeSocket = new RecordingSocket(); var welcomePeer = AddPeer(hub, welcomeSocket, "edge");
        await (Task)typeof(ConnectionHub).GetMethod("QueueWelcome", Private)!.Invoke(hub, [welcomePeer])!;
        Check(welcomeSocket.Read().First()["activitySettingsNotifications"]?.GetValue<bool>() == true, "Supporting worker advertises additive v4 welcome capability");
        authority = (1, false);
        var unsupported = new RecordingSocket(); var unsupportedPeer = AddPeer(hub, unsupported, "safari");
        await (Task)typeof(ConnectionHub).GetMethod("QueueWelcome", Private)!.Invoke(hub, [unsupportedPeer])!;
        Check(unsupported.Read().First()["activitySettingsNotifications"]?.GetValue<bool>() == false && !hub.PublishActivitySettings(1, Commit(3)), "Unsupported worker neither advertises nor publishes notifications");
        var failing = new ConnectionHub { ActivitySettingsAuthority = () => (1, true) };
        await failing.ResetActivitySettingsAuthority(1);
        var failedPeer = AddPeer(failing, new SynchronousFailureSocket());
        Check(failing.PublishActivitySettings(1, Commit(1)), "Native enqueue accepts committed snapshot before asynchronous socket failure");
        await Drain(failedPeer);
        Check(((IDictionary)typeof(ConnectionHub).GetField("_peers", Private)!.GetValue(failing)!).Count == 0, "Failed Activity send removes peer without lock inversion");
        var reconnectAuthority = (Generation: 1L, Supported: true);
        var reconnectHub = new ConnectionHub { ActivitySettingsAuthority = () => reconnectAuthority };
        await reconnectHub.ResetActivitySettingsAuthority(1);
        var reconnectSocket = new RecordingSocket(); var reconnectPeer = AddPeer(reconnectHub, reconnectSocket);
        var retainedSocket = new RecordingSocket(); var retainedPeer = AddPeer(reconnectHub, retainedSocket, "classifier");
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        reconnectSocket.BlockFirst = release.Task;
        reconnectHub.PublishActivitySettings(1, Commit(1));
        reconnectAuthority = (2, true);
        var disconnected = reconnectHub.ResetActivitySettingsAuthority(2);
        Check(reconnectSocket.NormalClose == null && !disconnected.IsCompleted, "Worker reconnect normal close waits behind admitted snapshot send");
        Check(!reconnectPeer.GetType().GetField("Connected")!.GetValue(reconnectPeer)!.Equals(true), "Reconnect immediately prevents stale authenticated peer delivery");
        release.SetResult(); await disconnected.WaitAsync(TimeSpan.FromSeconds(5)); await Drain(retainedPeer);
        Check(reconnectSocket.NormalClose == WebSocketCloseStatus.NormalClosure && retainedSocket.NormalClose == null &&
            ((IDictionary)typeof(ConnectionHub).GetField("_peers", Private)!.GetValue(reconnectHub)!).Count == 1,
            "Worker change normally closes only browser peers and keeps Hub/nonbrowser ownership");
        var freshSocket = new RecordingSocket(); var freshPeer = AddPeer(reconnectHub, freshSocket);
        await (Task)typeof(ConnectionHub).GetMethod("QueueWelcome", Private)!.Invoke(reconnectHub, [freshPeer])!;
        Check(freshSocket.Read().First()["activitySettingsNotifications"]?.GetValue<bool>() == true, "Reauthenticated browser receives current capability in fresh welcome");
        Check(!reconnectHub.PublishActivitySettings(1, Commit(999)), "Delayed old generation cannot publish into new authority");
        await reconnectHub.ResetActivitySettingsAuthority(1);
        Check(reconnectHub.PublishActivitySettings(2, Commit(1)), "Late old ready/closed reset cannot roll back current generation");
        await Drain(freshPeer);
        Check(!reconnectHub.PublishActivitySettings(2, Commit(1)), "Refused old callback cannot mutate current epoch or revision");
        reconnectAuthority = (2, false); await reconnectHub.ResetActivitySettingsAuthority(2);
        var beforeReady = new RecordingSocket(); var beforeReadyPeer = AddPeer(reconnectHub, beforeReady);
        await (Task)typeof(ConnectionHub).GetMethod("QueueWelcome", Private)!.Invoke(reconnectHub, [beforeReadyPeer])!;
        Check(beforeReady.Read().First()["activitySettingsNotifications"]?.GetValue<bool>() == false, "Current closed/unsupported generation has false welcome");
        reconnectAuthority = (2, true); await reconnectHub.ResetActivitySettingsAuthority(2);
        Check(beforeReady.NormalClose == WebSocketCloseStatus.NormalClosure, "Ready capability after false welcome forces normal reauthentication");
        if (fixturePath != null) await RunActualActivityOriginRace(fixturePath);
    }
    private static async Task RunActualActivityOriginRace(string fixturePath)
    {
        using var worker = new ClassifierWorkerClient(fixturePath);
        var hub = new ConnectionHub { ActivitySettingsAuthority = () => worker.ActivitySettingsAuthority };
        worker.ActivitySettingsCapabilityChanged += generation => { _ = hub.ResetActivitySettingsAuthority(generation); };
        var oldEpoch = Guid.NewGuid().ToString(); var newEpoch = Guid.NewGuid().ToString();
        JsonObject Body(string epoch, long revision) => new() { ["settings"] = new JsonObject { ["idleThresholdSeconds"] = 60 },
            ["epoch"] = epoch, ["revision"] = revision, ["changedAtMs"] = 1_750_000_000_000.0 };
        using var admitted = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var oldCallback = new TaskCompletionSource<(long Generation, bool Accepted)>(TaskCreationOptions.RunContinuationsAsynchronously);
        worker.Event += received =>
        {
            if (received.Message["event"]?.GetValue<string>() == "activity-settings-committed" && received.Message["value"]?["epoch"]?.GetValue<string>() == oldEpoch)
            { admitted.Set(); if (!release.Wait(TimeSpan.FromSeconds(8))) throw new Exception("Old callback barrier timed out"); }
        };
        worker.Event += received =>
        {
            if (received.Message["event"]?.GetValue<string>() == "activity-settings-committed" && received.Message["value"] is JsonObject body)
            {
                var accepted = hub.PublishActivitySettings(received.SessionGeneration, body);
                if (body["epoch"]?.GetValue<string>() == oldEpoch) oldCallback.TrySetResult((received.SessionGeneration, accepted));
            }
        };
        var childPIDs = new List<int>();
        try
        {
            await worker.Request("capability-ready", new JsonObject { ["flag"] = true });
            var originalGeneration = worker.ActivitySettingsAuthority.Generation;
            var oldIdentity = await worker.Request("settings-commit-events", new JsonObject { ["commits"] = new JsonArray() });
            var oldPID = oldIdentity!["pid"]!.GetValue<int>(); childPIDs.Add(oldPID);
            // Retain the client's original creator-owned Process object. An
            // externally queried PID object cannot report ExitCode on Windows.
            var ownSession = typeof(ClassifierWorkerClient).GetField("_session", Private)!.GetValue(worker)!;
            var oldProcess = (System.Diagnostics.Process)ownSession.GetType().GetProperty("Process")!.GetValue(ownSession)!;
            Check(oldProcess.Id == oldPID && (long)ownSession.GetType().GetProperty("Generation")!.GetValue(ownSession)! == originalGeneration,
                "Origin race observes the exact creator-owned Process and generation");
            var delayedRequest = worker.Request("settings-commit-events", new JsonObject { ["commits"] = new JsonArray(Body(oldEpoch, 999)), ["exitAfterCommits"] = true });
            Check(admitted.Wait(TimeSpan.FromSeconds(5)), "Actual old callback delayed after reader admissibility and before host bridge");
            await oldProcess.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Check(oldProcess.ExitCode == 0, "Delayed callback child exits naturally without cancellation");
            await worker.Request("capability-ready", new JsonObject { ["flag"] = true });
            var currentGeneration = worker.ActivitySettingsAuthority.Generation;
            Check(currentGeneration > originalGeneration && worker.ActivitySettingsAuthority.Supported, "Replacement has a newer host-owned ready generation");
            var socket = new RecordingSocket(); var peer = AddPeer(hub, socket);
            var replacement = await worker.Request("settings-commit-events", new JsonObject { ["commits"] = new JsonArray(Body(newEpoch, 1)) });
            childPIDs.Add(replacement!["pid"]!.GetValue<int>());
            await Drain(peer);
            Check(socket.Read().Single()["body"]?["epoch"]?.GetValue<string>() == newEpoch, "New session commit arrives before releasing old callback");
            release.Set(); await delayedRequest.WaitAsync(TimeSpan.FromSeconds(5));
            var stale = await oldCallback.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Drain(peer);
            Check(stale.Generation == originalGeneration && !stale.Accepted && socket.Read().Length == 1, "Creator-owned old callback token refuses publication after replacement");
            await hub.ResetActivitySettingsAuthority(originalGeneration);
            Check(!hub.PublishActivitySettings(currentGeneration, Body(newEpoch, 1)) && hub.PublishActivitySettings(currentGeneration, Body(newEpoch, 2)),
                "Late old callback/ready/close cannot poison new epoch or roll back revision");
            await Drain(peer);
            Check(socket.Read().Length == 2 && socket.Read().All(frame => frame["body"]?["epoch"]?.GetValue<string>() == newEpoch && frame["SessionGeneration"] == null && frame["body"]?["SessionGeneration"] == null),
                "Public Activity wire carries committed body without host generation tokens");
        }
        finally
        {
            release.Set();
            var outcome = await worker.ShutdownAsync();
            Check(outcome.Kind == WorkerShutdownKind.CleanExit && outcome.ExitCode == 0, "Origin race replacement exits by normal EOF");
            foreach (var pid in childPIDs)
                try { using var child = System.Diagnostics.Process.GetProcessById(pid); Check(child.HasExited, "Exact origin-race child independently absent"); }
                catch (ArgumentException) { Console.WriteLine("PASS Exact origin-race child independently absent"); }
        }
    }
    public static async Task RunRosterEcho()
    {
        Environment.SetEnvironmentVariable("VAULT_STORAGE_ROOT",Path.Combine(Path.GetTempPath(),"vault-roster-echo-"+Guid.NewGuid()));
        var hub=Seed();
        var retainedState=new JsonObject{["version"]=1,["objects"]=new JsonObject{["focus"]=new JsonObject{["kind"]="timer",["direction"]="down",["durationMs"]=12000,["elapsedMs"]=1234}}};
        hub.RuleTimePolicy=(_,_)=>Task.FromResult<JsonObject?>(new JsonObject{["ok"]=true,["ready"]=true,["state"]=retainedState.DeepClone()});
        await hub.SyncRuleTimeAsync("chrome","c",null,new JsonObject{["version"]=1,["objects"]=new JsonObject()},new());
        var socket=new RecordingSocket();var peer=AddPeer(hub,socket);
        JsonArray Roster(string name="Chrome",int seed=1)=>new(new JsonObject{["id"]="c",["name"]=name,["timeState"]=new JsonObject{["version"]=1,["objects"]=new JsonObject(),["sample"]=seed}});
        // Chrome acknowledges each shared snapshot by announcing its roster.
        // Exercise actual hub capture + SendTail with that consumer behavior;
        // repeated cluster snapshots must not amplify this acknowledgement.
        typeof(ConnectionHub).GetMethod("BroadcastClusters",Private)!.Invoke(hub,null);
        var cursor=0;var handled=0;
        for(var round=0;round<4;round++)
        {
            await Drain(peer);var incoming=socket.Read().Skip(cursor).ToArray();cursor+=incoming.Length;
            var shared=incoming.Where(f=>f["kind"]?.GetValue<string>() is "clusters" or "cluster-updated").ToArray();
            if(shared.Length==0)break;
            handled+=shared.Length;
            foreach(var _ in shared)hub.SetRoster("chrome",Roster());
        }
        await Drain(peer);
        Check(handled==2 && socket.Read().Skip(cursor).All(f=>f["kind"]?.GetValue<string>()=="rosters"),
            "Shared-snapshot roster acknowledgements quiesce without recursive cluster broadcasts");
        var start=socket.Read().Length;
        hub.SetRoster("chrome",Roster());hub.SetRoster("chrome",Roster("Renamed",2));await Drain(peer);
        var rosterOnly=socket.Read().Skip(start).ToArray();
        Check(rosterOnly.Length==2 && rosterOnly.All(f=>f["kind"]?.GetValue<string>()=="rosters") &&
            rosterOnly[^1]["rosters"]?["chrome"]?[0]?["name"]?.GetValue<string>()=="Renamed" &&
            rosterOnly[^1]["rosters"]?["chrome"]?[0]?["timeState"]?["sample"]?.GetValue<int>()==2,
            "Unchanged membership still publishes current roster names and managed-time seeds without shared adoption echoes");
        start=socket.Read().Length;hub.SetRoster("chrome",new JsonArray());await Drain(peer);
        var removal=socket.Read().Skip(start).ToArray();var clusters=removal.Last(f=>f["kind"]?.GetValue<string>()=="clusters")["clusters"]!.AsArray();
        Check(removal.Any(f=>f["kind"]?.GetValue<string>()=="cluster-updated") && clusters.Count==1 &&
            clusters[0]!["members"]!.AsArray().Count==2 && clusters[0]!["members"]!.AsArray().All(m=>m!["program"]?.GetValue<string>()!="chrome"),
            "Actual roster deletion publishes the new surviving linked membership");
        // A deleted group's retained record is intentionally pruned; the final
        // state for surviving members remains the shared snapshot unchanged.
        Check(JsonNode.DeepEquals(clusters[0]!["shared"]?["ruleTime"],retainedState),
            "Roster deletion preserves the final managed-time snapshot for surviving linked members");
        hub.SetRoster("edge",new JsonArray());await Drain(peer);
        var registry=JsonNode.Parse(File.ReadAllText(WindowsBlocker.WebUI.Storage.ClustersPath))!["value"]!.AsArray();
        Check(hub.ActiveClusterCount()==0 && registry.OfType<JsonObject>().Any(c=>JsonNode.DeepEquals(c["DetachedRuleTime"]?["windowsapp/w"]?["state"],retainedState)),
            "Removing the second of three members durably retains the surviving member's final shared snapshot");
    }
    public static async Task Run(string? appAssembly=null)
    {
        await RunRosterEcho();
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
                socket.Read().Last(f=>f["kind"]?.GetValue<string>()=="rosters")["rosters"]!["edge"]!.AsArray().Count==2,"Usage and roster discovery changes survive independently at unchanged policy timestamp");
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
