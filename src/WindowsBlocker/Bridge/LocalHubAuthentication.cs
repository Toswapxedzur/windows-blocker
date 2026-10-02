using System.Security.AccessControl;
using System.Security.Principal;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using WindowsBlocker.WebUI;

namespace WindowsBlocker.Bridge;

public static class LocalHubAuthentication
{
    public const int Version = 4;
    public static readonly HashSet<string> BrowserPrograms = new() { "chrome", "edge", "safari" };
    public static readonly HashSet<string> Programs = new() { "chrome", "edge", "safari", "windowsapp", "macapp", "classifier" };
    public static string Challenge() => Base64Url(RandomNumberGenerator.GetBytes(32));
    public static bool ValidChallenge(string challenge) => Regex.IsMatch(challenge, "^[A-Za-z0-9_-]{43}$");
    public static string Proof(string program, string challenge, byte[] secret)
    {
        if (!Programs.Contains(program) || !ValidChallenge(challenge) || secret.Length != 32) throw new ArgumentException("Invalid challenge");
        return Base64Url(HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes($"vault-local-hub-v4\nprogram={program}\nchallenge={challenge}")));
    }
    public static bool Verify(string program, string challenge, string proof, byte[] secret)
    {
        try { return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Proof(program, challenge, secret)), Encoding.ASCII.GetBytes(proof)); }
        catch { return false; }
    }
    public static string McpBearerToken() => Base64Url(HMACSHA256.HashData(Secret(), Encoding.UTF8.GetBytes("vault-mcp-bearer-v1")));
    public static byte[] Secret()
    {
        using var mutex = new Mutex(false, Storage.Development ? "Local\\AdamanciaVaultAuthDevelopment" : "Local\\AdamanciaVaultAuthProduction");
        mutex.WaitOne();
        try
        {
            var path = Storage.HubSecretPath;
            if (!File.Exists(path))
            {
                var sid = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("No Windows user");
                var acl = new FileSecurity();
                acl.SetAccessRuleProtection(true, false);
                acl.SetOwner(sid);
                acl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl, AccessControlType.Allow));
                using var file = new FileInfo(path).Create(FileMode.CreateNew, FileSystemRights.FullControl, FileShare.None, 4096, FileOptions.WriteThrough, acl);
                file.Write(RandomNumberGenerator.GetBytes(32));
                file.Flush(true);
            }
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length != 32) throw new InvalidDataException("Invalid hub secret");
            return bytes;
        }
        finally { mutex.ReleaseMutex(); }
    }
    private static string Base64Url(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
