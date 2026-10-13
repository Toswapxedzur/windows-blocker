using System.Text;
using System.Text.Json.Nodes;
Console.InputEncoding=new UTF8Encoding(false);Console.OutputEncoding=new UTF8Encoding(false);
string? evidencePath=null;
var remembered=new List<string>();
var flushHang=false;
var hangOnEof=false;
var exitCode=0;
while(await Console.In.ReadLineAsync() is { } line)
{
    var request=JsonNode.Parse(line)!;
    var operation=request["operation"]!.GetValue<string>();
    if(operation=="shutdown-proof")
    {
        evidencePath=request["data"]?["path"]?.GetValue<string>();
        flushHang=request["data"]?["flushHang"]?.GetValue<bool>()==true;
        hangOnEof=request["data"]?["hangOnEof"]?.GetValue<bool>()==true;
        exitCode=request["data"]?["exitCode"]?.GetValue<int>()??0;
    }
    if(operation=="remember") remembered.Add(request["data"]!["text"]!.GetValue<string>());
    if(flushHang && ((operation=="activity" && request["data"]?["kind"]?.GetValue<string>()=="native-flush") || operation=="hostEvent"))
    {
        if(evidencePath!=null)File.WriteAllText(evidencePath,new JsonObject{["flushAttempted"]=true,["pid"]=Environment.ProcessId}.ToJsonString());
        await Task.Delay(Timeout.Infinite);
    }
    if(operation=="background-error")Console.WriteLine(new JsonObject{["event"]="error",["error"]="worker-response-too-large",["sourceEvent"]="activity"}.ToJsonString());
    if(operation=="capability-ready")
    {
        var ready=new JsonObject{["event"]="ready"};
        if(request["data"]!.AsObject().ContainsKey("flag"))ready["activitySettingsNotifications"]=request["data"]!["flag"]!.DeepClone();
        Console.WriteLine(ready.ToJsonString());
    }
    if(operation=="exit-normal-held")Console.WriteLine(new JsonObject{["event"]="hold-natural-exit",["pid"]=Environment.ProcessId}.ToJsonString());
    if(operation=="settings-commit-events")
    {
        foreach(var body in request["data"]!["commits"]!.AsArray())
            Console.WriteLine(new JsonObject { ["event"]="activity-settings-committed", ["value"]=body!.DeepClone() }.ToJsonString());
        Console.WriteLine(new JsonObject { ["id"]=request["id"]!.DeepClone(),["ok"]=true,["value"]=new JsonObject{["pid"]=Environment.ProcessId} }.ToJsonString());
        if(request["data"]?["exitAfterCommits"]?.GetValue<bool>()==true)break;
        continue;
    }
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
    if(operation is "exit-normal" or "exit-normal-held")break;
}

if(hangOnEof)await Task.Delay(10000);
if(evidencePath!=null)File.WriteAllText(evidencePath,new JsonObject{["eof"]=true,["pid"]=Environment.ProcessId,["remembered"]=new JsonArray(remembered.Select(x=>(JsonNode?)JsonValue.Create(x)).ToArray())}.ToJsonString());
Environment.ExitCode=exitCode;
