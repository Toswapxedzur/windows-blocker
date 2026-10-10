using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using WindowsBlocker.Bridge;
using WindowsBlocker.Core;
using WindowsBlocker.Enforcement;
using WindowsBlocker.WebUI;

namespace WindowsBlocker.Rules;

public sealed class RuleTickOutput
{
    public List<(string Message, string Level)> HudLogs { get; } = new();
    public List<string> CloseOnce { get; } = new();
}

// Current bare (on,v) rules. The isolated worker retains state on Run and
// suppresses disabled groups; native code honors only desktop actions.
public sealed class RuleEngine(WebStore store)
{
    public const string SystemGroupId = "__system__";
    private CustomRuleRuntime? _runtime;
    private readonly SemaphoreSlim _operations=new(1);
    private readonly Dictionary<string,string> _loaded = new();
    private readonly Dictionary<string,string> _quarantined = new();
    private readonly Dictionary<string,HashSet<string>> _blocks = new();
    private readonly Dictionary<string,List<PanelSnapshot>> _panels = new();
    private readonly HashSet<string> _suppressed = new();
    private readonly List<RuleLogEntry> _logs = new();
    private readonly List<Dictionary<string,string>> _systemEvents = new();
    private Dictionary<string,AppIdentity> _running = new();
    private string _front = "";
    private bool _seeded;
    public IReadOnlySet<string> BlockedIdentities => _blocks.Where(b => !_suppressed.Contains(b.Key)).SelectMany(b => b.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
    public void AttachRuntime(CustomRuleRuntime runtime)
    {
        _runtime = runtime;
        runtime.GroupReset += (id, error) => { _quarantined[id] = _loaded.GetValueOrDefault(id, ""); _loaded.Remove(id); _blocks.Remove(id); _panels.Remove(id); Trace.WriteLine($"Rule {id}: {error}"); };
    }
    public async Task<RuleTickOutput> TickAsync(AppIdentity foreground,List<AppIdentity> running)
    { await _operations.WaitAsync(); try { return await TickCore(foreground,running); } finally { _operations.Release(); } }
    private async Task<RuleTickOutput> TickCore(AppIdentity foreground, List<AppIdentity> running)
    {
        var output = new RuleTickOutput();
        if (_runtime == null) return output;
        var imported = store.ImportedGroups();
        if (imported == null) return output; // An unreadable/unsupported snapshot cannot retire working rules.
        var groups = imported.Groups;
        var wanted = groups.Where(g => g.GroupType == BlockGroupType.Custom && g.CustomRuleSource.Length > 0).ToDictionary(g => g.Id);
        foreach (var id in _loaded.Keys.Concat(_quarantined.Keys).Distinct().Where(id => !wanted.ContainsKey(id)).ToList()) await UnloadCore(id);
        foreach (var (id, group) in wanted)
        {
            if (_loaded.GetValueOrDefault(id) != group.CustomRuleSource && _quarantined.GetValueOrDefault(id) != group.CustomRuleSource) await Load(group, group.CustomRuleSource);
            await _runtime.SuppressAsync(id, !group.Enabled);
            if (group.Enabled) _suppressed.Remove(id); else _suppressed.Add(id);
        }
        var next = running.DistinctBy(p => p.Canonical).ToDictionary(p => p.Canonical);
        if (_seeded)
        {
            foreach (var app in next.Values.Where(a => !_running.ContainsKey(a.Canonical))) await Dispatch("app", new { kind = "launch", appId = app.Canonical, name = app.DisplayName }, null, output);
            foreach (var app in _running.Values.Where(a => !next.ContainsKey(a.Canonical))) await Dispatch("app", new { kind = "quit", appId = app.Canonical, name = app.DisplayName }, null, output);
            if (foreground.Canonical != _front)
            {
                if (_running.TryGetValue(_front, out var old)) await Dispatch("app", new { kind = "blur", appId = old.Canonical, name = old.DisplayName }, null, output);
                if (!foreground.IsEmpty) await Dispatch("app", new { kind = "focus", appId = foreground.Canonical, name = foreground.DisplayName, previousAppId = _front }, null, output);
            }
        }
        _seeded = true; _running = next; _front = foreground.Canonical;
        await Dispatch("tick", new { frontmost = foreground.IsEmpty ? null : new { appId = foreground.Canonical, name = foreground.DisplayName }, running = next.Values.Select(p => new { appId = p.Canonical, name = p.DisplayName }).ToArray() }, null, output);
        return output;
    }
    private async Task<LoadResult?> Load(BlockGroup group, string source, bool saveSource = false)
    {
        if (_runtime == null) return null;
        try
        {
            var raw = JsonNode.Parse(store.LoadRawJson() ?? "{}");
            if (raw is not JsonObject saved) return new() { Error = "invalid-rule-storage" };
            StorageSchema.ValidateWeb(saved);
            var state = saved["cbRuleState"]?[group.Id]?.ToJsonString() ?? "{}";
            var result = await _runtime.PrepareLoadAsync(group.Id, source, state);
            if (result == null || !result.Ok) { await _runtime.DiscardLoadAsync(group.Id); return result; }
            try
            {
                // Persist registration state and active source in one guarded write.
                // Neither native caches nor the old worker change before this succeeds.
                store.Update(root =>
                {
                    var native = (root["blockedGroups"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(g => g["id"]?.GetValue<string>() == group.Id)
                        ?? throw new InvalidDataException("group-not-found");
                    if (saveSource && ConnectionHub.IsLocked(native)) throw new InvalidDataException("group-locked");
                    if (result.States.Count > 0)
                    {
                        var states = root["cbRuleState"] as JsonObject ?? new(); root["cbRuleState"] = states;
                        foreach (var (id, json) in result.States)
                        {
                            if (id != group.Id) throw new InvalidDataException("invalid-rule-state-group");
                            states[id] = JsonNode.Parse(json) as JsonObject ?? throw new InvalidDataException("invalid-rule-state");
                        }
                    }
                    if (saveSource) { native["activeEventSource"] = source; native["blockingRulesText"] = source; native["enabled"] = true; native["lastAbortReason"] = null; }
                });
            }
            catch
            {
                await _runtime.DiscardLoadAsync(group.Id);
                return new() { Error = "rule-storage-write-failed" };
            }
            if (!await _runtime.CommitLoadAsync(group.Id)) return new() { Error = "rule-runtime-commit-failed" };
            AppendLogs(result.Logs);
            _loaded[group.Id] = source; _quarantined.Remove(group.Id); _blocks.Remove(group.Id); _panels[group.Id] = result.Panels;
            return result;
        }
        catch { return new() { Error = "invalid-rule-storage" }; }
    }
    public async Task<JsonObject> RunRuleAsync(string groupId,string source)
    { await _operations.WaitAsync(); try { return await RunCore(groupId,source); } finally { _operations.Release(); } }
    private async Task<JsonObject> RunCore(string groupId, string source)
    {
        JsonObject? g;
        BlockGroup? group;
        try
        {
            var raw = JsonNode.Parse(store.LoadRawJson() ?? "{}");
            if (raw is not JsonObject saved) return new() { ["ok"] = false, ["error"] = "invalid-rule-storage" };
            StorageSchema.ValidateWeb(saved);
            g = (raw["blockedGroups"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(g => g["id"]?.GetValue<string>() == groupId);
            group = store.ImportedGroups()?.Groups.FirstOrDefault(g => g.Id == groupId && g.GroupType == BlockGroupType.Custom);
        }
        catch { return new() { ["ok"] = false, ["error"] = "invalid-rule-storage" }; }
        if (g == null || group == null) return new() { ["ok"] = false, ["error"] = "group-not-found" };
        if (ConnectionHub.IsLocked(g)) return new() { ["ok"] = false, ["error"] = "group-locked" };
        var result = await Load(group, source, saveSource: true);
        if (result?.Ok == true)
        {
            _suppressed.Remove(groupId); if (_runtime != null) await _runtime.SuppressAsync(groupId, false);
        }
        return new() { ["ok"] = result?.Ok == true, ["handlers"] = result?.Handlers ?? 0, ["error"] = result?.Error ?? (result == null ? "rules-not-running" : null) };
    }
    public async Task UnloadGroupAsync(string groupId)
    { await _operations.WaitAsync(); try { await UnloadCore(groupId); } finally { _operations.Release(); } }
    private async Task UnloadCore(string groupId)
    {
        if (_runtime != null) await _runtime.UnloadAsync(groupId);
        _loaded.Remove(groupId); _quarantined.Remove(groupId); _blocks.Remove(groupId); _panels.Remove(groupId); _suppressed.Remove(groupId);
    }
    public async Task<RuleTickOutput> FireUserEventAsync(string type,string groupId,Dictionary<string,string> data,AppIdentity foreground)
    { await _operations.WaitAsync(); try { return await FireCore(type,groupId,data,foreground); } finally { _operations.Release(); } }
    private async Task<RuleTickOutput> FireCore(string type, string groupId, Dictionary<string,string> data, AppIdentity foreground)
    {
        var output = new RuleTickOutput();
        var evData = data.ToDictionary(k => k.Key, k => (object?)k.Value);
        if (data.TryGetValue("valuesJSON", out var values)) { try { evData["values"] = JsonNode.Parse(values); } catch { } evData.Remove("valuesJSON"); }
        await Dispatch(type == "snoozePress" ? "snooze" : type == "panelEvent" ? "panel" : type, evData, groupId, output); return output;
    }
    private async Task Dispatch(string type, object data, string? groupId, RuleTickOutput output, int depth = 0)
    {
        if (_runtime == null || depth > 6) return;
        var ids = groupId != null ? new[] { groupId } : _loaded.Keys.ToArray();
        foreach (var id in ids)
        {
            if (_suppressed.Contains(id) || !_loaded.ContainsKey(id)) continue;
            var result = await _runtime.DispatchAsync(new() { Type = type, GroupId = id, Data = data });
            if (result == null) continue;
            AppendLogs(result.Logs);
            foreach (var diagnostic in result.Diagnostics) Trace.WriteLine($"Rule {diagnostic.GroupId}: {diagnostic.Message}");
            foreach (var (gid, panels) in result.Panels) _panels[gid] = panels;
            if (result.States.Count > 0) store.Update(root => { var states = root["cbRuleState"] as JsonObject ?? new(); root["cbRuleState"] = states; foreach (var (gid, json) in result.States) states[gid] = JsonNode.Parse(json); });
            foreach (var action in result.Actions)
            {
                switch (action.Kind)
                {
                    case "block": var blocks = _blocks.GetValueOrDefault(action.GroupId) ?? new(StringComparer.OrdinalIgnoreCase); _blocks[action.GroupId] = blocks; if (action.AppId != null) { if (action.On == false) blocks.Remove(action.AppId); else blocks.Add(action.AppId); } break;
                    case "quit": if (action.AppId != null) output.CloseOnce.Add(action.AppId); break;
                    case "open": OpenApp(action.AppId); break;
                    case "file": await Dispatch("file", LocalFolderGrant.Handle(action), action.GroupId, output, depth + 1); break;
                }
            }
            if (result.Quarantine.HasValue && result.Quarantine.Value.ValueKind == JsonValueKind.Object) { var source = _loaded.GetValueOrDefault(id, ""); await UnloadCore(id); _quarantined[id] = source; }
        }
    }
    private static void OpenApp(string? appId)
    {
        appId=WindowsAppId.Normalize(appId ?? ""); if(appId==null) return;
        try
        {
            if (appId.Contains('!')) Process.Start(new ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{appId}") { UseShellExecute = true });
            else if (Path.IsPathFullyQualified(appId) && File.Exists(appId)) Process.Start(new ProcessStartInfo(appId) { UseShellExecute = true });
        }
        catch { }
    }
    private void AppendLogs(IEnumerable<RuntimeLog> logs)
    {
        foreach (var log in logs)
        {
            if (store.ImportedGroups()?.Groups.Any(g=>g.Id==log.GroupId)!=true) continue;
            var name = store.ImportedGroups()?.Groups.FirstOrDefault(g => g.Id == log.GroupId)?.Name ?? "";
            _logs.Add(new() { Timestamp = DateTimeOffset.Now.ToString("o"), GroupId = log.GroupId, Group = name, Message = log.Message });
            while (_logs.Count(l => l.GroupId == log.GroupId) > 200) _logs.RemoveAt(_logs.FindIndex(l => l.GroupId == log.GroupId));
        }
    }
    public void ClearLog(string groupId) => _logs.RemoveAll(l => l.GroupId == groupId);
    public string? DrainLogJson() { if (_logs.Count == 0) return null; var json = JsonSerializer.Serialize(_logs); _logs.Clear(); return json; }
    public IReadOnlyDictionary<string,List<PanelSnapshot>> PanelsSnapshot() => _panels.Where(p => !_suppressed.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value);
    public void ShowSystemPanel(string json) { try { var panel = JsonSerializer.Deserialize<PanelSnapshot>(json); if (panel != null) { panel.GroupId = SystemGroupId; _panels[SystemGroupId] = [panel]; } } catch { } }
    public void DismissSystemPanel(string id) { if (_panels.TryGetValue(SystemGroupId, out var panels)) panels.RemoveAll(p => p.Id == id); }
    public void BufferSystemPanelEvent(Dictionary<string,string> data) => _systemEvents.Add(data);
    public string? DrainSystemPanelEventsJson() { if (_systemEvents.Count == 0) return null; var json = JsonSerializer.Serialize(_systemEvents); _systemEvents.Clear(); return json; }
}
