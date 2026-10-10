using System.Text.Json.Nodes;

namespace WindowsBlocker.Bridge;

/// Routes the advertised Activity tools to the same store used by the page.
public static class ActivityMcpRequest
{
    public static bool Handles(string name) => name is "list_activity_groups" or "save_activity_group" or "delete_activity_group";

    public static JsonObject Create(string name, JsonObject arguments)
    {
        if (!Handles(name)) throw new InvalidOperationException("unknown-tool");
        return new JsonObject { ["kind"] = "mcp", ["tool"] = name, ["arguments"] = arguments.DeepClone() };
    }
}
