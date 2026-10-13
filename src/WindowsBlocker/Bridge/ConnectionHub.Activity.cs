using System.Text;
using System.Text.Json.Nodes;

namespace WindowsBlocker.Bridge;

public sealed partial class ConnectionHub
{
    // The host supplies this from the accepted bundled worker, never from a
    // browser message or the classifier's general-purpose broadcast route.
    public Func<bool>? ActivitySettingsNotificationsSupported { get; set; }
    private string? _activitySettingsEpoch;
    private long _activitySettingsRevision = -1;
    private const long MaxJavaScriptInteger = 9_007_199_254_740_991;

    public bool PublishWorkerActivitySettings(JsonObject evt)
    {
        if (Text(evt["event"]) != "activity-settings-committed" ||
            evt["value"] is not JsonObject body || body["settings"] is not JsonObject ||
            body["epoch"] is not JsonValue epochValue || !epochValue.TryGetValue<string>(out var epoch) ||
            !Guid.TryParseExact(epoch, "D", out _) ||
            body["revision"] is not JsonValue revisionValue || !revisionValue.TryGetValue<long>(out var revision) ||
            revision < 0 || revision > MaxJavaScriptInteger ||
            body["changedAtMs"] is not JsonValue timeValue || !timeValue.TryGetValue<double>(out var changedAt) ||
            !double.IsFinite(changedAt) || changedAt < 0 || changedAt > MaxJavaScriptInteger ||
            Encoding.UTF8.GetByteCount(body.ToJsonString()) > 88_000) return false;
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
}
