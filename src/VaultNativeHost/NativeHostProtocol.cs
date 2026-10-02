namespace VaultNativeHost;

public static class NativeHostProtocol
{
    public const uint MaximumFrameLength = 64 * 1024;
    public static bool DevelopmentAlias(string? path) => string.Equals(Path.GetFileName(path),"vault-local-hub-native-host-development.exe",StringComparison.OrdinalIgnoreCase);
    public static bool AllowedOrigin(string origin, bool development)
    {
        var id = development ? "fjichnkbaoilbfbjcjkggllmbicmeegk" : "mcbmcmephdaapjepopobikobjmfdeamm";
        return origin == $"chrome-extension://{id}/";
    }
}
