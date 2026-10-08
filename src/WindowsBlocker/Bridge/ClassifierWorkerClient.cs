using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using WindowsBlocker.WebUI;

namespace WindowsBlocker.Bridge;

// The bundled shared Swift backend runs as the same unelevated user. Its
// stdout is only framed JSONL; stderr goes to developer diagnostics.
public sealed class ClassifierWorkerClient : IDisposable
{
    private sealed record Pending(Process Process,TaskCompletionSource<JsonNode?> Completion);
    private readonly ConcurrentDictionary<string,Pending> _pending = new();
    private readonly SemaphoreSlim _write = new(1);
    private Process? _process;
    private readonly object _gate = new();
    public event Action<JsonObject>? Event;
    private readonly string? _executablePath;
    public ClassifierWorkerClient(string? executablePath=null) => _executablePath=executablePath;
    public async Task<JsonNode?> Request(string operation, JsonObject data)
    {
        var process=EnsureStarted();
        var id = Guid.NewGuid().ToString(); var completion = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously); _pending[id] = new(process,completion);
        try
        {
            await _write.WaitAsync();
            try { await process.StandardInput.WriteLineAsync(new JsonObject { ["id"] = id, ["operation"] = operation, ["data"] = data.DeepClone() }.ToJsonString()); await process.StandardInput.FlushAsync(); }
            finally { _write.Release(); }
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(operation is "action" or "mcp" ? 120 : operation=="hub" ? 30 : 20));
        }
        finally { _pending.TryRemove(id, out _); }
    }
    private Process EnsureStarted()
    {
        lock (_gate)
        {
            if (_process is { HasExited:false }) return _process;
            var exe = _executablePath ?? Path.Combine(AppContext.BaseDirectory, "ClassifierWorker", "VaultClassifierWorker.exe");
            if (!File.Exists(exe)) throw new InvalidOperationException("classifier-worker-unavailable");
            var start = new ProcessStartInfo(exe) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, WorkingDirectory = Path.GetDirectoryName(exe)! };
            start.Environment["VAULT_STORAGE_APP_VERSION"] = WindowsBlocker.Core.StorageSchema.AppVersion;
            start.Environment["VAULT_ENVIRONMENT"] = Storage.Development ? "development" : "production";
            start.Environment["ADAMANCIA_VAULT_ENVIRONMENT"] = Storage.Development ? "development" : "production";
            start.Environment["VAULT_DATA_ROOT"] = Path.Combine(Storage.RootDirectory, "Classifier");
            _process = Process.Start(start) ?? throw new InvalidOperationException("classifier-worker-launch-failed");
            var process = _process;
            _ = ReadAsync(process);
            _ = Task.Run(async () => { try { while (await process.StandardError.ReadLineAsync() is { } line) Trace.WriteLine("Classifier: " + line); } catch { } });
            return process;
        }
    }
    private async Task ReadAsync(Process process)
    {
        try
        {
            while (await process.StandardOutput.ReadLineAsync() is { } line)
            {
                if (Encoding.UTF8.GetByteCount(line) > 16 * 1024 * 1024) throw new InvalidDataException("Worker response too large");
                if (JsonNode.Parse(line) is not JsonObject message) continue;
                if (message["event"] != null)
                {
                    bool active; lock(_gate) active=_process==process;
                    if(active)
                    {
                        if(message["event"]?.GetValue<string>()=="error") Trace.WriteLine("Classifier background error: "+message["error"]?.GetValue<string>()+" ("+message["sourceEvent"]?.GetValue<string>()+")");
                        Event?.Invoke(message);
                    }
                    continue;
                }
                var id = message["id"]?.GetValue<string>() ?? "";
                if (!_pending.TryGetValue(id,out var pending) || pending.Process!=process || !_pending.TryRemove(id,out _)) continue;
                var completion=pending.Completion;
                if (message["ok"]?.GetValue<bool>() == true) completion.TrySetResult(message["value"]?.DeepClone()); else completion.TrySetException(new InvalidOperationException(message["error"]?.GetValue<string>() ?? "classifier-worker-error"));
            }
        }
        catch (Exception ex) { Trace.WriteLine("Classifier worker: " + ex.Message); }
        finally
        {
            foreach (var (id,pending) in _pending) if(pending.Process==process && _pending.TryRemove(id,out _)) pending.Completion.TrySetException(new InvalidOperationException("classifier-worker-stopped"));
            lock(_gate) { if(_process==process) _process=null; process.Dispose(); }
        }
    }
    public async Task ShutdownAsync()
    {
        if (_process is not { HasExited:false }) return;
        try
        {
            await Request("activity",new JsonObject { ["kind"] = "native-flush" }).WaitAsync(TimeSpan.FromSeconds(3));
            await Request("hostEvent",new JsonObject { ["kind"] = "flush" }).WaitAsync(TimeSpan.FromSeconds(3));
            _process.StandardInput.Close();
            await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));
        }
        catch { }
    }
    public void Dispose()
    {
        lock (_gate) { if (_process is { HasExited:false }) _process.Kill(true); _process?.Dispose(); _process = null; }
    }
}
