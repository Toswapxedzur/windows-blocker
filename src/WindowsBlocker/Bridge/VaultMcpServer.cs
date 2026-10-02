using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WindowsBlocker.WebUI;

namespace WindowsBlocker.Bridge;

// Same stateless streamable-HTTP MCP envelope as Mac Vault. Authentication
// uses the device hub secret; hostile web origins cannot invoke native tools.
public sealed class VaultMcpServer : IDisposable
{
    private WebApplication? _listener;
    public Func<string,JsonObject,Task<JsonNode?>>? Invoke { get; set; }
    public Func<Task<JsonArray>>? Tools { get; set; }
    public string? LastError { get; private set; }
    public void Start()
    {
        if (_listener != null) return;
        try { _=LocalHubAuthentication.Secret(); _listener=LoopbackServer.Start(Storage.McpPort,Respond); LastError=null; }
        catch (Exception ex) { LastError = ex.Message; }
    }
    private async Task Respond(HttpContext context)
    {
        try
        {
            var request = context.Request; var response = context.Response;
            if (!IPAddress.IsLoopback(context.Connection.RemoteIpAddress!) || request.Path.Value != "/mcp") { response.StatusCode = 404; return; }
            var header = request.Headers["Authorization"].ToString(); var expected = "Bearer " + LocalHubAuthentication.McpBearerToken();
            if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(header),Encoding.UTF8.GetBytes(expected))) { response.StatusCode = 401; return; }
            var origin = request.Headers["Origin"].ToString();
            if (!string.IsNullOrEmpty(origin) && (!Uri.TryCreate(origin,UriKind.Absolute,out var originUri) || originUri.Host is not ("127.0.0.1" or "localhost" or "[::1]"))) { response.StatusCode = 403; return; }
            if (request.Method != "POST") { response.StatusCode = 405; response.Headers["Allow"] = "POST"; return; }
            if ((request.ContentLength ?? 0) is < 0 or > 1_048_576) { response.StatusCode = 413; return; }
            using var input = new MemoryStream(); await request.Body.CopyToAsync(input); if (input.Length > 1_048_576) { response.StatusCode=413; return; }
            var message = JsonNode.Parse(input.ToArray()) as JsonObject ?? throw new InvalidDataException("invalid-request");
            if (!message.ContainsKey("id")) { response.StatusCode = 202; return; }
            var reply = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = message["id"]?.DeepClone() }; var parameters = message["params"] as JsonObject ?? new();
            switch (message["method"]?.GetValue<string>())
            {
                case "initialize": reply["result"] = new JsonObject { ["protocolVersion"] = parameters["protocolVersion"]?.DeepClone() ?? JsonValue.Create("2025-06-18"), ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false } }, ["serverInfo"] = new JsonObject { ["name"] = "Windows Vault",["version"] = "0.0.3" } }; break;
                case "ping": reply["result"] = new JsonObject(); break;
                case "tools/list": reply["result"] = new JsonObject { ["tools"] = Tools != null ? await Tools() : new JsonArray() }; break;
                case "tools/call":
                    var name = parameters["name"]?.GetValue<string>() ?? ""; var args = parameters["arguments"] as JsonObject ?? new(); JsonNode? value; bool error=false;
                    try { value = Invoke != null ? await Invoke(name,args) : throw new InvalidOperationException("tools-unavailable"); }
                    catch (Exception ex) { value = new JsonObject { ["error"] = ex.Message }; error=true; }
                    reply["result"] = new JsonObject { ["isError"] = error, ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = value?.ToJsonString() ?? "null" }) }; break;
                default: reply["error"] = new JsonObject { ["code"] = -32601, ["message"] = "Method not found" }; break;
            }
            var bytes = Encoding.UTF8.GetBytes(reply.ToJsonString()); response.ContentType = "application/json"; response.ContentLength = bytes.Length; await response.Body.WriteAsync(bytes);
        }
        catch { context.Response.StatusCode = 400; }

    }
    public Task StopAsync() { var listener=_listener; _listener=null; return LoopbackServer.StopAsync(listener); }
    public void Dispose() => StopAsync().GetAwaiter().GetResult();
}
