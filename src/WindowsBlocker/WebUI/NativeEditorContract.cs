using System.Text.Json;

namespace WindowsBlocker.WebUI;

public static class NativeEditorContract
{
    public const string Host = "appassets.windowsblocker";
    public static bool TrustedUri(string value) => Uri.TryCreate(value,UriKind.Absolute,out var uri) && uri.Scheme == "https" && uri.Host == Host && uri.Port == 443 && string.IsNullOrEmpty(uri.UserInfo);
    // The canonical shim reads a JSON string at document start.
    public static string SeedScript(string storeJson) => $"window.__cbNativeStoreSeed = {JsonSerializer.Serialize(storeJson)};";
}
