using System.Buffers.Binary;
using System.Text.Json.Nodes;
using WindowsBlocker.Bridge;

if (args.Length == 2 && args[0] == "--mcp-proxy" && args[1] is "development" or "production")
{
    Environment.SetEnvironmentVariable("VAULT_ENVIRONMENT", args[1]);
    await VaultNativeHost.NativeMcpProxy.RunAsync();
    return;
}
var development = VaultNativeHost.NativeHostProtocol.DevelopmentAlias(Environment.ProcessPath);
Environment.SetEnvironmentVariable("VAULT_ENVIRONMENT", development ? "development" : "production");
var origin = args.FirstOrDefault() ?? "";
if (!VaultNativeHost.NativeHostProtocol.AllowedOrigin(origin, development)) return;
var browserProgram = VaultNativeHost.NativeBrowserParent.CurrentProgram(development);
if (browserProgram == null) return;
using var input = Console.OpenStandardInput(); using var output = Console.OpenStandardOutput();
var lengthBytes = new byte[4];
while (true)
{
    try { input.ReadExactly(lengthBytes); } catch (EndOfStreamException) { break; }
    var size = BinaryPrimitives.ReadUInt32LittleEndian(lengthBytes);
    if (size == 0 || size > VaultNativeHost.NativeHostProtocol.MaximumFrameLength) break;
    var body = new byte[(int)size]; try { input.ReadExactly(body); } catch (EndOfStreamException) { break; }
    JsonObject reply;
    try
    {
        var request = JsonNode.Parse(body) as JsonObject ?? throw new InvalidDataException();
        var program = request["program"]?.GetValue<string>() ?? "";
        if (request["kind"]?.GetValue<string>() != "local-hub-challenge" || request["v"]?.GetValue<int>() != 4 || program != browserProgram) throw new InvalidDataException();
        var challenge = request["challenge"]?.GetValue<string>() ?? "";
        reply = new() { ["ok"] = true, ["proof"] = LocalHubAuthentication.Proof(program, challenge, LocalHubAuthentication.Secret()) };
    }
    catch { reply = new() { ["ok"] = false, ["error"] = "authentication-failed" }; }
    var bytes = System.Text.Encoding.UTF8.GetBytes(reply.ToJsonString()); BinaryPrimitives.WriteUInt32LittleEndian(lengthBytes, (uint)bytes.Length); output.Write(lengthBytes); output.Write(bytes); output.Flush();
}
