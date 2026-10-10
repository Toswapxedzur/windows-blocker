using System.Text.Json.Nodes;
using WindowsBlocker.Bridge;

// Executes the actual managed hub on disposable storage. This subset does not
// exercise Windows process APIs, WPF, authentication, or installed GUI behavior.
static class HubFirstLinkContracts
{
    static void Check(bool ok, string label) { if (!ok) throw new Exception(label); Console.WriteLine("PASS " + label); }
    static JsonObject Snapshot(ConnectionHub hub) => (JsonObject)JsonNode.Parse(hub.ClustersJson())!["clusters"]![0]!;
    static void Roster(ConnectionHub hub, string program, string id) => hub.SetRoster(program,new JsonArray(new JsonObject { ["id"]=id,["name"]=program,["frozen"]=false }));
    static JsonObject Frame(bool native) => new() {
        ["scalars"]=new JsonObject { ["name"]="Fixture",["allowedMinutes"]=15,["resetIntervalHours"]=24 },
        ["scopes"]=new JsonArray(native
            ? new JsonObject { ["surface"]="apps",["apps"]=new JsonArray() }
            : new JsonObject { ["surface"]="site",["sites"]=new JsonArray("example.com") }), ["ts"]=1
    };
    public static void Run()
    {
        InitialStateContracts();
        foreach(var nativeFirst in new[] { true,false })
        {
            var hub=new ConnectionHub(); Roster(hub,"windowsapp","w"); Roster(hub,"chrome","c");
            Check(hub.Link("windowsapp","w","chrome","c")==null,"explicit first link succeeds");
            var first=Snapshot(hub);
            Check(first["shared"]?["scopes"]==null,"uncontributed link withholds scopes");
            Check(((JsonArray)first["members"]!).All(m=>m?["contributed"]?.GetValue<bool>()==false),"every member initially exposes contributed=false");
            hub.ApplySync(nativeFirst ? "windowsapp" : "chrome",nativeFirst ? "w" : "c",Frame(nativeFirst));
            var partial=Snapshot(hub);
            Check(partial["shared"]?["scopes"]==null,(nativeFirst ? "Apps-first" : "Websites-first")+" snapshot cannot erase the other participant's original lines");
            Check(((JsonArray)partial["members"]!).Count(m=>m?["contributed"]?.GetValue<bool>()==true)==1,"partial link records exactly one contribution");
            hub.ApplySync(nativeFirst ? "chrome" : "windowsapp",nativeFirst ? "c" : "w",Frame(!nativeFirst));
            var scopes=(JsonArray)Snapshot(hub)["shared"]!["scopes"]!;
            Check(scopes.Any(s=>s?["surface"]?.GetValue<string>()=="apps") && scopes.Any(s=>s?["surface"]?.GetValue<string>()=="site"),"completed link retains Apps and Websites");
            Roster(hub,"edge","e"); Check(hub.Link("chrome","c","edge","e")==null,"second browser joins existing link");
            Check(Snapshot(hub)["shared"]?["scopes"]==null,"new participant must contribute before the enlarged union is adopted");
            hub.ApplySync("edge","e",new() { ["scopes"]=new JsonArray(),["ts"]=2 });
            Check(((JsonArray)Snapshot(hub)["shared"]!["scopes"]!).Count==2,"empty first contribution preserves existing owned lines");
            hub.ApplySync("chrome","c",new() { ["scopes"]=new JsonArray(),["ts"]=10 });
            var deleted=(JsonArray)Snapshot(hub)["shared"]!["scopes"]!;
            Check(deleted.Count==1 && deleted[0]?["surface"]?.GetValue<string>()=="apps","explicit later website deletion remains effective");
        }
        foreach(var rolling in new[] { false,true })
        {
            var hub=new ConnectionHub(); Roster(hub,"windowsapp","w"); Roster(hub,"chrome","c");
            hub.Link("windowsapp","w","chrome","c");
            var definition=Frame(true); ((JsonObject)definition["scalars"]!)["rollingLimit"]=rolling;
            hub.ApplySync("windowsapp","w",definition);
            hub.ApplySync("chrome","c",Frame(false));
            var anchor=Snapshot(hub)["shared"]!["usageResetAtMs"]!.GetValue<double>();
            var minute=Math.Floor(DateTimeOffset.Now.ToUnixTimeMilliseconds()/60000d)*60000;
            var transfer=new JsonObject { ["usageTransferId"]="fixture-transfer" };
            if(rolling) transfer["usageBuckets"]=new JsonObject { [minute.ToString(System.Globalization.CultureInfo.InvariantCulture)]=60000 };
            else { transfer["usageDeltaMs"]=60000; transfer["usageDeltaAnchorMs"]=anchor; }
            hub.ApplySync("chrome","c",transfer); hub.ApplySync("chrome","c",transfer);
            var shared=(JsonObject)Snapshot(hub)["shared"]!;
            double Usage(JsonObject value) => rolling ? ((JsonObject)value["usageBuckets"]!).Sum(b=>b.Value!.GetValue<double>()) : value["usageMs"]!.GetValue<double>();
            Check(Usage(shared)==60000,(rolling ? "rolling" : "fixed")+" duplicate offline transfer counts once");
            Check(shared["usageTransferReceipts"]?["chrome:fixture-transfer"]!=null,"snapshot acknowledges the sender's batch");
            var restarted=new ConnectionHub();
            typeof(ConnectionHub).GetMethod("Restore",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(restarted,null);
            restarted.ApplySync("chrome","c",transfer);
            Check(Usage((JsonObject)Snapshot(restarted)["shared"]!)==60000,"durable receipt prevents double-count after hub restart");
        }
    }
    static void InitialStateContracts()
    {
        foreach(var nativeInitiator in new[]{true,false}) foreach(var nativeFirst in new[]{true,false}) foreach(var rolling in new[]{true,false}) foreach(var restart in new[]{true,false})
        {
            var hub=new ConnectionHub(); Roster(hub,"windowsapp","w"); Roster(hub,"chrome","c");
            hub.Link(nativeInitiator ? "windowsapp" : "chrome",nativeInitiator ? "w" : "c",nativeInitiator ? "chrome" : "windowsapp",nativeInitiator ? "c" : "w");
            var anchor=DateTimeOffset.Now.ToUnixTimeMilliseconds();
            var key=(Math.Floor(anchor/60000d)*60000).ToString(System.Globalization.CultureInfo.InvariantCulture);
            JsonObject Original(bool native,double seed) {
                var f=Frame(native); f["scalars"] = new JsonObject { ["name"]=native ? "Native original" : "Browser chosen",["allowedMinutes"]=native ? 10 : 20,["resetIntervalHours"]=24,["rollingLimit"]=rolling };
                f["usageResetAtMs"]=anchor; f["usageMs"]=seed;
                if(rolling) f["usageBucketsSeed"]=new JsonObject{[key]=seed};
                return f;
            }
            hub.ApplySync(nativeFirst ? "windowsapp" : "chrome",nativeFirst ? "w" : "c",Original(nativeFirst,120_000));
            var delta=new JsonObject { ["usageDeltaMs"]=1_000,["usageDeltaAnchorMs"]=anchor };
            if(rolling) { delta.Remove("usageDeltaMs"); delta["usageBuckets"]=new JsonObject{[key]=1_000}; }
            hub.ApplySync(nativeFirst ? "windowsapp" : "chrome",nativeFirst ? "w" : "c",delta);
            if(restart) { hub=new ConnectionHub(); typeof(ConnectionHub).GetMethod("Restore",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(hub,null); }
            hub.ApplySync(nativeFirst ? "chrome" : "windowsapp",nativeFirst ? "c" : "w",Original(!nativeFirst,300_000));
            var shared=(JsonObject)Snapshot(hub)["shared"]!;
            var used=rolling ? ((JsonObject)shared["usageBuckets"]!).Sum(b=>b.Value!.GetValue<double>()) : shared["usageMs"]!.GetValue<double>();
            Check(used==301_000,$"{(rolling ? "rolling" : "fixed")} unequal initial seed preserves postjoin delta, nativeFirst={nativeFirst}, restart={restart}");
            Check(shared["scalars"]?["name"]?.GetValue<string>()==(nativeInitiator ? "Native original" : "Browser chosen") && shared["scalars"]?["allowedMinutes"]?.GetValue<double>()==(nativeInitiator ? 10 : 20),"Initiator original settings win either contribution order without resetting usage");
            hub.ApplySync(nativeInitiator ? "windowsapp" : "chrome",nativeInitiator ? "w" : "c",Original(nativeInitiator,900_000));
            shared=(JsonObject)Snapshot(hub)["shared"]!;
            used=rolling ? ((JsonObject)shared["usageBuckets"]!).Sum(b=>b.Value!.GetValue<double>()) : shared["usageMs"]!.GetValue<double>();
            Check(used==301_000,"Completed link ignores late ordinary absolute seeds");
        }
        var store=new WindowsBlocker.WebUI.WebStore();
        var nativeOriginal=JsonNode.Parse("""{"blockedGroups":[{"id":"w","name":"Native original","allowedMinutes":10,"resetIntervalHours":24,"scopes":[{"surface":"apps","apps":[]}]}],"usageTimersMs":{"w":300000},"usageResetAtMs":{"w":12345}}""")!; nativeOriginal["usageResetAtMs"]!["w"]=DateTimeOffset.Now.ToUnixTimeMilliseconds(); store.SaveRaw(nativeOriginal.ToJsonString());
        var nativeHub=new ConnectionHub(); Roster(nativeHub,"windowsapp","w"); Roster(nativeHub,"chrome","c"); nativeHub.Link("chrome","c","windowsapp","w");
        var browser=Frame(false);browser["scalars"]!["name"]="Browser chosen";browser["scalars"]!["allowedMinutes"]=20;
        nativeHub.ApplySync("chrome","c",browser);
        nativeHub.ReconcileLocal(store);
        var nativeShared=(JsonObject)Snapshot(nativeHub)["shared"]!;
        Check(nativeShared["usageMs"]!.GetValue<double>()==300_000,"Native first contribution includes persisted original usage atomically");
        Check(nativeShared["scalars"]?["name"]?.GetValue<string>()=="Browser chosen","Native original contribution cannot replace browser initiator settings");
        Check(JsonNode.Parse(store.LoadRawJson()!)?["blockedGroups"]?[0]?["name"]?.GetValue<string>()=="Browser chosen","Native adopts shared settings after its original contribution");
        var pending=new ConnectionHub(); Roster(pending,"windowsapp","w"); Roster(pending,"chrome","c"); pending.Link("windowsapp","w","chrome","c");
        var before=Frame(true);before["usageMs"]=120_000;before["usageResetAtMs"]=DateTimeOffset.Now.ToUnixTimeMilliseconds();pending.ApplySync("windowsapp","w",before);
        pending.ReportLocalUsage("w",2_000,0); Roster(pending,"edge","e");pending.Link("chrome","c","edge","e");
        var delayed=Frame(false);delayed["usageMs"]=300_000;pending.ApplySync("chrome","c",delayed);
        var third=Frame(false);third["usageMs"]=200_000;pending.ApplySync("edge","e",third);
        Check(pending.SharedUsage("w")?.Ms==302_000,"Adding a third member while an original is pending preserves the earlier postjoin delta");
        var rollover=new ConnectionHub();Roster(rollover,"windowsapp","w");Roster(rollover,"chrome","c");rollover.Link("windowsapp","w","chrome","c");
        var oldAnchor=DateTimeOffset.Now.ToUnixTimeMilliseconds()-2_000;
        var original=Frame(true);original["usageMs"]=120_000;original["usageResetAtMs"]=oldAnchor;rollover.ApplySync("windowsapp","w",original);
        var edit=Frame(true);edit["scalars"]!["allowedMinutes"]=30;edit["ts"]=DateTimeOffset.Now.ToUnixTimeMilliseconds();rollover.ApplySync("windowsapp","w",edit);
        rollover.ReportLocalUsage("w",1_000,0);
        var stale=Frame(false);stale["usageMs"]=300_000;stale["usageResetAtMs"]=oldAnchor;rollover.ApplySync("chrome","c",stale);
        Check(rollover.SharedUsage("w")?.Ms==1_000,"A true budget restart rejects the pending participant's expired original seed");
    }
}
