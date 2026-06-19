using System;
using System.Collections.Generic;
using System.IO;
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

    /// Persists the raw store JSON string pushed from the editor bridge.
    public void SaveRaw(string rawJson)
    {
        // Validate it parses before writing so we never persist garbage.
        try
        {
            using var _ = JsonDocument.Parse(rawJson);
        }
        catch
        {
            return;
        }
        lock (_gate)
        {
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, rawJson);
            File.Move(tmp, FilePath, overwrite: true);
        }
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
            ResetAtMs = DoubleMap(root["usageResetAtMs"])
        };
    }

    /// Merges the given per-group usage entries into the stored snapshot,
    /// preserving every other key. No-op when both maps are empty.
    public void WriteUsage(Dictionary<string, double> timersMs, Dictionary<string, double> resetAtMs)
    {
        if (timersMs.Count == 0 && resetAtMs.Count == 0)
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
        var json = LoadRawJson();
        if (json is null)
        {
            return null;
        }
        try
        {
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
                    result[k] = v.GetValue<double>();
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
        var ms = node.GetValue<double>();
        return ms > 0 ? DateTimeOffset.FromUnixTimeMilliseconds((long)ms) : null;
    }
}
