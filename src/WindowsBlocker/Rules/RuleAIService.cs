using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WindowsBlocker.Bridge;
using WindowsBlocker.WebUI;

namespace WindowsBlocker.Rules;

/// Provider-neutral rule calls. The shared Swift worker owns credentials and
/// generation; this host owns group admission, linked daily usage and replies.
public sealed class RuleAIService
{
    private readonly ConnectionHub _hub;
    private readonly ClassifierWorkerClient _worker;
    private readonly string _ledgerPath;
    private readonly object _usageGate = new();
    private int _inFlight;
    private sealed class Ledger
    {
        public int Version { get; set; } = 1;
        public long Day { get; set; }
        public List<string[]> Reservations { get; set; } = new();
    }

    public RuleAIService(ConnectionHub hub, ClassifierWorkerClient worker)
    {
        _hub = hub;
        _worker = worker;
        _ledgerPath = Path.Combine(Storage.RootDirectory, "rule-ai-usage.json");
    }

    private static JsonObject Failure(string error) => new() {
        ["ok"] = false, ["error"] = error, ["text"] = null, ["citations"] = new JsonArray()
    };

    public async Task<JsonObject> Request(string program, string groupId, string operation, string payload)
    {
        if (operation == "models")
        {
            try
            {
                var result = await _worker.Request("ruleAIModels", new());
                return new JsonObject { ["ok"] = true, ["models"] = result?["models"]?.DeepClone() ?? new JsonArray() };
            }
            catch { return new JsonObject { ["ok"] = false, ["error"] = "ai-unavailable", ["models"] = new JsonArray() }; }
        }
        if (operation != "generate") return Failure("ai-invalid-input");
        if (Interlocked.Increment(ref _inFlight) > 2) { Interlocked.Decrement(ref _inFlight); return Failure("ai-busy"); }
        try
        {
            if (!ValidInput(payload)) return Failure("ai-invalid-input");
            var context = _hub.CaptureRuleAI(program, groupId);
            if (context == null || !ValidConfiguration(context.Configuration, out var limit)) return Failure("ai-not-configured");
            var choices = await _worker.Request("ruleAIModels", new()) as JsonObject;
            if (choices?["models"] is not JsonArray models || !models.OfType<JsonObject>().Any(choice =>
                Text(choice["value"]) == Text(context.Configuration["providerModel"]) &&
                Text(choice["accountID"]) == Text(context.Configuration["accountID"]) &&
                (choice["efforts"] as JsonArray)?.Any(item => Text(item) == Text(context.Configuration["reasoningEffort"])) == true))
                return Failure("ai-not-configured");
            try { Reserve(context.Members, limit); }
            catch (InvalidOperationException ex) { return Failure(ex.Message); }
            var answer = await _worker.Request("ruleAIGenerate", new() {
                ["configuration"] = context.Configuration.DeepClone(), ["payload"] = payload
            }) as JsonObject;
            var current = _hub.CaptureRuleAI(program, groupId);
            if (current == null || current.Revision != context.Revision ||
                current.Configuration.ToJsonString() != context.Configuration.ToJsonString() ||
                !current.Members.SequenceEqual(context.Members)) return Failure("ai-unavailable");
            if (answer == null || Text(answer["text"]).Length == 0) return Failure("ai-invalid-output");
            return new JsonObject { ["ok"] = true, ["error"] = "", ["text"] = answer["text"]?.DeepClone(),
                ["citations"] = answer["citations"]?.DeepClone() ?? new JsonArray() };
        }
        catch { return Failure("ai-request-failed"); }
        finally { Interlocked.Decrement(ref _inFlight); }
    }

    private static string Text(JsonNode? value) => value?.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : "";

    private static bool ValidInput(string payload)
    {
        if (Encoding.UTF8.GetByteCount(payload) > 48_000) return false;
        try
        {
            var request = JsonNode.Parse(payload) as JsonObject;
            if (request == null || request.Any(entry => entry.Key is not ("prompt" or "input"))) return false;
            var prompt = Text(request["prompt"]);
            return !string.IsNullOrWhiteSpace(prompt) && Encoding.UTF8.GetByteCount(prompt) <= 20_000 &&
                (request["input"] == null || Encoding.UTF8.GetByteCount(request["input"]!.ToJsonString()) <= 20_000);
        }
        catch { return false; }
    }

    private static bool ValidConfiguration(JsonObject configuration, out int? limit)
    {
        limit = null;
        var model = Text(configuration["providerModel"]);
        var account = Text(configuration["accountID"]);
        var effort = Text(configuration["reasoningEffort"]);
        if (model.Length is < 1 or > 520 || account.Length > 256 || effort is not ("default" or "low" or "medium" or "high")) return false;
        if (!model.StartsWith("chatgpt:", StringComparison.Ordinal) && !model.StartsWith("api:", StringComparison.Ordinal)) return false;
        if (configuration["dailyRequestLimit"] != null)
        {
            if (configuration["dailyRequestLimit"]?.GetValueKind() != JsonValueKind.Number ||
                !int.TryParse(configuration["dailyRequestLimit"]!.ToJsonString(), out var value) || value is < 1 or > 100_000) return false;
            limit = value;
        }
        return true;
    }

    private void Reserve(string[] members, int? limit)
    {
        if (members.Length is < 1 or > 16 || members.Any(value => value.Length is < 1 or > 512)) throw new InvalidOperationException("ai-not-configured");
        lock (_usageGate)
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_ledgerPath)!);
            using var lockFile = new FileStream(_ledgerPath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var day = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 86400;
            var ledger = File.Exists(_ledgerPath) ? JsonSerializer.Deserialize<Ledger>(File.ReadAllText(_ledgerPath)) : new Ledger { Day = day };
            if (ledger == null || ledger.Version != 1 || ledger.Reservations.Count > 100_000) throw new InvalidOperationException("ai-usage-storage-unavailable");
            if (ledger.Day != day) ledger = new Ledger { Day = day };
            var scope = members.ToHashSet(StringComparer.Ordinal);
            if (limit.HasValue && ledger.Reservations.Count(entry => entry.Any(scope.Contains)) >= limit.Value)
                throw new InvalidOperationException("ai-daily-limit");
            if (ledger.Reservations.Count >= 100_000) throw new InvalidOperationException("ai-usage-storage-unavailable");
            ledger.Reservations.Add(members.Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray());
            var bytes = JsonSerializer.SerializeToUtf8Bytes(ledger);
            if (bytes.Length > 8_000_000) throw new InvalidOperationException("ai-usage-storage-unavailable");
            var temp = _ledgerPath + ".tmp";
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, _ledgerPath, true);
        }
        catch (InvalidOperationException) { throw; }
        catch { throw new InvalidOperationException("ai-usage-storage-unavailable"); }
    }
}
