using System;
using System.Collections.Generic;
using System.Linq;

namespace WindowsBlocker.Core;

// Ported from MacBlockerCore/UsageBudget.swift (same rules as the extension).
//
// - Fixed budget: usage resets every ResetIntervalHours, counted from the
//   group's anchor. With ResetAtMidnight, the periods restart at local 00:00
//   each day instead (00:00, then every N hours; the last period of the day
//   ends early at midnight).
// - Rolling limit: usage is kept per minute and counts until it is N hours
//   old; with ResetAtMidnight the window never reaches before today's 00:00.
public static class UsageBudget
{
    public const double BucketMs = 60_000;

    public static double IntervalMs(BlockGroup group) => Math.Max(0, group.ResetIntervalHours) * 3_600_000.0;

    public static double StartOfDayMs(double nowMs)
    {
        var local = DateTimeOffset.FromUnixTimeMilliseconds((long)nowMs).ToLocalTime().DateTime.Date;
        return new DateTimeOffset(local).ToUnixTimeMilliseconds();
    }

    public static double NextMidnightMs(double nowMs)
    {
        var local = DateTimeOffset.FromUnixTimeMilliseconds((long)nowMs).ToLocalTime().DateTime.Date.AddDays(1);
        return new DateTimeOffset(local).ToUnixTimeMilliseconds();
    }

    /// The start of the budget period containing nowMs. anchorMs is the stored
    /// period start; it only matters when midnight re-anchoring is off.
    public static double PeriodStartMs(double anchorMs, BlockGroup group, double nowMs)
    {
        var interval = IntervalMs(group);
        if (group.ResetAtMidnight)
        {
            var dayStart = StartOfDayMs(nowMs);
            if (interval <= 0) return dayStart;
            return dayStart + Math.Floor((nowMs - dayStart) / interval) * interval;
        }
        if (interval <= 0 || nowMs - anchorMs < interval) return anchorMs;
        return anchorMs + Math.Floor((nowMs - anchorMs) / interval) * interval;
    }

    /// When the current fixed budget next resets, or null when it never does.
    public static double? NextResetMs(double periodStartMs, BlockGroup group, double nowMs)
    {
        var interval = IntervalMs(group);
        if (group.ResetAtMidnight)
        {
            var midnight = NextMidnightMs(nowMs);
            return interval > 0 ? Math.Min(periodStartMs + interval, midnight) : midnight;
        }
        return interval > 0 ? periodStartMs + interval : null;
    }

    public static double BucketStartMs(double nowMs) => Math.Floor(nowMs / BucketMs) * BucketMs;

    /// Earliest instant still inside the rolling window.
    public static double WindowStartMs(BlockGroup group, double nowMs)
    {
        var start = nowMs - IntervalMs(group);
        if (group.ResetAtMidnight) start = Math.Max(start, StartOfDayMs(nowMs));
        return start;
    }

    /// Minute buckets still inside the window. A bucket counts until its whole
    /// minute has aged out.
    public static Dictionary<double, double> PruneBuckets(IReadOnlyDictionary<double, double> buckets, BlockGroup group, double nowMs)
    {
        var start = WindowStartMs(group, nowMs);
        return buckets.Where(b => b.Key + BucketMs > start && b.Value > 0).ToDictionary(b => b.Key, b => b.Value);
    }

    public static double UsedMs(IReadOnlyDictionary<double, double> buckets) => buckets.Values.Sum();

    /// When the oldest counted minute leaves the window (or midnight clears it),
    /// i.e. when rolling time starts coming back. Null with no usage.
    public static double? NextReturnMs(IReadOnlyDictionary<double, double> buckets, BlockGroup group, double nowMs)
    {
        if (buckets.Count == 0) return null;
        var next = buckets.Keys.Min() + BucketMs + IntervalMs(group);
        if (group.ResetAtMidnight) next = Math.Min(next, NextMidnightMs(nowMs));
        return next;
    }

    public static bool SameBuckets(IReadOnlyDictionary<double, double> a, IReadOnlyDictionary<double, double> b) =>
        a.Count == b.Count && a.All(entry => b.TryGetValue(entry.Key, out var v) && v == entry.Value);
}
