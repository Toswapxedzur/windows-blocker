using System.Text.Json.Nodes;
using WindowsBlocker.Bridge;
using WindowsBlocker.Rules;
using WindowsBlocker.WebUI;

// A console-only runner: actual native admission and RuleEngine, with canonical
// policy evaluated by a separate Mini2 Node process over bounded JSON stdio.
internal static class Program
{
    static int sequence;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static JsonObject Copy(JsonObject value) => (JsonObject)value.DeepClone();
    static JsonObject Document(WebStore store) => (JsonObject)JsonNode.Parse(store.LoadRawJson()!)!;
    static JsonObject Group(WebStore store, string id = "w") => Copy(Document(store)["blockedGroups"]!.AsArray().OfType<JsonObject>().Single(g => g["id"]!.GetValue<string>() == id));
    static JsonObject Shared(ConnectionHub hub) => Copy(JsonNode.Parse(hub.ClustersJson())!["clusters"]![0]!["shared"]!.AsObject());
    static async Task<JsonNode?> Policy(string module, string method, object?[] args)
    {
        var id = ++sequence;
        Console.WriteLine(new JsonObject { ["kind"] = "policy", ["id"] = id, ["module"] = module, ["method"] = method,
            ["args"] = System.Text.Json.JsonSerializer.SerializeToNode(args) }.ToJsonString());
        var line = await Console.In.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20));
        if (line == null || line.Length > 1_048_576) throw new Exception("policy-response-unavailable");
        var response = JsonNode.Parse(line)!.AsObject();
        if (response["id"]!.GetValue<int>() != id) throw new Exception("policy-response-id-mismatch");
        return response["error"] != null ? new JsonObject { ["error"] = response["error"]!.DeepClone() } : response["value"]?.DeepClone();
    }
    static async Task<JsonObject> Change(JsonObject? before, JsonObject? after) =>
        (await Policy("CBGroupScopes", "editorChange", [before, after, "desktop"]))!.AsObject();
    static async Task<JsonObject> Request(JsonObject before, Action<JsonObject> edit, string? intent = null)
    {
        var after = Copy(before); edit(after);
        var request = new JsonObject { ["changes"] = new JsonArray(await Change(before, after)) };
        if (intent != null) request["intent"] = intent;
        return request;
    }
    static JsonObject Apps(string path = "C:\\Fixture\\old.exe") => new() { ["surface"] = "apps", ["id"] = "apps-1", ["apps"] = new JsonArray(new JsonObject { ["id"] = path, ["name"] = "Fixture app" }), ["appsExcept"] = false, ["action"] = "block" };
    static JsonObject Site(string site, bool except = false) => new() { ["surface"] = "site", ["id"] = "site-1", ["sites"] = new JsonArray(site), ["sitesExcept"] = except, ["action"] = "block" };
    static JsonObject NativeGroup(string id = "w") => new() { ["id"] = id, ["name"] = "Fixture " + id, ["enabled"] = true, ["mode"] = "budget", ["groupType"] = "site", ["allowedMinutes"] = 20, ["resetIntervalHours"] = 24, ["resetAtMidnight"] = false, ["rollingLimit"] = false, ["scopes"] = new JsonArray(Apps()) };
    static JsonObject Frame(double minutes, double ts, JsonArray? scopes = null) => new() {
        ["scalars"] = new JsonObject { ["name"] = "Fixture w", ["enabled"] = true, ["mode"] = "budget", ["allowedMinutes"] = minutes, ["resetIntervalHours"] = 24, ["resetAtMidnight"] = false, ["rollingLimit"] = false },
        ["scopes"] = scopes ?? new JsonArray(Site("example.com")), ["ts"] = ts
    };
    static (WebStore Store, ConnectionHub Hub, NativePolicyTools Tools) Seed(bool linked = true)
    {
        var store = new WebStore(); store.SaveRaw(new JsonObject { ["blockedGroups"] = new JsonArray(NativeGroup()), ["quickAddGroupId"] = "w",
            ["cbRuleState"] = new JsonObject { ["w"] = new JsonObject { ["events"] = 3 } }, ["cbRuleLog"] = new JsonObject { ["w"] = new JsonArray("keep until deletion") },
            ["usageTimersMs"] = new JsonObject { ["w"] = 4321 }, ["usageResetAtMs"] = new JsonObject { ["w"] = DateTimeOffset.Now.ToUnixTimeMilliseconds() },
            ["usageBucketsMs"] = new JsonObject { ["w"] = new JsonObject { [DateTimeOffset.Now.ToUnixTimeMilliseconds().ToString()] = 4321 } }, ["groupSnoozes"] = new JsonObject { ["w"] = new JsonObject() } }.ToJsonString());
        var hub = new ConnectionHub(store); var tools = new NativePolicyTools(store, hub, new RuleEngine(store)) { Policy = Policy };
        if (linked) {
            hub.SetRoster("windowsapp", new JsonArray(new JsonObject { ["id"] = "w" })); hub.SetRoster("chrome", new JsonArray(new JsonObject { ["id"] = "c" }));
            Check(hub.Link("chrome", "c", "windowsapp", "w") == null, "Seed link refused");
            hub.ApplySync("chrome", "c", Frame(20, 0)); hub.ApplySync("windowsapp", "w", Frame(20, 0, new JsonArray(Apps()))); hub.ReconcileLocal(store);
        }
        return (store, hub, tools);
    }
    static JsonObject RegistryCluster() => Copy(JsonNode.Parse(File.ReadAllText(Storage.ClustersPath))!["value"]![0]!.AsObject());
    static JsonObject Runtime(JsonObject cluster)
    { var value = Copy(cluster); foreach (var key in new[] { "Name", "Scalars", "Scopes", "Timestamp" }) value.Remove(key); return value; }
    static async Task Refuse(NativePolicyTools tools, JsonObject request, string fragment)
    { try { await tools.ApplyEditorRequest(request); } catch (Exception ex) { Check(ex.Message.Contains(fragment, StringComparison.Ordinal), "Unexpected refusal: " + ex.Message); return; } throw new Exception("Edit falsely acknowledged: " + fragment); }
    static async Task Main()
    {
        var previousRoot = Environment.GetEnvironmentVariable("VAULT_STORAGE_ROOT"); var previousEnvironment = Environment.GetEnvironmentVariable("VAULT_ENVIRONMENT");
        var root = Path.Combine(Path.GetTempPath(), "vault-editor-admission-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        Environment.SetEnvironmentVariable("VAULT_ENVIRONMENT", "development"); var failures = new List<string>(); var passed = 0;
        async Task Case(string name, Func<Task> action)
        {
            Environment.SetEnvironmentVariable("VAULT_STORAGE_ROOT", Path.Combine(root, name));
            try { await action(); passed++; Console.WriteLine(new JsonObject { ["kind"] = "case", ["name"] = name, ["ok"] = true }.ToJsonString()); }
            catch (Exception ex) { failures.Add(name); Console.WriteLine(new JsonObject { ["kind"] = "case", ["name"] = name, ["ok"] = false, ["error"] = ex.ToString() }.ToJsonString()); }
        }
        try {
            await Case("stale-apps24-retains-rich-linked-state-and-sites", async () => {
                var (store, hub, tools) = Seed(); var view = Group(store);
                hub.SetRoster("safari", new JsonArray(new JsonObject { ["id"] = "s" })); Check(hub.Link("safari", "s", "windowsapp", "w") == null, "Third member refused");
                // Different first-contribution behavior creates a retained alias; Safari stays un-contributed so joining ledgers remain populated.
                // An acknowledged browser replacement can carry the explicit independent Website branch.
                var current = Shared(hub); var sites = current["scopes"]!.AsArray(); sites.Add(new JsonObject { ["surface"] = "site", ["id"] = "site-2", ["entryID"] = "site:linked_0123456789abcdef01234567", ["sites"] = new JsonArray("safe.example"), ["sitesExcept"] = true, ["action"] = "pause" });
                var update = Frame(24, DateTimeOffset.Now.ToUnixTimeMilliseconds(), (JsonArray)sites.DeepClone());
                hub.ApplySync("chrome", "c", update);
                var anchor = Shared(hub)["usageResetAtMs"]!.GetValue<double>(); var now = DateTimeOffset.Now.ToUnixTimeMilliseconds(); var bucket = now.ToString();
                hub.ApplySync("chrome", "c", new JsonObject { ["usageDeltaMs"] = 1234, ["usageDeltaAnchorMs"] = anchor, ["usageBuckets"] = new JsonObject { [bucket] = 321 }, ["usageTransferId"] = "fixture-transfer", ["snoozeTs"] = now,
                    ["snooze"] = new JsonObject { ["kind"] = "budget", ["startsAtMs"] = now, ["untilMs"] = now + 300000, ["extraMs"] = 300000, ["changedAtMs"] = now } });
                var before = RegistryCluster();
                Check(before["JoiningUsageBase"] != null && before["JoiningUsageDelta"]!.GetValue<double>() > 0 && before["JoiningBucketDeltas"]!.AsObject().Count > 0 && before["UsageTransferReceipts"]!.AsObject().Count > 0 && before["scopeOrigins"]!.AsObject().Count > 0 && before["Snooze"]!.AsObject().Count > 0 && before["Anchor"]!.GetValue<double>() > 0, "Rich fixture was empty");
                var request = await Request(view, g => g["scopes"]![0]!["apps"] = new JsonArray(new JsonObject { ["id"] = "C:\\Fixture\\new.exe", ["name"] = "New app" }));
                var reply = await tools.ApplyEditorRequest(request); var saved = reply["groups"]![0]!;
                Check(reply["ok"]!.GetValue<bool>() && saved["allowedMinutes"]!.GetValue<double>() == 24, "Stale Apps edit restored20");
                Check(saved["scopes"]!.AsArray().OfType<JsonObject>().Count(s => s["surface"]!.GetValue<string>() == "site") == 2 && saved["scopes"]!.AsArray().OfType<JsonObject>().Single(s => s["surface"]!.GetValue<string>() == "apps")["apps"]![0]!["id"]!.GetValue<string>().EndsWith("new.exe"), "Precise Apps or Website retention failed");
                Check(JsonNode.DeepEquals(Runtime(before), Runtime(RegistryCluster())), "Configuration edit lost populated linked runtime/ledger state");
                Console.WriteLine(new JsonObject { ["kind"] = "rich-state", ["before"] = Runtime(before), ["after"] = Runtime(RegistryCluster()), ["saved"] = saved.DeepClone() }.ToJsonString());
            });
            await Case("pending-native30-and-peer-site-survive-enabled", async () => { var (store, hub, tools) = Seed(); var view = Group(store); store.Update(doc => doc["blockedGroups"]![0]!["allowedMinutes"] = 30); await tools.ApplyEditorRequest(await Request(view, g => g["enabled"] = false)); Check(Group(store)["allowedMinutes"]!.GetValue<double>() == 30 && Shared(hub)["scalars"]!["allowedMinutes"]!.GetValue<double>() == 30 && Group(store)["scopes"]!.AsArray().Count == 2, "Pending30 or Website lost"); });
            await Case("genuine-scalar-edit-retains-newer-unrelated-value", async () => { var (store, hub, tools) = Seed(); var view = Group(store); hub.ApplySync("chrome", "c", Frame(24, DateTimeOffset.Now.ToUnixTimeMilliseconds())); await tools.ApplyEditorRequest(await Request(view, g => g["name"] = "Owner rename")); Check(Group(store)["name"]!.GetValue<string>() == "Owner rename" && Group(store)["allowedMinutes"]!.GetValue<double>() == 24, "Explicit scalar intent missing/newer value lost"); });
            await Case("current-lock-refuses-stale-native-request", async () => { var (store, hub, tools) = Seed(); var request = await Request(Group(store), g => g["name"] = "Refused"); hub.ApplySync("chrome", "c", new JsonObject { ["lockBase"] = 0, ["lock"] = new JsonObject { ["lockedAtMs"] = DateTimeOffset.Now.ToUnixTimeMilliseconds(), ["lockWaitHours"] = 1, ["lockVersion"] = 1 } }); var bytes = File.ReadAllBytes(store.FilePath); var authority = Shared(hub); await Refuse(tools, request, "group-lock-changed"); Check(bytes.SequenceEqual(File.ReadAllBytes(store.FilePath)) && JsonNode.DeepEquals(authority, Shared(hub)), "Lock refusal partially committed"); });
            await Case("locked-ordinary-edit-and-premature-unlock-refuse", async () => { var (store, _, tools) = Seed(false); store.Update(doc => { var g = doc["blockedGroups"]![0]!; g["lockedAtMs"] = DateTimeOffset.Now.ToUnixTimeMilliseconds(); g["lockWaitHours"] = 1; g["lockVersion"] = 2; }); var view = Group(store); var bytes = File.ReadAllBytes(store.FilePath); await Refuse(tools, await Request(view, g => g["name"] = "Refused"), "group-locked"); await Refuse(tools, await Request(view, g => { g["lockedAtMs"] = null; g["lockWaitHours"] = 0; g["lockVersion"] = 3; }, "lock-fields"), "group-unlock-wait"); Check(bytes.SequenceEqual(File.ReadAllBytes(store.FilePath)), "Lock refusals wrote bytes"); });
            await Case("multi-change-refusal-has-no-partial-native-save", async () => { var (store, hub, tools) = Seed(); var request = await Request(Group(store), g => g["name"] = "Must not save"); request["changes"]!.AsArray().Add(new JsonObject { ["id"] = "missing", ["lock"] = new JsonObject(), ["fields"] = new JsonObject { ["set"] = new JsonObject { ["name"] = "Bad" } } }); var bytes = File.ReadAllBytes(store.FilePath); var authority = Shared(hub); await Refuse(tools, request, "group-not-found"); Check(bytes.SequenceEqual(File.ReadAllBytes(store.FilePath)) && JsonNode.DeepEquals(authority, Shared(hub)), "Earlier change leaked on refusal"); });
            await Case("native-deletion-cleans-rule-state-quickadd-and-members", async () => { var (store, hub, tools) = Seed(); var reply = await tools.ApplyEditorRequest(new JsonObject { ["changes"] = new JsonArray(await Change(Group(store), null)) }); var doc = Document(store); Check(reply["ok"]!.GetValue<bool>() && doc["blockedGroups"]!.AsArray().Count == 0 && doc["cbRuleState"]!["w"] == null && doc["cbRuleLog"]!["w"] == null && doc["quickAddGroupId"]!.GetValue<string>() == "" && hub.ActiveClusterCount() == 0, "Deletion retained rule state/quickAdd/member"); });
            await Case("native-reorder-preserves-new-peer-group", async () => { var (store, _, tools) = Seed(false); store.Update(doc => { doc["blockedGroups"]!.AsArray().Add(NativeGroup("other")); doc["blockedGroups"]!.AsArray().Add(NativeGroup("fresh")); }); var reply = await tools.ApplyEditorRequest(new JsonObject { ["changes"] = new JsonArray(), ["order"] = new JsonArray("other", "w"), ["movedId"] = "w" }); Check(reply["groups"]!.AsArray().Select(g => g!["id"]!.GetValue<string>()).SequenceEqual(new[] { "other", "w", "fresh" }), "Reorder discarded fresh group"); });
            await Case("native-create-and-owned-scope-refusal", async () => { var (store, _, tools) = Seed(false); var created = (await Policy("CBGroupScopes", "newGroup", ["site", new JsonObject { ["name"] = "Native new" }, "desktop"]))!.AsObject(); await tools.ApplyEditorRequest(new JsonObject { ["changes"] = new JsonArray(await Change(null, created)) }); Check(Document(store)["blockedGroups"]!.AsArray().Count == 2, "Creation missing"); var wrong = NativeGroup("wrong"); wrong["scopes"] = new JsonArray(Site("wrong.example")); var bytes = File.ReadAllBytes(store.FilePath); await Refuse(tools, new JsonObject { ["changes"] = new JsonArray(await Change(null, wrong)) }, "invalid-group-create"); Check(bytes.SequenceEqual(File.ReadAllBytes(store.FilePath)), "Unowned creation wrote bytes"); });
            await Case("unlinked-budget-period-change-resets-runtime", async () => { var (store, _, tools) = Seed(false); await tools.ApplyEditorRequest(await Request(Group(store), g => g["resetIntervalHours"] = 12)); var doc = Document(store); Check(doc["usageTimersMs"]!["w"] == null && doc["usageResetAtMs"]!["w"] == null && doc["usageBucketsMs"]!["w"] == null && doc["groupSnoozes"]!["w"] == null, "Actual canonical budgetRestarts was not applied"); });
            await Case("canonical-save-refusal-is-honest-through-policy", async () => { var (store, hub, tools) = Seed(); var request = await Request(Group(store), g => g["enabled"] = false); var bytes = File.ReadAllBytes(store.FilePath); var registry = File.ReadAllBytes(Storage.ClustersPath); var authority = Shared(hub); using (var guard = new FileStream(store.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read)) await Refuse(tools, request, "editor-storage-save-failed"); Check(bytes.SequenceEqual(File.ReadAllBytes(store.FilePath)) && registry.SequenceEqual(File.ReadAllBytes(Storage.ClustersPath)) && JsonNode.DeepEquals(authority, Shared(hub)), "Canonical refusal changed authority/bytes"); });
            await Case("registry-save-refusal-restores-exact-canonical-through-policy", async () => { var (store, hub, tools) = Seed(); var request = await Request(Group(store), g => g["enabled"] = false); var bytes = File.ReadAllBytes(store.FilePath); var registry = File.ReadAllBytes(Storage.ClustersPath); var authority = Shared(hub); using (var guard = new FileStream(Storage.ClustersPath, FileMode.Open, FileAccess.Read, FileShare.Read)) await Refuse(tools, request, "editor-storage-save-failed"); Check(bytes.SequenceEqual(File.ReadAllBytes(store.FilePath)) && registry.SequenceEqual(File.ReadAllBytes(Storage.ClustersPath)) && JsonNode.DeepEquals(authority, Shared(hub)), "Registry refusal changed authority/bytes"); });
            await Case("async-canonical-admission-rechecks-new-authority", async () => { var (store, hub, tools) = Seed(); var request = await Request(Group(store), g => g["enabled"] = false); var paused = new TaskCompletionSource(); var release = new TaskCompletionSource(); tools.Policy = async (module, method, args) => { var result = await Policy(module, method, args); if (method == "applyEditorRequest") { paused.SetResult(); await release.Task; } return result; }; var edit = tools.ApplyEditorRequest(request); await paused.Task; hub.ApplySync("chrome", "c", Frame(24, DateTimeOffset.Now.ToUnixTimeMilliseconds())); var bytes = File.ReadAllBytes(store.FilePath); var authority = Shared(hub); release.SetResult(); try { await edit; throw new Exception("Stale edit succeeded"); } catch (InvalidOperationException ex) { Check(ex.Message.Contains("store-changed"), "Wrong stale refusal"); } Check(bytes.SequenceEqual(File.ReadAllBytes(store.FilePath)) && JsonNode.DeepEquals(authority, Shared(hub)), "Async refusal altered latest authority"); });
        }
        finally { Environment.SetEnvironmentVariable("VAULT_STORAGE_ROOT", previousRoot); Environment.SetEnvironmentVariable("VAULT_ENVIRONMENT", previousEnvironment); Directory.Delete(root, true); }
        Console.WriteLine(new JsonObject { ["kind"] = "summary", ["passed"] = passed, ["failures"] = new JsonArray(failures.Select(s => (JsonNode)JsonValue.Create(s)!).ToArray()), ["fixtureRemoved"] = !Directory.Exists(root), ["policyCalls"] = sequence }.ToJsonString());
        if (failures.Count != 0) Environment.ExitCode = 1;
    }
}
