using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using WindowsBlocker.Bridge;
using WindowsBlocker.WebUI;

namespace VaultNativeHost;

// Private stdio bridge for clients without HTTP transport. End users need
// neither Node nor a fetched npm package; credentials stay out of config args.
internal static class NativeMcpProxy
{
    private const int RequestLimit = 1_048_576;
    private const int ResponseLimit = 8 * 1_048_576;

    public static Task RunAsync() => RunAsync(Console.OpenStandardInput(), Console.OpenStandardOutput(), new Uri($"http://127.0.0.1:{Storage.McpPort}/mcp"), LocalHubAuthentication.McpBearerToken);

    internal static async Task RunAsync(Stream inputStream, Stream outputStream, Uri endpoint, Func<string> tokenProvider)
    {
        using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(125) };
        using var input = new StreamReader(inputStream, new UTF8Encoding(false, true), leaveOpen: true);
        using var output = new StreamWriter(outputStream, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
        while (await ReadLineAsync(input) is { } line)
        {
            JsonNode? id = null;
            bool notification = false;
            try
            {
                if (Encoding.UTF8.GetByteCount(line) > RequestLimit) throw new InvalidDataException("request-too-large");
                var message = JsonNode.Parse(line) as JsonObject ?? throw new InvalidDataException("invalid-request");
                var hasId = message.ContainsKey("id"); id = message["id"]?.DeepClone();
                if (message["jsonrpc"]?.GetValue<string>() != "2.0" || message["method"]?.GetValueKind() != System.Text.Json.JsonValueKind.String) throw new InvalidDataException("invalid-request");
                notification = !hasId;
                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenProvider());
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Content = new StringContent(line, Encoding.UTF8, "application/json");
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();
                if (!hasId) continue;
                await using var body = await response.Content.ReadAsStreamAsync();
                using var buffer = new MemoryStream();
                var chunk = new byte[8192];
                int count;
                while ((count = await body.ReadAsync(chunk)) != 0)
                {
                    if (buffer.Length + count > ResponseLimit) throw new InvalidDataException("response-too-large");
                    buffer.Write(chunk, 0, count);
                }
                var reply = JsonNode.Parse(buffer.ToArray()) as JsonObject ?? throw new InvalidDataException("invalid-response");
                if (reply["jsonrpc"]?.GetValue<string>() != "2.0" || !JsonNode.DeepEquals(id, reply["id"])) throw new InvalidDataException("unmatched-response");
                await output.WriteLineAsync(reply.ToJsonString());
            }
            catch (Exception error)
            {
                // Notifications have no response. Invalid JSON still receives
                // a protocol error with a null ID; stdout stays JSON-only.
                if (!notification)
                    await output.WriteLineAsync(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JsonObject { ["code"] = error is System.Text.Json.JsonException ? -32700 : -32000, ["message"] = error is HttpRequestException ? "Windows Vault is unavailable." : error.Message } }.ToJsonString());
            }
        }
    }

    private static async Task<string?> ReadLineAsync(StreamReader input)
    {
        var line = new StringBuilder();
        var character = new char[1];
        while (await input.ReadAsync(character.AsMemory()) != 0)
        {
            if (character[0] == '\n') return line.ToString().TrimEnd('\r');
            if (line.Length == RequestLimit) throw new InvalidDataException("request-too-large");
            line.Append(character[0]);
        }
        return line.Length == 0 ? null : line.ToString().TrimEnd('\r');
    }
}
