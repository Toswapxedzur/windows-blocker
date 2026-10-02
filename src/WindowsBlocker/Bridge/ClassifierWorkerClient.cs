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
    private readonly ConcurrentDictionary<string,TaskCompletionSource<JsonNode?>> _pending = new();
    private readonly SemaphoreSlim _write = new(1);
    private Process? _process;
    private readonly object _gate = new();
    public event Action<JsonObject>? Event;
    public async Task<JsonNode?> Request(string operation, JsonObject data)
    {
        EnsureStarted();
        var id = Guid.NewGuid().ToString(); var completion = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously); _pending[id] = completion;
        try
        {
            await _write.WaitAsync();
            try { await _process!.StandardInput.WriteLineAsync(new JsonObject { ["id"] = id, ["operation"] = operation, ["data"] = data.DeepClone() }.ToJsonString()); await _process.StandardInput.FlushAsync(); }
            finally { _write.Release(); }
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(operation is "action" or "mcp" ? 120 : 20));
        }
        finally { _pending.TryRemove(id, out _); }
    }
    private void EnsureStarted()
    {
        lock (_gate)
        {
            if (_process is { HasExited:false }) return;
            var exe = Path.Combine(AppContext.BaseDirectory, "ClassifierWorker", "VaultClassifierWorker.exe");
            if (!File.Exists(exe)) throw new InvalidOperationException("classifier-worker-unavailable");
            var start = new ProcessStartInfo(exe) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, WorkingDirectory = Path.GetDirectoryName(exe)! };
            start.Environment["VAULT_ENVIRONMENT"] = Storage.Development ? "development" : "production";
            start.Environment["ADAMANCIA_VAULT_ENVIRONMENT"] = Storage.Development ? "development" : "production";
            start.Environment["VAULT_DATA_ROOT"] = Path.Combine(Storage.RootDirectory, "Classifier");
            _process = Process.Start(start) ?? throw new InvalidOperationException("classifier-worker-launch-failed");
            _ = ReadAsync(_process);
            _ = Task.Run(async () => { while (await _process.StandardError.ReadLineAsync() is { } line) Trace.WriteLine("Classifier: " + line); });
        }
    }
    private async Task ReadAsync(Process process)
    {
        try
        {
            while (await process.StandardOutput.ReadLineAsync() is { } line)
            {
                if (line.Length > 8 * 1024 * 1024) throw new InvalidDataException("Worker response too large");
                if (JsonNode.Parse(line) is not JsonObject message) continue;
                if (message["event"] != null) { Event?.Invoke(message); continue; }
                var id = message["id"]?.GetValue<string>() ?? "";
                if (!_pending.TryRemove(id,out var completion)) continue;
                if (message["ok"]?.GetValue<bool>() == true) completion.TrySetResult(message["value"]?.DeepClone()); else completion.TrySetException(new InvalidOperationException(message["error"]?.GetValue<string>() ?? "classifier-worker-error"));
            }
        }
        catch (Exception ex) { Trace.WriteLine("Classifier worker: " + ex.Message); }
        finally { foreach (var completion in _pending.Values) completion.TrySetException(new InvalidOperationException("classifier-worker-stopped")); _pending.Clear(); }
    }
    public void Dispose()
    {
        lock (_gate) { if (_process is { HasExited:false }) _process.Kill(true); _process?.Dispose(); _process = null; }
    }
}
