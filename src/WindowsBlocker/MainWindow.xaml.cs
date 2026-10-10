using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using WindowsBlocker.Bridge;
using WindowsBlocker.Core;
using WindowsBlocker.Enforcement;
using WindowsBlocker.Rules;
using WindowsBlocker.SelfPreservation;
using WindowsBlocker.WebUI;

namespace WindowsBlocker;

public partial class MainWindow : Window
{
    private const string VirtualHost = NativeEditorContract.Host;
    private const string StoreKey = "__cb_chrome_storage__";

    private readonly WebStore _store = new();
    private readonly BlockedAppRegistry _registry = new();
    private readonly ConnectionHub _hub;
    private readonly ClassifierWorkerClient _classifier = new();
    private readonly VaultMcpServer _mcp = new();
    private readonly NativePolicyTools _policy;
    private readonly EnforcementEngine _engine;
    private readonly RuleEngine _ruleEngine;
    private readonly SelfPreservationGuard _guard = new();
    private WinEventMonitor? _monitor;
    private DispatcherTimer? _timer;
    private TimerOverlayWindow? _overlay;
    private ToastOverlayWindow? _toast;
    private PanelOverlay? _panel;
    private QuickAddWindow? _quickAdd;
    private CustomRuleRuntime? _runtime;
    private bool _ruleBusy;
    private bool _folderChooserOpen;
    private IntPtr _selfHwnd;

    // Leading-edge throttle for high-frequency panel change events (slider drag,
    // typing) so the rule isn't dispatched on every keystroke — mirrors macOS's
    // 0.1s panel throttle with a trailing flush.
    private readonly Dictionary<string, DateTime> _panelLastFire = new();
    private readonly Dictionary<string, Action> _panelPending = new();
    private DispatcherTimer? _panelFlush;

    public MainWindow()
    {
        _hub = new ConnectionHub(_store);
        InitializeComponent();
        McpConnectionsMenu.Header = NativeLanguage.Text("windows.connections.menu", "AI connections…");
        NativeMenu.FlowDirection = NativeLanguage.Language == "ar" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        _engine = new EnforcementEngine(_store, _registry, _hub);
        _ruleEngine = new RuleEngine(_store);
        _policy = new NativePolicyTools(_store,_hub,_ruleEngine);
        _policy.Policy = InvokeCanonicalPolicy;
        _policy.RulesEvent = output => { foreach(var app in output.CloseOnce) _engine.CloseMatching([app]); _panel?.ReplaceAll(_ruleEngine.PanelsSnapshot()); };
        _mcp.Invoke = InvokeMcpTool;
        _mcp.Tools = McpTools;
        _store.SeedIfNeeded();
        _hub.ClassifierRequest = HubClassifierRequest;
        _classifier.Event += OnClassifierEvent;
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private void OpenMcpConnections(object sender,RoutedEventArgs e) => new McpConnectionsWindow(this).ShowDialog();

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _selfHwnd = new WindowInteropHelper(this).Handle;
        _engine.SetSelfWindow(_selfHwnd);
        await InitWebViewAsync();
        StartMonitorAndTimer();
        _hub.Start();
        _mcp.Start();
        if(_mcp.LastError==null) McpConnectorRegistry.ApplyDefaultConnections();
        // Show the first-launch choice even when the editor opens before Classifier.
        try { PromptDictionaryContribution(await _classifier.Request("snapshot", new())); }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine("Dictionary startup snapshot unavailable: " + ex.Message); }


        // Bring the bridge up if the user previously enabled it (same as macOS,
        // which auto-starts the hub on launch from the persisted setting).
        if (_store.LoadConnectionServerEnabled())
        {
            _hub.Start();
        }
    }

    private async System.Threading.Tasks.Task InitWebViewAsync()
    {
        var assets = Path.Combine(AppContext.BaseDirectory, "WebAssets");
        await InitRuleWebViewAsync(assets);

        Web.CreationProperties = new CoreWebView2CreationProperties
        {
            UserDataFolder = Path.Combine(Storage.RootDirectory, "WebView2")
        };
        await Web.EnsureCoreWebView2Async();
        var core = Web.CoreWebView2!;

        core.SetVirtualHostNameToFolderMapping(VirtualHost, assets, CoreWebView2HostResourceAccessKind.DenyCors);
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;

        // 1) Map the WKWebView bridge name chrome-shim.js expects onto WebView2's
        //    postMessage, so chrome-shim.js itself stays verbatim.
        await core.AddScriptToExecuteOnDocumentCreatedAsync(BridgeShimScript.Replace("environment:'production'",Storage.Development ? "environment:'development'" : "environment:'production'"));

        // 2) Seed chrome.storage from the native store before chrome-shim.js reads
        //    it (chrome-shim loads from localStorage[StoreKey] on init).
        var seed = _store.LoadRawJson();
        if (seed is not null)
        {
            await core.AddScriptToExecuteOnDocumentCreatedAsync(SeedScript(seed));
        }

        core.WebMessageReceived += OnWebMessage;
        core.AddWebResourceRequestedFilter("*app-inventory.json", CoreWebView2WebResourceContext.All);
        core.AddWebResourceRequestedFilter("https://appassets.windowsblocker/worker-resource*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += OnWebResourceRequested;
        ExternalLinkHandler.Attach(core);
        core.NavigationCompleted += OnNavigationCompleted;

        core.Navigate($"https://{VirtualHost}/popup.html");
    }

    private async Task InitRuleWebViewAsync(string assets)
    {
        try
        {
            RuleWeb.CreationProperties = new CoreWebView2CreationProperties
            {
                // A separate profile prevents rule code from sharing the
                // editor's localStorage, cookies, cache, or service workers.
                UserDataFolder = Path.Combine(Storage.RootDirectory, "RuleWebView2")
            };
            await RuleWeb.EnsureCoreWebView2Async();
            var core = RuleWeb.CoreWebView2!;
            core.SetVirtualHostNameToFolderMapping(VirtualHost, assets, CoreWebView2HostResourceAccessKind.DenyCors);
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.NewWindowRequested += (_, args) => args.Handled = true;
            core.NavigationStarting += (_, args) => { if (!TrustedEditorUri(args.Uri)) args.Cancel = true; };

            var navigation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            void Completed(object? _, CoreWebView2NavigationCompletedEventArgs args)
                => navigation.TrySetResult(args.IsSuccess);
            core.NavigationCompleted += Completed;
            core.Navigate($"https://{VirtualHost}/rule-host.html");
            var completed = await Task.WhenAny(navigation.Task, Task.Delay(TimeSpan.FromSeconds(5)));
            core.NavigationCompleted -= Completed;
            if (completed != navigation.Task || !await navigation.Task)
            {
                return;
            }

            var runtime = new CustomRuleRuntime(core);
            if (await runtime.IsReadyAsync())
            {
                _runtime = runtime;
                _ruleEngine.AttachRuntime(runtime);
            }
        }
        catch
        {
            // Native blocking remains available if the optional custom-rule
            // worker host cannot start.
        }
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess || Web.CoreWebView2 is null)
        {
            return;
        }
        // Push the live application inventory directly into the editor. WebView2
        // serves virtual-host paths from the mapped folder and does not reliably
        // raise WebResourceRequested for them, so chrome-shim's fetch of the
        // (non-existent) app-inventory.json file can 404. Pushing the data via the
        // same __cbApplyAppInventory hook makes the app picker deterministic and
        // removes the dependency on intercepting that request.
        PushAppInventory();
        // Seed the bridge UI immediately rather than waiting for the first tick.
        PushConnectionState();
        PushClusters();
    }

    private async void PushAppInventory()
    {
        if (Web.CoreWebView2 is null)
        {
            return;
        }
        try
        {
            // Enumerating installed apps (Shell COM) + extracting icons is heavy,
            // so it runs off the UI thread; push the result once it's ready.
            var json = await AppInventory.BuildJsonAsync().ConfigureAwait(true);
            if (Web.CoreWebView2 is null)
            {
                return;
            }
            _ = Web.CoreWebView2.ExecuteScriptAsync(
                $"window.__cbApplyAppInventory && window.__cbApplyAppInventory({json});");
        }
        catch
        {
            // A failure to enumerate apps must not break editor load.
        }
    }

    private static string SeedScript(string storeJson) => NativeEditorContract.SeedScript(storeJson);

    private const string BridgeShimScript = @"
(function(){
  window.webkit = window.webkit || {};
  window.webkit.messageHandlers = window.webkit.messageHandlers || {};
  window.__cbNativeProgramId = 'windowsapp';
  window.__vaultRuntimeMetadata = { product:'windows-vault', environment:'production' };
  ['activity','vaultClassifier'].forEach(function(channel){
    window.webkit.messageHandlers[channel]={postMessage:function(message){window.chrome.webview.postMessage({kind:channel === 'activity' ? 'activity-message' : 'classifier-message',message:message});}};
  });
  window.webkit.messageHandlers.cbBridge = {
    postMessage: function(msg){ try { window.chrome.webview.postMessage(msg); } catch (e) {} }
  };
})();";

    private static bool TrustedEditorUri(string uri) => NativeEditorContract.TrustedUri(uri);

    private async void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            if (!TrustedEditorUri(e.Source) || e.WebMessageAsJson.Length > 2 * 1024 * 1024) return;
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            if (!root.TryGetProperty("kind", out var kindEl) || kindEl.ValueKind != JsonValueKind.String)
            {
                return;
            }
            switch (kindEl.GetString())
            {
                case "classifier-message":
                    if (root.TryGetProperty("message", out var classifierMessage) && JsonNode.Parse(classifierMessage.GetRawText()) is JsonObject classifierBody)
                    {
                        var answer = await _classifier.Request("action", classifierBody);
                        if (answer?["snapshot"] != null) ApplyClassifierSnapshot(answer["snapshot"]);
                        if (answer?["list"] != null) _ = Web.CoreWebView2.ExecuteScriptAsync($"window.VaultClassifier && window.VaultClassifier.receiveList({answer["list"]!.ToJsonString()});");
                        if (answer?["knowledgeRow"] != null) _ = Web.CoreWebView2.ExecuteScriptAsync($"window.VaultClassifier && window.VaultClassifier.receiveKnowledgeRow({answer["knowledgeRow"]!.ToJsonString()});");
                    }
                    break;
                case "activity-message":
                    if (root.TryGetProperty("message", out var activityMessage) && JsonNode.Parse(activityMessage.GetRawText()) is JsonObject activityBody)
                        ApplyActivity(await _classifier.Request("activity", activityBody));
                    break;
                case "scene-shown":
                    if (root.TryGetProperty("scene", out var scene) && (scene.GetString() == "classifier" || scene.GetString() == "settings")) ApplyClassifierSnapshot(await _classifier.Request("snapshot", new()));
                    if (root.TryGetProperty("scene", out var activityScene) && activityScene.GetString() == "activity") ApplyActivity(await _classifier.Request("activity", new JsonObject { ["kind"] = "ready" }));
                    break;
                case "vault-classifier-tag-names":
                    NativeReply(root, new JsonObject { ["ok"] = true, ["names"] = await _classifier.Request("tagNames", new JsonObject { ["platform"] = ReadMessageString(root,"platform") }) });
                    break;
                case "mcp-connectors": NativeReply(root,McpConnectorRegistry.Snapshot()); break;
                case "mcp-connect": McpConnectorRegistry.Set(ReadMessageString(root,"id"),true); NativeReply(root,McpConnectorRegistry.Snapshot()); break;
                case "mcp-disconnect": McpConnectorRegistry.Set(ReadMessageString(root,"id"),false); NativeReply(root,McpConnectorRegistry.Snapshot()); break;
                case "persist-store":
                    if (root.TryGetProperty("changes", out var changes) && JsonNode.Parse(changes.GetRawText()) is JsonObject patch) _store.Merge(patch);
                    break;

                // Web-app bridge: the editor drives the local hub through cbBridge,
                // exactly as the macOS BlockerWebView wires ConnectionHub. Each
                // message carries the original sendMessage payload under "message".
                case "connection-server-start":
                    _hub.Start();
                    PushConnectionState();
                    break;
                case "connection-server-stop":
                    _hub.Stop();
                    PushConnectionState();
                    break;
                case "connection-status":
                    PushConnectionState();
                    break;
                case "groups-announce":
                    _hub.AnnounceFromBridge(MessageJson(root));
                    break;
                case "group-link":
                    _hub.ConnectFromBridge(MessageJson(root));
                    break;
                case "group-unlink":
                    _hub.DisconnectFromBridge(MessageJson(root));
                    break;
                case "group-sync":
                    _hub.SyncFromBridge(MessageJson(root));
                    break;
                case "clusters-status":
                    PushClusters();
                    break;

                // Custom-rule surface: the editor asks the native rule engine to
                // drive the isolated worker host. No rule source is evaluated in
                // this privileged editor WebView.
                case "run-custom-group":
                {
                    var (gid, src) = ReadGroupSource(root);
                    if (gid.Length > 0)
                    {
                        var result = await _ruleEngine.RunRuleAsync(gid, src);
                        NativeReply(root, new JsonObject { ["ok"] = true, ["loadResult"] = result });
                        PushRuleLog();
                    }
                    break;
                }
                case "reset-group-runtime":
                    var resetId=ReadMessageString(root,"groupId");
                    _store.Update(doc=>{foreach(var key in new[]{"usageTimersMs","usageResetAtMs","usageBucketsMs","groupSnoozes","groupSnoozeTotalsMs"}) if(doc[key] is JsonObject map) map.Remove(resetId); var anchors=doc["usageResetAtMs"] as JsonObject ?? new();doc["usageResetAtMs"]=anchors;anchors[resetId]=DateTimeOffset.Now.ToUnixTimeMilliseconds();});
                    break;
                case "unload-custom-group":
                {
                    var gid = ReadMessageString(root, "groupId");
                    if (gid.Length > 0)
                    {
                        _ = _ruleEngine.UnloadGroupAsync(gid);
                    }
                    break;
                }
                case "fire-snooze-press":
                {
                    var gid = ReadMessageString(root, "groupId");
                    if (gid.Length > 0)
                    {
                        _ = FireUserEventAndRenderAsync("snoozePress", gid, new Dictionary<string, string>());
                    }
                    break;
                }
                case "custom-panel-event":
                {
                    var (gid, data) = ReadPanelMessage(root);
                    if (gid.Length > 0)
                    {
                        _ = FireUserEventAndRenderAsync("panelEvent", gid, data);
                    }
                    break;
                }
                case "show-system-panel":
                {
                    var snapshot = ReadMessageObjectJson(root, "snapshot");
                    if (snapshot is not null)
                    {
                        _ruleEngine.ShowSystemPanel(snapshot);
                        _panel?.ReplaceAll(_ruleEngine.PanelsSnapshot());
                    }
                    break;
                }
                case "dismiss-system-panel":
                    _ruleEngine.DismissSystemPanel(ReadMessageString(root, "id"));
                    _panel?.ReplaceAll(_ruleEngine.PanelsSnapshot());
                    break;
                case "refresh-blocking-rules":
                    // Next tick re-evaluates groups; nothing extra to do here.
                    break;
                case "clear-rule-log":
                    _ruleEngine.ClearLog(ReadMessageString(root, "groupId")); NativeReply(root, new JsonObject { ["ok"] = true }); break;
                case "local-folder-status": PushFolderStatus(); break;
                case "local-folder-choose":
                    // WebView2 callbacks cannot run the nested message loop of a
                    // native modal dialog. Open it after this callback returns.
                    if (_folderChooserOpen) break;
                    _folderChooserOpen = true;
                    _ = Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try { LocalFolderGrant.Choose(this); }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException) { }
                        finally { _folderChooserOpen = false; PushFolderStatus(); }
                    }));
                    break;
                case "local-folder-revoke": LocalFolderGrant.Revoke(); PushFolderStatus(); break;
                case "local-folder-reveal":
                    RevealLocalFolder();
                    break;

                default:
                    break;
            }
        }
        catch
        {
            // Ignore malformed bridge messages.
        }
    }

    private async void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        try
        {
            if (e.Request.Uri.StartsWith("https://appassets.windowsblocker/worker-resource?url=",StringComparison.Ordinal))
            {
                var deferral = e.GetDeferral();
                try
                {
                    var original = Uri.UnescapeDataString(e.Request.Uri.Split("?url=",2)[1]);
                    var response = await _classifier.Request("resource",new JsonObject { ["url"] = original }) as JsonObject;
                    var data = response?["dataBase64"]?.GetValue<string>();
                    if (data != null) e.Response = Web.CoreWebView2.Environment.CreateWebResourceResponse(new MemoryStream(Convert.FromBase64String(data)),200,"OK","Content-Type: " + response!["contentType"]!.GetValue<string>());
                }
                catch { e.Response = Web.CoreWebView2.Environment.CreateWebResourceResponse(new MemoryStream(),404,"Not found",""); }
                finally { deferral.Complete(); }
                return;
            }
            if (!e.Request.Uri.EndsWith("app-inventory.json", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            var json = AppInventory.CachedJson();
            var bytes = Encoding.UTF8.GetBytes(json);
            var stream = new MemoryStream(bytes);
            e.Response = Web.CoreWebView2.Environment.CreateWebResourceResponse(
                stream, 200, "OK", "Content-Type: application/json");
        }
        catch
        {
            // Leave e.Response null → WebView2 serves the (possibly missing) file.
        }
    }

    private void StartMonitorAndTimer()
    {
        _monitor = new WinEventMonitor(hwnd => _engine.OnWindowEvent(hwnd));
        _monitor.Start();

        // The floating timer HUD (macOS TimerOverlayPanel analog). Create the
        // handle up front so the click-through extended styles are applied before
        // it is ever shown; it stays hidden until there is a countdown to show.
        _overlay = new TimerOverlayWindow();
        new WindowInteropHelper(_overlay).EnsureHandle();

        // Toast/log overlay (macOS ToastOverlayPanelController) and interactive
        // panel overlay (PanelOverlayPanelController) for custom-rule output.
        _toast = new ToastOverlayWindow();
        new WindowInteropHelper(_toast).EnsureHandle();
        _panel = new PanelOverlay { OnEvent = OnPanelEvent };
        _quickAdd = new QuickAddWindow(async (id,app) => { await _policy.Invoke("add_application",new JsonObject { ["id"]=id,["appId"]=app.Canonical,["name"]=app.DisplayName }); PushNativeStore(); });

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private bool _nativeTickBusy;
    private async void OnTick(object? sender, EventArgs e)
    {
        if(_nativeTickBusy) return; _nativeTickBusy=true;
        // A transient failure (e.g. a momentarily malformed store write) must
        // never kill the enforcement timer, or blocking would silently stop.
        try
        {
            // Resolve the focused app once and share it with both engines: native
            // enforcement consumes last tick's rule-blocked set; the rule engine
            // produces this tick's set for the next pass (≤1s latency).
            _hub.ReconcileLocal(_store);
            if(_runtime!=null && await _policy.SettleSnoozesAsync()) { _hub.ReconcileLocal(_store); PushNativeStore(); }
            McpConnectionsMenu.Header = NativeLanguage.Text("windows.connections.menu", "AI connections…");
            NativeMenu.FlowDirection = NativeLanguage.Language == "ar" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            if (JsonNode.Parse(_store.LoadRawJson() ?? "{}") is JsonObject quickAddStore) _quickAdd?.Reload(quickAddStore);
            var foreground = ProcessIdentity.ForWindow(NativeMethods.GetForegroundWindow());
            var status = _engine.Tick(foreground, _ruleEngine.BlockedIdentities);
            _ = RecordActivity(foreground);

            PushUsage();
            PushPermission();
            // Mirror macOS: push live bridge status / clusters / rejections each
            // second so the editor's connection panel stays current.
            PushConnectionState();
            PushClusters();
            PushGroupRejection();

            // Drive custom rules. The dispatch is async (WebView2), so guard
            // against overlapping ticks; native enforcement above always runs.
            if (_runtime is not null && !_ruleBusy)
            {
                _ruleBusy = true;
                try
                {
                    var running = _engine.SnapshotRunningIdentities();
                    var ruleOut = await _ruleEngine.TickAsync(foreground, running);

                    if (ruleOut.CloseOnce.Count > 0)
                    {
                        _engine.CloseMatching(ruleOut.CloseOnce);
                    }

                    var rows = new List<TimerDisplayItem>(status.Timers);
                    _overlay?.UpdateRows(rows);

                    foreach (var (message, level) in ruleOut.HudLogs)
                    {
                        _toast?.Show(message, level);
                    }
                    _panel?.ReplaceAll(_ruleEngine.PanelsSnapshot());
                }
                finally
                {
                    _ruleBusy = false;
                }
            }
            else
            {
                _overlay?.UpdateRows(status.Timers);
            }

            // Rule logs + system-panel events (e.g. parental-PIN round-trip) are
            // drained every tick, independent of whether the rule runtime ran,
            // so a system panel shown via the editor still gets its events back.
            PushRuleLog();
            PushSystemPanelEvents();
        }
        catch
        {
            // Swallow and continue on the next tick.
        }
        finally { _nativeTickBusy=false; }
    }

    private async Task<JsonNode?> InvokeCanonicalPolicy(string module,string method,object?[] args)
    {
        var result = await Dispatcher.InvokeAsync(async () => _runtime != null ? await _runtime.PolicyAsync(module,method,args) : null).Task.Unwrap();
        return result != null && result.TryGetValue("value",out var value) ? JsonNode.Parse(value.GetRawText()) : throw new InvalidOperationException("policy-runtime-unavailable");
    }
    private async Task<JsonNode?> InvokeMcpTool(string name,JsonObject args)
    {
        NativeToolSchema.Validate(await McpTools(),name,args);
        if (NativePolicyTools.Names.Contains(name))
            return await Dispatcher.InvokeAsync(async () => { var value = await _policy.Invoke(name,args); PushNativeStore(); return value; }).Task.Unwrap();
        if (name.StartsWith("extension_"))
        {
            var operations = new Dictionary<string,string> { ["extension_state"]="settings-get",["extension_create_group"]="settings-create-group",["extension_set_group"]="settings-set-group",["extension_delete_group"]="settings-delete-group",["extension_lock_group"]="settings-lock-group",["extension_set_lock_gates"]="settings-set-lock-gates",["extension_delete_all"]="settings-delete-all",["extension_unlock_group"]="settings-unlock-group",["extension_snooze_group"]="settings-snooze-group",["extension_run_custom_rule"]="settings-run-custom-rule",["extension_end_snooze"]="settings-end-snooze",["extension_move_group"]="settings-move-group",["extension_set_global"]="settings-set-global" };
            if (!operations.TryGetValue(name,out var operation)) throw new InvalidOperationException("unknown-tool");
            var body = (JsonObject)args.DeepClone(); body.Remove("browser");
            return await BrowserTool(operation,body,args["browser"]?.GetValue<string>());
        }
        return await _classifier.Request("mcp",ClassifierMcpRequest.Create(name,args));
    }
    private async Task<JsonNode?> BrowserTool(string operation,JsonObject body,string? browser) => await _hub.SendBrowserRequest(operation,body,browser);
    private Task<JsonArray> McpTools()
    {
        var file = Path.Combine(AppContext.BaseDirectory,"WebAssets","mcp-tools.json");
        if (File.Exists(file) && JsonNode.Parse(File.ReadAllText(file)) is JsonArray tools) return Task.FromResult(tools);
        throw new InvalidOperationException("mcp-tool-catalog-unavailable");
    }
    private void PushNativeStore()
    {
        var json = _store.LoadRawJson(); if (json != null && Web.CoreWebView2 != null) _ = Web.CoreWebView2.ExecuteScriptAsync($"window.__cbApplyNativeStore && window.__cbApplyNativeStore({JsonSerializer.Serialize(json)});");
    }

    private async Task<JsonObject> HubClassifierRequest(string sourcePeer, string requestId, string operation, JsonObject body)
    {
        if(operation=="activity-record")
        {
            var recorded=await _classifier.Request("activity",new JsonObject { ["kind"]="browser-record",["body"]=body.DeepClone() });
            return new JsonObject { ["stored"]=recorded?["accepted"]?.DeepClone() ?? JsonValue.Create(0) };
        }
        if(operation=="activity-settings")
        {
            var settings=await _classifier.Request("activity",new JsonObject { ["kind"]="settings",["settings"]=body["settings"]?.DeepClone() });
            return new JsonObject { ["settings"]=settings?["settings"]?.DeepClone() ?? new JsonObject() };
        }
        var result=await _classifier.Request("hub",new JsonObject { ["sourcePeerID"]=sourcePeer,["requestID"]=requestId,["operation"]=operation,["body"]=body.DeepClone() });
        if (result is JsonObject obj && obj["body"] is JsonObject answer) return (JsonObject)answer.DeepClone();
        if (result is JsonObject error && error["error"] is JsonNode code) throw new InvalidOperationException(code.GetValue<string>());
        return result as JsonObject ?? new();
    }
    private void OnClassifierEvent(JsonObject evt) => Dispatcher.InvokeAsync(() =>
    {
        if (evt["event"]?.GetValue<string>() == "state" && evt["value"] != null) ApplyClassifierSnapshot(evt["value"]);
        if (evt["event"]?.GetValue<string>() == "activity") ApplyActivity(evt["value"]);
        if (evt["event"]?.GetValue<string>() == "broadcast") _hub.BroadcastClassifier(evt);
    });
    private void ApplyClassifierSnapshot(JsonNode? snapshot)
    {
        PromptDictionaryContribution(snapshot);
        if (snapshot != null && Web.CoreWebView2 != null) _ = Web.CoreWebView2.ExecuteScriptAsync($"window.VaultClassifier && window.VaultClassifier.receive({snapshot.ToJsonString()});");
    }
    private bool _dictionaryContributionPromptShowing;
    private async void PromptDictionaryContribution(JsonNode? snapshot)
    {
        var settings = snapshot?["settings"]?["dictionaries"];
        if (!IsLoaded || _dictionaryContributionPromptShowing || settings is null || settings["contributionChoiceMade"]?.GetValue<bool>() != false) return;
        _dictionaryContributionPromptShowing = true;
        try
        {
            var answer = MessageBox.Show(this,
                NativeLanguage.Text("native.dictionary.windowsBody", "Vault can occasionally send public creator IDs and their displayed subscriber/follower counts to customblocker.com to expand the creator dictionary. No term names, titles, browsing history or personal definitions are sent. Contributions are capped at 50 per day and retained for 7 days. You can disable this anytime in Classifier → Settings.\n\nShare creator IDs and subscriber counts?"),
                NativeLanguage.Text("native.dictionary.title", "Help improve the creator dictionary"), MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes,
                NativeLanguage.Language == "ar" ? MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign : MessageBoxOptions.None);
            var response = await _classifier.Request("action", new JsonObject {
                ["action"] = "completeDictionaryOnboarding", ["data"] = new JsonObject { ["enabled"] = answer == MessageBoxResult.Yes }
            });
            if (response?["snapshot"] is JsonNode updated) ApplyClassifierSnapshot(updated);
        }
        catch (Exception ex) { System.Diagnostics.Trace.WriteLine("Dictionary first-launch choice could not be saved: " + ex.Message); }
        finally { _dictionaryContributionPromptShowing = false; }
    }

    private void ApplyActivity(JsonNode? response)
    {
        if (response is not JsonObject r || Web.CoreWebView2 == null) return;
        var kind = r["kind"]?.GetValue<string>();
        if (kind is "snapshot" or "known-items") ActivityNativeIcons.Enrich(r,AppInventory.IconForStoredId);
        if (kind == "history") { _ = Web.CoreWebView2.ExecuteScriptAsync($"window.activityHistory && window.activityHistory({r["request"]?.ToJsonString() ?? "{}"},{r["value"]?.ToJsonString() ?? "{}"});"); return; }
        if (kind == "known-items") { _ = Web.CoreWebView2.ExecuteScriptAsync($"window.activityKnownItems && window.activityKnownItems({r["items"]?.ToJsonString() ?? "[]"},{r["icons"]?.ToJsonString() ?? "{}"});"); return; }
        if (kind == "group-save") { _ = Web.CoreWebView2.ExecuteScriptAsync($"window.activityGroupSaved && window.activityGroupSaved({r["answer"]?.ToJsonString() ?? "{}"});"); ApplyActivity(r["snapshot"]); return; }
        var args = new[] { "snapshot", "icons", "facts", "collection", "tags", "contentSnapshot" }.Select(k => r[k]?.ToJsonString() ?? (k == "tags" ? "[]" : "{}"));
        _ = Web.CoreWebView2.ExecuteScriptAsync($"window.activityApply && window.activityApply({string.Join(",", args)});");
    }
    private long? _lastActivitySample;
    private async Task RecordActivity(AppIdentity foreground)
    {
        var sample=System.Diagnostics.Stopwatch.GetTimestamp(); var elapsed=_lastActivitySample.HasValue ? Math.Max(0,(sample-_lastActivitySample.Value)*1000.0/System.Diagnostics.Stopwatch.Frequency) : 0; _lastActivitySample=sample;
        try
        {
            var app=AppInventory.DescribeActivity(foreground);
            await _classifier.Request("activity",new JsonObject { ["kind"]="native-sample",["appId"]=foreground.IsEmpty ? null : foreground.Canonical,["name"]=app.Name,["icon"]=app.Icon,["elapsedMs"]=elapsed,["monotonicMs"]=(long)(sample*1000.0/System.Diagnostics.Stopwatch.Frequency),["atMs"]=DateTimeOffset.Now.ToUnixTimeMilliseconds() });
        } catch { }
    }

    // ---- Custom-rule helpers ------------------------------------------------

    private async Task RunRuleAndPushLogAsync(string groupId, string source)
    {
        try
        {
            await _ruleEngine.RunRuleAsync(groupId, source);
            PushRuleLog();
        }
        catch
        {
            // A rule that fails to load must not break the editor.
        }
    }

    private async Task FireUserEventAndRenderAsync(string type, string groupId, Dictionary<string, string> data)
    {
        if (_runtime is null)
        {
            return;
        }
        try
        {
            var foreground = ProcessIdentity.ForWindow(NativeMethods.GetForegroundWindow());
            var output = await _ruleEngine.FireUserEventAsync(type, groupId, data, foreground);
            if (output.CloseOnce.Count > 0)
            {
                _engine.CloseMatching(output.CloseOnce);
            }
            foreach (var (message, level) in output.HudLogs)
            {
                _toast?.Show(message, level);
            }
            _panel?.ReplaceAll(_ruleEngine.PanelsSnapshot());
            PushRuleLog();
        }
        catch
        {
            // Ignore a single failed user-event dispatch.
        }
    }

    // Routes a panel-overlay interaction to the rule (or buffers system-panel
    // events for the editor), mirroring MacEnforcementBridge's panel handler.
    private void OnPanelEvent(string groupId, string panelId, string controlId, string eventName, string value, string valuesJson)
    {
        var data = new Dictionary<string, string>
        {
            ["panelId"] = panelId,
            ["controlId"] = controlId,
            ["eventName"] = eventName,
            ["value"] = value
        };
        if (!string.IsNullOrEmpty(valuesJson))
        {
            data["valuesJSON"] = valuesJson;
        }

        if (groupId == RuleEngine.SystemGroupId)
        {
            _ruleEngine.BufferSystemPanelEvent(data);
            return;
        }

        if (eventName == "click")
        {
            _ = FireUserEventAndRenderAsync("panelEvent", groupId, data);
            return;
        }

        // Throttle change events (leading edge + trailing flush).
        var key = $"{groupId}|{panelId}|{controlId}";
        var now = DateTime.UtcNow;
        if (_panelLastFire.TryGetValue(key, out var last) && (now - last).TotalMilliseconds < 100)
        {
            _panelPending[key] = () => _ = FireUserEventAndRenderAsync("panelEvent", groupId, data);
            EnsurePanelFlush();
            return;
        }
        _panelLastFire[key] = now;
        _panelPending.Remove(key);
        _ = FireUserEventAndRenderAsync("panelEvent", groupId, data);
    }

    private void EnsurePanelFlush()
    {
        if (_panelFlush is not null)
        {
            return;
        }
        _panelFlush = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _panelFlush.Tick += (_, _) =>
        {
            _panelFlush?.Stop();
            _panelFlush = null;
            var pending = _panelPending.Values.ToList();
            _panelPending.Clear();
            foreach (var fire in pending)
            {
                fire();
            }
        };
        _panelFlush.Start();
    }

    private void PushRuleLog()
    {
        if (Web.CoreWebView2 is null)
        {
            return;
        }
        var json = _ruleEngine.DrainLogJson();
        if (json is not null)
        {
            _ = Web.CoreWebView2.ExecuteScriptAsync(
                $"window.__cbApplyNativeRuleLog && window.__cbApplyNativeRuleLog({json});");
        }
    }

    private void PushSystemPanelEvents()
    {
        if (Web.CoreWebView2 is null)
        {
            return;
        }
        var json = _ruleEngine.DrainSystemPanelEventsJson();
        if (json is not null)
        {
            _ = Web.CoreWebView2.ExecuteScriptAsync(
                $"window.__cbSystemPanelEvent && window.__cbSystemPanelEvent({json});");
        }
    }

    private void NativeReply(JsonElement body, JsonObject reply)
    {
        if (body.TryGetProperty("requestId", out var id) && id.ValueKind == JsonValueKind.String)
            _ = Web.CoreWebView2.ExecuteScriptAsync($"window.__cbNativeReply && window.__cbNativeReply({JsonSerializer.Serialize(id.GetString())},{reply.ToJsonString()});");
    }
    private void PushFolderStatus() => _ = Web.CoreWebView2.ExecuteScriptAsync($"window.__cbLocalFolderStatus && window.__cbLocalFolderStatus({LocalFolderGrant.Status().ToJsonString()});");

    private void RevealLocalFolder()
    {
        try
        {
            var dir = LocalFolderGrant.Folder;
            if (dir == null) return;
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
        }
        catch
        {
            // Best effort; revealing the folder is non-critical.
        }
    }

    // ---- bridge message readers --------------------------------------------

    private static (string groupId, string source) ReadGroupSource(JsonElement root)
    {
        if (root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.Object)
        {
            var gid = m.TryGetProperty("groupId", out var g) && g.ValueKind == JsonValueKind.String ? g.GetString() ?? "" : "";
            var src = m.TryGetProperty("source", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() ?? "" : "";
            return (gid, src);
        }
        return ("", "");
    }

    private static string ReadMessageString(JsonElement root, string key)
    {
        if (root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.Object &&
            m.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
        {
            return v.GetString() ?? "";
        }
        return "";
    }

    private static string? ReadMessageObjectJson(JsonElement root, string key)
    {
        if (root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.Object &&
            m.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Object)
        {
            return v.GetRawText();
        }
        return null;
    }

    private static (string groupId, Dictionary<string, string> data) ReadPanelMessage(JsonElement root)
    {
        var data = new Dictionary<string, string>();
        var groupId = "";
        if (root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in m.EnumerateObject())
            {
                if (prop.Name == "groupId")
                {
                    groupId = prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() ?? "" : prop.Value.ToString();
                    continue;
                }
                data[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                    ? prop.Value.GetString() ?? ""
                    : prop.Value.GetRawText();
            }
        }
        return (groupId, data);
    }

    /// Extracts the inner sendMessage payload the chrome-shim wraps as `message`.
    private static string MessageJson(JsonElement root) =>
        root.TryGetProperty("message", out var message) ? message.GetRawText() : "{}";

    private void PushConnectionState()
    {
        if (Web.CoreWebView2 is null)
        {
            return;
        }
        var json = _hub.CurrentStatusJson();
        _ = Web.CoreWebView2.ExecuteScriptAsync(
            $"window.__cbConnectionState && window.__cbConnectionState({json});");
    }

    private void PushClusters()
    {
        if (Web.CoreWebView2 is null)
        {
            return;
        }
        var json = _hub.ClustersJson();
        _ = Web.CoreWebView2.ExecuteScriptAsync(
            $"window.__cbClustersState && window.__cbClustersState({json});");
    }

    private void PushGroupRejection()
    {
        if (Web.CoreWebView2 is null)
        {
            return;
        }
        var json = _hub.TakeLocalRejectionJson();
        if (json is null)
        {
            return;
        }
        _ = Web.CoreWebView2.ExecuteScriptAsync(
            $"window.__cbLinkRefused && window.__cbLinkRefused({json});");
    }

    private void PushUsage()
    {
        if (Web.CoreWebView2 is null)
        {
            return;
        }
        var timers = _store.LoadUsageTimers();
        if (timers.TimersMs.Count == 0 && timers.ResetAtMs.Count == 0)
        {
            return;
        }
        var payload = JsonSerializer.Serialize(new
        {
            usageTimersMs = timers.TimersMs,
            usageResetAtMs = timers.ResetAtMs,
            usageBucketsMs = timers.BucketsMs.ToDictionary(g => g.Key, g => WebStore.BucketJson(g.Value))
        });
        _ = Web.CoreWebView2.ExecuteScriptAsync(
            $"window.__cbApplyNativeUsage && window.__cbApplyNativeUsage({payload});");
    }

    private void PushPermission()
    {
        if (Web.CoreWebView2 is null)
        {
            return;
        }
        // App blocking needs no special OS permission on Windows for
        // same-integrity windows, so the Device Control UI always reads granted.
        _ = Web.CoreWebView2.ExecuteScriptAsync(
            "window.__cbPermissionState && window.__cbPermissionState({\"appBlockingGranted\":true});");
    }

    private bool _flushedForClosing;
    private bool _closingFlush;
    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_guard.ShouldCancelClose())
        {
            e.Cancel = true;
            MessageBox.Show(this, NativeLanguage.Text("windows.quit.body", _guard.WarningMessage), NativeLanguage.Text("windows.quit.title", _guard.WarningTitle),
                MessageBoxButton.OK, MessageBoxImage.Warning, MessageBoxResult.OK,
                NativeLanguage.Language == "ar" ? MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign : MessageBoxOptions.None);
            return;
        }
        if (!_flushedForClosing)
        {
            e.Cancel = true; if(_closingFlush) return; _closingFlush=true; _timer?.Stop(); await _mcp.StopAsync(); await _hub.StopAsync(); await _classifier.ShutdownAsync(); _flushedForClosing = true; Close(); return;
        }
        _mcp.Dispose();
        _classifier.Dispose();
        _timer?.Stop();
        _monitor?.Dispose();
        _hub.Stop();
        // Close every auxiliary top-level window too, or the app would keep
        // running (each is a window under the default OnLastWindowClose mode).
        _quickAdd?.Close();
        _quickAdd = null;
        _overlay?.Close();
        _overlay = null;
        _toast?.Close();
        _toast = null;
        _panel?.Teardown();
        _panel = null;
    }
}
