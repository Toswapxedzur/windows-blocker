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
    Console.WriteLine(new JsonObject { ["id"]=request["id"]!.DeepClone(),["ok"]=true,["value"]=request["data"]!.DeepClone() }.ToJsonString());
}
