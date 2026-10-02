using System.Text.Json;

namespace WindowsBlocker.Core;

public sealed class ChromeExtensionImportResult
{
    public List<BlockGroup> Groups { get; init; } = new();
}

// Desktop policy reads only the current Apps scope. Browser scopes remain with
// their browser; a stored old shape is safely ignored rather than widened.
public static class ChromeExtensionImporter
{
    public static ChromeExtensionImportResult ImportGroups(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var source = root.ValueKind == JsonValueKind.Array ? root
            : root.ValueKind == JsonValueKind.Object && root.TryGetProperty("blockedGroups", out var groups) && groups.ValueKind == JsonValueKind.Array ? groups
            : throw new FormatException("Unsupported store shape");
        return new() { Groups = source.EnumerateArray().Where(g => g.ValueKind == JsonValueKind.Object).Select(ImportGroup).ToList() };
    }

    private static BlockGroup ImportGroup(JsonElement obj)
    {
        var appLine = Array(obj, "scopes").FirstOrDefault(s => Str(s, "surface") == "apps");
        var apps = Array(appLine, "apps").Where(a => Str(a, "id")?.Trim().Length > 0).Select(a => new BlockTarget
        {
            Id = Str(a, "id")!.Trim(), Kind = BlockTarget.TargetKind.Application,
            NormalizedValue = Str(a, "id")!.Trim(), DisplayName = Str(a, "name") ?? Str(a, "id")!, Tags = new() { "windows", "application" }
        }).ToList();
        var days = obj.TryGetProperty("activeDays", out var d) && d.ValueKind == JsonValueKind.Array
            ? d.EnumerateArray().Select(e => Weekdays.Parse(e.ValueKind == JsonValueKind.String ? e.GetString()! : "")).Where(e => e.HasValue).Select(e => e!.Value).ToHashSet()
            : new(Weekdays.All);
        return new()
        {
            Id = Str(obj, "id") ?? Guid.NewGuid().ToString(), Name = Str(obj, "name") ?? "Block Group",
            GroupType = Str(obj, "groupType") == "custom" ? BlockGroupType.Custom : BlockGroupType.Site,
            Enabled = Bool(obj, "enabled") && Str(obj, "effect") != "allow",
            Mode = Str(obj, "mode") is "after-minutes" or "timer" ? BlockingMode.AfterMinutes : BlockingMode.Instant,
            AllowedMinutes = Positive(obj, "allowedMinutes", 15), ResetIntervalHours = Positive(obj, "resetIntervalHours", 24),
            ResetAtMidnight = Bool(obj, "resetAtMidnight"), RollingLimit = Bool(obj, "rollingLimit"), ActiveDays = days,
            TimeWindows = ScheduleParser.ParseWindows(Str(obj, "timeWindowsText") ?? ""),
            CustomRuleSource = Str(obj, "activeEventSource") ?? "", Targets = apps,
            ApplicationAllowlist = Bool(appLine, "appsExcept")
        };
    }

    internal static string? Str(JsonElement obj, string key) => obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    internal static bool Bool(JsonElement obj, string key) => obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.True;
    internal static IEnumerable<JsonElement> Array(JsonElement obj, string key) => obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.Array ? v.EnumerateArray() : Enumerable.Empty<JsonElement>();
    private static double Positive(JsonElement obj, string key, double fallback) => obj.TryGetProperty(key, out var v) && v.TryGetDouble(out var n) && double.IsFinite(n) && n > 0 ? n : fallback;
}
