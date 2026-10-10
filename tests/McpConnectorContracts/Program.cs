using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using VaultNativeHost;
using WindowsBlocker.Bridge;

var count = 0;
void Check(bool ok, string what) { if (!ok) throw new Exception(what); count++; Console.WriteLine("PASS " + what); }
void Refuses(Action action, string what) { try { action(); } catch (InvalidDataException) { Check(true, what); return; } throw new Exception(what); }
var proxy = @"C:\Program Files\Windows Vault\NativeHost\VaultNativeHost.exe";
var entry = McpConnectorRegistry.StdioEntry(proxy, "development");
var existing = "{\"mcpServers\":{\"other\":{\"command\":\"keep-me\"}},\"setting\":\"keep\"}";
var updated = McpConnectorRegistry.ApplyJson(existing, "mcpServers", "vault-dev", entry, true);
var document = JsonNode.Parse(updated)!;
Check(document["setting"]!.GetValue<string>() == "keep" && document["mcpServers"]!["other"]!["command"]!.GetValue<string>() == "keep-me", "JSON connect preserves client settings and other servers");
Check(document["mcpServers"]!["vault-dev"]!["command"]!.GetValue<string>() == proxy && document["mcpServers"]!["vault-dev"]!["args"]![0]!.GetValue<string>() == "--mcp-proxy", "Bundled stdio helper accepts paths with spaces and needs no Node");
Check(McpConnectorRegistry.ApplyJson(updated, "mcpServers", "vault-dev", entry, true) == updated, "JSON registration is idempotent");
var disconnected = JsonNode.Parse(McpConnectorRegistry.ApplyJson(updated, "mcpServers", "vault-dev", entry, false))!;
Check(disconnected["mcpServers"]!["vault-dev"] == null && disconnected["mcpServers"]!["other"] != null, "Disconnect removes only Vault entry");
Refuses(() => McpConnectorRegistry.ApplyJson("{ invalid", "mcpServers", "vault", entry, true), "Malformed JSON is refused");
Refuses(() => McpConnectorRegistry.ApplyJson("{\"mcpServers\":42}", "mcpServers", "vault", entry, true), "Malformed server maps are refused");
Check(JsonNode.Parse(McpConnectorRegistry.ApplyJson("{ // comment\n\"keep\":true, }", "servers", "vault", entry, true))!["keep"]!.GetValue<bool>(), "Client JSON comments and trailing commas retain setting values");
var toml = "# user's comment\r\nmodel = \"keep-model\"\r\n[mcp_servers.other]\r\ncommand = \"keep\"\r\n";
var connectedToml = McpConnectorRegistry.ApplyCodexToml(toml, "vault-dev", proxy, "development", true);
Check(connectedToml.StartsWith(toml, StringComparison.Ordinal) && connectedToml.Contains("C:\\\\Program Files"), "TOML preserves original text and escapes Windows paths");
Check(McpConnectorRegistry.ApplyCodexToml(connectedToml, "vault-dev", proxy, "development", true) == connectedToml, "TOML registration is idempotent");
Check(McpConnectorRegistry.ApplyCodexToml(connectedToml, "vault-dev", proxy, "development", false).StartsWith(toml, StringComparison.Ordinal), "TOML disconnect preserves unrelated text");
Refuses(() => McpConnectorRegistry.ApplyCodexToml("[mcp_servers.\"vault-dev\"]\ncommand=\"other\"", "vault-dev", proxy, "development", true), "Existing hand-managed Vault TOML table is refused");
Refuses(() => McpConnectorRegistry.ApplyCodexToml("# >>> vault-mcp (managed by Windows Vault) >>>", "vault-dev", proxy, "development", true), "Incomplete managed TOML block is refused");
var catalog = McpConnectorRegistry.Catalog(@"C:\Users\test", @"C:\Users\test\AppData\Roaming");
Check(catalog.Length == 9 && catalog.Select(c => c.Id).Distinct().Count() == 9, "Windows exposes the nine native Mac client connectors");
Check(catalog.Single(c => c.Id == "zed").File.EndsWith(@"Zed\settings.json") && catalog.Single(c => c.Id == "vscode").ServerKey == "servers", "Windows client paths and schema roots match their configuration formats");

var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [] });
builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
await using var server = builder.Build();
var notificationCount = 0;
server.MapPost("/mcp", async context => {
    if (context.Request.Headers.Authorization != "Bearer fixture-token") { context.Response.StatusCode = 401; return; }
    var request = JsonNode.Parse(await new StreamReader(context.Request.Body).ReadToEndAsync())!;
    if (request["id"] == null) { notificationCount++; context.Response.StatusCode = 202; return; }
    var result = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = request["method"]!.GetValue<string>() == "wrong-id" ? JsonValue.Create(999) : request["id"]!.DeepClone(), ["result"] = new JsonObject { ["name"] = "Vault 中文" } };
    context.Response.ContentType = "application/json"; await context.Response.WriteAsync(result.ToJsonString());
});
await server.StartAsync();
var endpoint = new Uri(server.Urls.Single() + "/mcp");
using var input = new MemoryStream(Encoding.UTF8.GetBytes("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\"}\n{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}\n{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"wrong-id\"}\ninvalid-json\n"));
using var output = new MemoryStream();
await NativeMcpProxy.RunAsync(input, output, endpoint, () => "fixture-token");
var lines = Encoding.UTF8.GetString(output.ToArray()).Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => JsonNode.Parse(l)!).ToArray();
Check(lines.Length == 3 && notificationCount == 1, "Stdio bridge emits no reply for notifications and stdout is JSON only");
Check(lines[0]["id"]!.GetValue<int>() == 1 && lines[0]["result"]!["name"]!.GetValue<string>() == "Vault 中文", "Authenticated stdio bridge preserves ID and Unicode payload");
Check(lines[1]["id"]!.GetValue<int>() == 2 && lines[1]["error"] != null, "Stdio bridge rejects mismatched HTTP reply IDs");
Check(lines[2]["error"]!["code"]!.GetValue<int>() == -32700, "Invalid stdio JSON receives a parse error");
await server.StopAsync();
Check(ClassifierMcpRequest.Create("classifier_state",new JsonObject { ["section"]="assets.classifierTypes" })["section"]!.GetValue<string>()=="assets.classifierTypes", "Classifier state selector reaches the shared worker rather than returning an unrelated overview");
Check(ClassifierMcpRequest.Create("classifier_actions",new())["kind"]!.GetValue<string>()=="actions", "Classifier action catalog requests the shared action descriptors");
var actionArguments=new JsonObject { ["action"]="setClassifierTypePaused",["data"]=new JsonObject { ["typeID"]="Group 中文",["paused"]=true,["position"]=0 } };
var mappedAction=ClassifierMcpRequest.Create("classifier_action",actionArguments);
Check(mappedAction["kind"]!.GetValue<string>()=="action" && mappedAction["action"]!.GetValue<string>()=="setClassifierTypePaused" && mappedAction["data"]!["paused"]!.GetValue<bool>() && mappedAction["data"]!["position"]!.GetValue<int>()==0 && mappedAction["data"]!["typeID"]!.GetValue<string>()=="Group 中文", "Classifier tool dispatch preserves action, UTF-8, booleans and numeric zero for Swift validation");
mappedAction["data"]!["paused"]=false;
Check(actionArguments["data"]!["paused"]!.GetValue<bool>() && ClassifierMcpRequest.Create("classifier_action",new JsonObject{["action"]="state"})["data"] is JsonObject {Count:0}, "Classifier mapping leaves caller arguments intact and supplies omitted action data");
var unknownRefused=false;try {ClassifierMcpRequest.Create("unknown-tool",new());}catch(InvalidOperationException ex){unknownRefused=ex.Message=="unknown-tool";}
Check(unknownRefused,"Unknown Classifier tools cannot silently return a successful state response");
var publicTools = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "mcp-tools.json")))!.AsArray();
var activityTools = publicTools.OfType<JsonObject>().Select(t => t["name"]!.GetValue<string>()).Where(n => n.Contains("activity_group", StringComparison.Ordinal)).ToArray();
Check(activityTools.Length > 0 && activityTools.All(ActivityMcpRequest.Handles), "Every advertised Activity-group tool selects dedicated routing from the shipped catalog");
foreach (var tool in activityTools)
{
    var arguments = new JsonObject { ["name"] = "Group 中文", ["merge"] = false, ["members"] = new JsonArray("app|fixture"), ["move"] = true };
    var request = ActivityMcpRequest.Create(tool, arguments);
    Check(ActivityMcpRequest.Handles(tool) && request["kind"]!.GetValue<string>() == "mcp" && request["tool"]!.GetValue<string>() == tool,
        "Advertised Activity tool selects its dedicated shared-store MCP handler: " + tool);
    Check(request["arguments"]!.ToJsonString() == arguments.ToJsonString(), "Activity arguments reach canonical validation unchanged: " + tool);
    request["arguments"]!["members"]![0] = "app|changed";
    Check(arguments["members"]![0]!.GetValue<string>() == "app|fixture", "Activity dispatch deep-clones caller arguments: " + tool);
}
Check(!ActivityMcpRequest.Handles("classifier_state") && !ActivityMcpRequest.Handles("save_activity_group_extra"), "Activity routing cannot capture other tools or similar prefixes");
var activityUnknownRefused = false;
try { ActivityMcpRequest.Create("unknown-tool", new()); } catch (InvalidOperationException ex) { activityUnknownRefused = ex.Message == "unknown-tool"; }
Check(activityUnknownRefused, "Unknown Activity tools refuse successful fallback responses");
Console.WriteLine($"{count} MCP connector/proxy/Classifier/Activity contracts passed");
