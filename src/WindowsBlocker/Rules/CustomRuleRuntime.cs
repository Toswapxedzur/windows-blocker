using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace WindowsBlocker.Rules;

/// <summary>
/// Drives a dedicated, unprivileged WebView2 rule host. Each group runs in its
/// own disposable Web Worker, so rule code cannot touch the editor DOM/native
/// bridge or observe another group's messages. A deadline terminates a stuck
/// worker without blocking the UI thread.
/// </summary>
public sealed class CustomRuleRuntime
{
    private static readonly TimeSpan ExecutionTimeout = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(2);

    private readonly CoreWebView2 _web;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<string?>> _pending = new();

    private static readonly JsonSerializerOptions SerializeOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly JsonSerializerOptions DeserializeOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public string? LastError { get; private set; }
    public int Generation { get; private set; }

    /// Fired after a group's worker is terminated for exceeding its deadline.
    public event Action<string, string>? GroupReset;

    public CustomRuleRuntime(CoreWebView2 web)
    {
        _web = web;
        _web.WebMessageReceived += OnWebMessageReceived;
    }

    public async Task<bool> IsReadyAsync()
        => await RequestAsync<object>("ping", "", null, null, StartupTimeout) is not null;

    public Task<LoadResult?> LoadAsync(string groupId, string source)
        => RequestAsync<LoadResult>("load", groupId, source, null, ExecutionTimeout);

    public async Task UnloadAsync(string groupId)
        => _ = await RequestAsync<object>("unload", groupId, null, null, ExecutionTimeout);

    public Task<DispatchResult?> DispatchAsync(CustomRuleEvent ev)
        => RequestAsync<DispatchResult>("dispatch", ev.GroupId, null, ev, ExecutionTimeout);

    private async Task<T?> RequestAsync<T>(
        string operation,
        string groupId,
        string? source,
        CustomRuleEvent? ev,
        TimeSpan timeout) where T : class
    {
        var requestId = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[requestId] = completion;
        LastError = null;

        var message = new Dictionary<string, object?>
        {
            ["kind"] = "rule-runtime-request",
            ["requestId"] = requestId,
            ["operation"] = operation,
            ["groupId"] = groupId,
            ["source"] = source,
            ["event"] = ev
        };

        try
        {
            _web.PostWebMessageAsJson(JsonSerializer.Serialize(message, SerializeOptions));
        }
        catch
        {
            _pending.TryRemove(requestId, out _);
            LastError = "runtime-unavailable";
            return null;
        }

        var winner = await Task.WhenAny(completion.Task, Task.Delay(timeout));
        if (winner != completion.Task)
        {
            _pending.TryRemove(requestId, out _);
            LastError = "runtime-timeout";
            if (!string.IsNullOrEmpty(groupId))
            {
                ResetGroup(groupId);
                Generation++;
                GroupReset?.Invoke(groupId, LastError);
            }
            return null;
        }

        var raw = await completion.Task;
        if (raw is null)
        {
            return null;
        }
        try
        {
            return JsonSerializer.Deserialize<T>(raw, DeserializeOptions);
        }
        catch
        {
            LastError = "invalid-runtime-result";
            return null;
        }
    }

    private void ResetGroup(string groupId)
    {
        try
        {
            _web.PostWebMessageAsJson(JsonSerializer.Serialize(new
            {
                kind = "rule-runtime-reset",
                groupId
            }));
        }
        catch
        {
            // The host is already unavailable; the engine still quarantines
            // the offending source through GroupReset.
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var messageJson = e.WebMessageAsJson;
            if (messageJson.Length > 2 * 1024 * 1024)
            {
                return;
            }
            using var document = JsonDocument.Parse(messageJson);
            var root = document.RootElement;
            if (!root.TryGetProperty("kind", out var kind)
                || kind.GetString() != "rule-runtime-response"
                || !root.TryGetProperty("requestId", out var requestIdElement))
            {
                return;
            }
            var requestId = requestIdElement.GetString() ?? "";
            if (!_pending.TryRemove(requestId, out var completion))
            {
                return;
            }
            if (!root.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
            {
                LastError = root.TryGetProperty("error", out var error)
                    ? error.GetString() ?? "runtime-error"
                    : "runtime-error";
                completion.TrySetResult(null);
                return;
            }
            completion.TrySetResult(root.TryGetProperty("result", out var result)
                ? result.GetRawText()
                : "{}");
        }
        catch
        {
            // Ignore messages not emitted by the dedicated rule host.
        }
    }
}
