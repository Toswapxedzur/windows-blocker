using System.Text.Json.Nodes;
using WindowsBlocker.Bridge;

namespace WindowsBlocker.WebUI;

/// Resolves historical application icons by their stored Windows identity.
/// Browser and creator icons remain the shared Activity store's own values.
public static class ActivityNativeIcons
{
    public static void Enrich(JsonObject reply,Func<string,string?> resolve)
    {
        var keys=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Visit(JsonNode? node)
        {
            if(node is JsonObject item)
            {
                foreach(var (name,value) in item)
                {
                    if((name is "key" or "id") && value is JsonValue text && text.TryGetValue<string>(out var id))
                    {
                        if(id.StartsWith("app|",StringComparison.Ordinal))id=id[4..];
                        if(WindowsAppId.Normalize(id)!=null)keys.Add(id);
                    }
                    Visit(value);
                }
            }
            else if(node is JsonArray array)foreach(var value in array)
            {
                if(value is JsonValue text && text.TryGetValue<string>(out var id) && id.StartsWith("app|",StringComparison.Ordinal) && WindowsAppId.Normalize(id[4..])!=null)keys.Add(id[4..]);
                Visit(value);
            }
        }
        Visit(reply["snapshot"]);Visit(reply["contentSnapshot"]);Visit(reply["items"]);
        var icons=reply["icons"] as JsonObject;
        if(icons==null){icons=new();reply["icons"]=icons;}
        foreach(var key in keys)
        {
            if(icons[key] is JsonValue current && current.TryGetValue<string>(out var existing) && !string.IsNullOrEmpty(existing))continue;
            var icon=resolve(key);if(icon!=null)icons[key]=icon;
        }
    }
}
