using System;
using System.Collections.Generic;

namespace WindowsBlocker.Core;

// Ported from MacBlockerCore/BlockGroup.swift. The Windows port runs the same
// custom JavaScript rule engine as macOS (see Rules/RuleEngine), so the `Custom`
// group type and CustomRuleSource are first-class, not just round-tripped.
public enum BlockGroupType
{
    Site,
    YouTube,
    TikTok,
    Facebook,
    Instagram,
    Twitch,
    Reddit,
    Discord,
    Twitter,
    Custom,
    App,
    Category
}

public enum BlockingMode
{
    Instant,
    AfterMinutes,
    Timer
}

public static class BlockingModeExtensions
{
    public static bool IsTimed(this BlockingMode mode) =>
        mode == BlockingMode.AfterMinutes || mode == BlockingMode.Timer;
}

public enum FreezeMode
{
    None,
    Normal,
    Strict,
    Parental
}

public sealed class BlockTarget
{
    public enum TargetKind
    {
        Application,
        Category,
        WebDomain,
        UrlPattern,
        LegacyPlatform
    }

    public string Id { get; init; } = Guid.NewGuid().ToString();
    public TargetKind Kind { get; init; }
    public string DisplayName { get; init; } = "";

    // On macOS this held a bundle identifier; on Windows it holds the
    // executable path (lowercased) or process name supplied by the app picker.
    public string NormalizedValue { get; init; } = "";
    public HashSet<string> Tags { get; init; } = new();
}

public sealed class BlockGroup
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public BlockGroupType GroupType { get; init; } = BlockGroupType.Site;
    public string Name { get; init; } = "Block Group";
    public bool Enabled { get; init; } = true;
    public BlockingMode Mode { get; init; } = BlockingMode.Instant;
    public int AllowedMinutes { get; init; } = 15;
    public int ResetIntervalHours { get; init; } = 24;
    public bool AllowSnooze { get; init; } = true;
    public int SnoozeMinutes { get; init; } = 30;
    public int SnoozeActivationDelayMinutes { get; init; }
    public int SnoozeCooldownMinutes { get; init; }
    public int SnoozeConfirmations { get; init; }
    public HashSet<Weekday> ActiveDays { get; init; } = new(Weekdays.All);
    public List<TimeWindow> TimeWindows { get; init; } = new();
    public FreezeMode FreezeMode { get; init; } = FreezeMode.None;
    public int StrictFreezeHours { get; init; } = 24;
    public DateTimeOffset? FrozenAt { get; init; }
    public string? ParentalPasswordHash { get; init; }
    public string? ParentalPasswordSalt { get; init; }
    public string FallbackMessage { get; init; } = "";

    // The user's custom JavaScript rule source. Executed by Rules/RuleEngine via
    // the WebView2-hosted MacBlockerRuntime, exactly as macOS runs it in JSCore.
    public string CustomRuleSource { get; init; } = "";
    public List<BlockTarget> Targets { get; init; } = new();
    public List<string> UnsupportedLegacyFeatures { get; init; } = new();
}
