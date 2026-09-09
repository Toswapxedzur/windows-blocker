using System;

namespace WindowsBlocker.Core;

// Ported from MacBlockerCore/PolicyDecision.swift (only the timer row model survives).
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
