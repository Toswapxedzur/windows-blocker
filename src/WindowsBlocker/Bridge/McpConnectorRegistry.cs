using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WindowsBlocker.WebUI;

namespace WindowsBlocker.Bridge;

// Match the native Mac catalog and launch policy. Only our server entry is
// changed; invalid existing configuration is refused rather than overwritten.
public static class McpConnectorRegistry
{
    internal sealed record Connector(string Id, string Name, string File, string ServerKey, bool Stdio, string[] DetectionPaths);
    private static readonly object Gate = new();
    private const string MarkerStart = "# >>> vault-mcp (managed by Windows Vault) >>>";
    private const string MarkerEnd = "# <<< vault-mcp (managed by Windows Vault) <<<";
    private static string Key => Storage.Development ? "vault-dev" : "vault";
    private static string Url => $"http://127.0.0.1:{Storage.McpPort}/mcp";
    private static string Proxy => Path.Combine(AppContext.BaseDirectory, "NativeHost", "VaultNativeHost.exe");

    internal static Connector[] Catalog(string home, string roaming)
    {
        string H(params string[] parts) => Path.Combine(new[] { home }.Concat(parts).ToArray());
        string R(params string[] parts) => Path.Combine(new[] { roaming }.Concat(parts).ToArray());
        var cline = R("Code", "User", "globalStorage", "saoudrizwan.claude-dev");
        var clineHome = H(".cline", "data", "settings", "cline_mcp_settings.json");
        var windsurf = H(".codeium", "windsurf", "mcp_config.json");
        if (File.Exists(R("devin", "mcp_config.json"))) windsurf = R("devin", "mcp_config.json");
        return [
            new("claude-code", "Claude Code", H(".claude.json"), "mcpServers", false, [H(".claude.json"), H(".claude")]),
            new("claude-desktop", "Claude Desktop", R("Claude", "claude_desktop_config.json"), "mcpServers", true, [R("Claude")]),
            new("codex", "Codex CLI", H(".codex", "config.toml"), "", true, [H(".codex")]),
            new("cursor", "Cursor", H(".cursor", "mcp.json"), "mcpServers", false, [H(".cursor"), R("Cursor")]),
            new("vscode", "VS Code", R("Code", "User", "mcp.json"), "servers", false, [R("Code")]),
            new("vscode-insiders", "VS Code Insiders", R("Code - Insiders", "User", "mcp.json"), "servers", false, [R("Code - Insiders")]),
            new("windsurf", "Windsurf", windsurf, "mcpServers", true, [H(".codeium", "windsurf"), R("Windsurf"), R("devin")]),
            new("cline", "Cline", File.Exists(clineHome) ? clineHome : Path.Combine(cline, "settings", "cline_mcp_settings.json"), "mcpServers", false, [cline, H(".cline")]),
            new("zed", "Zed", R("Zed", "settings.json"), "context_servers", true, [R("Zed")])
        ];
    }

    private static Connector[] Connectors()
    {
        // Hermetic development fixture support; production always uses the
        // actual current-user client locations.
        var fixture = Storage.Development ? Environment.GetEnvironmentVariable("VAULT_MCP_CLIENT_ROOT") : null;
        if (!string.IsNullOrWhiteSpace(fixture))
        {
            var root = Path.GetFullPath(fixture);
            return Catalog(Path.Combine(root, "home"), Path.Combine(root, "roaming"));
        }
        return Catalog(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
    }
    private static bool Installed(Connector connector) => connector.DetectionPaths.Any(p => File.Exists(p) || Directory.Exists(p));
    private static bool Connected(Connector connector)
    {
        if (!File.Exists(connector.File)) return false;
        try
        {
            var text = File.ReadAllText(connector.File);
            return connector.Id == "codex" ? text.Contains(MarkerStart, StringComparison.Ordinal) && text.Contains($"[mcp_servers.{Key}]", StringComparison.Ordinal) : ParseJson(text)?[connector.ServerKey]?[Key] is JsonObject;
        }
        catch { return false; }
    }
    public static JsonObject Snapshot()
    {
        lock (Gate)
        {
            var items = new JsonArray();
            foreach (var connector in Connectors().Where(Installed))
                items.Add(new JsonObject { ["id"] = connector.Id, ["name"] = connector.Name, ["displayName"] = connector.Name, ["detected"] = true, ["connected"] = Connected(connector), ["configPath"] = connector.File });
            return new() { ["connectors"] = items, ["url"] = Url };
        }
    }

    // Called only after the authenticated MCP listener has started successfully.
    public static void ApplyDefaultConnections()
    {
        foreach (var connector in Connectors().Where(Installed))
        {
            if (Connected(connector)) continue;
            try { Set(connector.Id, true); }
            catch (Exception error) { Trace.WriteLine($"MCP connection {connector.Id}: {error.Message}"); }
        }
    }

    public static void Set(string id, bool connected)
    {
        lock (Gate)
        {
            var connector = Connectors().FirstOrDefault(c => c.Id == id) ?? throw new InvalidOperationException("connector-not-found");
            if (connected && connector.Stdio && !File.Exists(Proxy)) throw new InvalidOperationException("mcp-proxy-unavailable");
            var existing = File.Exists(connector.File) ? File.ReadAllText(connector.File) : "";
            var environment = Storage.Development ? "development" : "production";
            var entry = connector.Stdio ? StdioEntry(Proxy, environment) : HttpEntry(Url, LocalHubAuthentication.McpBearerToken());
            var updated = connector.Id == "codex" ? ApplyCodexToml(existing, Key, Proxy, environment, connected) : ApplyJson(existing, connector.ServerKey, Key, entry, connected);
            Directory.CreateDirectory(Path.GetDirectoryName(connector.File)!);
            var temporary = connector.File + ".vault-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(temporary, updated, new UTF8Encoding(false));
                File.Move(temporary, connector.File, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    internal static JsonObject StdioEntry(string proxy, string environment) => new() { ["command"] = proxy, ["args"] = new JsonArray("--mcp-proxy", environment) };
    internal static JsonObject HttpEntry(string url, string token) => new() { ["type"] = "http", ["url"] = url, ["headers"] = new JsonObject { ["Authorization"] = "Bearer " + token } };
    private static JsonObject? ParseJson(string text) => JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }) as JsonObject;
    internal static string ApplyJson(string existing, string serversKey, string key, JsonObject entry, bool connected)
    {
        JsonObject document;
        try { document = string.IsNullOrWhiteSpace(existing) ? new JsonObject() : ParseJson(existing) ?? throw new InvalidDataException("existing-config-unreadable"); }
        catch (JsonException error) { throw new InvalidDataException("existing-config-unreadable", error); }
        if (document[serversKey] != null && document[serversKey] is not JsonObject) throw new InvalidDataException("existing-config-unreadable");
        var servers = document[serversKey] as JsonObject ?? new JsonObject();
        if (connected) servers[key] = entry.DeepClone(); else servers.Remove(key);
        if (servers.Count == 0) document.Remove(serversKey); else if (document[serversKey] == null) document[serversKey] = servers;
        return document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }
    internal static string ApplyCodexToml(string existing, string key, string proxy, string environment, bool connected)
    {
        var start = existing.IndexOf(MarkerStart, StringComparison.Ordinal);
        var end = existing.IndexOf(MarkerEnd, StringComparison.Ordinal);
        if ((start < 0) != (end < 0) || start >= 0 && end < start) throw new InvalidDataException("existing-config-unreadable");
        var baseline = existing;
        if (start >= 0)
        {
            end += MarkerEnd.Length;
            if (end < existing.Length && existing[end] == '\r') end++;
            if (end < existing.Length && existing[end] == '\n') end++;
            baseline = existing[..start] + existing[end..];
        }
        if (!connected) return baseline;
        // Refuse a duplicate hand-managed target table; do not corrupt TOML.
        var escaped = System.Text.RegularExpressions.Regex.Escape(key);
        if (System.Text.RegularExpressions.Regex.IsMatch(baseline, "(?m)^\\s*\\[\\s*mcp_servers\\s*\\.\\s*(?:" + escaped + "|\"" + escaped + "\"|'" + escaped + "')\\s*(?:\\]|\\.)")) throw new InvalidDataException("existing-vault-server-unmanaged");
        var separator = baseline.Length == 0 || baseline.EndsWith('\n') ? "" : "\n";
        return baseline + separator + MarkerStart + "\n[mcp_servers." + key + "]\ncommand = " + JsonSerializer.Serialize(proxy) + "\nargs = [\"--mcp-proxy\", " + JsonSerializer.Serialize(environment) + "]\n" + MarkerEnd + "\n";
    }
}
