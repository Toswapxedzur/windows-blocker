using System.Collections;
using System.Net.WebSockets;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Text;
using System.Threading.Channels;
using WindowsBlocker.Bridge;
using WindowsBlocker.Core;
using WindowsBlocker.WebUI;

// Real hub methods, disposable registry, unstarted in-memory sockets. No listener.
static class BootstrapUsageContracts
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static void Check(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS bootstrap "+label);}
    static JsonObject Policy(bool rolling=false)=>new(){["name"]="Original",["enabled"]=true,["mode"]="after-minutes",["allowedMinutes"]=30,["resetIntervalHours"]=24,["rollingLimit"]=rolling};
    static JsonObject Definition(bool rolling=false)=>new(){["scalars"]=Policy(rolling),["scopes"]=new JsonArray(new JsonObject{["surface"]="site",["sites"]=new JsonArray("example.com")}),["ts"]=0};
    static JsonObject Cluster(ConnectionHub h)=>(JsonObject)JsonNode.Parse(h.ClustersJson())!["clusters"]![0]!;
    static JsonObject Shared(ConnectionHub h)=>(JsonObject)Cluster(h)["shared"]!;
    static bool Contributed(JsonObject c)=>c["members"]!.AsArray().OfType<JsonObject>().Single(m=>m["program"]!.GetValue<string>()=="chrome")["contributed"]!.GetValue<bool>();
    static JsonArray Rows()=>StorageSchema.Payload(JsonNode.Parse(File.ReadAllText(Storage.ClustersPath))!,"hub.clusters",3).AsArray();
    static void Roster(ConnectionHub h,string p,string id)=>h.SetRoster(p,new JsonArray(new JsonObject{["id"]=id,["name"]=id}));
    static ConnectionHub Stage(bool rolling,out long anchor,bool native=true,string initiator="windowsapp")
    {
        File.Delete(Storage.ClustersPath);var h=new ConnectionHub();Roster(h,"windowsapp","w");Roster(h,"chrome","c");
        Check(h.Link(initiator,initiator=="chrome"?"c":"w",initiator=="chrome"?"windowsapp":"chrome",initiator=="chrome"?"w":"c")==null,"staged link");
        anchor=DateTimeOffset.Now.ToUnixTimeMilliseconds()-2000;
        if(native){var f=Definition(rolling);f["scopes"]=new JsonArray(new JsonObject{["surface"]="apps",["apps"]=new JsonArray()});f["usageMs"]=5000;f["usageResetAtMs"]=anchor;if(rolling)f["usageBucketsSeed"]=new JsonObject{[Minute(anchor)]=5000};h.ApplySync("windowsapp","w",f);}
        return h;
    }
    static string Minute(long now)=>(Math.Floor(now/60000d)*60000).ToString(System.Globalization.CultureInfo.InvariantCulture);
    static ConnectionHub Restart(){var h=new ConnectionHub();typeof(ConnectionHub).GetMethod("Restore",Private)!.Invoke(h,null);return h;}
    sealed class Socket:WebSocket
    {
        public readonly List<JsonObject> Frames=[];
        readonly Channel<byte[]> input=Channel.CreateUnbounded<byte[]>();
        public readonly TaskCompletionSource<JsonObject> ChallengeReady=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource<JsonObject> WelcomeReady=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Feed(JsonObject frame)=>input.Writer.TryWrite(Encoding.UTF8.GetBytes(frame.ToJsonString()));
        public override WebSocketState State=>WebSocketState.Open;
        public override WebSocketCloseStatus? CloseStatus=>null;public override string? CloseStatusDescription=>null;public override string? SubProtocol=>null;
        public override void Abort(){} public override void Dispose(){}
        public override Task CloseAsync(WebSocketCloseStatus s,string? d,CancellationToken t)=>Task.CompletedTask;
        public override Task CloseOutputAsync(WebSocketCloseStatus s,string? d,CancellationToken t)=>Task.CompletedTask;
        public override async Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte>b,CancellationToken t){var bytes=await input.Reader.ReadAsync(t);bytes.CopyTo(b.AsSpan());return new(bytes.Length,WebSocketMessageType.Text,true);}
        public override Task SendAsync(ArraySegment<byte>b,WebSocketMessageType ty,bool e,CancellationToken t){var frame=(JsonObject)JsonNode.Parse(b.AsSpan())!;lock(Frames)Frames.Add(frame);if(frame["kind"]?.GetValue<string>()=="challenge")ChallengeReady.TrySetResult(frame);if(frame["kind"]?.GetValue<string>()=="welcome")WelcomeReady.TrySetResult(frame);return Task.CompletedTask;}
    }
    static object Peer(ConnectionHub h,Socket socket,bool capable)
    {
        var type=typeof(ConnectionHub).GetNestedType("Peer",BindingFlags.NonPublic)!;var peer=Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,[socket],null)!;
        type.GetField("Program")!.SetValue(peer,"chrome");type.GetField("Connected")!.SetValue(peer,true);type.GetField("SupportsOriginalUsage")!.SetValue(peer,capable);
        ((IDictionary)typeof(ConnectionHub).GetField("_peers",Private)!.GetValue(h)!).Add(type.GetField("Id")!.GetValue(peer)!,peer);return peer;
    }
    static async Task Drain(object peer)=>await ((Task)peer.GetType().GetField("SendTail")!.GetValue(peer)!).WaitAsync(TimeSpan.FromSeconds(5));
    static async Task<JsonObject> Welcome(ConnectionHub h,object peer,Socket socket){await (Task)typeof(ConnectionHub).GetMethod("QueueWelcome",Private)!.Invoke(h,[peer])!;return socket.Frames.Last(f=>f["kind"]!.GetValue<string>()=="clusters")["clusters"]!.AsArray()[0]!.AsObject();}
    public static async Task Run()
    {
        var exports=new JsonObject();
        foreach(var bad in new JsonNode?[]{null,JsonValue.Create(true),JsonValue.Create("1"),JsonValue.Create(1.0),JsonValue.Create(1.25),JsonValue.Create(2)})Check(!ConnectionHub.IsOriginalUsageVersion(bad),"capability refuses malformed declaration "+(bad?.ToJsonString()??"null"));
        Check(ConnectionHub.IsOriginalUsageVersion(JsonValue.Create(1)),"strict capability one");
        var secret=Enumerable.Range(0,32).Select(n=>(byte)n).ToArray();var authChallenge=new string('c',43);
        JsonObject Hello()=>new(){["kind"]="hello",["v"]=4,["program"]="chrome",["challenge"]=authChallenge,["proof"]=LocalHubAuthentication.Proof("chrome",authChallenge,secret)};
        foreach(var bad in new JsonNode?[]{null,JsonValue.Create(true),JsonValue.Create("1"),JsonValue.Create(1.0),JsonValue.Create(2)}){var hello=Hello();hello["originalUsageVersion"]=bad;Check(ConnectionHub.HelloRejectionReason(hello,authChallenge,secret,0)=="invalid-original-usage-version","actual hello rejects present malformed original capability");}
        var capableHello=Hello();capableHello["originalUsageVersion"]=1;Check(ConnectionHub.HelloRejectionReason(capableHello,authChallenge,secret,0)==null&&ConnectionHub.HelloRejectionReason(capableHello,authChallenge,secret,2)=="browser-identity-upgrade-required","usage capability is independent and cannot replace required identity");
        foreach(var rolling in new[]{false,true})
        {
            var h=Stage(rolling,out var anchor);var socket=new Socket();var peer=Peer(h,socket,true);
            var first=await Welcome(h,peer,socket);Check(!Contributed(first),"first wire retains original "+rolling);
            h.ApplySync("chrome","c",Definition(rolling));await Drain(peer);
            Check(!Contributed(Cluster(h)) && Rows()[0]!["Contributed"]!.AsArray().Any(p=>p!.GetValue<string>()=="chrome"),"config complete is independently usage pending "+rolling);
            h.ApplySync("windowsapp","w",rolling?new(){["usageBuckets"]=new JsonObject{[Minute(anchor)]=1000}}:new(){["usageDeltaMs"]=1000});
            // A structural write preserves the joining increment across restart.
            h.ApplySync("windowsapp","w",new(){["scalars"]=Policy(rolling),["ts"]=DateTimeOffset.Now.ToUnixTimeMilliseconds()});
            h=Restart();Check(!Contributed(Cluster(h)),"pending survives restart "+rolling);
            var original=new JsonObject{["usageResetAtMs"]=anchor};if(rolling)original["usageBucketsSeed"]=new JsonObject{[Minute(anchor)]=8000};else original["usageMs"]=8000;
            h.ApplySync("chrome","c",original);var shared=Shared(h);
            var used=rolling?shared["usageBuckets"]!.AsObject().Sum(b=>b.Value!.GetValue<double>()):shared["usageMs"]!.GetValue<double>();
            Check(used==9000 && Contributed(Cluster(h)),"late original MAX8 plus fresh joining1 exactly once "+rolling);
            Check(Rows()[0]!["OriginalUsagePending"]!.AsArray().Count==0,"completion saved "+rolling);
            var late=(JsonObject)original.DeepClone();if(rolling)late["usageBucketsSeed"]=new JsonObject{[Minute(anchor)]=900000};else late["usageMs"]=900000;h.ApplySync("chrome","c",late);
            Check((rolling?Shared(h)["usageBuckets"]!.AsObject().Sum(b=>b.Value!.GetValue<double>()):Shared(h)["usageMs"]!.GetValue<double>())==9000,"completed originals do not replay "+rolling);
        }
        {
            var h=Stage(false,out var anchor);var socket=new Socket();var peer=Peer(h,socket,false);var initial=await Welcome(h,peer,socket);var firstWire=socket.Frames.Last(f=>f["kind"]!.GetValue<string>()=="clusters").DeepClone();Check(initial["shared"]==null,"legacy unknown union has no shared adoption");
            h.ApplySync("chrome","c",Definition());await Drain(peer);var fixedFrame=socket.Frames.Last(f=>f["kind"]!.GetValue<string>()=="clusters")["clusters"]![0]!["shared"]!.AsObject();
            Check(!fixedFrame.ContainsKey("usageMs")&&!fixedFrame.ContainsKey("usageBuckets")&&!fixedFrame.ContainsKey("usageResetAtMs"),"legacy fixed retains original total and anchor");
            var definitionWire=socket.Frames.Last(f=>f["kind"]!.GetValue<string>()=="clusters").DeepClone();
            var combined=new JsonObject{["usageMs"]=8000,["usageResetAtMs"]=anchor,["usageDeltaMs"]=1000};h.ApplySync("chrome","c",combined);Check(Shared(h)["usageMs"]!.GetValue<double>()==8000,"pending browser absolute includes delta once after a reused baseline");
            await Drain(peer);var browserGroup=Policy();browserGroup["id"]="c";browserGroup["scopes"]=Definition()["scopes"]!.DeepClone();
            exports["fixedOrdinary"]=new JsonObject{["browserSeed"]=new JsonObject{["blockedGroups"]=new JsonArray(browserGroup),["usageTimersMs"]=new JsonObject{["c"]=8000},["usageResetAtMs"]=new JsonObject{["c"]=anchor},["usageBucketsMs"]=new JsonObject{["c"]=new JsonObject()}},["nativeFrames"]=new JsonArray(firstWire,definitionWire),["groupIds"]=new JsonObject{["browser"]="c",["native"]="w"},["clockMs"]=anchor+1000,["incomingOriginal"]=combined.DeepClone(),["completedFrame"]=socket.Frames.Last(f=>f["kind"]!.GetValue<string>()=="clusters").DeepClone(),["expectedCompletedUsage"]=8000,["provenance"]="Actual Windows production serializer/network snapshots and admission; generated disposable source fixture, no listener."};
            h.ApplySync("chrome","c",new(){["usageMs"]=9000,["usageResetAtMs"]=anchor,["usageDeltaMs"]=1000});Check(Shared(h)["usageMs"]!.GetValue<double>()==9000,"subsequent nonpending delta remains counted once");
            var native=Stage(false,out anchor,false);var nativeReport=Definition();nativeReport["usageMs"]=5000;nativeReport["usageDeltaMs"]=1000;nativeReport["usageResetAtMs"]=anchor;native.ApplySync("windowsapp","w",nativeReport);Check(Shared(native)["usageMs"]!.GetValue<double>()==6000,"native first report retains Windows seed-plus-delta contract (Mac differs)");
        }
        {
            var h=Stage(true,out var rollingAnchor);var socket=new Socket();var peer=Peer(h,socket,false);
            var unsafeMethod=typeof(ConnectionHub).GetMethod("UnsafeLegacyRollingJoin",BindingFlags.Static|BindingFlags.NonPublic)!;var clusterState=((IDictionary)typeof(ConnectionHub).GetField("_clusters",Private)!.GetValue(h)!).Values.Cast<object>().Single();
            Check((bool)unsafeMethod.Invoke(null,[clusterState,"chrome"])!,"known pending rolling refuses admission before welcome");
            peer.GetType().GetField("Connected")!.SetValue(peer,false);
            var receive=(Task)typeof(ConnectionHub).GetMethod("ReceiveAsync",Private)!.Invoke(h,[peer])!;
            var challenge=(await socket.ChallengeReady.Task.WaitAsync(TimeSpan.FromSeconds(5)))["challenge"]!.GetValue<string>();
            socket.Feed(new(){["kind"]="hello",["v"]=4,["program"]="chrome",["challenge"]=challenge,["proof"]=LocalHubAuthentication.Proof("chrome",challenge,LocalHubAuthentication.Secret())});
            await receive.WaitAsync(TimeSpan.FromSeconds(5));
            Check(socket.Frames.Any(f=>f["kind"]?.GetValue<string>()=="rejected"&&f["reason"]?.GetValue<string>()=="browser-upgrade-required")&&!socket.Frames.Any(f=>f["kind"]?.GetValue<string>() is "welcome" or "clusters" or "cluster-updated"),"actual authenticated receive refuses rolling before welcome/harmful snapshot");
            var rollingGroup=Policy(true);rollingGroup["id"]="c";rollingGroup["scopes"]=Definition(true)["scopes"]!.DeepClone();
            exports["rollingOrdinary"]=new JsonObject{["browserSeed"]=new JsonObject{["blockedGroups"]=new JsonArray(rollingGroup),["usageTimersMs"]=new JsonObject{["c"]=8000},["usageResetAtMs"]=new JsonObject{["c"]=rollingAnchor},["usageBucketsMs"]=new JsonObject{["c"]=new JsonObject{[Minute(rollingAnchor)]=8000}}},["nativeFrames"]=new JsonArray(socket.Frames.Select(f=>f.DeepClone()).ToArray()),["groupIds"]=new JsonObject{["browser"]="c",["native"]="w"},["clockMs"]=rollingAnchor+1000,["legacyAdmissionReason"]="browser-upgrade-required",["provenance"]="Actual authenticated Windows ReceiveAsync rejection before welcome; generated disposable in-memory transport fixture, no listener."};
            h=Stage(false,out _,false,"chrome");socket=new Socket();peer=Peer(h,socket,false);peer.GetType().GetField("Connected")!.SetValue(peer,false);
            receive=(Task)typeof(ConnectionHub).GetMethod("ReceiveAsync",Private)!.Invoke(h,[peer])!;
            challenge=(await socket.ChallengeReady.Task.WaitAsync(TimeSpan.FromSeconds(5)))["challenge"]!.GetValue<string>();
            socket.Feed(new(){["kind"]="hello",["v"]=4,["program"]="chrome",["challenge"]=challenge,["proof"]=LocalHubAuthentication.Proof("chrome",challenge,LocalHubAuthentication.Secret())});
            await socket.WelcomeReady.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(5200); // Retirement must outlive the initial auth deadline.
            var discovered=Definition(true);discovered["kind"]="group-sync";discovered["groupId"]="c";socket.Feed(discovered);
            await receive.WaitAsync(TimeSpan.FromSeconds(5));
            Check(!(bool)peer.GetType().GetField("Connected")!.GetValue(peer)! && socket.Frames.Any(f=>f["kind"]?.GetValue<string>()=="rejected"&&f["reason"]?.GetValue<string>()=="browser-upgrade-required"),"discovered rolling retires before projection");
            Check(!socket.Frames.Any(f=>f["cluster"]?["shared"]!=null || f["clusters"] is JsonArray array&&array.Any(c=>c?["shared"]!=null)),"legacy rolling gets no destructive shared frame");
        }
        {
            var h=Stage(false,out var anchor);h.ApplySync("chrome","c",Definition());var before=File.ReadAllText(Storage.ClustersPath);File.WriteAllText(Storage.ClustersPath,"unsupported");
            h.ApplySync("chrome","c",new(){["usageMs"]=8000,["usageResetAtMs"]=anchor,["usageDeltaMs"]=1000});Check(Shared(h)["usageMs"]!.GetValue<double>()==5000&&!Contributed(Cluster(h))&&File.ReadAllText(Storage.ClustersPath)=="unsupported","failed original save does not publish completion");
            File.WriteAllText(Storage.ClustersPath,before);h=Restart();h.ApplySync("chrome","c",new(){["usageMs"]=8000,["usageResetAtMs"]=anchor,["usageDeltaMs"]=1000});Check(Shared(h)["usageMs"]!.GetValue<double>()==8000&&Contributed(Cluster(h)),"restart retries original after failed save");
        }
        {
            var h=Stage(false,out var anchor);h.ApplySync("chrome","c",Definition());var reset=Policy();reset["resetIntervalHours"]=12;h.ApplySync("windowsapp","w",new(){["scalars"]=reset,["ts"]=DateTimeOffset.Now.ToUnixTimeMilliseconds()});h.ApplySync("windowsapp","w",new(){["usageDeltaMs"]=1000});h.ApplySync("chrome","c",new(){["usageMs"]=8000,["usageResetAtMs"]=anchor});
            Check(Shared(h)["usageMs"]!.GetValue<double>()==1000&&Contributed(Cluster(h)),"expired fixed original resolves pending without resurrection");
            h=Stage(true,out anchor);h.ApplySync("chrome","c",Definition(true));h.ApplySync("chrome","c",new(){["usageBucketsSeed"]=new JsonObject{[Minute(anchor-25*3600000L)]=8000},["usageResetAtMs"]=anchor});Check(Shared(h)["usageBuckets"]!.AsObject().Sum(b=>b.Value!.GetValue<double>())==5000&&Contributed(Cluster(h)),"aged rolling history resolves pending without resurrection");
        }
        {
            var h=Stage(false,out _);var bytes=File.ReadAllText(Storage.ClustersPath);bool refused=false;try{StorageSchema.Payload(JsonNode.Parse(bytes)!,"hub.clusters",2);}catch(InvalidDataException){refused=true;}Check(refused,"old schema2 reader preserves/refuses schema3");
            var rows=Rows();rows[0]!.AsObject().Remove("OriginalUsagePending");File.WriteAllText(Storage.ClustersPath,StorageSchema.Wrap(rows,"hub.clusters",2).ToJsonString());h=Restart();h.ApplySync("chrome","c",Definition());Check(!Contributed(Cluster(h))&&Rows()[0]!["OriginalUsagePending"]!.AsArray().Count==1,"schema2 unfinished row migrates pending and writes3");
            var missing=(JsonArray)rows.DeepClone();File.WriteAllText(Storage.ClustersPath,StorageSchema.Wrap(missing,"hub.clusters",3).ToJsonString());h=Restart();Check(h.ActiveClusterCount()==0,"schema3 missing ledger refuses rather than guessing ready");
            var duplicate=(JsonArray)rows.DeepClone();duplicate[0]!["OriginalUsagePending"]=new JsonArray("chrome","chrome");var invalid=StorageSchema.Wrap(duplicate,"hub.clusters",3).ToJsonString();File.WriteAllText(Storage.ClustersPath,invalid);h=Restart();Roster(h,"chrome","c");Check(h.ActiveClusterCount()==0&&File.ReadAllText(Storage.ClustersPath)==invalid,"malformed duplicate pending ledger is preserved read-only");
        }
        {
            var h=Stage(true,out var anchor);h.ApplySync("chrome","c",Definition(true));h.ApplySync("chrome","c",new(){["usageBucketsSeed"]=new JsonObject{[Minute(anchor)]=8000},["usageResetAtMs"]=anchor});
            var rows=Rows();rows[0]!.AsObject().Remove("OriginalUsagePending");File.WriteAllText(Storage.ClustersPath,StorageSchema.Wrap(rows,"hub.clusters",2).ToJsonString());h=Restart();
            var socket=new Socket();var peer=Peer(h,socket,false);var snapshot=await Welcome(h,peer,socket);Check(Contributed(snapshot)&&snapshot["shared"]?["usageBuckets"]?[Minute(anchor)]?.GetValue<double>()==8000,"established schema2 rolling remains legacy compatible");
        }
        {
            var h=Stage(true,out _,true,"chrome");var socket=new Socket();var peer=Peer(h,socket,false);Roster(h,"edge","e");Check(h.Link("chrome","c","edge","e")==null,"tentative noninitiator rolling does not refuse unknown initiator policy");h.ApplySync("chrome","c",Definition());await Drain(peer);Check((bool)peer.GetType().GetField("Connected")!.GetValue(peer)!&&Shared(h)["scalars"]?["rollingLimit"]?.GetValue<bool>()==false,"actual fixed initiator remains connected");
        }
        {
            File.Delete(Storage.ClustersPath);var store=new WebStore();var group=Policy(true);group["id"]="w";group["scopes"]=new JsonArray(new JsonObject{["surface"]="apps",["apps"]=new JsonArray()});store.SaveRaw(new JsonObject{["blockedGroups"]=new JsonArray(group)}.ToJsonString());var h=new ConnectionHub(store);h.ReconcileLocal(store);Roster(h,"chrome","c");var socket=new Socket();var peer=Peer(h,socket,false);var native=File.ReadAllText(store.FilePath);var registry=File.ReadAllText(Storage.ClustersPath);
            Check(h.Link("windowsapp","w","chrome","c")=="browser-upgrade-required"&&h.ActiveClusterCount()==0&&File.ReadAllText(store.FilePath)==native&&File.ReadAllText(Storage.ClustersPath)==registry,"known new native rolling link refuses before local or registry mutation");await Drain(peer);
        }
        var exportPath=Environment.GetEnvironmentVariable("VAULT_BOOTSTRAP_EXPORT");
        if(!string.IsNullOrEmpty(exportPath)){using var file=new FileStream(exportPath,FileMode.CreateNew,FileAccess.Write,FileShare.None);var bytes=Encoding.UTF8.GetBytes(exports.ToJsonString());file.Write(bytes);file.Flush(true);}
        File.Delete(Storage.ClustersPath);
    }
}
