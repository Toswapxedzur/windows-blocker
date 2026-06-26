using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
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
    private const string VirtualHost = "appassets.windowsblocker";
    private const string StoreKey = "__cb_chrome_storage__";

    private readonly WebStore _store = new();
    private readonly BlockedAppRegistry _registry = new();
    private readonly ConnectionHub _hub = new();
    private readonly EnforcementEngine _engine;
    private readonly RuleEngine _ruleEngine;
    private readonly SelfPreservationGuard _guard = new();
    private WinEventMonitor? _monitor;
    private DispatcherTimer? _timer;
    private TimerOverlayWindow? _overlay;
    private ToastOverlayWindow? _toast;
    private PanelOverlay? _panel;
    private CustomRuleRuntime? _runtime;
    private bool _ruleBusy;
    private IntPtr _selfHwnd;

    // Leading-edge throttle for high-frequency panel change events (slider drag,
    // typing) so the rule isn't dispatched on every keystroke — mirrors macOS's
    // 0.1s panel throttle with a trailing flush.
    private readonly Dictionary<string, DateTime> _panelLastFire = new();
    private readonly Dictionary<string, Action> _panelPending = new();
    private DispatcherTimer? _panelFlush;

    public MainWindow()
    {
        InitializeComponent();
        _engine = new EnforcementEngine(_store, _registry, _hub);
        _ruleEngine = new RuleEngine(_store);
        _store.SeedIfNeeded();
        Loaded += OnLoaded;
        Closing += OnClosing;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _selfHwnd = new WindowInteropHelper(this).Handle;
        _engine.SetSelfWindow(_selfHwnd);
        await InitWebViewAsync();
        StartMonitorAndTimer();

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
        Web.CreationProperties = new CoreWebView2CreationProperties
        {
            UserDataFolder = Path.Combine(Storage.RootDirectory, "WebView2")
        };
        await Web.EnsureCoreWebView2Async();
        var core = Web.CoreWebView2!;

        core.SetVirtualHostNameToFolderMapping(VirtualHost, assets, CoreWebView2HostResourceAccessKind.Allow);
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;

        // 1) Map the WKWebView bridge name chrome-shim.js expects onto WebView2's
        //    postMessage, so chrome-shim.js itself stays verbatim.
        await core.AddScriptToExecuteOnDocumentCreatedAsync(BridgeShimScript);

        // 2) Seed chrome.storage from the native store before chrome-shim.js reads
        //    it (chrome-shim loads from localStorage[StoreKey] on init).
        var seed = _store.LoadRawJson();
        if (seed is not null)
        {
            await core.AddScriptToExecuteOnDocumentCreatedAsync(SeedScript(seed));
        }

        // 3) Inject the verbatim custom-rule runtime (MacBlockerRuntime) so custom
        //    JavaScript rules run inside this page exactly as they do in macOS's
        //    JavaScriptCore. A __nativeGetAllTabs stub returns no tabs (browser
        //    tab reading is the extension's job on Windows).
        var runtimeJs = TryReadAsset(assets, "custom-rule-runtime.js");
        if (runtimeJs is not null)
        {
            await core.AddScriptToExecuteOnDocumentCreatedAsync(
                "window.__nativeGetAllTabs = function(){ return \"[]\"; };");
            await core.AddScriptToExecuteOnDocumentCreatedAsync(runtimeJs);
            _runtime = new CustomRuleRuntime(core);
            _ruleEngine.AttachRuntime(_runtime);
        }

        core.WebMessageReceived += OnWebMessage;
        core.AddWebResourceRequestedFilter("*app-inventory.json", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += OnWebResourceRequested;
        core.NewWindowRequested += (_, args) => args.Handled = true;
        core.NavigationCompleted += OnNavigationCompleted;

        core.Navigate($"https://{VirtualHost}/popup.html");
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

    private static string? TryReadAsset(string assetsDir, string fileName)
    {
        try
        {
            var path = Path.Combine(assetsDir, fileName);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch
        {
            return null;
        }
    }

    private static string SeedScript(string storeJson)
    {
        // Encode the store text as a JS string literal.
        var literal = JsonSerializer.Serialize(storeJson);
        return $"try{{window.localStorage.setItem(\"{StoreKey}\", {literal});}}catch(e){{}}";
    }

    private const string BridgeShimScript = @"
(function(){
  window.webkit = window.webkit || {};
  window.webkit.messageHandlers = window.webkit.messageHandlers || {};
  window.webkit.messageHandlers.cbBridge = {
    postMessage: function(msg){ try { window.chrome.webview.postMessage(msg); } catch (e) {} }
  };
})();";

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            if (!root.TryGetProperty("kind", out var kindEl) || kindEl.ValueKind != JsonValueKind.String)
            {
                return;
            }
            switch (kindEl.GetString())
            {
                case "persist-store":
                    if (root.TryGetProperty("store", out var store))
                    {
                        _store.SaveRaw(store.GetRawText());
                    }
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
                case "group-connect":
                    _hub.ConnectFromBridge(MessageJson(root));
                    break;
                case "group-disconnect":
                    _hub.DisconnectFromBridge(MessageJson(root));
                    break;
                case "group-sync":
                    _hub.SyncFromBridge(MessageJson(root));
                    break;
                case "clusters-status":
                    PushClusters();
                    break;

                // Custom-rule surface: the editor drives the WebView2-hosted
                // MacBlockerRuntime through these, exactly as macOS's BlockerWebView
                // drives MacEnforcementBridge.
                case "run-custom-group":
                {
                    var (gid, src) = ReadGroupSource(root);
                    if (gid.Length > 0)
                    {
                        _ = RunRuleAndPushLogAsync(gid, src);
                    }
                    break;
                }
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

    private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        try
        {
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

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private async void OnTick(object? sender, EventArgs e)
    {
        // A transient failure (e.g. a momentarily malformed store write) must
        // never kill the enforcement timer, or blocking would silently stop.
        try
        {
            // Resolve the focused app once and share it with both engines: native
            // enforcement consumes last tick's rule-blocked set; the rule engine
            // produces this tick's set for the next pass (≤1s latency).
            var foreground = ProcessIdentity.ForWindow(NativeMethods.GetForegroundWindow());
            var status = _engine.Tick(foreground, _ruleEngine.BlockedIdentities);

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
                    rows.AddRange(ruleOut.CustomTimers);
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

    private void RevealLocalFolder()
    {
        try
        {
            var dir = Path.Combine(Storage.RootDirectory, "LocalFiles");
            Directory.CreateDirectory(dir);
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
            $"window.__cbGroupRejected && window.__cbGroupRejected({json});");
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
            usageResetAtMs = timers.ResetAtMs
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

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_guard.ShouldCancelClose())
        {
            e.Cancel = true;
            MessageBox.Show(this, _guard.WarningMessage, _guard.WarningTitle,
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _timer?.Stop();
        _monitor?.Dispose();
        _hub.Stop();
        // Close every auxiliary top-level window too, or the app would keep
        // running (each is a window under the default OnLastWindowClose mode).
        _overlay?.Close();
        _overlay = null;
        _toast?.Close();
        _toast = null;
        _panel?.Teardown();
        _panel = null;
    }
}
