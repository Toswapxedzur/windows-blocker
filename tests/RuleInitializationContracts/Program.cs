using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using WindowsBlocker.Rules;
using WindowsBlocker.WebUI;
using WindowsBlocker.Enforcement;

internal static class Program
{
    [STAThread] public static void Main(string[] args)
    {
        var app = new Application();
        var web = new WebView2();
        var window = new Window { Title = "Owned Vault initialization contracts", Width = 480, Height = 200, Content = web };
        window.Loaded += async (_, _) =>
        {
            var checks = new List<string>();
            void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks.Add(label); }
            try
            {
                Environment.SetEnvironmentVariable("VAULT_ENVIRONMENT", "development");
                Environment.SetEnvironmentVariable("VAULT_STORAGE_ROOT", args[1]);
                web.CreationProperties = new() { UserDataFolder = Path.Combine(args[1], "WebView2") };
                await web.EnsureCoreWebView2Async();
                web.CoreWebView2.SetVirtualHostNameToFolderMapping("appassets.windowsblocker", args[0], CoreWebView2HostResourceAccessKind.DenyCors);
                var navigated = new TaskCompletionSource<bool>();
                web.CoreWebView2.NavigationCompleted += (_, ev) => navigated.TrySetResult(ev.IsSuccess);
                web.CoreWebView2.Navigate("https://appassets.windowsblocker/rule-host.html");
                Check(await navigated.Task.WaitAsync(TimeSpan.FromSeconds(10)), "Actual WebView2 native rule host navigates");
                var runtime = new CustomRuleRuntime(web.CoreWebView2);
                Check(await runtime.IsReadyAsync(), "Actual isolated worker host ready");
                var store = new WebStore();
                store.SaveRaw("{\"blockedGroups\":[{\"id\":\"init\",\"name\":\"Init\",\"groupType\":\"custom\",\"enabled\":true,\"activeEventSource\":\"\",\"blockingRulesText\":\"\"}],\"cbRuleState\":{\"other\":{\"keep\":7}}}");
                var engine = new RuleEngine(store); engine.AttachRuntime(runtime);
                const string source = "(on,v)=>{v.state.runs=(v.state.runs||0)+1; v.state.marker='saved'; on('panel',()=>{v.state.events=(v.state.events||0)+1;});}";
                var loaded = await engine.RunRuleAsync("init", source);
                Check(loaded["ok"]?.GetValue<bool>() == true, "Run accepts initialization-only state mutation");
                JsonNode? Read() => JsonNode.Parse(File.ReadAllText(store.FilePath));
                Check(Read()?["cbRuleState"]?["init"]?["runs"]?.GetValue<int>() == 1 && Read()?["cbRuleState"]?["init"]?["marker"]?.GetValue<string>() == "saved", "Run persists v.state before any event");
                Check(Read()?["cbRuleState"]?["other"]?["keep"]?.GetValue<int>() == 7, "Registration preserves unrelated group state");
                Check((await engine.RunRuleAsync("init", source))["ok"]?.GetValue<bool>() == true && Read()?["cbRuleState"]?["init"]?["runs"]?.GetValue<int>() == 2, "Second Run adopts saved state and persists initialization mutation once");
                var before = File.ReadAllText(store.FilePath);
                var oversized = await engine.RunRuleAsync("init", "(on,v)=>{v.state.bad='x'.repeat(300000);on('panel',()=>{v.state.badHandler=true;});}");
                Check(oversized["ok"]?.GetValue<bool>() == false && File.ReadAllText(store.FilePath) == before, "Oversized registration rejects without changing saved source or state");
                await engine.FireUserEventAsync("panelEvent", "init", new(), new AppIdentity());
                Check(Read()?["cbRuleState"]?["init"]?["events"]?.GetValue<int>() == 1 && Read()?["cbRuleState"]?["init"]?["badHandler"] == null && Read()?["cbRuleState"]?["init"]?["runs"]?.GetValue<int>() == 2, "Rejected load preserves previous exact compiled worker without initializer rerun");
                before = File.ReadAllText(store.FilePath);
                var looping = await engine.RunRuleAsync("init", "(on,v)=>{while(true){}}");
                Check(looping["ok"]?.GetValue<bool>() == false && File.ReadAllText(store.FilePath) == before, "Timed-out candidate preserves saved source and state");
                await engine.FireUserEventAsync("panelEvent", "init", new(), new AppIdentity());
                Check(Read()?["cbRuleState"]?["init"]?["events"]?.GetValue<int>() == 2 && Read()?["cbRuleState"]?["init"]?["runs"]?.GetValue<int>() == 2, "Timed-out candidate preserves prior worker without quarantine");
                before = File.ReadAllText(store.FilePath);
                foreach (var failure in new[] { "future", "malformed", "missing", "write" })
                {
                    var future = JsonNode.Parse(before)!; future["schemaVersion"] = 999;
                    string guarded = failure == "future" ? future.ToJsonString() : failure == "malformed" ? "{bad" : before;
                    if (failure == "missing") File.Delete(store.FilePath); else File.WriteAllText(store.FilePath, guarded);
                    FileStream? locked = failure == "write" ? new FileStream(store.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read) : null;
                    try
                    {
                        var rejected = await engine.RunRuleAsync("init", "(on,v)=>{v.state.replaced=true;on('panel',()=>{v.state.badHandler=true;});}");
                        Check(rejected["ok"]?.GetValue<bool>() == false, failure + " storage guard refuses successful Run");
                        Check(failure == "missing" ? !File.Exists(store.FilePath) : File.ReadAllText(store.FilePath) == guarded, failure + " storage guard preserves saved bytes");
                    }
                    finally { locked?.Dispose(); File.WriteAllText(store.FilePath, before); }
                    await engine.FireUserEventAsync("panelEvent", "init", new(), new AppIdentity());
                    Check(Read()?["cbRuleState"]?["init"]?["badHandler"] == null && Read()?["cbRuleState"]?["init"]?["runs"]?.GetValue<int>() == 2, failure + " storage guard preserves prior compiled rule");
                    before = File.ReadAllText(store.FilePath);
                }
                await engine.UnloadGroupAsync("init");
                var restarted = new RuleEngine(store); restarted.AttachRuntime(runtime);
                Check((await restarted.RunRuleAsync("init", source))["ok"]?.GetValue<bool>() == true && Read()?["cbRuleState"]?["init"]?["runs"]?.GetValue<int>() == 3, "Restarted native engine restores persisted initialization state");
                const string errorState = "(on,v)=>{v.state.error='valid-user-field';v.state.empty={};}";
                Check((await restarted.RunRuleAsync("init", errorState))["ok"]?.GetValue<bool>() == true && Read()?["cbRuleState"]?["init"]?["error"]?.GetValue<string>() == "valid-user-field", "Legitimate state.error persists with zero handlers");
                store.Update(root => ((JsonArray)root["blockedGroups"]!).Add(new JsonObject { ["id"] = "disabled-init", ["name"] = "Disabled Init", ["groupType"] = "custom", ["enabled"] = false, ["activeEventSource"] = "(on,v)=>{v.state.seeded=9;}", ["blockingRulesText"] = "(on,v)=>{v.state.seeded=9;}" }));
                await restarted.TickAsync(new AppIdentity(), new());
                Check(Read()?["cbRuleState"]?["disabled-init"]?["seeded"]?.GetValue<int>() == 9 && Read()?["blockedGroups"]?[1]?["enabled"]?.GetValue<bool>() == false, "Automatic disabled rule load persists initialization without enabling group");
                await restarted.UnloadGroupAsync("disabled-init");
                await restarted.UnloadGroupAsync("init");
                File.WriteAllText(args[2], new JsonObject { ["ok"] = true, ["checks"] = JsonSerializerNodes(checks) }.ToJsonString());
            }
            catch (Exception ex) { File.WriteAllText(args[2], new JsonObject { ["ok"] = false, ["error"] = ex.ToString(), ["checks"] = JsonSerializerNodes(checks) }.ToJsonString()); }
            finally { web.Dispose(); window.Close(); app.Shutdown(); }
        };
        app.Run(window);
    }
    private static JsonArray JsonSerializerNodes(IEnumerable<string> checks) => new(checks.Select(s => JsonValue.Create(s)).ToArray());
}
