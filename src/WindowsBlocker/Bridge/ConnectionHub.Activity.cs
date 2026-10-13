using System.Text;
using System.Text.Json.Nodes;
using System.Net.WebSockets;

namespace WindowsBlocker.Bridge;

public sealed partial class ConnectionHub
{
    // The host supplies this from the accepted bundled worker, never from a
    // browser message or the classifier's general-purpose broadcast route.
    public Func<bool>? ActivitySettingsNotificationsSupported { get; set; }
    private string? _activitySettingsEpoch;
    private long _activitySettingsRevision = -1;
    private const long MaxJavaScriptInteger = 9_007_199_254_740_991;

    public Task DisconnectActivityBrowsersAsync()
    {
        Task[] closes;
        lock (_gate)
        {
            var peers = _peers.Values.Where(p => p.Connected && LocalHubAuthentication.BrowserPrograms.Contains(p.Program)).ToArray();
            closes = peers.Select(peer =>
            {
                peer.Connected = false;
                peer.ClosingForWorker = true;
                // Share the normal send FIFO: CloseOutput is a WebSocket send
                // and must not overlap a queued snapshot write.
                lock (peer.SendGate)
                    return peer.SendTail = peer.SendTail.ContinueWith(async _ =>
                    {
                        try { if (peer.Socket.State == WebSocketState.Open) await peer.Socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "worker-session-changed", CancellationToken.None); }
                        catch (Exception ex) { System.Diagnostics.Trace.WriteLine("Activity reconnect close: " + ex.Message); }
                        finally { Remove(peer); }
                    }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();
            }).ToArray();
        }
        return Task.WhenAll(closes);
    }

    public bool PublishWorkerActivitySettings(JsonObject evt)
    {
        if (Text(evt["event"]) != "activity-settings-committed" ||
            evt["value"] is not JsonObject body || !ValidActivitySettingsBody(body, out var epoch, out var revision)) return false;
        lock (_gate)
        {
            if (ActivitySettingsNotificationsSupported?.Invoke() != true ||
                (_activitySettingsEpoch == epoch && revision <= _activitySettingsRevision)) return false;
            _activitySettingsEpoch = epoch;
            _activitySettingsRevision = revision;
            var frame = new JsonObject { ["kind"] = "activity-settings-updated", ["body"] = body.DeepClone() };
            // Capture and enqueue under the same gate, so a later commit cannot
            // overtake this one. SendAsync never performs socket I/O inline.
            foreach (var peer in _peers.Values.Where(p => p.Connected && LocalHubAuthentication.BrowserPrograms.Contains(p.Program)))
                _ = SendAsync(peer, frame);
            return true;
        }
    }

    internal static JsonObject ActivitySettingsResponse(JsonNode? response)
    {
        if (response is not JsonObject body || Text(body["kind"]) != "settings" || !ValidActivitySettingsBody(body, out _, out _))
            throw new InvalidDataException("invalid-activity-settings-response");
        var answer = new JsonObject();
        foreach (var key in new[] { "settings", "epoch", "revision", "changedAtMs" }) answer[key] = body[key]!.DeepClone();
        return answer;
    }

    private static bool ValidActivitySettingsBody(JsonObject body, out string? epoch, out long revision)
    {
        epoch = null; revision = -1;
        if (body["settings"] is not JsonObject ||
            body["epoch"] is not JsonValue epochValue || !epochValue.TryGetValue<string>(out epoch) ||
            !Guid.TryParseExact(epoch, "D", out _) ||
            body["revision"] is not JsonValue revisionValue || !revisionValue.TryGetValue<long>(out revision) ||
            revision < 0 || revision > MaxJavaScriptInteger ||
            body["changedAtMs"] is not JsonValue timeValue || !timeValue.TryGetValue<double>(out var changedAt) ||
            !double.IsFinite(changedAt) || changedAt < 0 || changedAt > MaxJavaScriptInteger ||
            Encoding.UTF8.GetByteCount(body.ToJsonString()) > 88_000) return false;
        return true;
    }
}
