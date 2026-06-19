using System;
using System.Collections.Generic;

namespace WindowsBlocker.Core;

// Ported from MacBlockerCore/PolicyDecision.swift.
public enum PolicyAction
{
    Allow,
    Shield,
    Unshield,
    ShowStatus,
    RequestSnooze,
    Log,
    Quarantine
}

public sealed class OverlayStatus
{
    public string Title { get; init; } = "";
    public string Message { get; init; } = "";
    public string? TimerGroupId { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
}

public sealed class PolicyDecision
{
    public PolicyAction Action { get; init; }
    public string? GroupId { get; init; }
    public HashSet<string> TargetIds { get; init; } = new();
    public string Reason { get; init; } = "";
    public string ShieldMessage { get; init; } = "";
    public OverlayStatus? OverlayStatus { get; init; }
    public Dictionary<string, string> Metadata { get; init; } = new();
}

public sealed class TimerDisplayItem
{
    public string GroupId { get; init; } = "";
    public string Name { get; init; } = "";
    public double RemainingSeconds { get; init; }

    public TimerDisplayItem(string groupId, string name, double remainingSeconds)
    {
        GroupId = groupId;
        Name = name;
        RemainingSeconds = Math.Max(0, remainingSeconds);
    }
}

public sealed class EvaluationResult
{
    public List<PolicyDecision> Decisions { get; init; } = new();
    public List<TimerDisplayItem> VisibleTimerItems { get; init; } = new();
}
