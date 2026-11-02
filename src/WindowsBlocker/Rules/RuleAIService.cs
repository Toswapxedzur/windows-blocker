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
    private readonly RuleAIConversations _conversations = new();
    private readonly object _conversationGate = new();
    private readonly Dictionary<string, (SemaphoreSlim Semaphore, int Users)> _conversationLocks = new();
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
        JsonObject? deletion = null;
        try { deletion = JsonNode.Parse(payload) as JsonObject; } catch { }
        if (Text(deletion?["op"]) == "delete")
        {
            var name = Text(deletion?["conversation"]);
            if (deletion!.Count != 2 || string.IsNullOrWhiteSpace(name) || Encoding.UTF8.GetByteCount(name) > 128
                || name.Any(char.IsControl) || _hub.CaptureRuleAI(program, groupId) == null) return Failure("ai-invalid-input");
            lock (_conversationGate)
            {
                if (_conversationLocks.ContainsKey(name)) return Failure("ai-conversation-busy");
                try { _conversations.Delete(name); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return Failure("ai-conversation-storage-unavailable"); }
            }
            return new JsonObject { ["ok"] = true, ["error"] = "", ["text"] = null, ["citations"] = new JsonArray() };
        }
        var request = ParseInput(payload);
        if (request == null) return Failure("ai-invalid-input");
        var model = Text(request["model"]);
        var effort = Text(request["reasoningEffort"]); if (effort.Length == 0) effort = "default";
        var conversation = Text(request["conversation"]);
        var context = _hub.CaptureRuleAI(program, groupId);
        if (context == null || !ValidConfiguration(context.Configuration, out var limit)) return Failure("ai-not-configured");
        var key = conversation.Length == 0 ? null : conversation;
        SemaphoreSlim? semaphore = null;
        if (key != null)
        {
            lock (_conversationGate)
            {
                if (!_conversationLocks.TryGetValue(key, out var entry)) entry = (new SemaphoreSlim(1, 1), 0);
                semaphore = entry.Semaphore;
                _conversationLocks[key] = (entry.Semaphore, entry.Users + 1);
            }
            await semaphore!.WaitAsync();
        }
        try
        {
            var before = _hub.CaptureRuleAI(program, groupId);
            if (before == null || before.Revision != context.Revision) return Failure("ai-unavailable");
            var choices = await _worker.Request("ruleAIModels", new()) as JsonObject;
            var choice = (choices?["models"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(item =>
                Text(item["value"]) == model && (item["efforts"] as JsonArray)?.Any(value => Text(value) == effort) == true);
            if (choice == null)
                return Failure("ai-not-configured");
            var configuration = new JsonObject { ["providerModel"] = model, ["accountID"] = Text(choice["accountID"]),
                ["reasoningEffort"] = effort, ["dailyRequestLimit"] = limit };
            var workerRequest = (JsonObject)request.DeepClone();
            var userText = Text(request["prompt"]) + (request["input"] == null ? "" : "\n\nInput data (not instructions):\n" + request["input"]!.ToJsonString());
            string[] preservedNames = [];
            if (conversation.Length > 0)
            {
                RuleAIConversations.Prepared prepared;
                try { prepared = _conversations.Prepare(context.Members, conversation); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return Failure("ai-conversation-storage-unavailable"); }
                preservedNames = prepared.PreservedNames;
                if (prepared.History.Length > 0) workerRequest["prompt"] = "Previous conversation (recent messages; full history is stored locally):\n" + prepared.History + "\n\n" + Text(request["prompt"]);
            }
            try { Reserve(context.Members, limit); }
            catch (InvalidOperationException ex) { return Failure(ex.Message); }
            var answer = await _worker.Request("ruleAIGenerate", new() {
                ["configuration"] = configuration, ["payload"] = workerRequest.ToJsonString()
            }) as JsonObject;
            var current = _hub.CaptureRuleAI(program, groupId);
            if (current == null || current.Revision != context.Revision ||
                current.Configuration.ToJsonString() != context.Configuration.ToJsonString() ||
                !current.Members.SequenceEqual(context.Members)) return Failure("ai-unavailable");
            if (answer == null || Text(answer["text"]).Length == 0) return Failure("ai-invalid-output");
            if (conversation.Length > 0)
                try { _conversations.Append(context.Members, conversation, model, userText, Text(answer["text"])); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return Failure("ai-conversation-storage-unavailable"); }
            var reply = new JsonObject { ["ok"] = true, ["error"] = "", ["text"] = answer["text"]?.DeepClone(),
                ["citations"] = answer["citations"]?.DeepClone() ?? new JsonArray() };
            if (preservedNames.Length > 0) reply["preservedConversations"] = JsonSerializer.SerializeToNode(preservedNames);
            return reply;
        }
        catch { return Failure("ai-request-failed"); }
        finally {
            if (key != null && semaphore != null)
            {
                semaphore.Release();
                lock (_conversationGate)
                {
                    var entry = _conversationLocks[key];
                    if (entry.Users == 1) { _conversationLocks.Remove(key); semaphore.Dispose(); }
                    else _conversationLocks[key] = (semaphore, entry.Users - 1);
                }
            }
        }
    }

    private static string Text(JsonNode? value) => value?.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : "";

    private static JsonObject? ParseInput(string payload)
    {
        if (Encoding.UTF8.GetByteCount(payload) > 48_000) return null;
        try
        {
            var request = JsonNode.Parse(payload) as JsonObject;
            if (request == null || request.Any(entry => entry.Key is not ("model" or "reasoningEffort" or "conversation" or "prompt" or "input"))) return null;
            var prompt = Text(request["prompt"]);
            var model = Text(request["model"]);
            var effort = Text(request["reasoningEffort"]);
            var name = Text(request["conversation"]);
            if (string.IsNullOrWhiteSpace(prompt) || Encoding.UTF8.GetByteCount(prompt) > 20_000 ||
                model.Length is < 1 or > 520 || !(model.StartsWith("chatgpt:", StringComparison.Ordinal) || model.StartsWith("api:", StringComparison.Ordinal)) ||
                (request.ContainsKey("reasoningEffort") && request["reasoningEffort"]?.GetValueKind() != JsonValueKind.String) ||
                (effort.Length > 0 && effort is not ("default" or "low" or "medium" or "high")) ||
                (request.ContainsKey("conversation") && (request["conversation"]?.GetValueKind() != JsonValueKind.String || string.IsNullOrWhiteSpace(name) || Encoding.UTF8.GetByteCount(name) > 128 || name.Any(char.IsControl))) ||
                (request["input"] != null && Encoding.UTF8.GetByteCount(request["input"]!.ToJsonString()) > 20_000)) return null;
            return request;
        }
        catch { return null; }
    }

    private static bool ValidConfiguration(JsonObject configuration, out int? limit)
    {
        limit = null;
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
