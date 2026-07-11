using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using WindowsBlocker.Core;
using WindowsBlocker.Enforcement;
using WindowsBlocker.WebUI;

namespace WindowsBlocker.Rules;

/// Per-tick rule output the MainWindow renders into the overlays + registry.
public sealed class RuleTickOutput
{
    public List<TimerDisplayItem> CustomTimers { get; } = new();
    public List<(string Message, string Level)> HudLogs { get; } = new();
    public List<string> CloseOnce { get; } = new();
}

/// The Windows analog of the custom-rule half of MacEnforcementBridge: it owns
/// the WebView2-hosted MacBlockerRuntime, reconciles loaded rules against the
/// editor's groups, builds + dispatches events (tick / focus / switch / lifecycle
/// / user-triggered), and folds rule output into native enforcement (app blocks,
/// close/open intents, local-file I/O), the timer HUD (custom timers), the toast
/// overlay (hud logs) and the panel overlay (interactive + system panels).
///
/// Web-level intents (blockSite, unblockSite, closeTab, …) are intentionally
/// ignored — neither acted on nor logged — and left entirely to the customBlocker
/// browser extension, matching the port's scope: the native app blocks whole
/// apps; the extension owns site/tab/DOM enforcement. The native app never reads
/// browser tabs (__nativeGetAllTabs returns none), so it cannot and must not
/// interfere with web-level events.
public sealed class RuleEngine
{
    public const string SystemGroupId = "__system__";
    private const int MaxLog = 200;
    private const int MaxFileChainDepth = 6;
    private const int MaxLocalFileBytes = 1024 * 1024;
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase) { ".txt", ".csv", ".json" };

    private readonly WebStore _store;
    private CustomRuleRuntime? _runtime;

    private readonly Dictionary<string, string> _loadedRuleSources = new();
    private readonly HashSet<string> _ruleBlockedIdentities = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<PanelSnapshot>> _panelsByGroup = new();
    private readonly List<RuleLogEntry> _ruleLog = new();
    private readonly List<Dictionary<string, string>> _systemPanelEvents = new();
    private HashSet<string> _lastRunningIds = new(StringComparer.OrdinalIgnoreCase);
    private string _lastForegroundId = "";
    // Until the first tick has captured the baseline running/foreground state we
    // must NOT synthesize lifecycle events, or every already-running app would be
    // reported as just-"launched" (and the first focus as a switch) at startup.
    // macOS avoids this naturally by using event-driven NSWorkspace launch/
    // terminate notifications; the Windows port diffs snapshots, so it seeds once.
    private bool _lifecycleSeeded;

    private HashSet<string> _blockedIdentities = new(StringComparer.OrdinalIgnoreCase);

    /// Identities native enforcement should block this tick (rule shield + the
    /// persistent blockApp set). Consumed by the next EnforcementEngine.Tick.
    public IReadOnlySet<string> BlockedIdentities => _blockedIdentities;

    public RuleEngine(WebStore store)
    {
        _store = store;
    }

    public void AttachRuntime(CustomRuleRuntime runtime) => _runtime = runtime;

    private static string NowIso() => DateTimeOffset.UtcNow.ToString("o");

    // -------------------------------------------------------------- tick

    public async Task<RuleTickOutput> TickAsync(AppIdentity foreground, List<AppIdentity> running)
    {
        var output = new RuleTickOutput();
        var groups = _store.ImportedGroups()?.Groups ?? new List<BlockGroup>();
        var ruleGroups = groups.Where(g => g.Enabled && !string.IsNullOrEmpty(g.CustomRuleSource)).ToList();

        await ReconcileRuntimeAsync(ruleGroups);

        // Lifecycle (open/close) diff + focus/switch derivation.
        var runningIds = new HashSet<string>(running.Select(r => r.Canonical), StringComparer.OrdinalIgnoreCase);
        var curForeground = foreground.IsEmpty ? "" : foreground.Canonical;

        List<string> launched;
        List<string> terminated;
        string prevForeground;
        bool switched;
        if (!_lifecycleSeeded)
        {
            // First tick: capture the baseline only. No app "launched" just
            // because windowsBlocker started, and no spurious initial focus
            // switch — exactly like macOS, which fires nothing for the apps that
            // were already running when it launched.
            _lifecycleSeeded = true;
            launched = new List<string>();
            terminated = new List<string>();
            prevForeground = curForeground;
            switched = false;
        }
        else
        {
            launched = runningIds.Except(_lastRunningIds).ToList();
            terminated = _lastRunningIds.Except(runningIds).ToList();
            prevForeground = _lastForegroundId;
            switched = curForeground != prevForeground;
        }
        _lastRunningIds = runningIds;
        _lastForegroundId = curForeground;

        var runningJson = RunningAppsJson(running);

        var nextPanels = new Dictionary<string, List<PanelSnapshot>>();
        var thisTickShield = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var now = DateTimeOffset.Now;

        if (_runtime != null && ruleGroups.Count > 0)
        {
            var snoozes = _store.LoadSnoozes();
            foreach (var group in ruleGroups)
            {
                if (!group.IsActive(now))
                {
                    continue;
                }
                if (snoozes.TryGetValue(group.Id, out var snooze) && snooze.Phase(now) == SnoozePhase.Active)
                {
                    continue;
                }

                var events = BuildEvents(group, foreground, runningJson, launched, terminated,
                    switched, prevForeground, curForeground);

                DispatchResult? latest = null;
                foreach (var ev in events)
                {
                    if (ev.Type != "tickEvent")
                    {
                        AppendLog("log", group.Name,
                            $"event fired: {ev.Type} | app: {(ev.Data.TryGetValue("bundleId", out var b) ? b : ev.Data.GetValueOrDefault("appId", "—"))}");
                    }

                    var result = await _runtime.DispatchAsync(ev);
                    if (result == null)
                    {
                        continue;
                    }
                    await ApplyResultAsync(group, result, foreground, thisTickShield, allowed, output, 0);
                    latest = result;
                }

                // Panels and custom timers are whole-state snapshots the runtime
                // re-emits on every dispatch, so the LAST event's result is the
                // most current — it reflects every handler that ran this tick
                // (e.g. a panel a focusEvent created, or a timer it started).
                if (latest != null)
                {
                    if (latest.Panels.Count > 0)
                    {
                        nextPanels[group.Id] = latest.Panels;
                    }
                    foreach (var timer in latest.Timers.Where(t => !t.IsPaused))
                    {
                        output.CustomTimers.Add(new TimerDisplayItem(
                            $"{timer.GroupId}.{timer.Id}",
                            string.IsNullOrEmpty(timer.DisplayName) ? timer.Id : timer.DisplayName,
                            Math.Max(0, timer.CurrentMs / 1000.0)));
                    }
                }
            }
        }

        // Authoritative panel state: tick groups replace, system panels persist.
        ReplacePanels(nextPanels);

        thisTickShield.ExceptWith(allowed);
        var blocked = new HashSet<string>(thisTickShield, StringComparer.OrdinalIgnoreCase);
        blocked.UnionWith(_ruleBlockedIdentities);
        _blockedIdentities = blocked;

        return output;
    }

    // -------------------------------------------------- user-driven events

    /// snoozePress / panelEvent / localFileEvent dispatched outside the tick loop.
    public async Task<RuleTickOutput> FireUserEventAsync(string type, string groupId, Dictionary<string, string> data, AppIdentity foreground)
    {
        var output = new RuleTickOutput();
        if (_runtime == null)
        {
            return output;
        }
        var groups = _store.ImportedGroups()?.Groups ?? new List<BlockGroup>();
        var group = groups.FirstOrDefault(g => g.Id == groupId);
        if (group == null || string.IsNullOrEmpty(group.CustomRuleSource))
        {
            return output;
        }
        if (!_loadedRuleSources.ContainsKey(group.Id))
        {
            await BuildRuleAsync(group);
        }

        var ev = MakeEvent(type, group, foreground, data, RunningAppsJson(new List<AppIdentity>()));
        AppendLog("log", group.Name, $"event fired: {type}");
        var result = await _runtime.DispatchAsync(ev);
        if (result == null)
        {
            return output;
        }
        var shield = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await ApplyResultAsync(group, result, foreground, shield, allowed, output, 0);
        // Single events close immediately rather than persistently blocking.
        shield.ExceptWith(allowed);
        output.CloseOnce.AddRange(shield);

        // Panel updates from this group's interaction take effect immediately.
        _panelsByGroup[group.Id] = result.Panels.Count > 0 ? result.Panels : new List<PanelSnapshot>();
        if (result.Panels.Count == 0)
        {
            _panelsByGroup.Remove(group.Id);
        }
        return output;
    }

    // --------------------------------------------------- dispatch application

    private async Task ApplyResultAsync(
        BlockGroup group, DispatchResult result, AppIdentity foreground,
        HashSet<string> shield, HashSet<string> allowed, RuleTickOutput output, int depth)
    {
        var fg = foreground.IsEmpty ? "" : foreground.Canonical;

        foreach (var decision in result.Decisions)
        {
            switch (decision.Action)
            {
                case "shield":
                    if (decision.TargetIds.Count == 0)
                    {
                        if (fg.Length > 0) shield.Add(fg);
                    }
                    else
                    {
                        foreach (var id in decision.TargetIds) shield.Add(id);
                    }
                    break;
                case "allow":
                    if (decision.TargetIds.Count == 0)
                    {
                        if (fg.Length > 0) allowed.Add(fg);
                    }
                    else
                    {
                        foreach (var id in decision.TargetIds) allowed.Add(id);
                    }
                    break;
                case "log":
                    var level = decision.MetaString("level", "log");
                    var surface = decision.MetaString("surface", "all");
                    AppendLog(level, group.Name, decision.Reason);
                    if (surface is "popup" or "screen" or "all")
                    {
                        output.HudLogs.Add(($"[{group.Name}] {decision.Reason}", level));
                    }
                    break;
                case "showStatus":
                    // helpers.overlay.show({...}) — surface the status banner as a
                    // HUD toast (the Windows analog of macOS's status overlay).
                    if (!string.IsNullOrEmpty(decision.Reason))
                    {
                        output.HudLogs.Add(($"[{group.Name}] {decision.Reason}", "log"));
                    }
                    break;
            }
        }

        foreach (var intent in result.Intents)
        {
            if (intent.Kind == "localFile")
            {
                if (depth < MaxFileChainDepth)
                {
                    var followUp = ProcessLocalFileIntent(intent, group);
                    if (followUp != null && _runtime != null)
                    {
                        var r = await _runtime.DispatchAsync(followUp);
                        if (r != null)
                        {
                            await ApplyResultAsync(group, r, foreground, shield, allowed, output, depth + 1);
                        }
                    }
                }
                continue;
            }
            ProcessWindowIntent(intent, fg, output);
        }
    }

    private void ProcessWindowIntent(RuleIntent intent, string foregroundId, RuleTickOutput output)
    {
        switch (intent.Action)
        {
            case "close":
                if (!string.IsNullOrEmpty(intent.Target)) output.CloseOnce.Add(intent.Target!);
                else if (foregroundId.Length > 0) output.CloseOnce.Add(foregroundId);
                break;
            case "blockApp":
                if (!string.IsNullOrEmpty(intent.Target))
                {
                    _ruleBlockedIdentities.Add(intent.Target!);
                    AppendLog("log", "system", $"App blocked: {intent.Target}");
                }
                break;
            case "unblockApp":
                if (!string.IsNullOrEmpty(intent.Target))
                {
                    _ruleBlockedIdentities.Remove(intent.Target!);
                    AppendLog("log", "system", $"App unblocked: {intent.Target}");
                }
                break;
            case "openApp":
                if (!string.IsNullOrEmpty(intent.Target)) OpenApp(intent.Target!);
                break;
            // Web-level intents (blockSite / unblockSite / closeTab /
            // closeTabsByPattern) are deliberately ignored here: site/tab/DOM
            // enforcement belongs entirely to the customBlocker browser
            // extension, which runs the same rule against its own web events.
            // The native app must not touch web-level events, so it neither acts
            // on nor logs them — it only enforces whole-app intents above.
        }
    }

    private void OpenApp(string target)
    {
        try
        {
            if (target.Contains('!'))
            {
                // AUMID → launch via the shell AppsFolder moniker.
                Process.Start(new ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{target}") { UseShellExecute = true });
            }
            else
            {
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            }
            AppendLog("log", "system", $"App opened: {target}");
        }
        catch (Exception ex)
        {
            AppendLog("error", "system", $"Open app failed: {target} ({ex.Message})");
        }
    }

    // --------------------------------------------------- local file I/O

    private string LocalFolderBase
    {
        get
        {
            var dir = Path.Combine(Storage.RootDirectory, "LocalFiles");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    private string? ResolveLocalPath(string relativePath, bool allowDirectory)
    {
        var raw = (relativePath ?? "").Trim().Replace('\\', '/');
        while (raw.Contains("//", StringComparison.Ordinal)) raw = raw.Replace("//", "/", StringComparison.Ordinal);
        raw = raw.TrimEnd('/');
        if (string.IsNullOrEmpty(raw))
        {
            return allowDirectory ? LocalFolderBase : null;
        }
        if (raw.StartsWith("/", StringComparison.Ordinal)
            || System.Text.RegularExpressions.Regex.IsMatch(raw, "^[A-Za-z]:/")
            || System.Text.RegularExpressions.Regex.IsMatch(raw, "^[A-Za-z][A-Za-z0-9+.-]*:"))
        {
            return null;
        }

        var parts = raw.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return null;
        foreach (var part in parts)
        {
            if (part is "." or ".." || part.StartsWith('.', StringComparison.Ordinal)
                || !System.Text.RegularExpressions.Regex.IsMatch(part, "^[A-Za-z0-9 _.,@()\\-]+$"))
            {
                return null;
            }
        }
        if (!allowDirectory && !AllowedExtensions.Contains(Path.GetExtension(parts[^1])))
        {
            return null;
        }

        var baseDir = Path.GetFullPath(LocalFolderBase);
        var resolved = Path.GetFullPath(Path.Combine(baseDir, Path.Combine(parts)));
        // Separator-bounded containment so a sibling like "LocalFilesEvil" can
        // never satisfy a bare prefix check.
        var boundary = baseDir.EndsWith(Path.DirectorySeparatorChar)
            ? baseDir
            : baseDir + Path.DirectorySeparatorChar;
        return resolved.Equals(baseDir, StringComparison.OrdinalIgnoreCase)
            || resolved.StartsWith(boundary, StringComparison.OrdinalIgnoreCase)
            ? resolved : null;
    }

    // Performs a local-file operation requested by a rule and returns the
    // localFileEvent to feed back to the rule (ported from processLocalFileIntent).
    private CustomRuleEvent? ProcessLocalFileIntent(RuleIntent intent, BlockGroup group)
    {
        if (string.IsNullOrEmpty(intent.GroupId) || string.IsNullOrEmpty(intent.RequestId))
        {
            return null;
        }
        var action = intent.Action;
        var path = intent.Path ?? "";
        var data = new Dictionary<string, string>
        {
            ["requestId"] = intent.RequestId!,
            ["eventName"] = action,
            ["action"] = action,
            ["ok"] = "false",
            ["path"] = path
        };

        try
        {
            switch (action)
            {
                case "read":
                case "readJson":
                {
                    var url = ResolveLocalPath(path, allowDirectory: false) ?? throw new LocalFileRequestException("invalid-path");
                    var text = ReadLimitedText(url);
                    data["text"] = text;
                    if (action == "readJson")
                    {
                        ValidateJson(text);
                        data["valueJSON"] = text;
                    }
                    data["eventName"] = "read";
                    data["bytes"] = Encoding.UTF8.GetByteCount(text).ToString();
                    break;
                }
                case "write":
                case "writeJson":
                {
                    var url = ResolveLocalPath(path, allowDirectory: false) ?? throw new LocalFileRequestException("invalid-path");
                    var text = intent.Text ?? "";
                    if (action == "writeJson") ValidateJson(text);
                    EnsureLocalFileSize(text);
                    Directory.CreateDirectory(Path.GetDirectoryName(url)!);
                    File.WriteAllText(url, text, Encoding.UTF8);
                    data["eventName"] = "write";
                    data["bytes"] = Encoding.UTF8.GetByteCount(text).ToString();
                    break;
                }
                case "append":
                {
                    var url = ResolveLocalPath(path, allowDirectory: false) ?? throw new LocalFileRequestException("invalid-path");
                    var text = File.Exists(url) ? ReadLimitedText(url) : "";
                    text += intent.Text ?? "";
                    EnsureLocalFileSize(text);
                    Directory.CreateDirectory(Path.GetDirectoryName(url)!);
                    File.WriteAllText(url, text, Encoding.UTF8);
                    data["bytes"] = Encoding.UTF8.GetByteCount(text).ToString();
                    break;
                }
                case "list":
                {
                    var dir = ResolveLocalPath(path, allowDirectory: true) ?? throw new LocalFileRequestException("invalid-path");
                    if (!Directory.Exists(dir)) throw new DirectoryNotFoundException();
                    var normalizedPath = NormalizeDirectoryPath(path);
                    var entries = new List<Dictionary<string, string>>();
                    foreach (var entry in Directory.GetFileSystemEntries(dir))
                    {
                        var name = Path.GetFileName(entry);
                        if (string.IsNullOrEmpty(name) || name.StartsWith('.', StringComparison.Ordinal)) continue;
                        var entryPath = string.IsNullOrEmpty(normalizedPath) ? name : normalizedPath + "/" + name;
                        if (Directory.Exists(entry))
                        {
                            entries.Add(new Dictionary<string, string>
                            {
                                ["name"] = name,
                                ["path"] = entryPath,
                                ["kind"] = "directory"
                            });
                        }
                        else if (AllowedExtensions.Contains(Path.GetExtension(entry)))
                        {
                            entries.Add(new Dictionary<string, string>
                            {
                                ["name"] = name,
                                ["path"] = entryPath,
                                ["kind"] = "file",
                                ["extension"] = Path.GetExtension(entry).ToLowerInvariant()
                            });
                        }
                    }
                    data["entriesJSON"] = JsonSerializer.Serialize(entries
                        .OrderBy(entry => entry["kind"], StringComparer.Ordinal)
                        .ThenBy(entry => entry["name"], StringComparer.Ordinal));
                    data["directoryPath"] = normalizedPath;
                    break;
                }
                case "exists":
                {
                    var url = ResolveLocalPath(path, allowDirectory: false) ?? throw new LocalFileRequestException("invalid-path");
                    data["exists"] = File.Exists(url) ? "true" : "false";
                    break;
                }
                default:
                    throw new LocalFileRequestException("unsupported-action");
            }
            data["ok"] = "true";
        }
        catch (LocalFileRequestException ex)
        {
            data["eventName"] = "error";
            data["error"] = ex.Code;
        }
        catch (FileNotFoundException)
        {
            data["eventName"] = "error";
            data["error"] = "not-found";
        }
        catch (DirectoryNotFoundException)
        {
            data["eventName"] = "error";
            data["error"] = "not-found";
        }
        catch (Exception)
        {
            data["eventName"] = "error";
            data["error"] = "local-file-error";
        }

        return MakeEvent("localFileEvent", group, new AppIdentity(), data, "[]");
    }

    private static void EnsureLocalFileSize(string text)
    {
        if (Encoding.UTF8.GetByteCount(text) > MaxLocalFileBytes)
        {
            throw new LocalFileRequestException("file-too-large");
        }
    }

    private static string ReadLimitedText(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException();
        if (info.Length > MaxLocalFileBytes) throw new LocalFileRequestException("file-too-large");
        var text = File.ReadAllText(path, Encoding.UTF8);
        EnsureLocalFileSize(text);
        return text;
    }

    private static void ValidateJson(string text)
    {
        try
        {
            using var _ = JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            throw new LocalFileRequestException("invalid-json");
        }
    }

    private static string NormalizeDirectoryPath(string path)
    {
        var raw = (path ?? "").Trim().Replace('\\', '/');
        while (raw.Contains("//", StringComparison.Ordinal)) raw = raw.Replace("//", "/", StringComparison.Ordinal);
        return raw.Trim('/');
    }

    private sealed class LocalFileRequestException : Exception
    {
        public string Code { get; }

        public LocalFileRequestException(string code) : base(code)
        {
            Code = code;
        }
    }

    // --------------------------------------------------- rule management

    private async Task ReconcileRuntimeAsync(List<BlockGroup> ruleGroups)
    {
        if (_runtime == null)
        {
            return;
        }
        var currentIds = new HashSet<string>(ruleGroups.Select(g => g.Id));

        // Unload groups that are gone or disabled.
        foreach (var id in _loadedRuleSources.Keys.ToList())
        {
            if (!currentIds.Contains(id))
            {
                await CleanRuleAsync(id);
            }
        }

        // Load (or reload on source change) current rule groups.
        foreach (var group in ruleGroups)
        {
            if (_loadedRuleSources.TryGetValue(group.Id, out var loaded))
            {
                if (loaded == group.CustomRuleSource)
                {
                    continue;
                }
                await CleanRuleAsync(group.Id);
            }
            await BuildRuleAsync(group);
        }
    }

    private async Task BuildRuleAsync(BlockGroup group)
    {
        if (_runtime == null || string.IsNullOrEmpty(group.CustomRuleSource))
        {
            return;
        }
        var load = await _runtime.LoadAsync(group.Id, group.CustomRuleSource);
        if (load == null)
        {
            AppendLog("error", group.Name, "Rule build failed.");
            return;
        }
        _loadedRuleSources[group.Id] = group.CustomRuleSource;
        AppendLog("log", group.Name, $"Rule built: {load.Handlers} handler(s)");
        foreach (var decision in load.Decisions.Where(d => d.Action == "log"))
        {
            AppendLog(decision.MetaString("level", "log"), group.Name, decision.Reason);
        }
    }

    private async Task CleanRuleAsync(string groupId)
    {
        if (_runtime != null && _loadedRuleSources.ContainsKey(groupId))
        {
            await _runtime.UnloadAsync(groupId);
        }
        _loadedRuleSources.Remove(groupId);
        _panelsByGroup.Remove(groupId);
    }

    /// Run = clean + build (the editor's "Run" button → run-custom-group).
    public async Task RunRuleAsync(string groupId, string source)
    {
        await CleanRuleAsync(groupId);
        var groups = _store.ImportedGroups()?.Groups ?? new List<BlockGroup>();
        var group = groups.FirstOrDefault(g => g.Id == groupId);
        if (group != null)
        {
            await BuildRuleAsync(group);
        }
    }

    public async Task UnloadGroupAsync(string groupId) => await CleanRuleAsync(groupId);

    // --------------------------------------------------- panels (overlay)

    private void ReplacePanels(Dictionary<string, List<PanelSnapshot>> tickPanels)
    {
        var system = _panelsByGroup.TryGetValue(SystemGroupId, out var sys) ? sys : null;
        _panelsByGroup.Clear();
        foreach (var (k, v) in tickPanels)
        {
            if (v.Count > 0) _panelsByGroup[k] = v;
        }
        if (system != null && system.Count > 0)
        {
            _panelsByGroup[SystemGroupId] = system;
        }
    }

    public Dictionary<string, List<PanelSnapshot>> PanelsSnapshot()
        => _panelsByGroup.ToDictionary(kv => kv.Key, kv => kv.Value);

    public void ShowSystemPanel(string snapshotJson)
    {
        PanelSnapshot? snap;
        try
        {
            snap = JsonSerializer.Deserialize<PanelSnapshot>(snapshotJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return;
        }
        if (snap == null)
        {
            return;
        }
        snap.GroupId = SystemGroupId;
        var list = _panelsByGroup.TryGetValue(SystemGroupId, out var existing) ? existing : new List<PanelSnapshot>();
        var idx = list.FindIndex(p => p.Id == snap.Id);
        if (idx >= 0) list[idx] = snap; else list.Add(snap);
        _panelsByGroup[SystemGroupId] = list;
    }

    public void DismissSystemPanel(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            _panelsByGroup.Remove(SystemGroupId);
            return;
        }
        if (_panelsByGroup.TryGetValue(SystemGroupId, out var list))
        {
            list.RemoveAll(p => p.Id == id);
            if (list.Count == 0) _panelsByGroup.Remove(SystemGroupId);
        }
    }

    public void BufferSystemPanelEvent(Dictionary<string, string> data) => _systemPanelEvents.Add(data);

    public string? DrainSystemPanelEventsJson()
    {
        if (_systemPanelEvents.Count == 0)
        {
            return null;
        }
        var json = JsonSerializer.Serialize(_systemPanelEvents);
        _systemPanelEvents.Clear();
        return json;
    }

    // --------------------------------------------------- logging

    private void AppendLog(string level, string group, string message)
    {
        _ruleLog.Add(new RuleLogEntry { Timestamp = NowIso(), Level = level, Group = group, Message = message });
        if (_ruleLog.Count > MaxLog)
        {
            _ruleLog.RemoveRange(0, _ruleLog.Count - MaxLog);
        }
    }

    public string? DrainLogJson()
    {
        if (_ruleLog.Count == 0)
        {
            return null;
        }
        var json = JsonSerializer.Serialize(_ruleLog);
        _ruleLog.Clear();
        return json;
    }

    // --------------------------------------------------- event building

    private List<CustomRuleEvent> BuildEvents(
        BlockGroup group, AppIdentity foreground, string runningJson,
        List<string> launched, List<string> terminated,
        bool switched, string prevForeground, string curForeground)
    {
        var events = new List<CustomRuleEvent>
        {
            MakeEvent("tickEvent", group, foreground, new Dictionary<string, string> { ["intervalMs"] = "1000" }, runningJson)
        };

        foreach (var id in launched)
        {
            events.Add(MakeEvent("openAppEvent", group, foreground, new Dictionary<string, string> { ["bundleId"] = id }, runningJson));
            events.Add(MakeEvent("appChangedEvent", group, foreground, new Dictionary<string, string> { ["reason"] = "open", ["bundleId"] = id }, runningJson));
        }
        foreach (var id in terminated)
        {
            events.Add(MakeEvent("closeAppEvent", group, foreground, new Dictionary<string, string> { ["bundleId"] = id }, runningJson));
            events.Add(MakeEvent("appChangedEvent", group, foreground, new Dictionary<string, string> { ["reason"] = "close", ["bundleId"] = id }, runningJson));
        }

        if (switched)
        {
            if (prevForeground.Length > 0)
            {
                events.Add(MakeEvent("unfocusEvent", group, foreground, new Dictionary<string, string> { ["bundleId"] = prevForeground }, runningJson));
            }
            if (curForeground.Length > 0)
            {
                events.Add(MakeEvent("focusEvent", group, foreground, new Dictionary<string, string> { ["bundleId"] = curForeground }, runningJson));
            }
            if (prevForeground.Length > 0 && curForeground.Length > 0)
            {
                events.Add(MakeEvent("switchAppEvent", group, foreground, new Dictionary<string, string>
                {
                    ["previousAppId"] = prevForeground,
                    ["currentAppId"] = curForeground
                }, runningJson));
                events.Add(MakeEvent("appChangedEvent", group, foreground, new Dictionary<string, string>
                {
                    ["reason"] = "switch",
                    ["previousAppId"] = prevForeground,
                    ["currentAppId"] = curForeground
                }, runningJson));
            }
        }

        return events;
    }

    private CustomRuleEvent MakeEvent(string type, BlockGroup group, AppIdentity foreground, Dictionary<string, string> data, string runningJson)
    {
        var appId = foreground.IsEmpty ? "" : foreground.Canonical;
        var enriched = new Dictionary<string, string>(data)
        {
            ["appId"] = appId,
            ["appName"] = foreground.IsEmpty ? "" : foreground.DisplayName,
            ["isBrowser"] = "false",
            ["groupName"] = group.Name,
            ["allApps"] = runningJson
        };

        RuleTarget? target = null;
        var match = group.Targets.FirstOrDefault(t =>
            t.Kind == BlockTarget.TargetKind.Application &&
            !foreground.IsEmpty &&
            EnforcementEngine.TargetMatchesIdentity(t.NormalizedValue, foreground));
        if (match != null)
        {
            target = new RuleTarget
            {
                Id = appId,
                Kind = "application",
                DisplayName = match.DisplayName,
                Value = match.NormalizedValue,
                Tags = match.Tags.ToList()
            };
        }

        return new CustomRuleEvent
        {
            Type = type,
            GroupId = group.Id,
            Target = target,
            Now = NowIso(),
            Url = appId.Length > 0 ? $"app://{appId}" : "",
            Hostname = appId,
            Data = enriched
        };
    }

    private static string RunningAppsJson(List<AppIdentity> running)
    {
        var apps = running.Select(r => new { id = r.Canonical, name = r.DisplayName, isBrowser = false });
        return JsonSerializer.Serialize(apps);
    }
}
