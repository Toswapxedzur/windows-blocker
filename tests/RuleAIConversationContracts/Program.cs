using System.Text.Json.Nodes;
using System.Text.Json;
using System.Text;
using System.Security.Cryptography;
using WindowsBlocker.Bridge;
using WindowsBlocker.Rules;
using WindowsBlocker.WebUI;

var root = Path.Combine(Path.GetTempPath(), "vault-rule-ai-contract-" + Guid.NewGuid().ToString("N"));
Storage.RootDirectory = root;
try
{
    var transcripts = new RuleAIConversations();
    transcripts.Append(["mac:a"], "topic", "chatgpt:first", "Mac question", "Mac answer");
    Check(transcripts.Setting("status", null) == 1024, "default total limit is 1 GB");
    Check(transcripts.Prepare(["browser:b"], "topic").History.Contains("Mac answer"), "unlinked groups share a named conversation");
    transcripts.Append(["browser:b"], "topic", "api:second", "Browser question", "Browser answer");
    var resumed = new RuleAIConversations().Prepare(["mac:a"], "topic");
    Check(resumed.History.Contains("Browser answer"), "conversation survives restart and model switch");
    transcripts.CopyAllBeforeUnlink(["mac:a", "browser:b"]);
    Check(transcripts.Prepare(["browser:b"], "topic").History.Contains("Mac answer"), "unlink does not delete global history");
    transcripts.Append(["mac:a"], "older", "chatgpt:first", "Old question", new string('x', 600_000));
    transcripts.Append(["mac:a"], "newer", "chatgpt:first", "New question", new string('y', 600_000));
    Check(transcripts.Setting("set", 1) == 1, "user can configure the total limit");
    Check(transcripts.Prepare(["mac:a"], "older").History.Length == 0, "least recently used conversation is evicted");
    Check(transcripts.Prepare(["browser:b"], "newer").History.Length > 0, "newer conversation remains");
    transcripts.Delete("topic");
    Check(transcripts.Prepare(["browser:b"], "topic").History.Length == 0, "explicit deletion removes a shared conversation");
    var legacyRoot = Path.Combine(root, "rule-ai-conversations");
    var nameHash = Hash("legacy");
    foreach (var (member, text) in new[] { ("mac:a", "Mac-only"), ("browser:b", "Browser-only") })
    {
        var folder = Path.Combine(legacyRoot, Hash(member));
        Directory.CreateDirectory(folder);
        var doc = new RuleAIConversations.Document(1, "legacy", [new(Guid.NewGuid().ToString("N"), "assistant", text, "fixture", 1)]);
        File.WriteAllBytes(Path.Combine(folder, nameHash + ".json"), JsonSerializer.SerializeToUtf8Bytes(doc));
    }
    var imported = transcripts.Prepare(["mac:a"], "legacy");
    Check(imported.PreservedNames.Length == 1 && imported.History.Contains("only"), "legacy same-name collision keeps a distinct alias");
    Check(transcripts.Prepare(["browser:b"], imported.PreservedNames[0]).History.Contains("only"), "legacy alias is globally usable");

    var hub = new ConnectionHub(["browser:b", "mac:a"]);
    var worker = new ClassifierWorkerClient();
    var service = new RuleAIService(hub, worker);
    var first = service.Request("windowsapp", "g", "generate", "{\"model\":\"chatgpt:first\",\"conversation\":\"sequential\",\"prompt\":\"First\"}");
    var second = service.Request("windowsapp", "g", "generate", "{\"model\":\"api:second\",\"conversation\":\"sequential\",\"prompt\":\"Second\"}");
    var replies = await Task.WhenAll(first, second);
    Check(replies.All(reply => reply["ok"]?.GetValue<bool>() == true), "same-name calls succeed");
    Check(worker.Prompts[1].Contains("First") && worker.Prompts[1].Contains("reply 1"), "second call sees the first turn across a model switch");
    Check(worker.MaxActive == 1, "same-name calls serialize");
    var deletedByRule = await service.Request("windowsapp", "g", "generate", "{\"op\":\"delete\",\"conversation\":\"sequential\"}");
    Check(deletedByRule["ok"]?.GetValue<bool>() == true, "rule command deletes a named conversation");
    Check(transcripts.Prepare(["mac:a"], "sequential").History.Length == 0, "rule deletion affects every group");

    worker.ResetConcurrency();
    var parallel = await Task.WhenAll(
        service.Request("windowsapp", "g", "generate", "{\"model\":\"chatgpt:first\",\"conversation\":\"left\",\"prompt\":\"Left\"}"),
        service.Request("windowsapp", "g", "generate", "{\"model\":\"api:second\",\"conversation\":\"right\",\"prompt\":\"Right\"}"));
    Check(parallel.All(reply => reply["ok"]?.GetValue<bool>() == true) && worker.MaxActive >= 2, "different names run concurrently");
    var noModel = await service.Request("windowsapp", "g", "generate", "{\"prompt\":\"Missing model\"}");
    Check(noModel["error"]?.GetValue<string>() == "ai-invalid-input", "model is required per call");
    Console.WriteLine("PASS Windows rule AI global persistence, eviction, deletion, model switching, ordered and parallel calls");
}
finally { if (Directory.Exists(root)) Directory.Delete(root, true); }

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

namespace WindowsBlocker.WebUI
{
    public static class Storage { public static string RootDirectory { get; set; } = ""; }
}
namespace WindowsBlocker.Bridge
{
    public sealed class ConnectionHub(string[] members)
    {
        public sealed record RuleAIContext(JsonObject Configuration, string[] Members, string Revision);
        public RuleAIContext CaptureRuleAI(string program, string groupId) => new(new(), members, "unchanged");
    }
    public sealed class ClassifierWorkerClient
    {
        public List<string> Prompts { get; } = [];
        private int _active;
        public int MaxActive { get; private set; }
        public void ResetConcurrency() { MaxActive = 0; }
        public async Task<JsonNode?> Request(string operation, JsonObject data)
        {
            if (operation == "ruleAIModels") return JsonNode.Parse("""{"models":[{"value":"chatgpt:first","accountID":"fixture","efforts":["default"]},{"value":"api:second","accountID":"fixture","efforts":["default"]}]}""");
            var number = Prompts.Count + 1;
            Prompts.Add(JsonNode.Parse(data["payload"]!.GetValue<string>())!["prompt"]!.GetValue<string>());
            var active = Interlocked.Increment(ref _active);
            MaxActive = Math.Max(MaxActive, active);
            await Task.Delay(60);
            Interlocked.Decrement(ref _active);
            return new JsonObject { ["text"] = "reply " + number, ["citations"] = new JsonArray() };
        }
    }
}
