using System.Text.Json.Nodes;
using WindowsBlocker.Bridge;
using WindowsBlocker.Rules;
using WindowsBlocker.WebUI;

var root = Path.Combine(Path.GetTempPath(), "vault-rule-ai-contract-" + Guid.NewGuid().ToString("N"));
Storage.RootDirectory = root;
try
{
    var transcripts = new RuleAIConversations();
    transcripts.Append(["mac:a"], "topic", "chatgpt:first", "Mac question", "Mac answer");
    transcripts.Append(["browser:b"], "topic", "api:second", "Browser question", "Browser answer");
    var linked = new[] { "browser:b", "mac:a" };
    var prepared = transcripts.Prepare(linked, "topic");
    Check(prepared.PreservedNames.Length == 1, "link keeps the conflicting transcript under a distinct name");
    Check(new RuleAIConversations().Prepare(linked, "topic").PreservedNames.SequenceEqual(prepared.PreservedNames), "preserved names remain discoverable after restart");
    Check(transcripts.Prepare(["mac:a"], prepared.PreservedNames[0]).History.Contains("question"), "preserved name is usable after unlink");
    transcripts.Append(linked, "topic", "api:second", "Shared question", "Shared answer");
    foreach (var member in linked)
        Check(transcripts.Prepare([member], "topic").History.Contains("Shared answer"), "each member retains shared history after unlink");
    var resumed = new RuleAIConversations().Prepare(["mac:a"], "topic");
    Check(resumed.History.Contains("Shared answer"), "conversation survives store restart");
    transcripts.Append(["mac:a"], "unopened", "chatgpt:first", "Prior-only question", "Prior-only answer");
    transcripts.CopyAllBeforeUnlink(linked);
    Check(new RuleAIConversations().Prepare(["browser:b"], "unopened").History.Contains("Prior-only answer"), "unlink copies even conversations never opened during the link");

    var hub = new ConnectionHub(linked);
    var worker = new ClassifierWorkerClient();
    var service = new RuleAIService(hub, worker);
    var first = service.Request("windowsapp", "g", "generate", "{\"model\":\"chatgpt:first\",\"conversation\":\"sequential\",\"prompt\":\"First\"}");
    var second = service.Request("windowsapp", "g", "generate", "{\"model\":\"api:second\",\"conversation\":\"sequential\",\"prompt\":\"Second\"}");
    var replies = await Task.WhenAll(first, second);
    Check(replies.All(reply => reply["ok"]?.GetValue<bool>() == true), "same-name calls succeed");
    Check(worker.Prompts[1].Contains("First") && worker.Prompts[1].Contains("reply 1"), "second call sees the first turn across a model switch");
    Check(worker.MaxActive == 1, "same-name calls serialize");

    worker.ResetConcurrency();
    var parallel = await Task.WhenAll(
        service.Request("windowsapp", "g", "generate", "{\"model\":\"chatgpt:first\",\"conversation\":\"left\",\"prompt\":\"Left\"}"),
        service.Request("windowsapp", "g", "generate", "{\"model\":\"api:second\",\"conversation\":\"right\",\"prompt\":\"Right\"}"));
    Check(parallel.All(reply => reply["ok"]?.GetValue<bool>() == true) && worker.MaxActive >= 2, "different names run concurrently");
    var noModel = await service.Request("windowsapp", "g", "generate", "{\"prompt\":\"Missing model\"}");
    Check(noModel["error"]?.GetValue<string>() == "ai-invalid-input", "model is required per call");
    Console.WriteLine("PASS Windows rule AI persistence, link collision, model switching, ordered and parallel calls");
}
finally { if (Directory.Exists(root)) Directory.Delete(root, true); }

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

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
