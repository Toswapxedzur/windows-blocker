using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WindowsBlocker.Core;

// Format versions describe local bytes. Product majors bound migrations.
public static class StorageSchema
{
    public const int WebSchema = 3;
    public static string AppVersion => (typeof(StorageSchema).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? typeof(StorageSchema).Assembly.GetName().Version?.ToString() ?? "0.2.0").Split('+')[0];
    private static int Major(string? version) => int.TryParse(version?.Split('.')[0], out var major) && major >= 0 ? major : -1;
    public static JsonObject Metadata(string format, int schema) => new() { ["format"] = format, ["schemaVersion"] = schema, ["product"] = "windows", ["writtenByAppVersion"] = AppVersion };
    public static void Validate(JsonObject? metadata, string format, int currentSchema)
    {
        if (metadata is null)
        {
            // Compatible alpha intake belongs to Windows app-major 0 and 1.
            if (Major(AppVersion) > 1) throw new InvalidDataException("Alpha storage import has expired; saved data is preserved.");
            return;
        }
        var schema = metadata["schemaVersion"]?.GetValue<int>() ?? -1;
        if (metadata["format"]?.GetValue<string>() != format || schema < 1 || schema > currentSchema || string.IsNullOrEmpty(metadata["product"]?.GetValue<string>()) || Major(metadata["writtenByAppVersion"]?.GetValue<string>()) < 0)
            throw new InvalidDataException("Unsupported storage schema; saved data is preserved.");
        if (schema < currentSchema && metadata["product"]?.GetValue<string>() == "windows" && Major(metadata["writtenByAppVersion"]?.GetValue<string>()) < Math.Max(0, Major(AppVersion) - 1))
            throw new InvalidDataException("App-major storage migration has expired; saved data is preserved.");
    }
    public static void ValidateWeb(JsonObject root)
    {
        var schema = root["schemaVersion"]?.GetValue<int>() ?? 0;
        if (schema < 0 || schema > WebSchema) throw new InvalidDataException("Unsupported web storage schema; saved data is preserved.");
        if (root.ContainsKey("storageMetadata") && root["storageMetadata"] is not JsonObject) throw new InvalidDataException("Invalid storage metadata.");
        var metadata = root["storageMetadata"] as JsonObject;
        if (metadata != null && metadata["schemaVersion"]?.GetValue<int>() != schema) throw new InvalidDataException("Inconsistent storage schema metadata.");
        Validate(metadata, "vault.web-store", WebSchema);
    }
    public static void StampWeb(JsonObject root)
    {
        ValidateWeb(root);
        root["schemaVersion"] = WebSchema;
        root["storageMetadata"] = Metadata("vault.web-store", WebSchema);
    }
    public static JsonNode Payload(JsonNode root, string format)
    {
        if (root is JsonObject obj && obj.ContainsKey("storageMetadata"))
        {
            if (obj["storageMetadata"] is not JsonObject metadata) throw new InvalidDataException("Invalid storage metadata.");
            Validate(metadata, format, 1);
            return obj["value"]?.DeepClone() ?? throw new InvalidDataException("Missing storage payload.");
        }
        Validate(null, format, 1);
        return root;
    }
    public static JsonObject Wrap(JsonNode value, string format) => new() { ["storageMetadata"] = Metadata(format, 1), ["value"] = value.DeepClone() };
    public static void AtomicWrite(string path, string json)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, json);
        if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
    }
}
