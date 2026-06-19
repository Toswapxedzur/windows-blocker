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
    private readonly EnforcementEngine _engine;
    private readonly SelfPreservationGuard _guard = new();
    private WinEventMonitor? _monitor;
    private DispatcherTimer? _timer;
    private IntPtr _selfHwnd;

    public MainWindow()
    {
        InitializeComponent();
        _engine = new EnforcementEngine(_store, _registry);
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
    }

    private async System.Threading.Tasks.Task InitWebViewAsync()
    {
        var assets = Path.Combine(AppContext.BaseDirectory, "WebAssets");
        Web.CreationProperties = new CoreWebView2CreationProperties
        {
            UserDataFolder = Path.Combine(Storage.RootDirectory, "WebView2")
        };
        await Web.EnsureCoreWebView2Async();
        var core = Web.CoreWebView2;

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

        core.Navigate($"https://{VirtualHost}/popup.html");
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
        _engine.Tick();
        PushUsage();
        PushPermission();
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
    }
}
