using System;
using System.Collections.Generic;
using System.Linq;

namespace WindowsBlocker.Core;

// Ported from MacBlockerCore/Schedule.swift.
public enum Weekday
{
    Monday,
    Tuesday,
    Wednesday,
    Thursday,
    Friday,
    Saturday,
    Sunday
}

public static class Weekdays
{
    public static readonly Weekday[] All =
    {
        Weekday.Monday, Weekday.Tuesday, Weekday.Wednesday, Weekday.Thursday,
        Weekday.Friday, Weekday.Saturday, Weekday.Sunday
    };

    public static Weekday From(DateTimeOffset date) => date.DayOfWeek switch
    {
        DayOfWeek.Sunday => Weekday.Sunday,
        DayOfWeek.Monday => Weekday.Monday,
        DayOfWeek.Tuesday => Weekday.Tuesday,
        DayOfWeek.Wednesday => Weekday.Wednesday,
        DayOfWeek.Thursday => Weekday.Thursday,
        DayOfWeek.Friday => Weekday.Friday,
        _ => Weekday.Saturday
    };

    public static Weekday? Parse(string raw) => raw.Trim().ToLowerInvariant() switch
    {
        "monday" => Weekday.Monday,
        "tuesday" => Weekday.Tuesday,
        "wednesday" => Weekday.Wednesday,
        "thursday" => Weekday.Thursday,
        "friday" => Weekday.Friday,
        "saturday" => Weekday.Saturday,
        "sunday" => Weekday.Sunday,
        _ => null
    };
}

public readonly struct TimeOfDay : IComparable<TimeOfDay>
{
    public int Hour { get; }
    public int Minute { get; }

    public TimeOfDay(int hour, int minute)
    {
        Hour = Math.Max(0, Math.Min(23, hour));
        Minute = Math.Max(0, Math.Min(59, minute));
    }

    public int MinutesSinceMidnight => Hour * 60 + Minute;

    public int CompareTo(TimeOfDay other) => MinutesSinceMidnight.CompareTo(other.MinutesSinceMidnight);
}

public readonly struct TimeWindow
{
    public TimeOfDay Start { get; }
    public TimeOfDay End { get; }

    public TimeWindow(TimeOfDay start, TimeOfDay end)
    {
        Start = start;
        End = end;
    }

    public bool Contains(DateTimeOffset date)
    {
        var current = new TimeOfDay(date.Hour, date.Minute).MinutesSinceMidnight;
        return current >= Start.MinutesSinceMidnight && current < End.MinutesSinceMidnight;
    }
}

public static class ScheduleParser
{
    public static List<TimeWindow> ParseWindows(string text)
    {
        var result = new List<TimeWindow>();
        foreach (var line in text.Split('\n', '\r'))
        {
            var window = ParseWindow(line);
            if (window is not null)
            {
                result.Add(window.Value);
            }
        }
        return result;
    }

    public static TimeWindow? ParseWindow(string text)
    {
        var cleaned = text.Trim().Replace(":", "");
        var parts = cleaned.Split('-');
        if (parts.Length != 2)
        {
            return null;
        }
        var start = ParseTime(parts[0]);
        var end = ParseTime(parts[1]);
        if (start is null || end is null || start.Value.CompareTo(end.Value) >= 0)
        {
            return null;
        }
        return new TimeWindow(start.Value, end.Value);
    }

    public static TimeOfDay? ParseTime(string text)
    {
        var value = text.Trim();
        if (value.Length != 4 ||
            !int.TryParse(value.Substring(0, 2), out var hour) ||
            !int.TryParse(value.Substring(2, 2), out var minute) ||
            hour is < 0 or > 23 ||
            minute is < 0 or > 59)
        {
            return null;
        }
        return new TimeOfDay(hour, minute);
    }
}

public static class BlockGroupScheduling
{
    public static bool IsActive(this BlockGroup group, DateTimeOffset date)
    {
        if (group.GroupType == BlockGroupType.Custom)
        {
            return group.Enabled;
        }
        if (!group.Enabled)
        {
            return false;
        }
        if (!group.ActiveDays.Contains(Weekdays.From(date)))
        {
            return false;
        }
        if (group.TimeWindows.Count == 0)
        {
            return true;
        }
        return group.TimeWindows.Any(w => w.Contains(date));
    }
}
