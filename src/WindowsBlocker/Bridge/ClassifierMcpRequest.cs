using System.Text.Json.Nodes;

namespace WindowsBlocker.Bridge;

/// Maps the public native tools to the shared worker's existing MCP protocol.
/// UI actions and tool actions consequently use the same Swift validation.
public static class ClassifierMcpRequest
{
    public static JsonObject Create(string name,JsonObject arguments) => name switch
    {
        "classifier_state" => new JsonObject { ["section"] = arguments["section"]?.DeepClone() },
        "classifier_actions" => new JsonObject { ["kind"] = "actions" },
        "classifier_action" => new JsonObject
        {
            ["kind"] = "action",
            ["action"] = arguments["action"]?.DeepClone(),
            ["data"] = arguments["data"]?.DeepClone() ?? new JsonObject()
        },
        _ => throw new InvalidOperationException("unknown-tool")
    };
}
