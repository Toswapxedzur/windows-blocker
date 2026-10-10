using System.Text.Json.Nodes;
using WindowsBlocker.Bridge;
using WindowsBlocker.Core;
using WindowsBlocker.Enforcement;
using WindowsBlocker.WebUI;

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
        UnlinkContracts();
        SameEntrySiteJoinContracts();
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
    static void SameEntrySiteJoinContracts()
    {
        foreach(var restart in new[]{false,true})
        {
            var hub=new ConnectionHub();Roster(hub,"windowsapp","w");Roster(hub,"chrome","c");hub.Link("windowsapp","w","chrome","c");
            var anchor=DateTimeOffset.Now.ToUnixTimeMilliseconds();var native=Frame(true);native["usageMs"]=120_000;native["usageResetAtMs"]=anchor;hub.ApplySync("windowsapp","w",native);
            var chrome=Frame(false);chrome["scopes"]![0]!["action"]="block";chrome["scopes"]![0]!["sitesExcept"]=false;chrome["usageMs"]=300_000;chrome["usageResetAtMs"]=anchor;hub.ApplySync("chrome","c",chrome);
            Roster(hub,"edge","e");hub.Link("chrome","c","edge","e");
            if(restart) { hub=new ConnectionHub();typeof(ConnectionHub).GetMethod("Restore",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(hub,null); }
            var edge=Frame(false);edge["scopes"]![0]!["sites"]=new JsonArray("example.org");edge["scopes"]![0]!["action"]="block";edge["scopes"]![0]!["sitesExcept"]=false;edge["usageMs"]=480_000;edge["usageResetAtMs"]=anchor;hub.ApplySync("edge","e",edge);
            var scopes=(JsonArray)Snapshot(hub)["shared"]!["scopes"]!;var sites=scopes.OfType<JsonObject>().Where(s=>s["surface"]?.GetValue<string>()=="site").SelectMany(s=>((JsonArray)s["sites"]!).Select(v=>v!.GetValue<string>())).ToHashSet();
            Check(sites.SetEquals(new[]{"example.com","example.org"}) && scopes.Any(s=>s?["surface"]?.GetValue<string>()=="apps"),"A delayed third browser contributes its original sites without deleting the first browser targets: restart="+restart);
            Check(hub.SharedUsage("w")?.Ms==480_000,"Same-entry three-member site join preserves the largest original shared usage: restart="+restart);
            hub.ApplySync("edge","e",new(){["scopes"]=edge["scopes"]!.DeepClone(),["ts"]=100});
            var edited=(JsonArray)Snapshot(hub)["shared"]!["scopes"]!;Check(((JsonArray)edited.OfType<JsonObject>().Single(s=>s["surface"]?.GetValue<string>()=="site")["sites"]!).Select(v=>v!.GetValue<string>()).SequenceEqual(new[]{"example.org"}),"An acknowledged ordinary site edit replaces targets instead of resurrecting original sites");
            hub.ApplySync("chrome","c",chrome);edited=(JsonArray)Snapshot(hub)["shared"]!["scopes"]!;
            Check(((JsonArray)edited.OfType<JsonObject>().Single(s=>s["surface"]?.GetValue<string>()=="site")["sites"]!).Count==1,"Late completed-peer originals cannot resurrect a deliberately removed site");
        }
    }
    static void UnlinkContracts()
    {
        foreach(var enabled in new[]{false,true}) foreach(var rolling in new[]{false,true}) foreach(var nativeLeaves in new[]{false,true}) foreach(var thirdBrowser in new[]{false,true})
        {
            var now=DateTimeOffset.Now.ToUnixTimeMilliseconds(); var minute=UsageBudget.BucketStartMs(now);
            var key=minute.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var store=new WebStore();
            var group=new JsonObject{["id"]="w",["name"]="Native",["enabled"]=enabled,["mode"]="after-minutes",["allowedMinutes"]=10,["resetIntervalHours"]=24,["rollingLimit"]=rolling,["scopes"]=new JsonArray(new JsonObject{["surface"]="apps",["apps"]=new JsonArray()})};
            store.SaveRaw(new JsonObject{["blockedGroups"]=new JsonArray(group),["usageTimersMs"]=new JsonObject{["w"]=1000},["usageResetAtMs"]=new JsonObject{["w"]=now},["usageBucketsMs"]=new JsonObject{["w"]=new JsonObject{[key]=1000}}}.ToJsonString());
            var hub=new ConnectionHub();hub.ReconcileLocal(store);Roster(hub,"chrome","c");hub.Link("chrome","c","windowsapp","w");
            var browser=Frame(false);browser["scalars"]=new JsonObject{["name"]="Shared",["enabled"]=enabled,["mode"]="after-minutes",["allowedMinutes"]=10,["resetIntervalHours"]=24,["rollingLimit"]=rolling};browser["usageMs"]=120_000;browser["usageResetAtMs"]=now;
            if(rolling) browser["usageBucketsSeed"]=new JsonObject{[key]=120_000};
            var snooze=new JsonObject{["kind"]="budget",["startsAtMs"]=now-1000,["untilMs"]=now+300_000,["cooldownUntilMs"]=now+330_000,["extraMs"]=300_000,["changedAtMs"]=now};
            browser["snooze"]=snooze.DeepClone();browser["snoozeTs"]=now;hub.ApplySync("chrome","c",browser);hub.ReconcileLocal(store);
            if(thirdBrowser) { Roster(hub,"edge","e");hub.Link("chrome","c","edge","e");var joining=(JsonObject)browser.DeepClone();hub.ApplySync("edge","e",joining);hub.ReconcileLocal(store); }
            var label=$"enabled={enabled}, rolling={rolling}, nativeLeaves={nativeLeaves}, thirdBrowser={thirdBrowser}";
            Check(store.LoadUsageTimers().TimersMs.GetValueOrDefault("w")==120_000,"Adoption persists authoritative shared usage regardless of enabled: "+label);
            var timers=store.LoadUsageTimers();var imported=store.ImportedGroups()!.Groups;
            var usage=new Dictionary<string,double>();var resets=new Dictionary<string,double>();var buckets=new Dictionary<string,Dictionary<double,double>>();
            var delta=new JsonObject{["usageTransferId"]="final-before-unlink"};
            if(rolling) delta["usageBuckets"]=new JsonObject{[key]=1000};else {delta["usageDeltaMs"]=1000;delta["usageDeltaAnchorMs"]=hub.SharedUsage("w")!.Value.ResetAtMs;}
            hub.ApplySync("chrome","c",delta);hub.ApplySync("chrome","c",delta);
            EnforcementEngine.AdoptLinkedUsage(hub,imported,timers,new(),DateTimeOffset.Now,usage,resets,buckets);
            Check(timers.TimersMs["w"]==121_000 && usage.GetValueOrDefault("w")==121_000,"Enforcement adopts browser changes for disabled as well as enabled linked budgets: "+label);
            if(!enabled && !rolling && !nativeLeaves && !thirdBrowser)
            {
                var original=File.ReadAllText(store.FilePath);Directory.CreateDirectory(store.FilePath+".tmp");bool failed=false;
                try { hub.Unlink("chrome","c"); } catch(Exception error) when(error is IOException or UnauthorizedAccessException) { failed=true; }
                finally { Directory.Delete(store.FilePath+".tmp"); }
                Check(failed && hub.SharedUsage("w")?.Ms==121_000 && File.ReadAllText(store.FilePath)==original,"A failed local usage write preserves the active link and original saved bytes");
                var future=JsonNode.Parse(original)!;future["schemaVersion"]=999;var preserved=future.ToJsonString();File.WriteAllText(store.FilePath,preserved);failed=false;
                try { hub.Unlink("chrome","c"); } catch(InvalidDataException) { failed=true; }
                Check(failed && hub.SharedUsage("w")?.Ms==121_000 && File.ReadAllText(store.FilePath)==preserved,"Unsupported local storage refuses unlink and preserves both data and membership");
                File.WriteAllText(store.FilePath,original);
                File.WriteAllText(store.FilePath,"{");failed=false;
                try { hub.Unlink("chrome","c"); } catch(System.Text.Json.JsonException) { failed=true; }
                Check(failed && hub.SharedUsage("w")?.Ms==121_000 && File.ReadAllText(store.FilePath)=="{","Malformed local storage refuses unlink without falling back to an empty document");
                File.Delete(store.FilePath);failed=false;
                try { hub.Unlink("chrome","c"); } catch(InvalidDataException) { failed=true; }
                Check(failed && hub.SharedUsage("w")?.Ms==121_000 && !File.Exists(store.FilePath),"Missing local storage refuses unlink without creating replacement data");
                var missingGroup=JsonNode.Parse(original)!;missingGroup["blockedGroups"]=new JsonArray();preserved=missingGroup.ToJsonString();File.WriteAllText(store.FilePath,preserved);failed=false;
                try { hub.Unlink("chrome","c"); } catch(InvalidDataException) { failed=true; }
                Check(failed && hub.SharedUsage("w")?.Ms==121_000 && File.ReadAllText(store.FilePath)==preserved,"Missing native group snapshot preserves the active link");
                File.WriteAllText(store.FilePath,original);
            }
            Check(hub.Unlink(nativeLeaves ? "windowsapp" : "chrome",nativeLeaves ? "w" : "c")==null,"Explicit unlink succeeds: "+label);
            Check(store.LoadUsageTimers().TimersMs["w"]==121_000,"Unlink persists final shared usage before detachment without duplicating transfer: "+label);
            hub.ReconcileLocal(store);hub.ReconcileLocal(store);
            var retained=store.LoadUsageTimers();var raw=JsonNode.Parse(store.LoadRawJson()!)!;
            Check(retained.TimersMs["w"]==121_000 && retained.ResetAtMs["w"]==now,"Repeated unlinked reconciliation keeps usage and current period: "+label);
            Check(raw["groupSnoozes"]?["w"]?.ToJsonString()==snooze.ToJsonString(),"Unlink retains budget snooze and cooldown exactly: "+label);
            if(rolling) Check(retained.BucketsMs["w"].GetValueOrDefault(minute)==121_000,"Unlink retains rolling minute history: "+label);
            Check(hub.ActiveClusterCount()==(thirdBrowser ? 1 : 0),"Unlink preserves the other members of a three-member cluster: "+label);
            if(thirdBrowser && !nativeLeaves) { Check(hub.Unlink("edge","e")==null,"Remaining browser can also unlink: "+label);hub.ReconcileLocal(store);raw=JsonNode.Parse(store.LoadRawJson()!)!; }
            Check(((JsonArray)raw["blockedGroups"]![0]!["scopes"]!).All(s=>s?["surface"]?.GetValue<string>()=="apps"),"Unlink keeps native Apps lines only: "+label);
            var restarted=new ConnectionHub(store);typeof(ConnectionHub).GetMethod("Restore",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(restarted,null);restarted.ReconcileLocal(store);
            Check(store.LoadUsageTimers().TimersMs["w"]==121_000,"Restart preserves detached shared usage: "+label);
        }
        foreach(var rolling in new[]{false,true})
        {
            var now=DateTimeOffset.Now.ToUnixTimeMilliseconds();var current=UsageBudget.BucketStartMs(now);var expired=current-172_800_000;
            var store=new WebStore();store.SaveRaw(new JsonObject{["blockedGroups"]=new JsonArray(new JsonObject{["id"]="w",["name"]="Disabled",["enabled"]=false,["mode"]="after-minutes",["resetIntervalHours"]=24,["rollingLimit"]=rolling,["scopes"]=new JsonArray(new JsonObject{["surface"]="apps"})}),["usageTimersMs"]=new JsonObject{["w"]=1000},["usageResetAtMs"]=new JsonObject{["w"]=now},["usageBucketsMs"]=new JsonObject{["w"]=new JsonObject{[current.ToString(System.Globalization.CultureInfo.InvariantCulture)]=1000}}}.ToJsonString());
            var hub=new ConnectionHub(store);hub.ReconcileLocal(store);Roster(hub,"chrome","c");hub.Link("chrome","c","windowsapp","w");
            var browser=Frame(false);browser["scalars"]=new JsonObject{["name"]="Shared",["enabled"]=false,["mode"]="after-minutes",["resetIntervalHours"]=24,["rollingLimit"]=rolling};browser["usageResetAtMs"]=now;browser["usageMs"]=1000;if(rolling) browser["usageBucketsSeed"]=new JsonObject{[current.ToString(System.Globalization.CultureInfo.InvariantCulture)]=1000};
            hub.ApplySync("chrome","c",browser);hub.ReconcileLocal(store);
            store.Merge(new(){["usageTimersMs"]=new JsonObject{["w"]=900_000},["usageResetAtMs"]=new JsonObject{["w"]=now-172_800_000},["usageBucketsMs"]=new JsonObject{["w"]=new JsonObject{[expired.ToString(System.Globalization.CultureInfo.InvariantCulture)]=900_000}}});
            hub.SetRoster("chrome",new JsonArray());hub.ReconcileLocal(store);
            var usage=store.LoadUsageTimers();
            Check(usage.TimersMs["w"]==1000 && usage.ResetAtMs["w"]==now,"Roster removal retains the current shared period without reviving expired original usage: rolling="+rolling);
            if(rolling) Check(usage.BucketsMs["w"].Count==1 && usage.BucketsMs["w"].GetValueOrDefault(current)==1000,"Detached rolling storage excludes expired original buckets");
        }
        var finalStore=new WebStore();finalStore.SaveRaw("""{"blockedGroups":[{"id":"w","name":"Local","enabled":false,"mode":"after-minutes","allowedMinutes":10,"resetIntervalHours":24,"scopes":[{"surface":"apps","apps":[{"id":"C:\\Fixture\\editor.exe","name":"Editor"}]}]}]}""");
        var finalHub=new ConnectionHub(finalStore);finalHub.ReconcileLocal(finalStore);Roster(finalHub,"chrome","c");finalHub.Link("chrome","c","windowsapp","w");
        var finalAt=DateTimeOffset.Now.ToUnixTimeMilliseconds();var initial=Frame(false);initial["usageMs"]=20_000;initial["usageResetAtMs"]=finalAt;initial["snooze"]=new JsonObject{["kind"]="budget",["changedAtMs"]=finalAt,["startsAtMs"]=finalAt,["untilMs"]=finalAt+300000,["cooldownUntilMs"]=finalAt+330000,["extraMs"]=300000};initial["snoozeTs"]=finalAt;
        finalHub.ApplySync("chrome","c",initial);finalHub.ReconcileLocal(finalStore);finalStore.Merge(new(){["groupSnoozeTotalsMs"]=new JsonObject{["w"]=777}});
        var finalScalars=(JsonObject)Snapshot(finalHub)["shared"]!["scalars"]!.DeepClone();finalScalars["name"]="Final shared settings";finalScalars["pauseSeconds"]=42;
        finalHub.ApplySync("chrome","c",new(){["scalars"]=finalScalars,["ts"]=finalAt+1,["snooze"]=new JsonObject(),["snoozeTs"]=finalAt+1,["lock"]=new JsonObject{["lockVersion"]=1},["lockBase"]=0});
        Check(finalHub.Unlink("windowsapp","w")==null,"Immediate unlink after a shared settings update succeeds");
        var finalDocument=JsonNode.Parse(finalStore.LoadRawJson()!)!;var finalGroup=finalDocument["blockedGroups"]![0]!;
        Check(finalGroup["name"]!.GetValue<string>()=="Final shared settings" && finalGroup["pauseSeconds"]!.GetValue<int>()==42 && finalGroup["lockVersion"]!.GetValue<int>()==1,"Detach preserves the final shared scalars and lock version before a reconciliation tick");
        Check(finalDocument["groupSnoozes"]?["w"]==null && finalDocument["groupSnoozeTotalsMs"]!["w"]!.GetValue<double>()==0,"Cleared shared snooze and zero total replace stale local snooze at detachment");
        Check(finalGroup["scopes"]![0]!["apps"]![0]!["id"]!.GetValue<string>()==@"C:\Fixture\editor.exe","Final snapshot retains exact native Apps targets");
        var lockStore=new WebStore();lockStore.SaveRaw("""{"blockedGroups":[{"id":"w","name":"Local","scopes":[{"surface":"apps","apps":[]}],"lockVersion":1,"lockWaitHours":2}]}""");
        var lockHub=new ConnectionHub(lockStore);lockHub.ReconcileLocal(lockStore);Roster(lockHub,"chrome","c");lockHub.Link("windowsapp","w","chrome","c");lockHub.ApplySync("chrome","c",Frame(false));lockHub.ReconcileLocal(lockStore);
        lockStore.Update(root=>{var local=(JsonObject)root["blockedGroups"]![0]!;local["lockVersion"]=2;local["lockWaitHours"]=8;local["parentalPasswordHash"]="fixture-hash";local["parentalPasswordSalt"]="fixture-salt";});
        lockStore.Update(root=>root["blockedGroups"]![0]!["lockedAtMs"]=DateTimeOffset.Now.ToUnixTimeMilliseconds());
        Check(lockHub.Unlink("windowsapp","w")=="group-locked" && lockHub.Unlink("chrome","c")=="group-locked" && lockHub.ActiveClusterCount()==1,"A newer local active lock refuses native and remote explicit unlink before shared reconciliation");
        lockStore.Update(root=>((JsonObject)root["blockedGroups"]![0]!).Remove("lockedAtMs"));
        Check(lockHub.Unlink("windowsapp","w")==null,"Unlink with a newer unreported local lock configuration succeeds");
        var lockGroup=JsonNode.Parse(lockStore.LoadRawJson()!)!["blockedGroups"]![0]!;
        Check(lockGroup["lockVersion"]!.GetValue<int>()==2 && lockGroup["lockWaitHours"]!.GetValue<int>()==8 && lockGroup["parentalPasswordHash"]!.GetValue<string>()=="fixture-hash" && lockGroup["parentalPasswordSalt"]!.GetValue<string>()=="fixture-salt","Detach cannot replace a newer local lock unit with stale shared protection settings");
        var removedStore=new WebStore();removedStore.SaveRaw("""{"blockedGroups":[{"id":"w","scopes":[{"surface":"apps","apps":[]}],"lockVersion":1}]}""");
        var removedHub=new ConnectionHub(removedStore);removedHub.ReconcileLocal(removedStore);Roster(removedHub,"chrome","c");removedHub.Link("windowsapp","w","chrome","c");removedHub.ApplySync("chrome","c",Frame(false));removedHub.ReconcileLocal(removedStore);
        removedStore.Update(root=>{var local=(JsonObject)root["blockedGroups"]![0]!;local["lockVersion"]=2;local["lockedAtMs"]=finalAt;local["lockWaitHours"]=8;});
        removedHub.SetRoster("chrome",new JsonArray());
        var protectedGroup=JsonNode.Parse(removedStore.LoadRawJson()!)!["blockedGroups"]![0]!;
        Check(removedHub.ActiveClusterCount()==0 && protectedGroup["lockVersion"]!.GetValue<int>()==2 && protectedGroup["lockedAtMs"]!.GetValue<long>()==finalAt && protectedGroup["lockWaitHours"]!.GetValue<int>()==8,"Deleted remote group can leave the cluster while newer local protection stays intact");
        var unlockedStore=new WebStore();unlockedStore.SaveRaw("""{"blockedGroups":[{"id":"w","scopes":[{"surface":"apps","apps":[]}],"lockVersion":1}]}""");
        var unlockedHub=new ConnectionHub(unlockedStore);unlockedHub.ReconcileLocal(unlockedStore);Roster(unlockedHub,"chrome","c");unlockedHub.Link("windowsapp","w","chrome","c");unlockedHub.ApplySync("chrome","c",Frame(false));unlockedHub.ReconcileLocal(unlockedStore);
        unlockedStore.Update(root=>root["blockedGroups"]![0]!["lockedAtMs"]=finalAt);
        unlockedHub.ApplySync("chrome","c",new(){["lock"]=new JsonObject{["lockVersion"]=2,["lockWaitHours"]=4},["lockBase"]=1});
        Check(unlockedHub.Unlink("windowsapp","w")==null,"A newer authoritative shared unlock permits unlink despite an older local locked snapshot");
        var unlockedGroup=JsonNode.Parse(unlockedStore.LoadRawJson()!)!["blockedGroups"]![0]!;
        Check(unlockedGroup["lockedAtMs"]==null && unlockedGroup["lockVersion"]!.GetValue<int>()==2 && unlockedGroup["lockWaitHours"]!.GetValue<int>()==4,"Newer shared lock configuration is adopted atomically before detachment");



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
        foreach(var staleAnchor in new[]{true,false})
        {
            var rollingJoin=new ConnectionHub();Roster(rollingJoin,"windowsapp","w");Roster(rollingJoin,"chrome","c");rollingJoin.Link("windowsapp","w","chrome","c");
            var at=DateTimeOffset.Now.ToUnixTimeMilliseconds();var minute=Math.Floor(at/60000d)*60000;
            var currentKey=minute.ToString(System.Globalization.CultureInfo.InvariantCulture);var expiredKey=(minute-60000).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var rollingOriginal=Frame(true);rollingOriginal["scalars"]!["rollingLimit"]=true;rollingOriginal["usageResetAtMs"]=at-2000;rollingOriginal["usageBucketsSeed"]=new JsonObject{[currentKey]=120_000};rollingJoin.ApplySync("windowsapp","w",rollingOriginal);
            var rollingEdit=Frame(true);rollingEdit["scalars"]!["rollingLimit"]=true;rollingEdit["scalars"]!["allowedMinutes"]=30;rollingEdit["ts"]=at+1;rollingJoin.ApplySync("windowsapp","w",rollingEdit);
            rollingJoin.ReportLocalUsage("w",0,0,bucketDeltas:new Dictionary<double,double>{[minute]=1000});
            var oldRolling=Frame(false);oldRolling["scalars"]!["rollingLimit"]=true;oldRolling["usageBucketsSeed"]=new JsonObject{[currentKey]=300_000,[expiredKey]=900_000};if(staleAnchor) oldRolling["usageResetAtMs"]=at-2000;
            rollingJoin.ApplySync("chrome","c",oldRolling);
            var total=rollingJoin.SharedUsage("w")!.Value.Buckets.Values.Sum();
            Check(total==1000,staleAnchor ? "Rolling restart rejects stale anchored original history" : "Rolling restart rejects original history without a reset anchor");
        }
    }
}
