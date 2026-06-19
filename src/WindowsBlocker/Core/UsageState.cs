using System;
using System.Collections.Generic;

namespace WindowsBlocker.Core;

// Ported from MacBlockerCore/UsageState.swift.
public enum SnoozePhase
{
    None,
    Pending,
    Active,
    Cooldown
}

public sealed class SnoozeState
{
    public DateTimeOffset? StartsAt { get; init; }
    public DateTimeOffset? Until { get; init; }
    public DateTimeOffset? CooldownUntil { get; init; }
    public string Justification { get; init; } = "";

    public SnoozePhase Phase(DateTimeOffset date)
    {
        if (StartsAt is { } s && date < s)
        {
            return SnoozePhase.Pending;
        }
        if (Until is { } u && date < u)
        {
            return SnoozePhase.Active;
        }
        if (CooldownUntil is { } c && date < c)
        {
            return SnoozePhase.Cooldown;
        }
        return SnoozePhase.None;
    }
}

public sealed class UsageSnapshot
{
    public Dictionary<string, double> UsageByGroupSeconds { get; init; } = new();
    public Dictionary<string, DateTimeOffset> ResetAtByGroup { get; init; } = new();
    public Dictionary<string, SnoozeState> SnoozesByGroup { get; init; } = new();
    public Dictionary<string, double> TotalSnoozedSecondsByGroup { get; init; } = new();
}

public enum RuntimePlatform
{
    IOS,
    IPadOS,
    MacOS,
    Windows
}

public sealed class ActivityContext
{
    public DateTimeOffset Now { get; init; } = DateTimeOffset.Now;
    public BlockTarget? Target { get; init; }
    public HashSet<string> ActiveTargetIds { get; init; } = new();
    public Dictionary<string, double> UsageByTargetSeconds { get; init; } = new();
    public RuntimePlatform Platform { get; init; } = RuntimePlatform.Windows;
}
