using System.IO;

namespace WindowsBlocker.WebUI;

public static class Storage
{
    public static bool Development => Environment.GetEnvironmentVariable("VAULT_ENVIRONMENT") == "development";
    public static int HubPort => Development ? 18787 : 8787;
    public static int McpPort => Development ? 18788 : 8788;
    public static string RootDirectory
    {
        get
        {
            var custom = Development ? Environment.GetEnvironmentVariable("VAULT_STORAGE_ROOT") : null;
            var dir = !string.IsNullOrWhiteSpace(custom) ? custom : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AdamanciaVault", Development ? "Development" : "Production");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
    public static string WebStorePath => Path.Combine(RootDirectory, "web-store.json");
    public static string ClustersPath => Path.Combine(RootDirectory, "clusters-v4.json");
    public static string HubSecretPath => Path.Combine(RootDirectory, "local-hub-secret.bin");
}
