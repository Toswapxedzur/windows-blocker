using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WindowsBlocker.Core;
using WindowsBlocker.WebUI;

namespace WindowsBlocker.Enforcement;

public sealed class EnforcementStatus
{
    public List<string> BlockedGroupNames { get; init; } = new();
    public List<TimerDisplayItem> Timers { get; init; } = new();
    public int WindowsClosedThisTick { get; init; }
}

// The Windows analog of MacEnforcementBridge: on each tick it derives the live
// blocked-app set from the editor's groups, updates the registry that drives the
// WM_CLOSE sweep + the WinEvent re-close path, and accrues "time spent" for
// timed groups so the editor countdown ticks. Custom JavaScript rules are out of
// scope, so there is no rule runtime here.
public sealed class EnforcementEngine
{
    private readonly WebStore _store;
    private readonly BlockedAppRegistry _registry;
    private readonly double _tickSeconds;
    private IntPtr _selfWindow;

    public BlockedAppRegistry Registry => _registry;

    public EnforcementEngine(WebStore store, BlockedAppRegistry registry, double tickSeconds = 1.0)
    {
        _store = store;
        _registry = registry;
        _tickSeconds = tickSeconds;
    }

    public void SetSelfWindow(IntPtr hwnd) => _selfWindow = hwnd;

    public EnforcementStatus Tick()
    {
        var now = DateTimeOffset.Now;
        var imported = _store.ImportedGroups();
        var groups = imported?.Groups ?? new List<BlockGroup>();
        var timers = _store.LoadUsageTimers();
        var snoozes = _store.LoadSnoozes();

        var blockedIdentities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var blockedGroupNames = new List<string>();
        var timerItems = new List<TimerDisplayItem>();
        var timedGroupsToMaybeAccrue = new List<BlockGroup>();

        foreach (var group in groups)
        {
            if (!group.IsActive(now))
            {
                continue;
            }
            if (snoozes.TryGetValue(group.Id, out var snooze) && snooze.Phase(now) == SnoozePhase.Active)
            {
                continue;
            }

            var appTargets = group.Targets
                .Where(t => t.Kind == BlockTarget.TargetKind.Application)
                .Select(t => t.NormalizedValue)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToList();

            switch (group.Mode)
            {
                case BlockingMode.Instant:
                    if (appTargets.Count > 0)
                    {
                        foreach (var id in appTargets) blockedIdentities.Add(id);
                        blockedGroupNames.Add(group.Name);
                    }
                    break;
                case BlockingMode.AfterMinutes:
                case BlockingMode.Timer:
                    var usedMs = timers.TimersMs.GetValueOrDefault(group.Id, 0);
                    var allowedMs = Math.Max(0, group.AllowedMinutes) * 60_000.0;
                    var remainingSeconds = Math.Max(0, (allowedMs - usedMs) / 1000.0);
                    timerItems.Add(new TimerDisplayItem(group.Id, group.Name, remainingSeconds));
                    if (remainingSeconds <= 0)
                    {
                        foreach (var id in appTargets) blockedIdentities.Add(id);
                        blockedGroupNames.Add(group.Name);
                    }
                    else
                    {
                        timedGroupsToMaybeAccrue.Add(group);
                    }
                    break;
            }
        }

        _registry.Update(blockedIdentities);

        // Single enumeration pass: collect running identities (for accrual) and
        // close any currently-open blocked windows (the tick backstop).
        var runningPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var runningNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var closed = 0;
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (hwnd == _selfWindow || !IsCloseableTopLevel(hwnd))
            {
                return true;
            }
            var identity = ProcessIdentity.ForWindow(hwnd);
            if (!identity.IsEmpty)
            {
                if (!string.IsNullOrEmpty(identity.ExecutablePath)) runningPaths.Add(identity.ExecutablePath);
                if (!string.IsNullOrEmpty(identity.ExecutableName)) runningNames.Add(identity.ExecutableName);
                if (_registry.IsBlocked(identity))
                {
                    WindowCloser.CloseWindow(hwnd);
                    closed++;
                }
            }
            return true;
        }, IntPtr.Zero);

        // Accrue time for timed groups whose target is currently running.
        var usageUpdates = new Dictionary<string, double>();
        foreach (var group in timedGroupsToMaybeAccrue)
        {
            var running = group.Targets
                .Where(t => t.Kind == BlockTarget.TargetKind.Application)
                .Any(t => TargetRunning(t.NormalizedValue, runningPaths, runningNames));
            if (running)
            {
                var current = timers.TimersMs.GetValueOrDefault(group.Id, 0);
                usageUpdates[group.Id] = current + _tickSeconds * 1000.0;
            }
        }
        if (usageUpdates.Count > 0)
        {
            _store.WriteUsage(usageUpdates, new Dictionary<string, double>());
        }

        return new EnforcementStatus
        {
            BlockedGroupNames = blockedGroupNames,
            Timers = timerItems,
            WindowsClosedThisTick = closed
        };
    }

    /// Called by the WinEvent monitor: close immediately if blocked.
    public void OnWindowEvent(IntPtr hwnd) =>
        WindowCloser.CloseIfBlocked(hwnd, _registry, _selfWindow);

    private static bool TargetRunning(string normalizedValue, HashSet<string> paths, HashSet<string> names)
    {
        var value = normalizedValue.Trim().ToLowerInvariant();
        if (value.Length == 0)
        {
            return false;
        }
        if (value.Contains('\\') || value.Contains('/'))
        {
            return paths.Contains(value) || names.Contains(Path.GetFileName(value));
        }
        return names.Contains(value.EndsWith(".exe") ? value : value + ".exe");
    }

    private static bool IsCloseableTopLevel(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd) || !NativeMethods.IsWindowVisible(hwnd))
        {
            return false;
        }
        if (NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT) != hwnd)
        {
            return false;
        }
        var exStyle = (long)(uint)NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        if ((exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0)
        {
            return false;
        }
        return NativeMethods.GetWindowTextLength(hwnd) > 0;
    }
}
