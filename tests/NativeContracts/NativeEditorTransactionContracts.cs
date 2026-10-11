using System.Net.WebSockets;
using System.Reflection;
using System.Text.Json.Nodes;
using WindowsBlocker.Bridge;
using WindowsBlocker.WebUI;

internal static class NativeEditorTransactionContracts
{
    static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static JsonObject Apps() => new() { ["id"] = "apps-1", ["surface"] = "apps", ["apps"] = new JsonArray() };
    static JsonObject Frame(double minutes, double ts) => new()
    {
        ["scalars"] = new JsonObject { ["name"] = "Fixture", ["enabled"] = true, ["mode"] = "budget", ["allowedMinutes"] = minutes },
        ["scopes"] = new JsonArray(new JsonObject { ["surface"] = "site", ["sites"] = new JsonArray("example.com") }), ["ts"] = ts
    };
    static JsonObject Shared(ConnectionHub hub) => (JsonObject)JsonNode.Parse(hub.ClustersJson())!["clusters"]![0]!["shared"]!;
    static JsonObject State(ConnectionHub hub) => (JsonObject)typeof(ConnectionHub).GetMethod("EditorFingerprint", Private)!.Invoke(hub, null)!;
    static (WebStore Store, ConnectionHub Hub) Seed()
    {
        var store = new WebStore(); store.SaveRaw(new JsonObject { ["blockedGroups"] = new JsonArray(new JsonObject {
            ["id"] = "w", ["name"] = "Fixture", ["enabled"] = true, ["mode"] = "budget", ["allowedMinutes"] = 20, ["scopes"] = new JsonArray(Apps())
        }), ["cbRuleState"] = new JsonObject { ["w"] = new JsonObject { ["keep"] = true } } }.ToJsonString());
        var hub = new ConnectionHub(store);
        hub.SetRoster("windowsapp", new JsonArray(new JsonObject { ["id"] = "w" }));
        hub.SetRoster("chrome", new JsonArray(new JsonObject { ["id"] = "c" }));
        Check(hub.Link("chrome", "c", "windowsapp", "w") == null, "Seed link refused");
        hub.ApplySync("chrome", "c", Frame(20, 0)); var native = Frame(20, 0); native["scopes"] = new JsonArray(Apps()); hub.ApplySync("windowsapp", "w", native);
        hub.ReconcileLocal(store); return (store, hub);
    }
    static JsonArray EditEnabled(ConnectionHub.EditorCapture capture)
    { var groups = (JsonArray)capture.Groups.DeepClone(); groups[0]!["enabled"] = false; return groups; }
    static bool Refuses(Action action, string? contains = null)
    { try { action(); return false; } catch (Exception ex) { return contains == null || ex.Message.Contains(contains, StringComparison.Ordinal); } }
    private sealed class ObservedSocket : WebSocket
    {
        public int Sends;
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string? CloseStatusDescription => null;
        public override WebSocketState State => WebSocketState.Open;
        public override string? SubProtocol => null;
        public override void Abort() { }
        public override Task CloseAsync(WebSocketCloseStatus status, string? description, CancellationToken token) => Task.CompletedTask;
        public override Task CloseOutputAsync(WebSocketCloseStatus status, string? description, CancellationToken token) => Task.CompletedTask;
        public override void Dispose() { }
        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken token) => throw new NotSupportedException();
        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType type, bool end, CancellationToken token)
        { Interlocked.Increment(ref Sends); return Task.CompletedTask; }
    }
    static ObservedSocket Observe(ConnectionHub hub)
    {
        var socket = new ObservedSocket();
        var type = typeof(ConnectionHub).GetNestedType("Peer", BindingFlags.NonPublic)!;
        var peer = Activator.CreateInstance(type, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new object[] { socket }, null)!;
        type.GetField("Connected")!.SetValue(peer, true); type.GetField("Program")!.SetValue(peer, "chrome");
        var peers = (System.Collections.IDictionary)typeof(ConnectionHub).GetField("_peers", Private)!.GetValue(hub)!;
        peers.Add(type.GetField("Id")!.GetValue(peer)!, peer); return socket;
    }
    static string RegistryPath => WindowsBlocker.WebUI.Storage.ClustersPath;
    public static async Task Run()
    {
        var previous = Environment.GetEnvironmentVariable("VAULT_STORAGE_ROOT");
        var root = Path.Combine(Path.GetTempPath(), "vault-editor-transaction-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        var failures = new List<string>();
        async Task Case(string name, Func<Task> action)
        {
            Environment.SetEnvironmentVariable("VAULT_STORAGE_ROOT", Path.Combine(root, name));
            try { await action(); Console.WriteLine("PASS " + name); }
            catch (Exception ex) { failures.Add(name); Console.WriteLine("FAIL " + name + ": " + ex.Message); }
        }
        try
        {
            await Case("capture-rebases-without-live-mutation", () => {
                var (store, hub) = Seed(); hub.ApplySync("chrome", "c", Frame(24, 2));
                var state = State(hub); var bytes = File.ReadAllBytes(store.FilePath); var registry = File.ReadAllBytes(RegistryPath);
                var capture = hub.CaptureEditor(store);
                Check(capture.Groups[0]!["allowedMinutes"]!.GetValue<double>() == 24, "Capture used stale local20");
                Check(JsonNode.DeepEquals(state, State(hub)) && bytes.SequenceEqual(File.ReadAllBytes(store.FilePath)) && registry.SequenceEqual(File.ReadAllBytes(RegistryPath)), "Preview mutated live authority or bytes");
                var groups = hub.CommitEditor(store, capture, EditEnabled(capture));
                Check(groups[0]!["allowedMinutes"]!.GetValue<double>() == 24 && Shared(hub)["scalars"]!["allowedMinutes"]!.GetValue<double>() == 24, "Enabled edit discarded shared24");
                Check(!Shared(hub)["scalars"]!["enabled"]!.GetValue<bool>(), "Enabled edit missing"); return Task.CompletedTask;
            });
            await Case("pending-native30-survives-editor-enabled", () => {
                var (store, hub) = Seed(); store.Update(doc => doc["blockedGroups"]![0]!["allowedMinutes"] = 30);
                var capture = hub.CaptureEditor(store); Check(capture.Groups[0]!["allowedMinutes"]!.GetValue<double>() == 30, "Preview erased pending30");
                hub.CommitEditor(store, capture, EditEnabled(capture));
                Check(Shared(hub)["scalars"]!["allowedMinutes"]!.GetValue<double>() == 30, "Commit erased pending30"); return Task.CompletedTask;
            });
            await Case("paused-policy-new-shared-refuses-stale-commit", async () => {
                var (store, hub) = Seed(); var capture = hub.CaptureEditor(store);
                var paused = new TaskCompletionSource(); var release = new TaskCompletionSource();
                var policy = Task.Run(async () => { paused.SetResult(); await release.Task; return EditEnabled(capture); });
                await paused.Task; hub.ApplySync("chrome", "c", Frame(24, 2)); var state = State(hub); var bytes = File.ReadAllBytes(store.FilePath);
                release.SetResult(); var accepted = await policy;
                Check(Refuses(() => hub.CommitEditor(store, capture, accepted), "store-changed"), "Stale async result committed");
                Check(JsonNode.DeepEquals(state, State(hub)) && bytes.SequenceEqual(File.ReadAllBytes(store.FilePath)), "Stale refusal mutated authority");
            });
            await Case("canonical-write-refusal-is-failure-atomic", () => {
                var (store, hub) = Seed(); var socket = Observe(hub); var capture = hub.CaptureEditor(store); var state = State(hub);
                using (var reader = new FileStream(store.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    Check(Refuses(() => hub.CommitEditor(store, capture, EditEnabled(capture)), "editor-storage-save-failed"), "Canonical save falsely acknowledged");
                Check(JsonNode.DeepEquals(state, State(hub)) && capture.CanonicalBytes.SequenceEqual(File.ReadAllBytes(store.FilePath)) && capture.RegistryBytes!.SequenceEqual(File.ReadAllBytes(RegistryPath)), "Canonical refusal changed state or files"); Check(socket.Sends == 0, "Refused editor stage emitted an outgoing frame"); return Task.CompletedTask;
            });
            await Case("registry-write-refusal-restores-exact-canonical", () => {
                var (store, hub) = Seed(); var socket = Observe(hub); var capture = hub.CaptureEditor(store); var state = State(hub);
                using (var reader = new FileStream(RegistryPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    Check(Refuses(() => hub.CommitEditor(store, capture, EditEnabled(capture)), "editor-storage-save-failed"), "Registry refusal falsely acknowledged");
                Check(JsonNode.DeepEquals(state, State(hub)) && capture.CanonicalBytes.SequenceEqual(File.ReadAllBytes(store.FilePath)) && capture.RegistryBytes!.SequenceEqual(File.ReadAllBytes(RegistryPath)), "Registry failure did not restore exact old bytes/state"); Check(socket.Sends == 0, "Refused editor stage emitted an outgoing frame"); return Task.CompletedTask;
            });
            await Case("rollback-refusal-holds-editor-and-retains-actual-disk", () => {
                var (store, hub) = Seed(); var socket = Observe(hub); var capture = hub.CaptureEditor(store); var state = State(hub);
                hub.EditorDiskPhaseForTests = phase => { if (phase == "canonical-rollback") throw new IOException("injected rollback refusal"); };
                using (var reader = new FileStream(RegistryPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    Check(Refuses(() => hub.CommitEditor(store, capture, EditEnabled(capture)), "editor-storage-partial-failure"), "Partial storage failure hidden");
                hub.EditorDiskPhaseForTests = null; var actual = File.ReadAllBytes(store.FilePath);
                Check(!capture.CanonicalBytes.SequenceEqual(actual) && JsonNode.DeepEquals(state, State(hub)), "Actual partial disk state or original live state was not retained");
                Check(Refuses(() => hub.CaptureEditor(store), "editor-storage-partial-failure") && actual.SequenceEqual(File.ReadAllBytes(store.FilePath)), "Held admission silently retried or rewrote data");
                Check(Refuses(() => store.Update(doc => doc["usageTimersMs"] = new JsonObject { ["w"] = 99 }), "editor-storage-partial-failure"), "Runtime-like Update bypassed storage hold");
                Check(Refuses(() => store.Merge(new JsonObject { ["groupSnoozes"] = new JsonObject() }), "editor-storage-partial-failure"), "Runtime-like Merge bypassed storage hold");
                Check(Refuses(() => hub.ReconcileLocal(store), "editor-storage-partial-failure") && Refuses(() => hub.ApplySync("chrome", "c", Frame(42, 3)), "editor-storage-partial-failure"), "Ordinary hub mutation bypassed hold");
                Check(actual.SequenceEqual(File.ReadAllBytes(store.FilePath)) && capture.RegistryBytes!.SequenceEqual(File.ReadAllBytes(RegistryPath)), "Held runtime/hub writer clobbered retained disk bytes"); Check(socket.Sends == 0, "Refused editor stage emitted an outgoing frame"); return Task.CompletedTask;
            });
            await Case("editor-delete-cleans-rule-state-and-linked-member", () => {
                var (store, hub) = Seed(); var capture = hub.CaptureEditor(store); hub.CommitEditor(store, capture, new JsonArray());
                var doc = JsonNode.Parse(store.LoadRawJson()!)!;
                Check(doc["blockedGroups"]!.AsArray().Count == 0 && doc["cbRuleState"]!["w"] == null && hub.ActiveClusterCount() == 0, "Deletion retained state/member"); return Task.CompletedTask;
            });
            await Case("old-mixed-full-group-persist-is-whole-refusal", () => {
                var (store, _) = Seed(); var old = File.ReadAllBytes(store.FilePath);
                var patch = new JsonObject { ["blockedGroups"] = new JsonArray(), ["globalSettings"] = new JsonObject { ["quickAddEnabled"] = false } };
                Check(Refuses(() => { NativeEditorContract.RequireKeyedPersist(patch); store.Merge(patch); }), "Old full-group producer accepted");
                Check(old.SequenceEqual(File.ReadAllBytes(store.FilePath)), "Mixed refusal still wrote settings");
                var keyed = new JsonObject { ["globalSettings"] = new JsonObject { ["quickAddEnabled"] = false } }; NativeEditorContract.RequireKeyedPersist(keyed); store.Merge(keyed);
                Check(JsonNode.Parse(store.LoadRawJson()!)!["globalSettings"]!["quickAddEnabled"]!.GetValue<bool>() == false, "Keyed settings retired"); return Task.CompletedTask;
            });
            await Case("future-registry-refuses-before-canonical-write", () => {
                var (store, hub) = Seed(); var registry = new JsonObject { ["storageMetadata"] = new JsonObject { ["format"] = "hub.clusters", ["schemaVersion"] = 999, ["product"] = "windows", ["writtenByAppVersion"] = "0.2.0" }, ["value"] = new JsonArray() };
                File.WriteAllText(RegistryPath, registry.ToJsonString()); var old = File.ReadAllBytes(store.FilePath); var registryBytes = File.ReadAllBytes(RegistryPath); var state = State(hub);
                Check(Refuses(() => hub.CaptureEditor(store)) && old.SequenceEqual(File.ReadAllBytes(store.FilePath)) && registryBytes.SequenceEqual(File.ReadAllBytes(RegistryPath)) && JsonNode.DeepEquals(state, State(hub)), "Unsupported registry overwritten"); return Task.CompletedTask;
            });
        }
        finally { Environment.SetEnvironmentVariable("VAULT_STORAGE_ROOT", previous); Directory.Delete(root, true); }
        if (failures.Count > 0) throw new Exception("Editor transaction failures: " + string.Join(", ", failures));
    }
}
