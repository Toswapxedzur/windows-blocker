using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WindowsBlocker.Rules;

// C# port of the data contract in MacBlockerCore/CustomJavaScriptPolicyRuntime.swift.
// These mirror the JSON shapes that the verbatim custom-rule-runtime.js
// (MacBlockerRuntime) produces from load()/dispatch(), and the event shape it
// consumes. Field names match the JS object keys exactly (camelCase), so the
// same runtime that backs macOS drives the Windows port unchanged.

/// An event handed to MacBlockerRuntime.dispatch(...). Mirrors CustomRuleEvent.
public sealed class CustomRuleEvent
{
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("groupID")] public string GroupId { get; set; } = "";
    [JsonPropertyName("target")] public RuleTarget? Target { get; set; }
    [JsonPropertyName("now")] public string Now { get; set; } = "";
    [JsonPropertyName("url")] public string Url { get; set; } = "";
    [JsonPropertyName("hostname")] public string Hostname { get; set; } = "";
    [JsonPropertyName("data")] public Dictionary<string, string> Data { get; set; } = new();
}

/// The block target a rule sees as `ev.target` (reads `.id`, `.tags`, `.displayName`).
public sealed class RuleTarget
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("kind")] public string Kind { get; set; } = "application";
    [JsonPropertyName("displayName")] public string DisplayName { get; set; } = "";
    [JsonPropertyName("value")] public string Value { get; set; } = "";
    [JsonPropertyName("tags")] public List<string> Tags { get; set; } = new();
}

public sealed class RuleDecision
{
    [JsonPropertyName("action")] public string Action { get; set; } = "";
    [JsonPropertyName("groupID")] public string GroupId { get; set; } = "";
    [JsonPropertyName("targetIDs")] public List<string> TargetIds { get; set; } = new();
    [JsonPropertyName("reason")] public string Reason { get; set; } = "";
    [JsonPropertyName("shieldMessage")] public string ShieldMessage { get; set; } = "";
    [JsonPropertyName("metadata")] public Dictionary<string, JsonElement> Metadata { get; set; } = new();

    public string MetaString(string key, string fallback = "")
        => Metadata.TryGetValue(key, out var v) ? RuleJson.AsString(v) : fallback;
}

public sealed class RuleIntent
{
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("action")] public string Action { get; set; } = "";
    [JsonPropertyName("target")] public string? Target { get; set; }
    [JsonPropertyName("pattern")] public string? Pattern { get; set; }
    [JsonPropertyName("path")] public string? Path { get; set; }
    [JsonPropertyName("text")] public string? Text { get; set; }
    [JsonPropertyName("groupId")] public string? GroupId { get; set; }
    [JsonPropertyName("requestId")] public string? RequestId { get; set; }
    [JsonPropertyName("browserBundleID")] public string? BrowserBundleId { get; set; }
    [JsonPropertyName("windowIndex")] public int? WindowIndex { get; set; }
    [JsonPropertyName("tabIndex")] public int? TabIndex { get; set; }
}

public sealed class CustomTimer
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("groupId")] public string GroupId { get; set; } = "";
    [JsonPropertyName("displayName")] public string DisplayName { get; set; } = "";
    [JsonPropertyName("direction")] public string Direction { get; set; } = "";
    [JsonPropertyName("currentMs")] public double CurrentMs { get; set; }
    [JsonPropertyName("isPaused")] public bool IsPaused { get; set; }
}

public sealed class PanelOption
{
    [JsonPropertyName("value")] public string Value { get; set; } = "";
    [JsonPropertyName("label")] public string Label { get; set; } = "";
}

public sealed class PanelTheme
{
    [JsonPropertyName("background")] public string? Background { get; set; }
    [JsonPropertyName("foreground")] public string? Foreground { get; set; }
    [JsonPropertyName("accent")] public string? Accent { get; set; }
    [JsonPropertyName("border")] public string? Border { get; set; }
    [JsonPropertyName("muted")] public string? Muted { get; set; }
    [JsonPropertyName("fontSize")] public string? FontSize { get; set; }
    [JsonPropertyName("titleSize")] public string? TitleSize { get; set; }
}

public sealed class PanelControl
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("label")] public string? Label { get; set; }
    [JsonPropertyName("text")] public string? Text { get; set; }
    [JsonPropertyName("value")] public JsonElement? Value { get; set; }
    [JsonPropertyName("disabled")] public bool? Disabled { get; set; }
    [JsonPropertyName("placeholder")] public string? Placeholder { get; set; }
    [JsonPropertyName("options")] public List<PanelOption>? Options { get; set; }
    [JsonPropertyName("min")] public double? Min { get; set; }
    [JsonPropertyName("max")] public double? Max { get; set; }
    [JsonPropertyName("step")] public double? Step { get; set; }
    [JsonPropertyName("action")] public string? Action { get; set; }
    [JsonPropertyName("timerId")] public string? TimerId { get; set; }
    [JsonPropertyName("timer")] public CustomTimer? Timer { get; set; }
    [JsonPropertyName("format")] public string? Format { get; set; }
    [JsonPropertyName("showExpired")] public bool? ShowExpired { get; set; }
    [JsonPropertyName("controls")] public List<PanelControl>? Controls { get; set; }
    [JsonPropertyName("layout")] public string? Layout { get; set; }
    [JsonPropertyName("align")] public string? Align { get; set; }
    [JsonPropertyName("priority")] public int? Priority { get; set; }
    [JsonPropertyName("role")] public string? Role { get; set; }
    [JsonPropertyName("autoFocus")] public bool? AutoFocus { get; set; }
    [JsonPropertyName("rows")] public int? Rows { get; set; }
    [JsonPropertyName("width")] public string? Width { get; set; }
    [JsonPropertyName("height")] public string? Height { get; set; }
    [JsonPropertyName("length")] public int? Length { get; set; }
    [JsonPropertyName("masked")] public bool? Masked { get; set; }
    [JsonPropertyName("autoSubmit")] public bool? AutoSubmit { get; set; }

    public string ValueString => Value.HasValue ? RuleJson.AsString(Value.Value) : "";
    public double ValueDouble => Value.HasValue ? RuleJson.AsDouble(Value.Value) : 0;
    public bool ValueBool => Value.HasValue && RuleJson.AsBool(Value.Value);
}

public sealed class PanelSnapshot
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("groupId")] public string? GroupId { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("position")] public string? Position { get; set; }
    [JsonPropertyName("align")] public string? Align { get; set; }
    [JsonPropertyName("layout")] public string? Layout { get; set; }
    [JsonPropertyName("priority")] public int? Priority { get; set; }
    [JsonPropertyName("width")] public string? Width { get; set; }
    [JsonPropertyName("textSize")] public string? TextSize { get; set; }
    [JsonPropertyName("role")] public string? Role { get; set; }
    [JsonPropertyName("autoFocus")] public bool? AutoFocus { get; set; }
    [JsonPropertyName("theme")] public PanelTheme? Theme { get; set; }
    [JsonPropertyName("controls")] public List<PanelControl>? Controls { get; set; }
    [JsonPropertyName("visible")] public bool? Visible { get; set; }
}

public sealed class DispatchResult
{
    [JsonPropertyName("decisions")] public List<RuleDecision> Decisions { get; set; } = new();
    [JsonPropertyName("intents")] public List<RuleIntent> Intents { get; set; } = new();
    [JsonPropertyName("timers")] public List<CustomTimer> Timers { get; set; } = new();
    [JsonPropertyName("panels")] public List<PanelSnapshot> Panels { get; set; } = new();
}

public sealed class LoadResult
{
    [JsonPropertyName("handlers")] public int Handlers { get; set; }
    [JsonPropertyName("decisions")] public List<RuleDecision> Decisions { get; set; } = new();
}

/// One line of the rule log forwarded to the editor via __cbApplyNativeRuleLog.
public sealed class RuleLogEntry
{
    [JsonPropertyName("timestamp")] public string Timestamp { get; set; } = "";
    [JsonPropertyName("level")] public string Level { get; set; } = "log";
    [JsonPropertyName("group")] public string Group { get; set; } = "";
    [JsonPropertyName("message")] public string Message { get; set; } = "";
}

/// JsonElement coercion helpers, matching AnyCodableValue's tolerant accessors.
internal static class RuleJson
{
    public static string AsString(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString() ?? "",
        JsonValueKind.Number => e.GetDouble().ToString(CultureInfo.InvariantCulture),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => ""
    };

    public static double AsDouble(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Number => e.GetDouble(),
        JsonValueKind.True => 1,
        JsonValueKind.False => 0,
        JsonValueKind.String => double.TryParse(e.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0,
        _ => 0
    };

    public static bool AsBool(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Number => e.GetDouble() != 0,
        JsonValueKind.String => e.GetString() == "true",
        _ => false
    };
}
