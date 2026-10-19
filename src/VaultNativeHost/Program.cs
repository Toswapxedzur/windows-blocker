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
var browser = VaultNativeHost.NativeBrowserParent.CurrentIdentity(development);
if (browser == null) return;
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
        if (request["kind"]?.GetValue<string>() != "local-hub-challenge" || request["v"]?.GetValue<int>() != 4 || program != browser.Program) throw new InvalidDataException();
        if (request.ContainsKey("browserIdentityVersion") && !LocalHubAuthentication.IsBrowserIdentityVersionOne(request["browserIdentityVersion"])) throw new InvalidDataException();
        var current = VaultNativeHost.NativeBrowserParent.CurrentIdentity(development);
        if (!VaultNativeHost.NativeBrowserParent.SameLaunch(browser, current)) throw new InvalidDataException();
        var challenge = request["challenge"]?.GetValue<string>() ?? "";
        var secret = LocalHubAuthentication.Secret();
        reply = new() { ["ok"] = true, ["proof"] = LocalHubAuthentication.Proof(program, challenge, secret) };
        if (request.ContainsKey("browserIdentityVersion"))
        {
            var value = current!.Value;
            reply["browserIdentityVersion"] = LocalHubAuthentication.BrowserIdentityVersion;
            reply["browserIdentity"] = new JsonObject { ["v"] = LocalHubAuthentication.BrowserIdentityVersion, ["kind"] = LocalHubAuthentication.BrowserIdentityKind,
                ["value"] = value, ["proof"] = LocalHubAuthentication.BrowserIdentityProof(program, challenge, LocalHubAuthentication.BrowserIdentityKind, value, secret) };
        }
    }
    catch { reply = new() { ["ok"] = false, ["error"] = "authentication-failed" }; }
    var bytes = System.Text.Encoding.UTF8.GetBytes(reply.ToJsonString()); BinaryPrimitives.WriteUInt32LittleEndian(lengthBytes, (uint)bytes.Length); output.Write(lengthBytes); output.Write(bytes); output.Flush();
}
