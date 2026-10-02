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
    public bool Budget { get; init; }
    public double ExtraMs { get; init; }
    public bool Exempts(DateTimeOffset now) => !Budget && Phase(now) == SnoozePhase.Active;
    public double Extra(DateTimeOffset now) => Budget && Phase(now) == SnoozePhase.Active ? Math.Max(0,ExtraMs) : 0;
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
