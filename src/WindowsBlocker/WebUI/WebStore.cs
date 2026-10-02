using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using WindowsBlocker.Core;

namespace WindowsBlocker.WebUI;

// Ported from MacBlockerWebUI/BlockerWebStore.swift. Persists the editor's raw
// chrome.storage snapshot (the same blockedGroups / globalSettings / usage keys
// the Chrome extension used) to web-store.json so the native policy core can
// read it back via ChromeExtensionImporter.
public sealed class WebStore
{
    private readonly object _gate = new();
    public string FilePath => Storage.WebStorePath;

    public void SeedIfNeeded()
    {
        if (File.Exists(FilePath))
        {
            return;
        }
        SaveRaw("{\"blockedGroups\":[]}");
    }

    public string? LoadRawJson()
    {
        lock (_gate)
        {
            return File.Exists(FilePath) ? File.ReadAllText(FilePath) : null;
        }
    }

    public double QuitRetryMinutes => Math.Clamp(Number(LoadObject()?["globalSettings"]?["quitRetryMinutes"]), 0, 1440);
    private static double Number(JsonNode? v) => v?.GetValueKind() == JsonValueKind.Number && double.TryParse(v.ToJsonString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? value : 0;

    public void SaveRaw(string rawJson)
    {
        if (JsonNode.Parse(rawJson) is not JsonObject root) return;
        lock (_gate) Write(root);
    }

    // Patch only the keys a writer changed. Native usage and another group's
    // state survive editor autosaves; null has chrome.storage.remove semantics.
    public void Merge(JsonObject changes)
    {
        lock (_gate)
        {
            var root = LoadObject() ?? new JsonObject();
            foreach (var (key, value) in changes)
            {
                if (value is null) root.Remove(key);
                else if (PerGroupKeys.Contains(key) && value is JsonObject entries)
                {
                    var map = root[key] as JsonObject ?? new();
                    root[key] = map;
                    foreach (var (id, entry) in entries) map[id] = entry?.DeepClone();
                }
                else root[key] = value.DeepClone();
            }
            Write(root);
        }
    }
    public void Update(Action<JsonObject> mutation)
    {
        lock (_gate) { var root = LoadObject() ?? new JsonObject(); var before = root.ToJsonString(); mutation(root); if (root.ToJsonString() != before) Write(root); }
    }
    private static readonly HashSet<string> PerGroupKeys = new() { "usageTimersMs", "usageResetAtMs", "usageBucketsMs", "groupSnoozes", "groupSnoozeTotalsMs", "cbRuleLog", "cbRuleQuarantine", "cbRuleState" };
    private void Write(JsonObject root)
    {
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, root.ToJsonString());
        File.Move(tmp, FilePath, overwrite: true);
    }

    public ChromeExtensionImportResult? ImportedGroups()
    {
        var json = LoadRawJson();
        if (json is null)
        {
            return null;
        }
        try
        {
            return ChromeExtensionImporter.ImportGroups(json);
        }
        catch
        {
            return null;
        }
    }

    // ----- Usage timers (mirror the extension's usageTimersMs / usageResetAtMs) -----

    public sealed class UsageTimers
    {
        public Dictionary<string, double> TimersMs { get; init; } = new();
        public Dictionary<string, double> ResetAtMs { get; init; } = new();
        // Rolling-limit groups: minute-start ms -> ms used in that minute.
        public Dictionary<string, Dictionary<double, double>> BucketsMs { get; init; } = new();
    }

    public UsageTimers LoadUsageTimers()
    {
        var root = LoadObject();
        if (root is null)
        {
            return new UsageTimers();
        }
        return new UsageTimers
        {
            TimersMs = DoubleMap(root["usageTimersMs"]),
            ResetAtMs = DoubleMap(root["usageResetAtMs"]),
            BucketsMs = BucketMaps(root["usageBucketsMs"])
        };
    }

    private static Dictionary<string, Dictionary<double, double>> BucketMaps(JsonNode? node)
    {
        var result = new Dictionary<string, Dictionary<double, double>>();
        if (node is not JsonObject groups) return result;
        foreach (var (groupId, value) in groups)
        {
            var buckets = new Dictionary<double, double>();
            foreach (var (minute, ms) in DoubleMap(value))
            {
                if (double.TryParse(minute, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var start) && ms > 0)
                {
                    buckets[start] = ms;
                }
            }
            result[groupId] = buckets;
        }
        return result;
    }

    public static Dictionary<string, double> BucketJson(IReadOnlyDictionary<double, double> buckets) =>
        buckets.ToDictionary(b => ((long)b.Key).ToString(System.Globalization.CultureInfo.InvariantCulture), b => b.Value);

    /// Merges the given per-group usage entries into the stored snapshot,
    /// preserving every other key. No-op when both maps are empty.
    public void WriteUsage(
        Dictionary<string, double> timersMs,
        Dictionary<string, double> resetAtMs,
        Dictionary<string, Dictionary<double, double>>? bucketsMs = null,
        Dictionary<string,double>? snoozeGivenMs = null)
    {
        bucketsMs ??= new(); snoozeGivenMs ??= new();
        if (timersMs.Count == 0 && resetAtMs.Count == 0 && bucketsMs.Count == 0 && snoozeGivenMs.Count == 0)
        {
            return;
        }
        lock (_gate)
        {
            var root = LoadObject();
            if (root is null)
            {
                return;
            }
            if (timersMs.Count > 0)
            {
                root["usageTimersMs"] = MergeInto(root["usageTimersMs"], timersMs);
            }
            if (resetAtMs.Count > 0)
            {
                root["usageResetAtMs"] = MergeInto(root["usageResetAtMs"], resetAtMs);
            }
            if (bucketsMs.Count > 0)
            {
                var all = root["usageBucketsMs"] as JsonObject ?? new JsonObject();
                root["usageBucketsMs"] = all;
                foreach (var (groupId, buckets) in bucketsMs)
                {
                    var obj = new JsonObject();
                    foreach (var (minute, ms) in BucketJson(buckets)) obj[minute] = ms;
                    all[groupId] = obj;
                }
            }
            if(snoozeGivenMs.Count>0) { var totals=root["groupSnoozeTotalsMs"] as JsonObject ?? new(); root["groupSnoozeTotalsMs"]=totals; foreach(var (id,delta) in snoozeGivenMs) totals[id]=Number(totals[id])+Math.Max(0,delta); }
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, root.ToJsonString());
            File.Move(tmp, FilePath, overwrite: true);
        }
    }

    public Dictionary<string, SnoozeState> LoadSnoozes()
    {
        var result = new Dictionary<string, SnoozeState>();
        var root = LoadObject();
        if (root?["groupSnoozes"] is not JsonObject snoozes)
        {
            return result;
        }
        foreach (var (groupId, value) in snoozes)
        {
            if (value is not JsonObject d)
            {
                continue;
            }
            result[groupId] = new SnoozeState
            {
                Budget = StringValue(d["kind"]) == "budget", ExtraMs = Number(d["extraMs"]),
                StartsAt = MsToDate(d["startsAtMs"]),
                Until = MsToDate(d["untilMs"]),
                CooldownUntil = MsToDate(d["cooldownUntilMs"]),
                Justification = StringValue(d["justification"])
            };
        }
        return result;
    }

    // Reads globalSettings.connection.serverEnabled so the hub can auto-start on
    // launch if the user previously enabled it (same behavior as the macOS app).
    public bool LoadConnectionServerEnabled()
    {
        var root = LoadObject();
        if (root?["globalSettings"] is not JsonObject settings)
        {
            return false;
        }
        if (settings["connection"] is not JsonObject connection)
        {
            return false;
        }
        return connection["serverEnabled"]?.GetValueKind() == JsonValueKind.True;
    }

    private JsonObject? LoadObject()
    {
        try
        {
            var json = LoadRawJson();
            if(json==null) return null;
            return JsonNode.Parse(json) as JsonObject;
        }
        catch
        {
            return null;
        }
    }

    private static JsonObject MergeInto(JsonNode? existing, Dictionary<string, double> updates)
    {
        var obj = existing as JsonObject ?? new JsonObject();
        var merged = new JsonObject();
        foreach (var (k, v) in obj)
        {
            merged[k] = v?.DeepClone();
        }
        foreach (var (k, v) in updates)
        {
            merged[k] = v;
        }
        return merged;
    }

    private static Dictionary<string, double> DoubleMap(JsonNode? node)
    {
        var result = new Dictionary<string, double>();
        if (node is JsonObject obj)
        {
            foreach (var (k, v) in obj)
            {
                if (v is not null && v.GetValueKind() == JsonValueKind.Number)
                {
                    result[k] = Math.Max(0,Number(v));
                }
            }
        }
        return result;
    }

    private static string StringValue(JsonNode? node) =>
        node is not null && node.GetValueKind() == JsonValueKind.String ? node.GetValue<string>() : "";

    private static DateTimeOffset? MsToDate(JsonNode? node)
    {
        if (node is null || node.GetValueKind() != JsonValueKind.Number)
        {
            return null;
        }
        var ms = Number(node);
        return ms > 0 && ms <= DateTimeOffset.MaxValue.ToUnixTimeMilliseconds() ? DateTimeOffset.FromUnixTimeMilliseconds((long)ms) : null;
    }
}
