using System.Text.Json;
using System.Text.Json.Nodes;

namespace WindowsBlocker.WebUI;

public static class NativeEditorContract
{
    public const string Host = "appassets.windowsblocker";
    public static bool TrustedUri(string value) => Uri.TryCreate(value,UriKind.Absolute,out var uri) && uri.Scheme == "https" && uri.Host == Host && uri.Port == 443 && string.IsNullOrEmpty(uri.UserInfo);
    public static bool ExternalLink(string value, bool userInitiated) => userInitiated && Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme is "https" or "http" && uri.Host.Length > 0 && uri.UserInfo.Length == 0 && !TrustedUri(value);
    public static void RequireKeyedPersist(JsonObject patch)
    {
        if (patch.ContainsKey("blockedGroups")) throw new InvalidOperationException("editor-definition-request-required");
    }
    // The canonical shim reads a JSON string at document start.
    public static string SeedScript(string storeJson) => $"window.__cbNativeStoreSeed = {JsonSerializer.Serialize(storeJson)};";
}
