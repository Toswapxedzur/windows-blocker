using System.Buffers.Binary;
using System.Text.Json.Nodes;
using WindowsBlocker.Bridge;

if ((Path.GetFileNameWithoutExtension(Environment.ProcessPath) ?? "").EndsWith("-development", StringComparison.OrdinalIgnoreCase)) Environment.SetEnvironmentVariable("VAULT_ENVIRONMENT", "development");
var development = WindowsBlocker.WebUI.Storage.Development;
var allowedIds = development ? new[] { "fjichnkbaoilbfbjcjkggllmbicmeegk" } : new[] { "mcbmcmephdaapjepopobikobjmfdeamm" };
var origin = args.FirstOrDefault() ?? "";
if (!Uri.TryCreate(origin, UriKind.Absolute, out var caller) || caller.Scheme != "chrome-extension" || !allowedIds.Contains(caller.Host)) return;
using var input = Console.OpenStandardInput(); using var output = Console.OpenStandardOutput();
var lengthBytes = new byte[4];
while (true)
{
    try { input.ReadExactly(lengthBytes); } catch (EndOfStreamException) { break; }
    var size = BinaryPrimitives.ReadUInt32LittleEndian(lengthBytes);
    if (size is 0 or > 1_048_576) break;
    var body = new byte[(int)size]; try { input.ReadExactly(body); } catch (EndOfStreamException) { break; }
    JsonObject reply;
    try
    {
        var request = JsonNode.Parse(body) as JsonObject ?? throw new InvalidDataException();
        var program = request["program"]?.GetValue<string>() ?? "";
        if (request["kind"]?.GetValue<string>() != "local-hub-challenge" || request["v"]?.GetValue<int>() != 4 || program is not ("chrome" or "edge")) throw new InvalidDataException();
        var challenge = request["challenge"]?.GetValue<string>() ?? "";
        reply = new() { ["ok"] = true, ["proof"] = LocalHubAuthentication.Proof(program, challenge, LocalHubAuthentication.Secret()) };
    }
    catch { reply = new() { ["ok"] = false, ["error"] = "authentication-failed" }; }
    var bytes = System.Text.Encoding.UTF8.GetBytes(reply.ToJsonString()); BinaryPrimitives.WriteUInt32LittleEndian(lengthBytes, (uint)bytes.Length); output.Write(lengthBytes); output.Write(bytes); output.Flush();
}
