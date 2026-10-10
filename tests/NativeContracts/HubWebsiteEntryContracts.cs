using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using WindowsBlocker.Bridge;
using WindowsBlocker.Core;
using WindowsBlocker.WebUI;

static class HubWebsiteEntryContracts
{
    static void Check(bool value,string label) { if(!value) throw new Exception(label); Console.WriteLine("PASS "+label); }
    static void Roster(ConnectionHub hub,string p,string id) => hub.SetRoster(p,new JsonArray(new JsonObject{["id"]=id,["frozen"]=false}));
    static JsonObject Snapshot(ConnectionHub hub) => (JsonObject)JsonNode.Parse(hub.ClustersJson())!["clusters"]![0]!;
    static JsonArray Lines(ConnectionHub hub) => (JsonArray)Snapshot(hub)["shared"]!["scopes"]!;
    static string Key(JsonNode line) => line["entryID"]?.GetValue<string>() ?? "site";
    static string Alias(string p,string id,string key) => "site:linked_"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(p+"\0"+id+"\0"+key))).ToLowerInvariant()[..24];
    static JsonObject Site(string target,bool except=false,string action="block",string? key=null) {
        var line=new JsonObject{["surface"]="site",["sites"]=new JsonArray(target),["sitesExcept"]=except,["action"]=action};
        if(key!=null) line["entryID"]=key;return line;
    }
    static JsonObject Frame(params JsonObject[] lines) => new(){["scopes"]=new JsonArray(lines.Select(l=>(JsonNode)l.DeepClone()).ToArray()),["scalars"]=new JsonObject{["name"]="Entry fixture",["enabled"]=true,["mode"]="instant",["resetIntervalHours"]=24},["ts"]=1};
    static ConnectionHub Restart() { var hub=new ConnectionHub();typeof(ConnectionHub).GetMethod("Restore",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(hub,null);return hub; }
    public static void Run()
    {
        var before=Environment.GetEnvironmentVariable("VAULT_STORAGE_ROOT");
        var root=Path.Combine(Path.GetTempPath(),"vault-website-contracts-"+Guid.NewGuid());
        try {
            foreach(var nativeInitiates in new[]{false,true}) foreach(var initiatorLast in new[]{false,true}) foreach(var restart in new[]{false,true})
            {
                Environment.SetEnvironmentVariable("VAULT_STORAGE_ROOT",Path.Combine(root,Guid.NewGuid().ToString()));
                var hub=new ConnectionHub();Roster(hub,"windowsapp","w");Roster(hub,"chrome","c");Roster(hub,"edge","e");
                hub.Link(nativeInitiates ? "windowsapp":"chrome",nativeInitiates ? "w":"c",nativeInitiates ? "chrome":"windowsapp",nativeInitiates ? "c":"w");hub.Link("chrome","c","edge","e");
                hub.ApplySync("windowsapp","w",Frame(new JsonObject{["surface"]="apps",["apps"]=new JsonArray(new JsonObject{["id"]="C:\\Apps\\fixture.exe"})}));
                var chrome=Site("news.example");var edge=Site("safe.example",true);
                hub.ApplySync(initiatorLast ? "edge":"chrome",initiatorLast ? "e":"c",Frame(initiatorLast ? edge:chrome));
                if(restart) hub=Restart();
                hub.ApplySync(initiatorLast ? "chrome":"edge",initiatorLast ? "c":"e",Frame(initiatorLast ? chrome:edge));
                var lines=Lines(hub).OfType<JsonObject>().Where(l=>l["surface"]?.GetValue<string>()=="site").ToArray();
                var label=$"nativeInitiates={nativeInitiates},initiatorLast={initiatorLast},restart={restart}";
                Check(lines.Length==2 && lines.Any(l=>l["sitesExcept"]!.GetValue<bool>()) && lines.Any(l=>!l["sitesExcept"]!.GetValue<bool>()),"Incompatible include and exclude Website scopes both survive: "+label);
                var baseSource=nativeInitiates && initiatorLast ? "edge":"chrome";var aliasSource=baseSource=="chrome" ? "edge":"chrome";var aliasId=aliasSource=="edge" ? "e":"c";
                Check(lines.Any(l=>Key(l)=="site" && ((JsonArray)l["sites"]!)[0]!.GetValue<string>()==(baseSource=="chrome" ? "news.example":"safe.example")) && lines.Any(l=>Key(l)==Alias(aliasSource,aliasId,"site")),"Stable Website alias and initiating default ownership: "+label);
                Check(Lines(hub).Count(l=>l?["surface"]?.GetValue<string>()=="apps")==1,"Preserved Website collision does not duplicate native Apps: "+label);
                var durable=Lines(hub).ToJsonString();hub=Restart();Check(Lines(hub).ToJsonString()==durable,"Website aliases and full definitions survive registry restart: "+label);
                Check(Snapshot(hub)["scopeOrigins"]==null && Snapshot(hub)["shared"]?["scopeOrigins"]==null,"Website origin metadata stays out of wire snapshots: "+label);
                var edited=(JsonArray)Lines(hub).DeepClone();var retained=edited.OfType<JsonObject>().First(l=>l["surface"]?.GetValue<string>()=="site" && Key(l)!="site");var retainedKey=Key(retained);
                hub.ApplySync("edge","e",new JsonObject{["scopes"]=new JsonArray(retained.DeepClone()),["ts"]=100});
                Check(Lines(hub).Count(l=>l?["surface"]?.GetValue<string>()=="site")==1 && Key(Lines(hub).First(l=>l?["surface"]?.GetValue<string>()=="site")!)==retainedKey,"Acknowledged deletion retains surviving alias without reminting: "+label);
                hub.ApplySync("chrome","c",new JsonObject{["scopes"]=new JsonArray(),["ts"]=101});
                Check(Lines(hub).All(l=>l?["surface"]?.GetValue<string>()!="site"),"Acknowledged removal deletes preserved Website entries: "+label);
                var emptyOrigins=(JsonObject)JsonNode.Parse(File.ReadAllText(Storage.ClustersPath))!["value"]![0]!["scopeOrigins"]!;
                Check(emptyOrigins.Count==0,"Acknowledged removal prunes all deleted Website provenance: "+label);
                hub.ApplySync("edge","e",new(){["scopes"]=new JsonArray(Site("new.example",false,"block","site:new_entry")),["scopeOrigins"]=new JsonObject{["site:new_entry"]="forged"},["ts"]=102});
                var actualOrigins=(JsonObject)JsonNode.Parse(File.ReadAllText(Storage.ClustersPath))!["value"]![0]!["scopeOrigins"]!;
                Check(actualOrigins.Count==1 && actualOrigins["site:new_entry"]!.GetValue<string>()=="edge\0e\0site:new_entry","New acknowledged entry derives trusted editor provenance and ignores incoming claims: "+label);
            }
            // Compatible legacy/default identity and branch-preserving aliases.
            foreach(var action in new[]{"block","pause"}) {
                Environment.SetEnvironmentVariable("VAULT_STORAGE_ROOT",Path.Combine(root,Guid.NewGuid().ToString()));var hub=new ConnectionHub();Roster(hub,"chrome","c");Roster(hub,"edge","e");hub.Link("chrome","c","edge","e");
                hub.ApplySync("chrome","c",Frame(Site("first.example")));
                hub.ApplySync("edge","e",Frame(Site("second.example",false,action,"site")));
                Check(Lines(hub).Count== (action=="block" ? 1:2),"Default entryID normalization unions only compatible Website action "+action);
                if(action=="block") Check(((JsonArray)Lines(hub)[0]!["sites"]!).Count==2,"Compatible explicit site identity preserves both listed targets");
                else Check(Lines(hub).Any(l=>l?["action"]?.GetValue<string>()=="pause") && Lines(hub).Any(l=>l?["action"]?.GetValue<string>()=="block"),"Incompatible pause and block actions both survive");
            }
            Environment.SetEnvironmentVariable("VAULT_STORAGE_ROOT",Path.Combine(root,Guid.NewGuid().ToString())); {
                var hub=new ConnectionHub();Roster(hub,"chrome","c");Roster(hub,"edge","e");hub.Link("chrome","c","edge","e");
                var reserved=Alias("edge","e","site");hub.ApplySync("chrome","c",Frame(Site("first.example"),Site("reserved.example",true,"pause",reserved)));
                hub.ApplySync("edge","e",Frame(Site("second.example",true)));
                Check(Lines(hub).Count==3 && Lines(hub).Any(l=>Key(l!)==reserved+"_2"),"Occupied incompatible deterministic Website alias uses suffix without overwriting");
                var previous=Lines(hub).ToJsonString();
                hub=Restart();Check(Lines(hub).ToJsonString()==previous,"Collision suffix is stable after restart");
            }
            Environment.SetEnvironmentVariable("VAULT_STORAGE_ROOT",Path.Combine(root,Guid.NewGuid().ToString())); {
                var hub=new ConnectionHub();Roster(hub,"chrome","c");Roster(hub,"edge","e");hub.Link("chrome","c","edge","e");
                hub.ApplySync("edge","e",Frame(Site("old.example",true)));
                var file=JsonNode.Parse(File.ReadAllText(Storage.ClustersPath))!;((JsonObject)file["value"]![0]!).Remove("scopeOrigins");File.WriteAllText(Storage.ClustersPath,file.ToJsonString());
                var clusterId=Snapshot(hub)["id"]!.GetValue<string>();hub=Restart();hub.ApplySync("chrome","c",Frame(Site("new.example")));
                var expected="site:linked_"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("retained\0"+clusterId+"\0site"))).ToLowerInvariant()[..24];
                Check(Lines(hub).Count==2 && Lines(hub).Any(l=>Key(l!)==expected),"Absent legacy origin map retains old incompatible branch with stable cluster fallback");
                var origins=(JsonObject)JsonNode.Parse(File.ReadAllText(Storage.ClustersPath))!["value"]![0]!["scopeOrigins"]!;
                Check(!origins.ContainsKey(expected) && origins["site"]!.GetValue<string>()=="chrome\0c\0site","Legacy fallback never fabricates member provenance");
            }
            Environment.SetEnvironmentVariable("VAULT_STORAGE_ROOT",Path.Combine(root,Guid.NewGuid().ToString())); {
                var hub=new ConnectionHub();foreach(var pair in new[]{("chrome","c"),("edge","e"),("safari","s")}) Roster(hub,pair.Item1,pair.Item2);
                hub.Link("chrome","c","edge","e");hub.Link("chrome","c","safari","s");
                var edge=Frame(Site("edge.example",true));edge["ts"]=0;edge["scalars"]!["allowedMinutes"]=1;edge["usageMs"]=100;edge["usageResetAtMs"]=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                var safari=Frame(Site("safari.example",true,"pause"));safari["ts"]=0;safari["scalars"]!["allowedMinutes"]=2;safari["usageMs"]=200;safari["usageResetAtMs"]=edge["usageResetAtMs"]!.DeepClone();
                hub.ApplySync("edge","e",edge);hub.ApplySync("safari","s",safari);var beforeReplay=Snapshot(hub)["shared"]!.ToJsonString();hub=Restart();hub.ApplySync("edge","e",edge);
                Check(Snapshot(hub)["shared"]!.ToJsonString()==beforeReplay,"Zero-timestamp original replay preserves other pending branches and budget after restart");
                var chrome=Frame(Site("chrome.example"));chrome["ts"]=0;hub.ApplySync("chrome","c",chrome);
                Check(Lines(hub).Count==3 && Lines(hub).Any(l=>Key(l!)=="site") && Lines(hub).Any(l=>Key(l!)==Alias("edge","e","site")) && Lines(hub).Any(l=>Key(l!)==Alias("safari","s","site")),"Late initiator retains all three originals with stable member aliases");
                var forwarded=(JsonArray)Lines(hub).DeepClone();hub.Unlink("safari","s");Roster(hub,"safari","s");hub.Link("chrome","c","safari","s");hub.ApplySync("safari","s",new(){["scopes"]=forwarded,["ts"]=0});
                Check(Lines(hub).Count==3,"Rejoin forwarding existing aliases deduplicates without reminting");
            }
            foreach(var invalid in new JsonNode?[]{JsonValue.Create("bad"),JsonValue.Create(3),new JsonObject{["site"]=3},null})
            {
                Environment.SetEnvironmentVariable("VAULT_STORAGE_ROOT",Path.Combine(root,Guid.NewGuid().ToString()));var hub=new ConnectionHub();Roster(hub,"chrome","c");Roster(hub,"edge","e");hub.Link("chrome","c","edge","e");hub.ApplySync("chrome","c",Frame(Site("news.example")));hub.ApplySync("edge","e",Frame(Site("safe.example",true)));
                var saved=JsonNode.Parse(File.ReadAllText(Storage.ClustersPath))!;saved["value"]![0]!["scopeOrigins"]=invalid?.DeepClone();File.WriteAllText(Storage.ClustersPath,saved.ToJsonString());var bytes=File.ReadAllText(Storage.ClustersPath);
                hub=Restart();Roster(hub,"chrome","c");Roster(hub,"edge","e");Check(hub.Link("chrome","c","edge","e")=="unsupported-storage" && File.ReadAllText(Storage.ClustersPath)==bytes,"Invalid present Website-origin map refuses writes and preserves registry bytes");
            }
        } finally { Environment.SetEnvironmentVariable("VAULT_STORAGE_ROOT",before);if(Directory.Exists(root)) Directory.Delete(root,true); }
    }
}
