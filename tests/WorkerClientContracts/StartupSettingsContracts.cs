using System.Collections.Concurrent;
using System.Diagnostics;
using WindowsBlocker.Bridge;

internal static class StartupSettingsContracts
{
    public static async Task Run(string executable, string root)
    {
        Directory.CreateDirectory(root);
        var oldMode = Environment.GetEnvironmentVariable("VAULT_CONTRACT_STARTUP_SETTINGS");
        var oldRelease = Environment.GetEnvironmentVariable("VAULT_CONTRACT_STARTUP_RELEASE");
        using var diagnostics = new StartupDiagnostics();
        Trace.Listeners.Add(diagnostics);
        try
        {
            // Both independent prerequisites must finish. The native monitor
            // keeps its existing position after UI, before the settings await.
            var release = Path.Combine(root, "startup-settings.release");
            Environment.SetEnvironmentVariable("VAULT_CONTRACT_STARTUP_SETTINGS", "gated");
            Environment.SetEnvironmentVariable("VAULT_CONTRACT_STARTUP_RELEASE", release);
            using (var worker = new ClassifierWorkerClient(executable))
            {
                var seen = ObserveRequest(worker);
                var ui = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var monitor = 0;
                var welcome = false;
                var startup = WelcomeAfterStartup();
                async Task WelcomeAfterStartup()
                {
                    await NativeStartup.PrepareAsync(worker, () => ui.Task, () => monitor++, () => false);
                    welcome = true;
                }
                try
                {
                    var pid = await seen.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    Need(worker.SupportsActivitySettingsNotifications && monitor == 0 && !welcome && !startup.IsCompleted,
                        "Worker did not start/announce readiness while UI remained delayed");
                    ui.SetResult();
                    await Until(() => monitor == 1);
                    Need(!welcome && !startup.IsCompleted, "Hub welcome preceded the existing settings reply");
                    File.WriteAllText(release, "release");
                    await startup.WaitAsync(TimeSpan.FromSeconds(5));
                    Need(welcome && monitor == 1, "Completed prerequisites did not allow startup exactly once");
                    await CleanExit(worker, pid);
                }
                finally
                {
                    File.WriteAllText(release, "release");
                    ui.TrySetResult();
                    await worker.ShutdownAsync();
                    await startup;
                }
            }
            Console.WriteLine("PASS Startup worker/settings overlap delayed UI; monitor follows UI and welcome waits for settings");

            Environment.SetEnvironmentVariable("VAULT_CONTRACT_STARTUP_SETTINGS", "wait-for-eof");
            using (var worker = new ClassifierWorkerClient(executable))
            {
                var seen = ObserveRequest(worker);
                var ui = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var closing = false;
                var monitor = 0;
                var welcome = false;
                var before = diagnostics.SettingsErrors;
                var startup = WelcomeAfterStartup();
                async Task WelcomeAfterStartup()
                {
                    await NativeStartup.PrepareAsync(worker, () => ui.Task, () => monitor++, () => closing);
                    if (!closing) welcome = true;
                }
                try
                {
                    var pid = await seen.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    closing = true;
                    await CleanExit(worker, pid);
                    await Until(() => diagnostics.SettingsErrors > before);
                    Need(!ui.Task.IsCompleted, "Quit diagnostic depended on resuming UI initialization");
                    ui.SetResult();
                    await startup.WaitAsync(TimeSpan.FromSeconds(5));
                    Need(monitor == 0 && !welcome, "Delayed UI resumed monitor/hub after Quit");
                }
                finally
                {
                    closing = true;
                    await worker.ShutdownAsync();
                    ui.TrySetResult();
                    await startup;
                }
            }
            Console.WriteLine("PASS Quit during delayed UI drains EOF, observes settings failure and never starts monitor/hub");

            using (var worker = new ClassifierWorkerClient(executable))
            {
                var seen = ObserveRequest(worker);
                var failure = new InvalidOperationException("main-webview-fixture-failure");
                var monitor = 0;
                var before = diagnostics.SettingsErrors;
                try
                {
                    await NativeStartup.PrepareAsync(worker, () => Task.FromException(failure), () => monitor++, () => false);
                    throw new Exception("Main WebView failure was hidden");
                }
                catch (InvalidOperationException ex) when (ReferenceEquals(ex, failure)) { }
                var pid = await seen.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Need(monitor == 0 && diagnostics.SettingsErrors > before, "Main UI failure missed worker/request cleanup diagnostics");
                await CleanExit(worker, pid);
            }
            Console.WriteLine("PASS Main WebView failure closes eager worker by EOF, observes settings failure and preserves original error");

            using (var worker = new ClassifierWorkerClient(executable))
            {
                var seen = ObserveRequest(worker);
                var failure = new InvalidOperationException("monitor-fixture-failure");
                var monitor = 0;
                var welcome = false;
                var before = diagnostics.SettingsErrors;
                try
                {
                    await NativeStartup.PrepareAsync(worker, () => Task.CompletedTask,
                        () => { monitor++; throw failure; }, () => false);
                    welcome = true;
                    throw new Exception("Monitor failure was hidden");
                }
                catch (InvalidOperationException ex) when (ReferenceEquals(ex, failure)) { }
                var pid = await seen.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Need(monitor == 1 && !welcome && diagnostics.SettingsErrors > before,
                    "Monitor failure missed request cleanup or allowed later startup");
                await CleanExit(worker, pid);
            }
            Console.WriteLine("PASS Monitor initialization failure drains worker EOF, observes settings failure and prevents later startup");

            Environment.SetEnvironmentVariable("VAULT_CONTRACT_STARTUP_SETTINGS", "refuse");
            using (var worker = new ClassifierWorkerClient(executable))
            {
                var seen = ObserveRequest(worker);
                var ui = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var monitor = 0;
                var startup = NativeStartup.PrepareAsync(worker, () => ui.Task, () => monitor++, () => false);
                try
                {
                    var pid = await seen.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    await Until(() => diagnostics.Contains("startup-settings-fixture-refusal"));
                    Need(!startup.IsCompleted && monitor == 0, "Settings fault consumed or bypassed pending UI initialization");
                    ui.SetResult();
                    await startup.WaitAsync(TimeSpan.FromSeconds(5));
                    Need(monitor == 1, "Observed settings failure changed the existing nonfatal startup behavior");
                    await CleanExit(worker, pid);
                }
                finally { ui.TrySetResult(); await worker.ShutdownAsync(); await startup; }
            }
            Console.WriteLine("PASS Settings refusal is observed while UI is pending and retains existing nonfatal behavior");

            using (var worker = new ClassifierWorkerClient(executable))
            {
                var seen = ObserveRequest(worker);
                var ui = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var context = new HeldContext();
                var closing = false;
                var monitor = 0;
                var before = diagnostics.SettingsErrors;
                var previous = SynchronizationContext.Current;
                Task startup;
                SynchronizationContext.SetSynchronizationContext(context);
                try { startup = NativeStartup.PrepareAsync(worker, () => ui.Task, () => monitor++, () => closing); }
                finally { SynchronizationContext.SetSynchronizationContext(previous); }
                try
                {
                    var pid = await seen.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    // No dispatcher callback is pumped before this assertion.
                    await Until(() => diagnostics.SettingsErrors > before);
                    Need(!startup.IsCompleted && monitor == 0, "Settings observation resumed UI initialization");
                    closing = true;
                    await CleanExit(worker, pid);
                    ui.SetResult();
                    await Until(() => context.Pending);
                    context.Drain();
                    await startup.WaitAsync(TimeSpan.FromSeconds(5));
                    Need(monitor == 0, "Restored dispatcher resumed monitor after Quit");
                }
                finally
                {
                    closing = true;
                    await worker.ShutdownAsync();
                    ui.TrySetResult();
                    await Until(() => { context.Drain(); return startup.IsCompleted; });
                    await startup.WaitAsync(TimeSpan.FromSeconds(5));
                }
            }
            Console.WriteLine("PASS Settings failure observation and EOF are independent of a nonpumping caller context; Quit prevents later monitor startup");

            using (var worker = new ClassifierWorkerClient(executable))
            {
                var invoked = 0;
                await NativeStartup.PrepareAsync(worker, () => { invoked++; return Task.CompletedTask; }, () => invoked++, () => true);
                Need(invoked == 0 && worker.ActivitySettingsAuthority.Generation == 0,
                    "Already-closing startup created a worker or resumed UI/monitor");
                Need((await worker.ShutdownAsync()).Kind == WorkerShutdownKind.AlreadyStopped,
                    "Already-closing startup changed the unused worker lifecycle");
            }
            Console.WriteLine("PASS Already-closing startup creates no worker, UI initialization or monitor");
        }
        finally
        {
            Trace.Listeners.Remove(diagnostics);
            Environment.SetEnvironmentVariable("VAULT_CONTRACT_STARTUP_SETTINGS", oldMode);
            Environment.SetEnvironmentVariable("VAULT_CONTRACT_STARTUP_RELEASE", oldRelease);
        }
    }

    private static TaskCompletionSource<int> ObserveRequest(ClassifierWorkerClient worker)
    {
        var seen = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        worker.Event += received =>
        {
            if (received.Message["event"]?.GetValue<string>() == "startup-settings-request")
                seen.TrySetResult(received.Message["pid"]!.GetValue<int>());
        };
        return seen;
    }

    private static async Task CleanExit(ClassifierWorkerClient worker, int pid)
    {
        var result = await worker.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Need(result.Kind == WorkerShutdownKind.CleanExit && result.ExitCode == 0, "Startup fixture did not exit normally by EOF");
        try { using var child = Process.GetProcessById(pid); Need(child.HasExited, "Original startup fixture remains alive"); }
        catch (ArgumentException) { }
    }

    private static async Task Until(Func<bool> ready)
    {
        var deadline = Stopwatch.StartNew();
        while (!ready())
        {
            Need(deadline.Elapsed < TimeSpan.FromSeconds(5), "Bounded startup observation did not arrive");
            await Task.Delay(10);
        }
    }
    private static void Need(bool valid, string message) { if (!valid) throw new Exception(message); }
    private sealed class HeldContext : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> callbacks = new();
        public bool Pending => !callbacks.IsEmpty;
        public override void Post(SendOrPostCallback callback, object? state) => callbacks.Enqueue((callback, state));
        public void Drain()
        {
            var previous = Current;
            SetSynchronizationContext(this);
            try { while (callbacks.TryDequeue(out var work)) work.Callback(work.State); }
            finally { SetSynchronizationContext(previous); }
        }
    }
    private sealed class StartupDiagnostics : TraceListener
    {
        private readonly ConcurrentQueue<string> lines = new();
        public int SettingsErrors => lines.Count(line => line.StartsWith("Activity startup settings unavailable: ", StringComparison.Ordinal));
        public bool Contains(string text) => lines.Any(line => line.Contains(text, StringComparison.Ordinal));
        public override void Write(string? message) { if (message != null) lines.Enqueue(message); }
        public override void WriteLine(string? message) => Write(message);
    }
}
