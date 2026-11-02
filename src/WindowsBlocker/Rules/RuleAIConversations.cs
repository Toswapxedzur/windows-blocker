using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WindowsBlocker.WebUI;

namespace WindowsBlocker.Rules;

// Each linked member receives a local copy. Unlink therefore retains history.
// Separate conversations are files, so their count is limited by the disk,
// rather than a fixed Vault thread setting.
public sealed class RuleAIConversations
{
    public sealed record Prepared(string History, string[] PreservedNames);
    public sealed record Message(string Id, string Role, string Text, string Model, long At);
    public sealed record Document(int Version, string Name, List<Message> Messages, List<string>? PreservedNames = null);
    private static readonly object Gate = new();
    private readonly string _root = Path.Combine(Storage.RootDirectory, "rule-ai-conversations");
    private static string Digest(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private string FileFor(string member, string name) => Path.Combine(_root, Digest(member), Digest(name) + ".json");
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
        foreach (var member in members)
        {
            var file = FileFor(member, document.Name);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            var temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, file, true);
        }
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
        var found = members.Select(member => (Member: member, Document: Read(member, name)))
            .Where(item => item.Document != null).ToList();
        if (found.Count == 0) return (new(1, name, new()), []);
        var longest = found.MaxBy(item => item.Document!.Messages.Count).Document!;
        var preservedNames = longest.PreservedNames?.ToList() ?? new List<string>();
        foreach (var (member, document) in found)
        {
            if (Prefix(document!.Messages, longest.Messages)) continue;
            var preservedName = SeparateName(name, member, members);
            Write(new(1, preservedName, document.Messages), members);
            preservedNames.Add(preservedName);
        }
        var primary = longest with { PreservedNames = preservedNames };
        Write(primary, members);
        return (primary, preservedNames.ToArray());
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
            return new(RecentText(document.Messages), preservedNames);
        }
    }
    public void Append(string[] members, string name, string model, string user, string assistant)
    {
        lock (Gate) {
            var document = Reconcile(members, name).Document;
            var at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            document.Messages.Add(new(Guid.NewGuid().ToString("N"), "user", user, model, at));
            document.Messages.Add(new(Guid.NewGuid().ToString("N"), "assistant", assistant, model, at));
            Write(document, members);
        }
    }
    public void CopyAllBeforeUnlink(string[] members)
    {
      lock (Gate) {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in members)
        {
            var folder = Path.Combine(_root, Digest(member));
            if (!Directory.Exists(folder)) continue;
            foreach (var file in Directory.EnumerateFiles(folder, "*.json"))
            {
                var document = JsonSerializer.Deserialize<Document>(File.ReadAllBytes(file));
                if (document?.Version != 1 || document.Messages == null || Path.GetFileName(file) != Digest(document.Name) + ".json")
                    throw new IOException("Invalid conversation file");
                names.Add(document.Name);
            }
        }
        foreach (var name in names.Order(StringComparer.Ordinal)) Reconcile(members, name);
      }
    }
}
