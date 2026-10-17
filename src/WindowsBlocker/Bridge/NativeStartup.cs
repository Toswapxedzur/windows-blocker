using System.Diagnostics;
using System.Text.Json.Nodes;

namespace WindowsBlocker.Bridge;

internal static class NativeStartup
{
    public static async Task PrepareAsync(ClassifierWorkerClient worker, Func<Task> initializeWebView,
        Action startMonitor, Func<bool> closing)
    {
        if (closing()) return;
        // Request starts/enqueues synchronously. Independent WebView work stays
        // on its caller's dispatcher; settings still finish before Hub startup.
        var settings = ReadSettingsAsync(worker);
        try
        {
            await initializeWebView();
            if (closing()) return;
            startMonitor();
            await settings;
        }
        catch
        {
            // Failed UI/monitor setup previously had no eager worker to leave
            // behind. Finish the same cooperative shutdown before propagating.
            try
            {
                var shutdown = await worker.ShutdownAsync();
                if (!shutdown.Clean) Trace.WriteLine($"Vault startup worker did not exit cleanly: {shutdown.Kind}; {shutdown.Error}");
            }
            catch (Exception ex) { Trace.WriteLine("Vault startup worker cleanup: " + ex.Message); }
            await settings;
            throw;
        }
    }

    private static async Task ReadSettingsAsync(ClassifierWorkerClient worker)
    {
        try { await worker.Request("activity", new JsonObject { ["kind"] = "settings" }).ConfigureAwait(false); }
        catch (Exception ex) { Trace.WriteLine("Activity startup settings unavailable: " + ex.Message); }
    }
}
