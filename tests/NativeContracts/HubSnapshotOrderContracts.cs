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
        public override WebSocketState State=>WebSocketState.Open;
        public override WebSocketCloseStatus? CloseStatus=>null;
        public override string? CloseStatusDescription=>null;
        public override string? SubProtocol=>null;
        public override void Abort() {}
        public override void Dispose() {}
        public override Task CloseAsync(WebSocketCloseStatus status,string? description,CancellationToken token)=>Task.CompletedTask;
        public override Task CloseOutputAsync(WebSocketCloseStatus status,string? description,CancellationToken token)=>Task.CompletedTask;
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
