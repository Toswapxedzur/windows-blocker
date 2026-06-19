using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace WindowsBlocker.Core;

// Ported from MacBlockerCore/ChromeExtensionImport.swift. Reads the editor's
// raw chrome.storage snapshot (the same `blockedGroups` schema the Chrome
// extension used) and maps it into the typed BlockGroup model.
public sealed class ChromeExtensionImportResult
{
    public List<BlockGroup> Groups { get; init; } = new();
    public List<string> Warnings { get; init; } = new();
}

public static class ChromeExtensionImporter
{
    public static ChromeExtensionImportResult ImportGroups(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        JsonElement source;
        if (root.ValueKind == JsonValueKind.Array)
        {
            source = root;
        }
        else if (root.ValueKind == JsonValueKind.Object &&
                 root.TryGetProperty("blockedGroups", out var arr) &&
                 arr.ValueKind == JsonValueKind.Array)
        {
            source = arr;
        }
        else
        {
            throw new FormatException("Unsupported store shape");
        }

        var warnings = new List<string>();
        var groups = new List<BlockGroup>();
        foreach (var element in source.EnumerateArray())
        {
            groups.Add(ImportGroup(element, warnings));
        }
        return new ChromeExtensionImportResult { Groups = groups, Warnings = warnings };
    }

    private static BlockGroup ImportGroup(JsonElement obj, List<string> warnings)
    {
        var groupType = ParseGroupType(Str(obj, "groupType") ?? "site");
        var id = Str(obj, "id") ?? Guid.NewGuid().ToString();
        var scheduleText = Str(obj, "timeWindowsText") ?? "";

        var sites = (Array(obj, "sites") ?? Enumerable.Empty<JsonElement>())
            .Select(e => NormalizeHost(AsString(e)))
            .Where(h => h is not null)
            .Select(h => new BlockTarget
            {
                Kind = BlockTarget.TargetKind.WebDomain,
                DisplayName = h!,
                NormalizedValue = h!,
                Tags = LegacyTags(groupType)
            })
            .ToList();

        // Each app entry is { id: <identity>, name: <displayName> }. On Windows
        // the identity is the executable path/name supplied by the app picker.
        var apps = (ArrayOfObjects(obj, "apps") ?? Enumerable.Empty<JsonElement>())
            .Select(entry =>
            {
                var identity = Str(entry, "id")?.Trim();
                if (string.IsNullOrEmpty(identity))
                {
                    return null;
                }
                var name = Str(entry, "name")?.Trim();
                return new BlockTarget
                {
                    Id = identity!,
                    Kind = BlockTarget.TargetKind.Application,
                    DisplayName = !string.IsNullOrEmpty(name) ? name! : identity!,
                    NormalizedValue = identity!,
                    Tags = new HashSet<string> { "windows", "application" }
                };
            })
            .Where(t => t is not null)
            .Select(t => t!)
            .ToList();

        var unsupported = new List<string>();
        if (groupType != BlockGroupType.Site && groupType != BlockGroupType.Custom)
        {
            unsupported.Add("Platform DOM/feed controls are imported as target metadata only.");
        }
        if (Bool(obj, "skipToNextOnBlock") == true)
        {
            unsupported.Add("skipToNextOnBlock is not available natively.");
        }
        var fallbackUrl = Str(obj, "fallbackUrl");
        if (!string.IsNullOrEmpty(fallbackUrl))
        {
            unsupported.Add("fallbackUrl is replaced by shield/status messaging.");
        }
        var label = Str(obj, "name") ?? id;
        warnings.AddRange(unsupported.Select(u => $"{label}: {u}"));

        var targets = new List<BlockTarget>();
        targets.AddRange(sites);
        targets.AddRange(apps);

        return new BlockGroup
        {
            Id = id,
            GroupType = MapGroupType(groupType),
            Name = Str(obj, "name") ?? DefaultName(groupType),
            Enabled = Bool(obj, "enabled") ?? true,
            Mode = ParseMode(Str(obj, "mode")),
            AllowedMinutes = Int(obj, "allowedMinutes") ?? 15,
            ResetIntervalHours = Int(obj, "resetIntervalHours") ?? 24,
            AllowSnooze = Bool(obj, "allowSnooze") ?? true,
            SnoozeMinutes = Int(obj, "snoozeMinutes") ?? 30,
            SnoozeActivationDelayMinutes = Int(obj, "snoozeActivationDelayMinutes") ?? 0,
            SnoozeCooldownMinutes = Int(obj, "snoozeCooldownMinutes") ?? 0,
            SnoozeConfirmations = Int(obj, "snoozeConfirmations") ?? 0,
            ActiveDays = ParseDays(obj),
            TimeWindows = ScheduleParser.ParseWindows(scheduleText),
            FreezeMode = MapFreezeMode(Str(obj, "freezeMode")),
            StrictFreezeHours = Int(obj, "strictFreezeHours") ?? 24,
            FrozenAt = DateFromMs(obj, "frozenAtMs"),
            ParentalPasswordHash = Str(obj, "parentalPasswordHash"),
            ParentalPasswordSalt = Str(obj, "parentalPasswordSalt"),
            FallbackMessage = "",
            CustomRuleSource = Str(obj, "blockingRulesText") ?? "",
            Targets = targets,
            UnsupportedLegacyFeatures = unsupported
        };
    }

    private static BlockGroupType ParseGroupType(string raw) => raw.ToLowerInvariant() switch
    {
        "site" => BlockGroupType.Site,
        "youtube" => BlockGroupType.YouTube,
        "tiktok" => BlockGroupType.TikTok,
        "facebook" => BlockGroupType.Facebook,
        "instagram" => BlockGroupType.Instagram,
        "twitch" => BlockGroupType.Twitch,
        "reddit" => BlockGroupType.Reddit,
        "discord" => BlockGroupType.Discord,
        "twitter" => BlockGroupType.Twitter,
        "custom" => BlockGroupType.Custom,
        "app" => BlockGroupType.App,
        "category" => BlockGroupType.Category,
        _ => BlockGroupType.Site
    };

    private static BlockingMode ParseMode(string? raw) => raw switch
    {
        "instant" => BlockingMode.Instant,
        "after-minutes" => BlockingMode.AfterMinutes,
        "timer" => BlockingMode.Timer,
        _ => BlockingMode.Instant
    };

    private static FreezeMode MapFreezeMode(string? raw) => raw switch
    {
        "frozen" or "normal" => FreezeMode.Normal,
        "strict" => FreezeMode.Strict,
        "parental" => FreezeMode.Parental,
        _ => FreezeMode.None
    };

    private static BlockGroupType MapGroupType(BlockGroupType groupType) => groupType switch
    {
        BlockGroupType.YouTube or BlockGroupType.TikTok or BlockGroupType.Facebook or
        BlockGroupType.Instagram or BlockGroupType.Twitch or BlockGroupType.Reddit or
        BlockGroupType.Discord or BlockGroupType.Twitter => BlockGroupType.App,
        _ => groupType
    };

    private static HashSet<string> LegacyTags(BlockGroupType groupType) => groupType switch
    {
        BlockGroupType.YouTube or BlockGroupType.TikTok or BlockGroupType.Facebook or BlockGroupType.Instagram
            => new HashSet<string> { "social", "shortVideo", groupType.ToString().ToLowerInvariant() },
        BlockGroupType.Twitch => new HashSet<string> { "video", "streaming", "twitch" },
        BlockGroupType.Reddit or BlockGroupType.Discord or BlockGroupType.Twitter
            => new HashSet<string> { "social", groupType.ToString().ToLowerInvariant() },
        _ => new HashSet<string>()
    };

    private static HashSet<Weekday> ParseDays(JsonElement obj)
    {
        var values = Array(obj, "activeDays");
        if (values is null)
        {
            return new HashSet<Weekday>(Weekdays.All);
        }
        var days = values
            .Select(e => Weekdays.Parse(AsString(e) ?? ""))
            .Where(d => d is not null)
            .Select(d => d!.Value)
            .ToHashSet();
        return days.Count == 0 ? new HashSet<Weekday>(Weekdays.All) : days;
    }

    private static string? NormalizeHost(string? value)
    {
        value = value?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }
        if (!value.Contains("://"))
        {
            value = "https://" + value;
        }
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host))
        {
            return null;
        }
        var host = uri.Host.ToLowerInvariant();
        return host.StartsWith("www.") ? host.Substring(4) : host;
    }

    private static string DefaultName(BlockGroupType groupType) =>
        groupType == BlockGroupType.Custom ? "Custom Block" : "Block Group";

    // ----- JSON helpers -----

    private static string? Str(JsonElement obj, string key) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static string? AsString(JsonElement e) => e.ValueKind == JsonValueKind.String ? e.GetString() : null;

    private static bool? Bool(JsonElement obj, string key)
    {
        if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(key, out var v))
        {
            return null;
        }
        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static int? Int(JsonElement obj, string key)
    {
        if (obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(key, out var v) &&
            v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d))
        {
            return (int)d;
        }
        return null;
    }

    private static DateTimeOffset? DateFromMs(JsonElement obj, string key)
    {
        if (obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(key, out var v) &&
            v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var ms) && ms > 0)
        {
            return DateTimeOffset.FromUnixTimeMilliseconds((long)ms);
        }
        return null;
    }

    private static IEnumerable<JsonElement>? Array(JsonElement obj, string key) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray()
            : null;

    private static IEnumerable<JsonElement>? ArrayOfObjects(JsonElement obj, string key) => Array(obj, key);
}
