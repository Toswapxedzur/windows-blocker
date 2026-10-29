using System.Text.Json;
using System.Text.Json.Nodes;
using WindowsBlocker.WebUI;

namespace WindowsBlocker.Rules;

/// Runs the shared, content-neutral record transaction in the trusted policy
/// worker and commits its result to this group's native store document.
public sealed class RuleRecordService(WebStore store)
{
    private readonly SemaphoreSlim _transactions = new(1, 1);

    public async Task<JsonObject> Request(CustomRuleRuntime runtime, string groupId, string payload)
    {
        await _transactions.WaitAsync();
        try
        {
            var raw = JsonNode.Parse(store.LoadRawJson() ?? "{}");
            if (raw is not JsonObject document ||
                !(document["blockedGroups"] as JsonArray ?? new()).OfType<JsonObject>().Any(group =>
                    group["id"]?.ToString() == groupId && group["groupType"]?.ToString() == "custom" &&
                    group["enabled"]?.GetValueKind() == JsonValueKind.True)) return Failure("store-unavailable");
            JsonNode? request;
            try { request = JsonNode.Parse(payload); }
            catch { return Failure("store-invalid-request"); }
            var rawRecords = document["cbRuleRecords"]?.DeepClone();
            var initialRecords = rawRecords ?? new JsonObject { ["version"] = 1, ["groups"] = new JsonObject() };
            var result = await runtime.PolicyAsync("VaultRuleRecords", "transact", [initialRecords, groupId, request,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), Guid.NewGuid().ToString("N")]);
            if (result == null || !result.TryGetValue("value", out var value) || value.ValueKind != JsonValueKind.Object)
                return Failure(runtime.LastError ?? "store-unavailable");
            var transaction = JsonNode.Parse(value.GetRawText()) as JsonObject;
            if (transaction?["reply"] is not JsonObject reply) return Failure("store-unavailable");
            if (transaction["changed"]?.GetValueKind() == JsonValueKind.True)
            {
                store.Update(current => {
                    if (current["cbRuleRecords"]?.ToJsonString() != rawRecords?.ToJsonString()) throw new InvalidOperationException("store-conflict");
                    var group = (current["blockedGroups"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(item => item["id"]?.ToString() == groupId);
                    if (group == null || group["groupType"]?.ToString() != "custom" || group["enabled"]?.GetValueKind() != JsonValueKind.True)
                        throw new InvalidOperationException("store-unavailable");
                    current["cbRuleRecords"] = transaction["document"]?.DeepClone();
                });
            }
            return (JsonObject)reply.DeepClone();
        }
        catch (InvalidOperationException error) { return Failure(error.Message); }
        catch { return Failure("store-unavailable"); }
        finally { _transactions.Release(); }
    }

    private static JsonObject Failure(string message) => new() { ["ok"] = false, ["error"] = message };
}
