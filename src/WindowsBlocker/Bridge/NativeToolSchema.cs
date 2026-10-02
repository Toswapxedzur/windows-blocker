using System.Text.Json;
using System.Text.Json.Nodes;

namespace WindowsBlocker.Bridge;

public static class NativeToolSchema
{
    public static void Validate(JsonArray catalog,string name,JsonObject arguments)
    {
        var tool=catalog.OfType<JsonObject>().FirstOrDefault(t=>t["name"]?.GetValue<string>()==name) ?? throw new InvalidOperationException("unknown-tool");
        Check(tool["inputSchema"] as JsonObject ?? new(),arguments,"arguments");
    }
    private static void Check(JsonObject schema,JsonNode? value,string path)
    {
        var type=schema["type"]?.GetValue<string>();
        var valid=type switch { "string"=>value?.GetValueKind()==JsonValueKind.String,"boolean"=>value?.GetValueKind() is JsonValueKind.True or JsonValueKind.False,"object"=>value is JsonObject,"array"=>value is JsonArray,"number"=>value?.GetValueKind()==JsonValueKind.Number,"integer"=>value?.GetValueKind()==JsonValueKind.Number && double.TryParse(value.ToJsonString(),out var n) && double.IsFinite(n) && n==Math.Truncate(n),_=>true };
        if(!valid) throw new InvalidOperationException("invalid-argument:"+path);
        if(value is JsonObject obj)
        {
            foreach(var key in (schema["required"] as JsonArray ?? new()).Select(k=>k!.GetValue<string>())) if(!obj.ContainsKey(key)) throw new InvalidOperationException("missing-argument:"+key);
            if(schema["properties"] is JsonObject properties) foreach(var (key,child) in properties) if(obj.ContainsKey(key) && child is JsonObject childSchema) Check(childSchema,obj[key],path+"."+key);
        }
        if(value is JsonArray array && schema["items"] is JsonObject itemSchema) for(var i=0;i<array.Count;i++) Check(itemSchema,array[i],path+"["+i+"]");
    }
}
