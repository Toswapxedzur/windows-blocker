using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WindowsBlocker.Bridge;
using WindowsBlocker.Core;
using WindowsBlocker.WebUI;

namespace WindowsBlocker.Enforcement;

public sealed class EnforcementStatus
{
    public List<string> BlockedGroupNames { get; init; } = new();
    public List<TimerDisplayItem> Timers { get; init; } = new();
    public int WindowsClosedThisTick { get; init; }
}

// The Windows analog of the native half of MacEnforcementBridge: on each tick it
// derives the live blocked-app set from the editor's groups, updates the registry
// that drives the WM_CLOSE sweep + the WinEvent re-close path, and accrues "time
// spent" for timed groups so the editor countdown ticks. Custom JavaScript rules
// are evaluated by Rules/RuleEngine, whose per-tick blocked-app set is unioned in
// here via the ruleBlockedIdentities parameter.
public sealed class EnforcementEngine
{
    private readonly WebStore _store;
    private readonly BlockedAppRegistry _registry;
    private readonly ConnectionHub? _hub;
    private readonly double _tickSeconds;
    private IntPtr _selfWindow;
    // Groups whose local total has already been seeded into the shared cluster
    // budget, so subsequent reports send only this tick's increment.
    private readonly HashSet<string> _clusterSeeded = new();

    public BlockedAppRegistry Registry => _registry;

    public EnforcementEngine(WebStore store, BlockedAppRegistry registry, ConnectionHub? hub = null, double tickSeconds = 1.0)
    {
        _store = store;
        _registry = registry;
        _hub = hub;
        _tickSeconds = tickSeconds;
    }

    public void SetSelfWindow(IntPtr hwnd) => _selfWindow = hwnd;

    /// Runs one enforcement tick.
    ///
    /// <paramref name="foreground"/> is the focused-app identity (computed once by
    /// the caller and shared with the rule engine) — time only accrues, and the
    /// HUD only shows a countdown, while a timed group's target app is frontmost,
    /// exactly like macOS, which gates both on the frontmost application.
    ///
    /// <paramref name="ruleBlockedIdentities"/> are app identities a custom rule
    /// asked to block/shield (from the previous tick's async rule dispatch); they
    /// are unioned into the group-derived blocked set so rule-driven app blocks
    /// drive the same WM_CLOSE sweep + re-close path.
    public EnforcementStatus Tick(AppIdentity? foreground = null, IReadOnlySet<string>? ruleBlockedIdentities = null)
    {
        var fg = foreground ?? ProcessIdentity.ForWindow(NativeMethods.GetForegroundWindow());

        var now = DateTimeOffset.Now;
        var imported = _store.ImportedGroups();
        var groups = imported?.Groups ?? new List<BlockGroup>();
        var timers = _store.LoadUsageTimers();
        var snoozes = _store.LoadSnoozes();

        var usageUpdates = new Dictionary<string, double>();
        var resetUpdates = new Dictionary<string, double>();

        // Reset-interval rollover (macOS reconcileUsage parity): zero a timed
        // group's used time once its reset window elapses. This runs for every
        // enabled timed group, independent of whether it is currently active or
        // snoozed, so a budget that expired mid-window is restored on schedule.
        ReconcileResets(groups, timers, now, usageUpdates, resetUpdates);

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
                    if (remainingSeconds <= 0)
                    {
                        foreach (var id in appTargets) blockedIdentities.Add(id);
                        blockedGroupNames.Add(group.Name);
                    }
                    else
                    {
                        timedGroupsToMaybeAccrue.Add(group);
                        // HUD shows the countdown only while this group's app is
                        // focused (macOS shows only the frontmost app's timer).
                        if (GroupTargetsForeground(group, fg))
                        {
                            timerItems.Add(new TimerDisplayItem(group.Id, group.Name, remainingSeconds));
                        }
                    }
                    break;
            }
        }

        // Union in identities a custom rule asked to block (rule shield/blockApp
        // decisions from the previous tick's async dispatch).
        if (ruleBlockedIdentities != null)
        {
            foreach (var id in ruleBlockedIdentities)
            {
                if (!string.IsNullOrWhiteSpace(id))
                {
                    blockedIdentities.Add(id);
                }
            }
        }

        _registry.Update(blockedIdentities);

        // Single enumeration pass: close any currently-open blocked windows (the
        // tick backstop). Blocking applies to every window of a blocked app,
        // regardless of focus.
        var closed = 0;
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (hwnd == _selfWindow || !IsCloseableTopLevel(hwnd))
            {
                return true;
            }
            var identity = ProcessIdentity.ForWindow(hwnd);
            if (!identity.IsEmpty && _registry.IsBlocked(identity))
            {
                WindowCloser.CloseWindow(hwnd);
                closed++;
            }
            return true;
        }, IntPtr.Zero);

        // Accrue time for timed groups whose target app is currently FOCUSED. For
        // a clustered group (linked over the web-app bridge) the cluster owns ONE
        // shared budget: we report this tick's local increment to the hub and
        // fold the authoritative shared total back into the local timer, so the
        // Windows countdown + enforcement reflect time spent on every linked
        // member (e.g. browser website time) — exactly like the macOS app.
        foreach (var group in timedGroupsToMaybeAccrue)
        {
            var focused = GroupTargetsForeground(group, fg);

            var current = timers.TimersMs.GetValueOrDefault(group.Id, 0);
            var addedMs = 0.0;
            if (focused)
            {
                addedMs = _tickSeconds * 1000.0;
                current += addedMs;
                usageUpdates[group.Id] = current;
            }

            // The gate keeps non-bridge groups entirely local: SharedUsage is null
            // unless this group is actually in a cluster involving this app.
            if (_hub != null && _hub.SharedUsage(group.Name) != null)
            {
                if (_clusterSeeded.Contains(group.Id))
                {
                    // resetAtMs:0 — the browser is the reset authority; our local
                    // window anchor isn't comparable, so we never drive rollover.
                    _hub.ReportLocalUsage(group.Name, addedMs, 0);
                }
                else
                {
                    // First report since joining: seed the shared budget with our
                    // current local total (no delta) so prior usage is preserved.
                    _hub.ReportLocalUsage(group.Name, 0, 0, current);
                    _clusterSeeded.Add(group.Id);
                }

                var shared = _hub.SharedUsage(group.Name);
                if (shared.HasValue)
                {
                    var total = Math.Max(0, shared.Value.Ms);
                    if (Math.Abs(current - total) > 0.5)
                    {
                        usageUpdates[group.Id] = total;
                    }
                    if (shared.Value.ResetAtMs > 0
                        && Math.Abs(timers.ResetAtMs.GetValueOrDefault(group.Id, -1) - shared.Value.ResetAtMs) > 0.5)
                    {
                        resetUpdates[group.Id] = shared.Value.ResetAtMs;
                    }
                }
            }
            else
            {
                // Not clustered: forget any seed flag so a future re-link re-seeds.
                _clusterSeeded.Remove(group.Id);
            }
        }
        if (usageUpdates.Count > 0 || resetUpdates.Count > 0)
        {
            _store.WriteUsage(usageUpdates, resetUpdates);
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

    /// Snapshot the identities of every currently-open closeable top-level
    /// window, de-duplicated. Used by the rule engine for the `allApps` event
    /// payload and for open/close lifecycle diffing — reusing the exact same
    /// identity resolution that drives blocking so rule ids and block ids agree.
    public List<AppIdentity> SnapshotRunningIdentities()
    {
        var result = new List<AppIdentity>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (hwnd == _selfWindow || !IsCloseableTopLevel(hwnd))
            {
                return true;
            }
            var identity = ProcessIdentity.ForWindow(hwnd);
            if (!identity.IsEmpty && seen.Add(identity.Canonical))
            {
                result.Add(identity);
            }
            return true;
        }, IntPtr.Zero);
        return result;
    }

    /// Close every open top-level window whose owning app matches one of the
    /// given target identities (rule close()/shield on the current tick). One-shot
    /// — does not add to the persistent registry.
    public int CloseMatching(IEnumerable<string> identities)
    {
        var targets = identities
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim().ToLowerInvariant())
            .ToList();
        if (targets.Count == 0)
        {
            return 0;
        }
        var closed = 0;
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (hwnd == _selfWindow || !IsCloseableTopLevel(hwnd))
            {
                return true;
            }
            var identity = ProcessIdentity.ForWindow(hwnd);
            if (identity.IsEmpty)
            {
                return true;
            }
            if (targets.Any(t => TargetMatchesIdentity(t, identity)))
            {
                WindowCloser.CloseWindow(hwnd);
                closed++;
            }
            return true;
        }, IntPtr.Zero);
        return closed;
    }

    // Reset-interval rollover, ported from MacEnforcementBridge.reconcileUsage.
    // Ensures each enabled timed group has a reset anchor and zeroes its used
    // time once a full reset interval has elapsed (advancing the anchor by whole
    // intervals so it stays phase-aligned). Mutates `timers` in place and records
    // the changes for persistence.
    private static void ReconcileResets(
        List<BlockGroup> groups,
        WebStore.UsageTimers timers,
        DateTimeOffset now,
        Dictionary<string, double> usageUpdates,
        Dictionary<string, double> resetUpdates)
    {
        double nowMs = now.ToUnixTimeMilliseconds();
        foreach (var group in groups)
        {
            if (!group.Enabled)
            {
                continue;
            }
            if (group.Mode != BlockingMode.Timer && group.Mode != BlockingMode.AfterMinutes)
            {
                continue;
            }
            var gid = group.Id;

            if (!timers.ResetAtMs.TryGetValue(gid, out var anchor) || anchor <= 0)
            {
                anchor = nowMs;
                timers.ResetAtMs[gid] = anchor;
                resetUpdates[gid] = anchor;
            }

            var intervalMs = Math.Max(0, group.ResetIntervalHours) * 3_600_000.0;
            if (intervalMs <= 0)
            {
                continue;
            }
            var sinceReset = nowMs - anchor;
            if (sinceReset >= intervalMs)
            {
                var elapsedIntervals = Math.Floor(sinceReset / intervalMs);
                var newStart = anchor + elapsedIntervals * intervalMs;
                timers.TimersMs[gid] = 0;
                timers.ResetAtMs[gid] = newStart;
                usageUpdates[gid] = 0;
                resetUpdates[gid] = newStart;
            }
        }
    }

    // True when any of the group's application targets is the focused identity —
    // the Windows analog of macOS's `targets.contains { $0.id == frontmost }`.
    internal static bool GroupTargetsForeground(BlockGroup group, AppIdentity foreground)
    {
        if (foreground.IsEmpty)
        {
            return false;
        }
        return group.Targets
            .Where(t => t.Kind == BlockTarget.TargetKind.Application)
            .Any(t => TargetMatchesIdentity(t.NormalizedValue, foreground));
    }

    // Matches one app target value (AUMID, full path, or bare name) against a
    // single resolved window identity. Mirrors BlockedAppRegistry's shapes; the
    // identity's path/name/AUMID are already lowercased.
    internal static bool TargetMatchesIdentity(string normalizedValue, AppIdentity identity)
    {
        var value = normalizedValue.Trim().ToLowerInvariant();
        if (value.Length == 0)
        {
            return false;
        }
        if (value.Contains('!'))
        {
            return !string.IsNullOrEmpty(identity.Aumid) && value == identity.Aumid;
        }
        if (value.Contains('\\') || value.Contains('/'))
        {
            return (!string.IsNullOrEmpty(identity.ExecutablePath) && value == identity.ExecutablePath)
                || (!string.IsNullOrEmpty(identity.ExecutableName) && Path.GetFileName(value) == identity.ExecutableName);
        }
        var exe = value.EndsWith(".exe") ? value : value + ".exe";
        return !string.IsNullOrEmpty(identity.ExecutableName) && exe == identity.ExecutableName;
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
