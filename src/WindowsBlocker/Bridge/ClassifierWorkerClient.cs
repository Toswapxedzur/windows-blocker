using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using WindowsBlocker.WebUI;

namespace WindowsBlocker.Bridge;

// Creator-owned process identity; never read from the worker JSON payload.
public sealed record WorkerEvent(long SessionGeneration, JsonObject Message);

public enum WorkerShutdownKind { AlreadyStopped, CleanExit, Failed, ForcedTermination }
public sealed record WorkerShutdownResult(WorkerShutdownKind Kind, int? ExitCode = null, string? Error = null)
{
    public bool Clean => Kind is WorkerShutdownKind.AlreadyStopped or WorkerShutdownKind.CleanExit;
}

// The bundled shared Swift backend runs as the same unelevated user. Its
// stdout is only framed JSONL; stderr goes to developer diagnostics.
public sealed class ClassifierWorkerClient : IDisposable
{
    private sealed class Session(Process process, long generation)
    {
        public Process Process { get; } = process;
        public long Generation { get; } = generation;
        public StreamWriter Input { get; } = process.StandardInput;
        public Task Writes { get; set; } = Task.CompletedTask;
        public string? WriteError { get; set; }
        public bool ActivitySettingsNotifications { get; set; }
        public TaskCompletionSource<WorkerShutdownResult> Exit { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private sealed record Pending(Session Session, TaskCompletionSource<JsonNode?> Completion);
    private readonly ConcurrentDictionary<string, Pending> _pending = new();
    private readonly object _gate = new();
    private Session? _session;
    private long _generation;
    private Task<WorkerShutdownResult>? _shutdown;
    private bool _stopping;
    private bool _disposed;
    public WorkerShutdownResult? LastShutdownResult { get; private set; }
    public event Action<WorkerEvent>? Event;
    public event Action<long>? ActivitySettingsCapabilityChanged;
    public (long Generation, bool Supported) ActivitySettingsAuthority
    {
        get { lock (_gate) return (_generation, _session is { } current && !current.Process.HasExited && current.ActivitySettingsNotifications); }
    }
    public bool SupportsActivitySettingsNotifications => ActivitySettingsAuthority.Supported;
    private readonly string? _executablePath;
    public ClassifierWorkerClient(string? executablePath = null) => _executablePath = executablePath;

    public async Task<JsonNode?> Request(string operation, JsonObject data)
    {
        var id = Guid.NewGuid().ToString();
        var completion = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Session session;
        Task write;
        bool started;
        // Admission and FIFO enqueue are one critical section. Shutdown captures
        // this tail, so every admitted frame is written before stdin EOF.
        lock (_gate)
        {
            if (_stopping || _disposed) throw new InvalidOperationException("classifier-worker-stopping");
            var frame = new JsonObject { ["id"] = id, ["operation"] = operation, ["data"] = data.DeepClone() }.ToJsonString();
            var previous = _session;
            session = EnsureStarted();
            started = previous != session;
            _pending[id] = new(session, completion);
            write = session.Writes = session.Writes.ContinueWith(async _ =>
            {
                try { await session.Input.WriteLineAsync(frame); await session.Input.FlushAsync(); }
                catch (Exception ex) { lock (_gate) session.WriteError ??= ex.Message; throw; }
            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();
        }
        // Never call into the Hub while holding the worker gate. Its welcome
        // capability getter takes this gate while the Hub owns its own gate.
        if (started) ActivitySettingsCapabilityChanged?.Invoke(session.Generation);
        try
        {
            await write;
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(operation is "action" or "mcp" ? 120 : operation == "hub" ? 30 : 20));
        }
        finally { _pending.TryRemove(id, out _); }
    }

    // Caller holds _gate. Only the reader owns Process disposal, under this gate.
    private Session EnsureStarted()
    {
        if (_session is { } current && !current.Process.HasExited) return current;
        var exe = _executablePath ?? Path.Combine(AppContext.BaseDirectory, "ClassifierWorker", "VaultClassifierWorker.exe");
        if (!File.Exists(exe)) throw new InvalidOperationException("classifier-worker-unavailable");
        var start = new ProcessStartInfo(exe) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, WorkingDirectory = Path.GetDirectoryName(exe)! };
        start.Environment["VAULT_STORAGE_APP_VERSION"] = WindowsBlocker.Core.StorageSchema.AppVersion;
        start.Environment["VAULT_ENVIRONMENT"] = Storage.Development ? "development" : "production";
        start.Environment["ADAMANCIA_VAULT_ENVIRONMENT"] = Storage.Development ? "development" : "production";
        start.Environment["VAULT_DATA_ROOT"] = Path.Combine(Storage.RootDirectory, "Classifier");
        var generation = checked(_generation + 1);
        var process = Process.Start(start) ?? throw new InvalidOperationException("classifier-worker-launch-failed");
        var session = new Session(process, generation);
        _generation = generation;
        _session = session;
        _ = Task.Run(() => ReadAsync(session));
        _ = Task.Run(async () => { try { while (await process.StandardError.ReadLineAsync() is { } line) Trace.WriteLine("Classifier: " + line); } catch { } });
        return session;
    }

    private async Task ReadAsync(Session session)
    {
        var process = session.Process;
        string? error = null;
        try
        {
            while (await process.StandardOutput.ReadLineAsync() is { } line)
            {
                if (Encoding.UTF8.GetByteCount(line) > 16 * 1024 * 1024) throw new InvalidDataException("Worker response too large");
                if (JsonNode.Parse(line) is not JsonObject message) continue;
                if (message["event"] != null)
                {
                    bool active; bool changed = false;
                    lock (_gate)
                    {
                        active = _session == session;
                        if (active && message["event"]?.GetValue<string>() == "ready")
                        {
                            var supported = !process.HasExited && message["activitySettingsNotifications"] is JsonValue flag && flag.TryGetValue<bool>(out var value) && value;
                            changed = session.ActivitySettingsNotifications != supported;
                            session.ActivitySettingsNotifications = supported;
                        }
                    }
                    if (changed) ActivitySettingsCapabilityChanged?.Invoke(session.Generation);
                    if (active)
                    {
                        if (message["event"]?.GetValue<string>() == "error") Trace.WriteLine("Classifier background error: " + message["error"]?.GetValue<string>() + " (" + message["sourceEvent"]?.GetValue<string>() + ")");
                        Event?.Invoke(new WorkerEvent(session.Generation, message));
                    }
                    continue;
                }
                var id = message["id"]?.GetValue<string>() ?? "";
                if (!_pending.TryGetValue(id, out var pending) || pending.Session != session || !_pending.TryRemove(id, out _)) continue;
                if (message["ok"]?.GetValue<bool>() == true) pending.Completion.TrySetResult(message["value"]?.DeepClone());
                else pending.Completion.TrySetException(new InvalidOperationException(message["error"]?.GetValue<string>() ?? "classifier-worker-error"));
            }
        }
        catch (Exception ex) { error = ex.Message; Trace.WriteLine("Classifier worker: " + error); }
        finally
        {
            foreach (var (id, pending) in _pending)
                if (pending.Session == session && _pending.TryRemove(id, out _)) pending.Completion.TrySetException(new InvalidOperationException("classifier-worker-stopped"));
            try
            {
                await process.WaitForExitAsync();
                var code = process.ExitCode;
                session.Exit.TrySetResult(new(error == null && code == 0 ? WorkerShutdownKind.CleanExit : WorkerShutdownKind.Failed, code, error));
            }
            catch (Exception ex) { session.Exit.TrySetResult(new(WorkerShutdownKind.Failed, Error: ex.Message)); }
            bool ended;
            lock (_gate) { ended = _session == session; if (ended) _session = null; process.Dispose(); }
            if (ended) ActivitySettingsCapabilityChanged?.Invoke(session.Generation);
        }
    }

    public Task<WorkerShutdownResult> ShutdownAsync()
    {
        lock (_gate)
        {
            if (_shutdown != null) return _shutdown;
            _stopping = true;
            var session = _session;
            var writes = session?.Writes ?? Task.CompletedTask;
            // Run outside producer locks/UI context. EOF is the shared worker's
            // cooperative shutdown signal; do not wait for a flush reply first.
            return _shutdown = Task.Run(() => ShutdownCoreAsync(session, writes));
        }
    }
    private async Task<WorkerShutdownResult> ShutdownCoreAsync(Session? session, Task writes)
    {
        WorkerShutdownResult result;
        if (session == null) result = new(WorkerShutdownKind.AlreadyStopped);
        else
        {
            try
            {
                // Retain the existing abnormal-exit bound for a blocked pipe or
                // child. EOF is not withheld waiting for any request reply.
                await writes.WaitAsync(TimeSpan.FromSeconds(3));
                lock (_gate)
                {
                    if (_session == session && !session.Exit.Task.IsCompleted) session.Input.Close();
                }
                result = await session.Exit.Task.WaitAsync(TimeSpan.FromSeconds(3));
                if (session.WriteError != null) result = result with { Kind = WorkerShutdownKind.Failed, Error = session.WriteError };
            }
            catch (TimeoutException)
            {
                result = await ForcedResultAsync(session, "classifier-worker-shutdown-timeout");
            }
            catch (Exception ex)
            {
                // Failed writes cannot hide the EOF attempt. The worker can
                // still flush already received data normally.
                try { lock (_gate) { if (_session == session && !session.Exit.Task.IsCompleted) session.Input.Close(); } }
                catch (Exception closeError) { Trace.WriteLine("Classifier EOF failed: " + closeError.Message); }
                try { await session.Exit.Task.WaitAsync(TimeSpan.FromSeconds(3)); }
                catch (TimeoutException)
                {
                    result = await ForcedResultAsync(session, ex.Message);
                    LastShutdownResult = result;
                    Trace.WriteLine($"Classifier shutdown: {result.Kind}; error={result.Error}");
                    return result;
                }
                result = new(WorkerShutdownKind.Failed, Error: ex.Message);
            }
        }
        LastShutdownResult = result;
        Trace.WriteLine($"Classifier shutdown: {result.Kind}; exit={result.ExitCode}; error={result.Error}");
        return result;
    }
    private async Task<WorkerShutdownResult> ForcedResultAsync(Session session, string error)
    {
        var killed = Terminate(session);
        try
        {
            var exit = await session.Exit.Task.WaitAsync(TimeSpan.FromSeconds(3));
            return new(killed ? WorkerShutdownKind.ForcedTermination : WorkerShutdownKind.Failed, exit.ExitCode, error);
        }
        catch (TimeoutException) { return new(killed ? WorkerShutdownKind.ForcedTermination : WorkerShutdownKind.Failed, Error: error + "; child-exit-unconfirmed"); }
    }
    private bool Terminate(Session session)
    {
        lock (_gate)
        {
            if (session.Exit.Task.IsCompleted || _session != session) return false;
            try { if (!session.Process.HasExited) { session.Process.Kill(true); return true; } }
            catch (Exception ex) { Trace.WriteLine("Classifier forced termination failed: " + ex.Message); }
            return false;
        }
    }
    public void Dispose()
    {
        Session? session;
        lock (_gate) { if (_disposed) return; _disposed = true; _stopping = true; session = _session; }
        if (session != null && Terminate(session))
        {
            LastShutdownResult = new(WorkerShutdownKind.ForcedTermination, Error: "classifier-worker-disposed-before-clean-exit");
            Trace.WriteLine("Classifier shutdown: forced termination during Dispose");
        }
    }
}
