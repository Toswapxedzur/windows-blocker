using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using WindowsBlocker.WebUI;

namespace WindowsBlocker.Rules;

public static class LocalFolderGrant
{
    public const int MaximumBytes = 1_048_576;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase) { ".txt", ".csv", ".json" };
    private static readonly HashSet<string> Errors = new(StringComparer.Ordinal) { "local-folder-not-available", "invalid-path", "unsupported-file-type", "unsupported-action", "file-too-large", "local-file-error" };
    private static string GrantPath => Path.Combine(Storage.RootDirectory, "local-folder-grant.txt");
    public static string? Folder { get { try { var p = Path.GetFullPath(File.ReadAllText(GrantPath)); return Directory.Exists(p) && !LocalFolderPathPolicy.RedirectsPath(p) ? p : null; } catch { return null; } } }
    public static void Choose(System.Windows.Window owner)
    {
        var picker = new Microsoft.Win32.OpenFolderDialog { Title = "Choose the folder custom rules may use", Multiselect = false };
        if (picker.ShowDialog(owner) == true) File.WriteAllText(GrantPath, LocalFolderPathPolicy.CanonicalRoot(picker.FolderName), Utf8);
    }
    public static void Revoke() { if (File.Exists(GrantPath)) File.Delete(GrantPath); }
    public static JsonObject Status() => new() { ["connected"] = Folder != null, ["name"] = Folder == null ? "" : Path.GetFileName(Folder) };
    public static JsonObject Handle(RuleAction action)
    {
        var result = new JsonObject { ["requestId"] = action.RequestId, ["op"] = action.Op, ["path"] = action.Path, ["ok"] = false, ["text"] = null, ["entries"] = null, ["exists"] = null, ["error"] = "" };
        try
        {
            var root = Folder ?? throw new IOException("local-folder-not-available");
            var relative = Normalize(action.Path ?? "", action.Op == "list");
            var full = Resolve(root, relative);
            if (action.Op != "list" && !Extensions.Contains(Path.GetExtension(full))) throw new IOException("unsupported-file-type");
            switch (action.Op)
            {
                case "read": case "readJson":
                    var bytes = ReadLimited(full);
                    result["text"] = Utf8.GetString(bytes);
                    if (action.Op == "readJson") using (JsonDocument.Parse(bytes)) { }
                    break;
                case "write": case "writeJson": case "append":
                    var data = Utf8.GetBytes(action.Payload ?? "");
                    if (action.Op == "writeJson") using (JsonDocument.Parse(data)) { }
                    EnforceSize(data.Length);
                    if (action.Op == "append" && File.Exists(full))
                    {
                        var before = ReadLimited(full); EnforceSize((long)before.Length + data.Length);
                        data = before.Concat(data).ToArray();
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                    Resolve(root, relative); // Newly created parents must still stay inside the chosen folder.
                    var temporary = Path.Combine(Path.GetDirectoryName(full)!, ".vault-write-" + Guid.NewGuid().ToString("N"));
                    try { File.WriteAllBytes(temporary, data); Resolve(root, relative); File.Move(temporary, full, true); }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                    break;
                case "exists": result["exists"] = File.Exists(full) || Directory.Exists(full); break;
                case "list":
                    result["entries"] = new JsonArray(Directory.EnumerateFileSystemEntries(full)
                        .Where(f => !Path.GetFileName(f).StartsWith('.') && !File.GetAttributes(f).HasFlag(FileAttributes.Hidden) && !LocalFolderPathPolicy.RedirectsPath(f))
                        .Where(f => Directory.Exists(f) || Extensions.Contains(Path.GetExtension(f)))
                        .OrderBy(f => Directory.Exists(f) ? 0 : 1).ThenBy(Path.GetFileName, StringComparer.Ordinal)
                        .Select(f => { var entry = new JsonObject { ["name"] = Path.GetFileName(f), ["path"] = Path.GetRelativePath(root, f).Replace('\\', '/'), ["kind"] = Directory.Exists(f) ? "directory" : "file" }; if (!Directory.Exists(f)) entry["extension"] = Path.GetExtension(f).ToLowerInvariant(); return (JsonNode)entry; }).ToArray());
                    break;
                default: throw new IOException("unsupported-action");
            }
            result["ok"] = true;
        }
        catch (Exception ex) { result["error"] = ex is JsonException ? "invalid-json" : ex is DecoderFallbackException ? "invalid-text" : ex is FileNotFoundException or DirectoryNotFoundException ? "not-found" : ex is IOException && Errors.Contains(ex.Message) ? ex.Message : "local-file-error"; }
        return result;
    }

    private static string Normalize(string path, bool directory)
    {
        var raw = Regex.Replace(path.Trim().Replace('\\', '/'), "/+", "/").TrimEnd('/');
        if (raw.Length == 0 && directory) return "";
        if (raw.Length == 0 || raw.StartsWith('/')) throw new IOException("invalid-path");
        foreach (var part in raw.Split('/'))
        {
            if (part.StartsWith('.') || part.EndsWith('.') || part.EndsWith(' ') || !Regex.IsMatch(part, "^[A-Za-z0-9 _.,@()-]+$")) throw new IOException("invalid-path");
            // Windows resolves device names even when they have an ordinary extension.
            if (Regex.IsMatch(part.Split('.')[0], "^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) throw new IOException("invalid-path");
        }
        return raw;
    }
    private static string Resolve(string root, string relative)
    {
        var full = Path.GetFullPath(Path.Combine(root, relative));
        if (!full.Equals(root, StringComparison.OrdinalIgnoreCase) && !full.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) throw new IOException("invalid-path");
        for (var walk = full; walk != null && walk.Length >= root.Length; walk = Path.GetDirectoryName(walk))
        {
            if (Path.Exists(walk) && LocalFolderPathPolicy.RedirectsPath(walk)) throw new IOException("invalid-path");
            if (walk.Equals(root, StringComparison.OrdinalIgnoreCase)) break;
        }
        return full;
    }
    private static byte[] ReadLimited(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        EnforceSize(stream.Length);
        using var data = new MemoryStream(); var buffer = new byte[8192]; int count;
        while ((count = stream.Read(buffer)) != 0) { EnforceSize(data.Length + count); data.Write(buffer, 0, count); }
        return data.ToArray();
    }
    private static void EnforceSize(long bytes) { if (bytes > MaximumBytes) throw new IOException("file-too-large"); }
}
