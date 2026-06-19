using System;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using WindowsBlocker.Bridge;
using WindowsBlocker.Enforcement;
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
    private readonly SelfPreservationGuard _guard = new();
    private WinEventMonitor? _monitor;
    private DispatcherTimer? _timer;
    private IntPtr _selfHwnd;

    public MainWindow()
    {
        InitializeComponent();
        _engine = new EnforcementEngine(_store, _registry, _hub);
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

    private void PushAppInventory()
    {
        if (Web.CoreWebView2 is null)
        {
            return;
        }
        try
        {
            var json = AppInventory.BuildJson();
            _ = Web.CoreWebView2.ExecuteScriptAsync(
                $"window.__cbApplyAppInventory && window.__cbApplyAppInventory({json});");
        }
        catch
        {
            // A failure to enumerate apps must not break editor load.
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

                // run-custom-group and the custom-rule surface are intentionally
                // not wired: custom JavaScript rules are out of scope for the port.
                // Snooze/freeze state rides along inside the persisted store.
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
            var json = AppInventory.BuildJson();
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

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        // A transient failure (e.g. a momentarily malformed store write) must
        // never kill the enforcement timer, or blocking would silently stop.
        try
        {
            _engine.Tick();
            PushUsage();
            PushPermission();
            // Mirror macOS: push live bridge status / clusters / rejections each
            // second so the editor's connection panel stays current.
            PushConnectionState();
            PushClusters();
            PushGroupRejection();
        }
        catch
        {
            // Swallow and continue on the next tick.
        }
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
    }
}
