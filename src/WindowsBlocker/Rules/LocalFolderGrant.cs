using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WindowsBlocker.WebUI;

namespace WindowsBlocker.Rules;

public static class LocalFolderGrant
{
    private static string GrantPath => Path.Combine(Storage.RootDirectory, "local-folder-grant.txt");
    public static string? Folder { get { try { var p = File.ReadAllText(GrantPath); return Directory.Exists(p) ? p : null; } catch { return null; } } }
    public static void Choose()
    {
        var picker = new Microsoft.Win32.OpenFolderDialog { Title = "Choose the folder custom rules may use", Multiselect = false };
        if (picker.ShowDialog() == true) File.WriteAllText(GrantPath, picker.FolderName);
    }
    public static void Revoke() { if (File.Exists(GrantPath)) File.Delete(GrantPath); }
    public static JsonObject Status() => new() { ["connected"] = Folder != null, ["name"] = Folder == null ? "" : Path.GetFileName(Folder) };
    public static JsonObject Handle(RuleAction action)
    {
        var result = new JsonObject { ["requestId"] = action.RequestId, ["op"] = action.Op, ["path"] = action.Path, ["ok"] = false };
        try
        {
            var root = Folder ?? throw new IOException("local-folder-not-available");
            var path = action.Path ?? "";
            if (Path.IsPathRooted(path) || path.Split('/', '\\').Any(p => p is ".." or ".") || path.Contains(':')) throw new IOException("invalid-path");
            var full = Path.GetFullPath(Path.Combine(root, path));
            if (full != root && !full.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) throw new IOException("invalid-path");
            var walk = full;
            while (walk.Length >= root.Length)
            {
                if ((Directory.Exists(walk) || File.Exists(walk)) && File.GetAttributes(walk).HasFlag(FileAttributes.ReparsePoint)) throw new IOException("invalid-path");
                if (walk == root) break; walk = Path.GetDirectoryName(walk)!;
            }
            var isList = action.Op == "list";
            if (!isList && !new[] { ".txt", ".csv", ".json" }.Contains(Path.GetExtension(full).ToLowerInvariant())) throw new IOException("unsupported-extension");
            const int maxBytes = 1_048_576;
            switch (action.Op)
            {
                case "read": if (new FileInfo(full).Length > maxBytes) throw new IOException("file-too-large"); result["text"] = File.ReadAllText(full, Encoding.UTF8); break;
                case "write": case "append":
                    var text = action.Payload ?? "";
                    if (Encoding.UTF8.GetByteCount(text) + (action.Op == "append" && File.Exists(full) ? new FileInfo(full).Length : 0) > maxBytes) throw new IOException("file-too-large");
                    if (Path.GetExtension(full).Equals(".json", StringComparison.OrdinalIgnoreCase)) using (JsonDocument.Parse((action.Op == "append" && File.Exists(full) ? File.ReadAllText(full) : "") + text)) { }
                    if (action.Op == "append") File.AppendAllText(full, text, Encoding.UTF8); else File.WriteAllText(full, text, Encoding.UTF8); break;
                case "exists": result["exists"] = File.Exists(full); break;
                case "list": result["entries"] = new JsonArray(Directory.EnumerateFileSystemEntries(full).Where(f => !File.GetAttributes(f).HasFlag(FileAttributes.ReparsePoint) && (Directory.Exists(f) || new[] { ".txt", ".csv", ".json" }.Contains(Path.GetExtension(f).ToLowerInvariant()))).Take(1000).Select(f => (JsonNode)new JsonObject { ["name"] = Path.GetFileName(f), ["path"] = Path.GetRelativePath(root, f).Replace('\\', '/'), ["kind"] = Directory.Exists(f) ? "directory" : "file" }).ToArray()); break;
                default: throw new IOException("unsupported-action");
            }
            result["ok"] = true;
        }
        catch (Exception ex) { result["error"] = ex is JsonException ? "invalid-json" : ex is FileNotFoundException or DirectoryNotFoundException ? "not-found" : ex is IOException ? ex.Message : "local-file-error"; }
        return result;
    }
}
