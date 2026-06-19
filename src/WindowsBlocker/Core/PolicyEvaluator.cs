using System;
using System.Collections.Generic;
using System.Linq;

namespace WindowsBlocker.Core;

// Ported from MacBlockerCore/PolicyEvaluator.swift. Pure logic, no platform
// dependencies — identical evaluation semantics to the macOS/iOS engine.
public sealed class PolicyEvaluator
{
    public EvaluationResult Evaluate(IReadOnlyList<BlockGroup> groups, UsageSnapshot usage, ActivityContext context)
    {
        var decisions = new List<PolicyDecision>();
        var timerItems = new List<TimerDisplayItem>();

        // Reversed so earlier groups take precedence (mirrors the Swift loop).
        for (var i = groups.Count - 1; i >= 0; i--)
        {
            var group = groups[i];
            if (!group.IsActive(context.Now))
            {
                continue;
            }
            if (IsSnoozed(group.Id, usage, context.Now))
            {
                continue;
            }

            var matchingTargets = MatchingTargetIds(group, context);
            if (matchingTargets.Count == 0 && group.GroupType != BlockGroupType.Custom)
            {
                continue;
            }

            switch (group.Mode)
            {
                case BlockingMode.Instant:
                    decisions.Add(ShieldDecision(group, matchingTargets));
                    break;
                case BlockingMode.AfterMinutes:
                case BlockingMode.Timer:
                    var usedSeconds = usage.UsageByGroupSeconds.GetValueOrDefault(group.Id, 0);
                    var allowedSeconds = Math.Max(0, group.AllowedMinutes) * 60.0;
                    var remaining = Math.Max(0, allowedSeconds - usedSeconds);
                    timerItems.Add(new TimerDisplayItem(group.Id, group.Name, remaining));
                    decisions.Add(remaining <= 0
                        ? ShieldDecision(group, matchingTargets)
                        : StatusDecision(group, matchingTargets, remaining));
                    break;
            }
        }

        return new EvaluationResult { Decisions = decisions, VisibleTimerItems = timerItems };
    }

    private static bool IsSnoozed(string groupId, UsageSnapshot usage, DateTimeOffset date) =>
        usage.SnoozesByGroup.TryGetValue(groupId, out var snooze) && snooze.Phase(date) == SnoozePhase.Active;

    private static HashSet<string> MatchingTargetIds(BlockGroup group, ActivityContext context)
    {
        var candidates = group.Targets.Select(t => t.Id).ToHashSet();
        if (candidates.Count == 0)
        {
            return new HashSet<string>();
        }
        if (context.Target is { } target && candidates.Contains(target.Id))
        {
            return new HashSet<string> { target.Id };
        }
        var activeMatches = candidates;
        activeMatches.IntersectWith(context.ActiveTargetIds);
        return activeMatches;
    }

    private static PolicyDecision ShieldDecision(BlockGroup group, HashSet<string> targetIds)
    {
        var message = string.IsNullOrEmpty(group.FallbackMessage) ? $"{group.Name} is blocked." : group.FallbackMessage;
        return new PolicyDecision
        {
            Action = PolicyAction.Shield,
            GroupId = group.Id,
            TargetIds = targetIds,
            Reason = message,
            ShieldMessage = message,
            OverlayStatus = new OverlayStatus { Title = group.Name, Message = message, TimerGroupId = group.Id }
        };
    }

    private static PolicyDecision StatusDecision(BlockGroup group, HashSet<string> targetIds, double remainingSeconds)
    {
        var message = $"{group.Name}: {(int)Math.Ceiling(remainingSeconds / 60)} minutes remaining.";
        return new PolicyDecision
        {
            Action = PolicyAction.ShowStatus,
            GroupId = group.Id,
            TargetIds = targetIds,
            Reason = message,
            OverlayStatus = new OverlayStatus { Title = group.Name, Message = message, TimerGroupId = group.Id }
        };
    }
}
