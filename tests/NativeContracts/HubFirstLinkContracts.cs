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
}
