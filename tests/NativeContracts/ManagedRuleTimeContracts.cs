using System.Text.Json.Nodes;
using WindowsBlocker.Bridge;
using WindowsBlocker.Core;
using System.Reflection;
using WindowsBlocker.WebUI;

// Screen-free native adapter checks. Pure RuleCore semantics are checked by the
// common JS suites; this controls the real async hub/persistence boundaries.
internal static class ManagedRuleTimeContracts
{
    public static async Task Run()
    {
        void Check(bool ok, string label) { if (!ok) throw new Exception(label); Console.WriteLine("PASS " + label); }
        var store = new WebStore();
        store.SaveRaw("{\"blockedGroups\":[{\"id\":\"w\",\"name\":\"Windows\",\"groupType\":\"custom\",\"enabled\":true}],\"cbRuleState\":{\"other\":{\"keep\":7}}}");
        store.CommitRuleDispatch(new Dictionary<string,string>{{"w","{\"done\":true}"}});
        var saved=File.ReadAllText(store.FilePath);
        Check(JsonNode.Parse(saved)?["cbRuleState"]?["w"]?["done"]?.GetValue<bool>()==true && JsonNode.Parse(saved)?["cbRuleState"]?["other"]?["keep"]?.GetValue<int>()==7,"native event writer saves completion and preserves unrelated state");
        using (var held=new FileStream(store.FilePath,FileMode.Open,FileAccess.Read,FileShare.Read)) {
            var refused=false;try {store.CommitRuleDispatch(new Dictionary<string,string>{{"w","{\"done\":false}"}});}catch(IOException){refused=true;}
            Check(refused && File.ReadAllText(store.FilePath)==saved,"native event writer exposes replacement failure and preserves completion bytes");
        }
        var rejected=false;try{store.CommitRuleDispatch(new Dictionary<string,string>{{"deleted","{}"}});}catch(InvalidDataException){rejected=true;}
        Check(rejected && File.ReadAllText(store.FilePath)==saved,"native event writer cannot resurrect a deleted group");
        JsonArray Roster(string id)=>new(new JsonObject{["id"]=id,["name"]=id});
        var hub=new ConnectionHub();hub.SetRoster("windowsapp",Roster("w"));hub.SetRoster("chrome",Roster("c"));
        Check(hub.Link("windowsapp","w","chrome","c")==null,"managed time adapter fixture links without starting a listener");
        var cluster=JsonNode.Parse(hub.ClustersJson())!["clusters"]![0]!["id"]!.GetValue<string>();
        var absent=await hub.SyncRuleTimeAsync("windowsapp","w",cluster,null,new());
        Check(absent["ready"]?.GetValue<bool>()==false,"native adapter fails closed until its trusted policy worker is available");
        var entered=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.RuleTimePolicy=async (method,args)=>{
            Check(method=="syncTime" && args[1] is JsonObject request && request["member"]?.GetValue<string>()=="windowsapp/w","native adapter binds the policy request to the pinned member");
            entered.TrySetResult(true);await release.Task.WaitAsync(TimeSpan.FromSeconds(5));
            return new(){["ok"]=true,["ready"]=true,["state"]=new JsonObject{["version"]=1,["objects"]=new JsonObject()},["acknowledgedSerial"]=1};
        };
        var pending=hub.SyncRuleTimeAsync("windowsapp","w",cluster,null,new());
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Check(hub.Unlink("chrome","c")==null,"membership can detach while policy work is awaiting its worker");
        release.TrySetResult(true);var stale=await pending.WaitAsync(TimeSpan.FromSeconds(5));
        Check(stale["ready"]?.GetValue<bool>()==false && stale["error"]?.GetValue<string>()=="custom-time-membership-changed","late worker result cannot acknowledge or resurrect a detached cluster");
        var remembered=new JsonObject{["version"]=1,["objects"]=new JsonObject(),["scope"]=cluster};
        var unknown=await hub.SyncRuleTimeAsync("windowsapp","w",null,remembered,new());
        Check(unknown["ready"]?.GetValue<bool>()==false && unknown["linked"]?.GetValue<bool>()==true,"missing registry cannot silently fork remembered shared time into a local allowance");
        await RetentionContracts(Check);
        HubFirstLinkContracts.Run(); // Existing screen-free unlink/budget/scope regressions; starts no listener.
        Console.WriteLine("WINDOWS_MANAGED_TIME_ADAPTER_PASS");
    }

    private static async Task RetentionContracts(Action<bool,string> Check)
    {
        // Real native registry/store boundaries, with an injected deterministic
        // policy result. Common-JS tests own detach/reducer semantics; these
        // tests exercise transfer ordering, persistence and ACK admission.
        var root = Path.Combine(Path.GetTempPath(), "vault-retention-contracts-" + Guid.NewGuid());
        var oldRoot = Environment.GetEnvironmentVariable("VAULT_STORAGE_ROOT");
        Environment.SetEnvironmentVariable("VAULT_STORAGE_ROOT", root);
        try
        {
            JsonArray Roster(string id) => new(new JsonObject { ["id"] = id, ["name"] = id });
            JsonObject State(int generation) => new() { ["version"] = 1, ["objects"] = new JsonObject {
                ["clock"] = new JsonObject { ["kind"] = "timer", ["direction"] = "down", ["durationMs"] = 10000,
                    ["elapsedMs"] = 0, ["startedAtMs"] = null, ["notified"] = false, ["generation"] = generation, ["incarnation"] = "fixture" } } };
            JsonArray Saved() => StorageSchema.Payload(JsonNode.Parse(File.ReadAllText(Storage.ClustersPath))!, "hub.clusters", 2).AsArray();
            ConnectionHub Reload()
            {
                var value = new ConnectionHub();
                typeof(ConnectionHub).GetMethod("Restore", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(value, null);
                return value;
            }
            var current = State(1);
            Task<JsonObject?> Policy(string method, object?[] args)
            {
                if (method == "syncTime") return Task.FromResult<JsonObject?>(new() { ["ok"] = true, ["ready"] = true, ["state"] = current.DeepClone(), ["acknowledgedSerial"] = 0 });
                if (method != "detachTime") throw new Exception("Unexpected policy route");
                var local = (JsonObject)((JsonObject?)args[0] ?? new JsonObject { ["version"] = 1, ["objects"] = new JsonObject() }).DeepClone();
                if ((bool)args[7]!) local.Remove("detachedReceipt");
                else { local["objects"] = ((JsonObject)args[1]!)["objects"]!.DeepClone(); local["scope"] = null;
                    local["detachedReceipt"] = new JsonObject { ["clusterId"] = (string)args[4]!, ["token"] = (string)args[5]! }; }
                return Task.FromResult<JsonObject?>(new() { ["ok"] = true, ["state"] = local });
            }
            var hub = new ConnectionHub(); hub.RuleTimePolicy = Policy;
            hub.SetRoster("chrome", Roster("c")); hub.SetRoster("edge", Roster("e"));
            Check(hub.Link("chrome", "c", "edge", "e") == null, "retention fixture links isolated browser members");
            var first = JsonNode.Parse(hub.ClustersJson())!["clusters"]![0]!["id"]!.GetValue<string>();
            await hub.SyncRuleTimeAsync("chrome", "c", first, State(0), new());
            Check(hub.Unlink("chrome", "c") == null && hub.ActiveClusterCount() == 0, "unlink retains final snapshots while hiding inactive cluster");
            var future = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 60000;
            var saved = Saved();
            foreach (var row in saved.OfType<JsonObject>()) foreach (var record in ((JsonObject)row["DetachedRuleTime"]!).Select(p => (JsonObject)p.Value!)) record["atMs"] = future;
            File.WriteAllText(Storage.ClustersPath, StorageSchema.Wrap(saved, "hub.clusters", 2).ToJsonString());
            hub = Reload(); hub.RuleTimePolicy = Policy; hub.SetRoster("chrome", Roster("c")); hub.SetRoster("safari", Roster("s"));
            Check(hub.Link("chrome", "c", "safari", "s") == null, "retained snapshot survives restart and permits relink preflight");
            var second = JsonNode.Parse(hub.ClustersJson())!["clusters"]![0]!["id"]!.GetValue<string>();
            current = State(2); await hub.SyncRuleTimeAsync("safari", "s", second, State(1), new());
            Check(hub.Unlink("chrome", "c") == null, "second unlink retains a distinct final snapshot");
            Check(Saved().OfType<JsonObject>().Single(c => c["Id"]!.GetValue<string>() == second)["DetachedRuleTime"]!["chrome/c"]!["atMs"]!.GetValue<long>() > future,
                "retained handover order advances despite clock reversal");
            hub = Reload(); hub.RuleTimePolicy = Policy;
            var stale = State(0); stale["scope"] = second;
            var oldest = await hub.SyncRuleTimeAsync("chrome", "c", second, stale, new());
            Check(oldest["clusterId"]!.GetValue<string>() == first && oldest["handoverPending"]!.GetValue<bool>(), "oldest retained snapshot outranks newer requested and saved scopes");
            var token = oldest["state"]!["detachedReceipt"]!["token"]!.GetValue<string>();
            var retry = await hub.SyncRuleTimeAsync("chrome", "c", second, stale, new());
            Check(retry["state"]!["detachedReceipt"]!["token"]!.GetValue<string>() == token, "lost transfer reply retains the same token without acknowledgment");
            var acknowledged = await hub.SyncRuleTimeAsync("chrome", "c", second, (JsonObject)oldest["state"]!, new());
            Check(acknowledged["clusterId"]!.GetValue<string>() == first && acknowledged["state"]!["detachedReceipt"] == null && acknowledged["handoverPending"]!.GetValue<bool>(), "saved receipt acknowledges its exact transfer before draining the chain");
            var latest = await hub.SyncRuleTimeAsync("chrome", "c", first, (JsonObject)acknowledged["state"]!, new());
            Check(latest["clusterId"]!.GetValue<string>() == second && latest["state"]!["objects"]!["clock"]!["generation"]!.GetValue<int>() == 2,
                "newer final state is applied after older handover and cannot roll backward");
            var latestAck = await hub.SyncRuleTimeAsync("chrome", "c", null, (JsonObject)latest["state"]!, new());
            Check(!latestAck["handoverPending"]!.GetValue<bool>() && !Saved().OfType<JsonObject>().Any(c => c["DetachedRuleTime"]?["chrome/c"] != null),
                "acknowledged member is durably removed while other offline members remain retained");

            var supportedRegistry = File.ReadAllText(Storage.ClustersPath);
            foreach (var invalid in new[] { "namespace-version", "namespace-objects", "total-count", "total-bytes" })
            {
                var rows = Saved();
                var row = rows.OfType<JsonObject>().First(c => ((JsonObject)c["DetachedRuleTime"]!).Count > 0);
                var record = ((JsonObject)row["DetachedRuleTime"]!).First().Value!.AsObject();
                if (invalid == "namespace-version") record["state"]!["version"] = 2;
                if (invalid == "namespace-objects") record["state"]!["objects"] = new JsonArray();
                if (invalid == "total-bytes") record["state"]!["padding"] = new string('x', 8 * 1024 * 1024);
                if (invalid == "total-count")
                {
                    var template = (JsonObject)row.DeepClone(); rows = new JsonArray();
                    for (var i = 0; i < 65; i++)
                    {
                        var clone = (JsonObject)template.DeepClone(); clone["Id"] = "oversized-" + i;
                        var records = new JsonObject();
                        for (var j = 0; j < 16; j++) records["chrome/c" + j] = record.DeepClone();
                        clone["DetachedRuleTime"] = records; rows.Add(clone);
                    }
                }
                var unsupported = StorageSchema.Wrap(rows,"hub.clusters",2).ToJsonString(); File.WriteAllText(Storage.ClustersPath, unsupported);
                var guarded = Reload(); guarded.RuleTimePolicy = (_,_) => throw new Exception("Unsupported retained state reached policy ACK");
                var answer = await guarded.SyncRuleTimeAsync("chrome", "c", null, new JsonObject { ["version"] = 1, ["objects"] = new JsonObject(),
                    ["detachedReceipt"] = new JsonObject { ["clusterId"] = row["Id"]!.DeepClone(), ["token"] = record["token"]!.DeepClone() } },new());
                Check(answer["ready"]?.GetValue<bool>() == false && File.ReadAllText(Storage.ClustersPath) == unsupported,
                    "Restore preserves unsupported retained " + invalid + " and refuses ACK/writes");
                File.WriteAllText(Storage.ClustersPath, supportedRegistry);
            }

            // Normal unlink IO failure rolls back BOTH existing canonical bytes
            // and staged hub state; it emits no success snapshot and can retry.
            Environment.SetEnvironmentVariable("VAULT_STORAGE_ROOT", Path.Combine(root, "save-failure"));
            var store = new WebStore(); store.SaveRaw("{\"blockedGroups\":[{\"id\":\"w\",\"name\":\"Windows\",\"groupType\":\"custom\",\"enabled\":true}],\"cbRuleState\":{\"w\":{\"keep\":9}}}");
            hub = new ConnectionHub(store); hub.RuleTimePolicy = Policy;
            hub.SetRoster("windowsapp", Roster("w")); hub.SetRoster("chrome", Roster("c"));
            Check(hub.Link("windowsapp", "w", "chrome", "c") == null, "save-failure fixture links native and browser members");
            var id = JsonNode.Parse(hub.ClustersJson())!["clusters"]![0]!["id"]!.GetValue<string>();
            await hub.SyncRuleTimeAsync("windowsapp", "w", id, State(0), new());
            var canonical = File.ReadAllBytes(store.FilePath); var registry = File.ReadAllBytes(Storage.ClustersPath);
            var broadcasts = 0; hub.ClusterSnapshotCapturedForTests = _ => broadcasts++;
            hub.UnlinkDiskPhaseForTests = _ => throw new IOException("injected unlink registry failure");
            var refused = false;
            try { hub.Unlink("chrome", "c"); } catch (IOException) { refused = true; }
            Check(refused, "unlink exposes retained-registry save failure instead of success");
            Check(hub.ActiveClusterCount() == 1 && canonical.SequenceEqual(File.ReadAllBytes(store.FilePath)) && registry.SequenceEqual(File.ReadAllBytes(Storage.ClustersPath)),
                "failed unlink restores original membership and both saved files");
            Check(broadcasts == 0, "failed unlink publishes no success membership snapshot");
            hub.UnlinkDiskPhaseForTests = null;
            Check(hub.Unlink("chrome", "c") == null && hub.ActiveClusterCount() == 0 && Saved().OfType<JsonObject>().Any(c => c["DetachedRuleTime"]?["chrome/c"] != null),
                "restored unlink retries and durably retains the offline final snapshot");
            hub.SetRoster("edge", Roster("e"));
            var beforeLink = File.ReadAllBytes(Storage.ClustersPath); broadcasts = 0;
            hub.LinkDiskPhaseForTests = _ => throw new IOException("injected link registry failure");
            refused = false; try { hub.Link("chrome", "c", "edge", "e"); } catch (IOException) { refused = true; }
            Check(refused && hub.ActiveClusterCount() == 0 && beforeLink.SequenceEqual(File.ReadAllBytes(Storage.ClustersPath)) && broadcasts == 0,
                "failed new link restores retained registry and publishes no success");
            hub.LinkDiskPhaseForTests = null;
            Check(hub.Link("chrome", "c", "edge", "e") == null && hub.ActiveClusterCount() == 1, "failed new link can retry after its original state is restored");
            hub.SetRoster("safari", Roster("s")); beforeLink = File.ReadAllBytes(Storage.ClustersPath); broadcasts = 0;
            hub.LinkDiskPhaseForTests = _ => throw new IOException("injected third-member link registry failure");
            refused = false; try { hub.Link("chrome", "c", "safari", "s"); } catch (IOException) { refused = true; }
            Check(refused && hub.ActiveClusterCount() == 1 && beforeLink.SequenceEqual(File.ReadAllBytes(Storage.ClustersPath)) && broadcasts == 0 &&
                JsonNode.Parse(hub.ClustersJson())!["clusters"]![0]!["members"]!.AsArray().Count == 2,
                "failed third-member link preserves original membership and exact saved registry");
        }
        finally { Environment.SetEnvironmentVariable("VAULT_STORAGE_ROOT", oldRoot); }
    }
}
