using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WindowsBlocker.WebUI;

namespace WindowsBlocker.Rules;

// One local transcript per name, shared across all rules and groups.
public sealed class RuleAIConversations
{
    public sealed record Prepared(string History, string[] PreservedNames);
    public sealed record Message(string Id, string Role, string Text, string Model, long At);
    public sealed record Document(int Version, string Name, List<Message> Messages, List<string>? PreservedNames = null, long? LastUsedAt = null);
    private static readonly object Gate = new();
    private static long LastTimestamp;
    private static long NextTimestamp() => LastTimestamp = Math.Max(LastTimestamp + 1, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    private readonly string _root = Path.Combine(Storage.RootDirectory, "rule-ai-conversations");
    private static string Digest(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private string FileFor(string member, string name) => Path.Combine(_root, Digest(name) + ".json");
    private Document? Read(string member, string name)
    {
        var file = FileFor(member, name);
        if (!File.Exists(file)) return null;
        var document = JsonSerializer.Deserialize<Document>(File.ReadAllBytes(file));
        if (document?.Version != 1 || document.Name != name || document.Messages == null) throw new IOException("Invalid conversation file");
        return document;
    }
    private void Write(Document document, string[] members)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document);
        var file = FileFor("", document.Name);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllBytes(temp, bytes);
        File.Move(temp, file, true);
    }
    private static bool Prefix(IReadOnlyList<Message> left, IReadOnlyList<Message> right) =>
        left.Count <= right.Count && left.Select((message, index) => message == right[index]).All(value => value);
    private string SeparateName(string name, string member, string[] members)
    {
        var baseName = new string(name.Take(88).ToArray()) + "~" + Digest(member)[..10];
        var candidate = baseName;
        for (var number = 2; members.Any(item => Read(item, candidate) != null); number++) candidate = baseName + "-" + number;
        return candidate;
    }
    private (Document Document, string[] PreservedNames) Reconcile(string[] members, string name)
    {
        Migrate();
        var document = Read("", name) ?? new Document(1, name, new());
        return (document, document.PreservedNames?.ToArray() ?? []);
    }
    private int LimitMB()
    {
        var settings = Path.Combine(_root, "settings.json");
        if (!File.Exists(settings)) return 1024;
        var value = JsonSerializer.Deserialize<int>(File.ReadAllBytes(settings));
        if (value is < 1 or > 1_000_000) throw new IOException("Invalid conversation limit");
        return value;
    }
    private void Trim(string? protecting = null)
    {
        if (!Directory.Exists(_root)) return;
        var limit = (long)LimitMB() * 1_048_576;
        var entries = Directory.EnumerateFiles(_root, "*.json", SearchOption.TopDirectoryOnly)
            .Where(file => Path.GetFileName(file) != "settings.json")
            .Select(file => {
                var info = new FileInfo(file);
                return (File: file, Used: info.LastWriteTimeUtc, Size: info.Length);
            }).ToList();
        var used = entries.Sum(entry => entry.Size);
        if (entries.Any(entry => entry.File == FileFor("", protecting ?? "") && entry.Size > limit)) throw new IOException("Conversation exceeds limit");
        foreach (var entry in entries.OrderBy(entry => entry.Used).ThenBy(entry => entry.File, StringComparer.Ordinal))
        {
            if (used <= limit) break;
            if (entry.File == FileFor("", protecting ?? "")) continue;
            File.Delete(entry.File);
            used -= entry.Size;
        }
        if (used > limit) throw new IOException("Conversation limit unavailable");
    }
    private void Migrate()
    {
        if (!Directory.Exists(_root)) return;
        var folders = Directory.EnumerateDirectories(_root).Order(StringComparer.Ordinal).ToList();
        if (folders.Count == 0) return;
        foreach (var folder in folders)
        {
            foreach (var old in Directory.EnumerateFiles(folder, "*.json"))
            {
                var oldDoc = JsonSerializer.Deserialize<Document>(File.ReadAllBytes(old));
                if (oldDoc?.Version != 1 || Path.GetFileName(old) != Digest(oldDoc.Name) + ".json") throw new IOException("Invalid legacy conversation");
                var imported = oldDoc;
                var primary = Read("", oldDoc.Name);
                if (primary != null && !primary.Messages.SequenceEqual(oldDoc.Messages))
                {
                    if (Prefix(primary.Messages, oldDoc.Messages))
                        Write(imported with { PreservedNames = primary.PreservedNames }, []);
                    else if (!Prefix(oldDoc.Messages, primary.Messages))
                    {
                        var baseName = new string(oldDoc.Name.Take(88).ToArray()) + "~" + Path.GetFileName(folder)[..10];
                        var candidate = baseName;
                        for (var number = 2; Read("", candidate) != null; number++) candidate = baseName + "-" + number;
                        imported = imported with { Name = candidate };
                        var names = (primary.PreservedNames ?? []).Append(candidate).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
                        Write(primary with { PreservedNames = names }, []);
                    }
                }
                if (Read("", imported.Name) == null) Write(imported, []);
                File.Delete(old);
            }
            if (!Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder);
        }
        Trim();
    }
    private static string RecentText(IReadOnlyList<Message> messages)
    {
        var pieces = new List<string>(); var bytes = 0;
        for (var index = messages.Count - 1; index >= 0; index--)
        {
            var message = messages[index];
            var piece = (message.Role == "assistant" ? "Assistant: " : "User: ") + message.Text + "\n";
            var size = Encoding.UTF8.GetByteCount(piece);
            if (bytes + size > 16_000)
            {
                if (pieces.Count == 0)
                {
                    var tail = "";
                    foreach (var character in piece.Reverse())
                    {
                        var next = character + tail;
                        if (Encoding.UTF8.GetByteCount(next) > 16_000) break;
                        tail = next;
                    }
                    pieces.Add(tail);
                }
                break;
            }
            pieces.Add(piece); bytes += size;
        }
        pieces.Reverse(); return string.Concat(pieces);
    }
    public Prepared Prepare(string[] members, string name)
    {
        lock (Gate) {
            var (document, preservedNames) = Reconcile(members, name);
            if (document.Messages.Count > 0) Write(document with { LastUsedAt = NextTimestamp() }, []);
            return new(RecentText(document.Messages), preservedNames);
        }
    }
    public void Append(string[] members, string name, string model, string user, string assistant)
    {
        lock (Gate) {
            var document = Reconcile(members, name).Document;
            var at = NextTimestamp();
            document.Messages.Add(new(Guid.NewGuid().ToString("N"), "user", user, model, at));
            document.Messages.Add(new(Guid.NewGuid().ToString("N"), "assistant", assistant, model, at));
            document = document with { LastUsedAt = at };
            if (JsonSerializer.SerializeToUtf8Bytes(document).Length > (long)LimitMB() * 1_048_576) throw new IOException("Conversation exceeds limit");
            Write(document, []);
            Trim(name);
        }
    }
    public void Delete(string name)
    {
        lock (Gate) { Migrate(); File.Delete(FileFor("", name)); }
    }
    public int Setting(string action, int? mb)
    {
        lock (Gate) {
            Migrate();
            if (action == "set")
            {
                if (mb is null or < 1 or > 1_000_000) throw new IOException("Invalid conversation limit");
                Directory.CreateDirectory(_root);
                var target = Path.Combine(_root, "settings.json");
                var temp = target + ".tmp";
                File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(mb.Value));
                File.Move(temp, target, true);
                Trim();
            }
            return LimitMB();
        }
    }
    public void CopyAllBeforeUnlink(string[] members)
    {
        lock (Gate) Migrate();
    }
}
