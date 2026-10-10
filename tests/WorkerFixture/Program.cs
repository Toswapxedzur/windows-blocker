using System.Text;
using System.Text.Json.Nodes;
Console.InputEncoding=new UTF8Encoding(false);Console.OutputEncoding=new UTF8Encoding(false);
while(await Console.In.ReadLineAsync() is { } line)
{
    var request=JsonNode.Parse(line)!;
    var operation=request["operation"]!.GetValue<string>();
    if(operation=="background-error")Console.WriteLine(new JsonObject{["event"]="error",["error"]="worker-response-too-large",["sourceEvent"]="activity"}.ToJsonString());
    if(operation=="crash") { Console.WriteLine(new JsonObject { ["event"]="hold",["pid"]=Environment.ProcessId }.ToJsonString());Console.Out.Flush();Environment.Exit(7); }
    if(operation=="delay") await Task.Delay(500);
    if(operation=="activity" && request["data"]?["kind"]?.GetValue<string>()=="mcp")
    {
        var tool=request["data"]!["tool"]!.GetValue<string>();
        var failed=request["data"]?["arguments"]?["id"]?.GetValue<string>()=="missing";
        Console.WriteLine(new JsonObject { ["id"]=request["id"]!.DeepClone(),["ok"]=!failed,
            [failed ? "error" : "value"]=failed ? JsonValue.Create("Activity group not found") : tool=="delete_activity_group" ? JsonValue.Create("Deleted.") : new JsonObject { ["id"]="fixture" } }.ToJsonString());
        continue;
    }
    Console.WriteLine(new JsonObject { ["id"]=request["id"]!.DeepClone(),["ok"]=true,["value"]=request["data"]!.DeepClone() }.ToJsonString());
}
