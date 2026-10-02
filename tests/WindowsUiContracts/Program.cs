using System.Net.Http.Headers;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows.Automation;
using WindowsBlocker.Bridge;
using WindowsBlocker.WebUI;

Environment.SetEnvironmentVariable("VAULT_ENVIRONMENT","development");
Environment.SetEnvironmentVariable("VAULT_STORAGE_ROOT",args[0]);
var resultFile=args[1]; var checks=new List<string>();
using var http=new HttpClient {Timeout=TimeSpan.FromSeconds(12)};

long id=0;
async Task<JsonNode?> Rpc(string method,JsonObject? parameters=null)
{
    var request=new JsonObject{["jsonrpc"]="2.0",["id"]=++id,["method"]=method,["params"]=parameters??new()};
    using var response=await http.PostAsync($"http://127.0.0.1:{Storage.McpPort}/mcp",new StringContent(request.ToJsonString(),Encoding.UTF8,"application/json"));response.EnsureSuccessStatusCode();
    return JsonNode.Parse(await response.Content.ReadAsStringAsync())?["result"];
}
async Task<JsonNode?> Tool(string name,JsonObject arguments,bool expectedError=false)
{
    var result=await Rpc("tools/call",new(){["name"]=name,["arguments"]=arguments});
    if(result?["isError"]?.GetValue<bool>()!=expectedError)throw new Exception($"Unexpected tool result for {name}: {result}");
    return JsonNode.Parse(result!["content"]![0]!["text"]!.GetValue<string>());
}
void Check(bool ok,string what){if(!ok)throw new Exception(what);checks.Add(what);Console.WriteLine("PASS "+what);}
JsonNode? ReadStore(){using var file=new FileStream(Storage.WebStorePath,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);return JsonNode.Parse(file);}
AutomationElement? NativeWindow(int processId,string automationId) => AutomationElement.RootElement.FindFirst(TreeScope.Descendants,new AndCondition(new PropertyCondition(AutomationElement.ProcessIdProperty,processId),new PropertyCondition(AutomationElement.AutomationIdProperty,automationId)));
async Task<JsonNode?> Proxy(string method)
{
    var start=new ProcessStartInfo(args[2]) { UseShellExecute=false,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true,StandardInputEncoding=new UTF8Encoding(false) };
    start.ArgumentList.Add("--mcp-proxy");start.ArgumentList.Add("development");
    using var process=Process.Start(start)!;
    try
    {
        await process.StandardInput.WriteLineAsync(new JsonObject { ["jsonrpc"]="2.0",["id"]="live-proxy",["method"]=method,["params"]=new JsonObject { ["protocolVersion"]="2025-06-18" } }.ToJsonString());await process.StandardInput.FlushAsync();
        var line=await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
        var response=JsonNode.Parse(line??"null");if(response?["id"]?.GetValue<string>()!="live-proxy" || response?["error"]!=null)throw new Exception("Native MCP helper rejected live request: "+line);
        process.StandardInput.Close();await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3));return response?["result"];
    }
    finally { if(!process.HasExited)process.Kill(true); }
}
try
{
    http.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",LocalHubAuthentication.McpBearerToken());
    Check(new FileInfo(Storage.HubSecretPath).GetAccessControl().AreAccessRulesProtected,"Normal user creates a protected hub secret ACL");
    await Rpc("initialize",new(){["protocolVersion"]="2025-06-18"});
    var tools=(await Rpc("tools/list"))?["tools"] as JsonArray;
    Check(tools?.Count==38,"38 canonical MCP tools discovered");
    JsonNode? settled=null;var settleDeadline=DateTimeOffset.UtcNow.AddSeconds(5);
    do {settled=ReadStore();if(settled?["groupSnoozes"]?["budget-used"]?["activeMsApplied"]?.GetValue<bool>()==true)break;await Task.Delay(100);}while(DateTimeOffset.UtcNow<settleDeadline);
    Check(settled?["groupSnoozeTotalsMs"]?["time-finished"]?.GetValue<int>()==2017,"Finished time snooze contributes its actual two seconds once");
    Check(settled?["groupSnoozes"]?["budget-used"]?["activeMsApplied"]?.GetValue<bool>()==true && settled?["groupSnoozeTotalsMs"]?["budget-used"]?.GetValue<int>()==45000,"Exhausted budget snooze settles without counting elapsed clock time");
    Check(settled?["groupSnoozes"]?["budget-unspent"]?["activeMsApplied"]?.GetValue<bool>()==false && settled?["groupSnoozeTotalsMs"]?["budget-unspent"]?.GetValue<int>()==1000,"Idle budget snooze preserves unspent room and usage-based total");
    Check((await Proxy("initialize"))?["serverInfo"]?["name"]?.GetValue<string>()=="Windows Vault","Bundled native MCP helper initializes against the running GUI endpoint");
    Check(((await Proxy("tools/list"))?["tools"] as JsonArray)?.Count==38,"Bundled native MCP helper lists the GUI's 38 canonical tools");
    using(var app=Process.GetProcessById(int.Parse(args[3])))
    {
        var menu=AutomationElement.FromHandle(app.MainWindowHandle).FindFirst(TreeScope.Descendants,new PropertyCondition(AutomationElement.AutomationIdProperty,"McpConnectionsMenu"));
        Check(menu!=null,"Native AI connections menu is reachable through accessibility");
        var opening=Task.Run(()=>((InvokePattern)menu!.GetCurrentPattern(InvokePattern.Pattern)).Invoke());
        AutomationElement? dialog=null;var dialogDeadline=DateTimeOffset.UtcNow.AddSeconds(8);
        do {if(opening.IsFaulted)await opening;dialog=NativeWindow(app.Id,"McpConnectionsWindow");if(dialog!=null)break;await Task.Delay(100);}while(DateTimeOffset.UtcNow<dialogDeadline);
        if(dialog==null) foreach(AutomationElement own in AutomationElement.RootElement.FindAll(TreeScope.Children,new PropertyCondition(AutomationElement.ProcessIdProperty,app.Id))) Console.WriteLine("Own window: "+own.Current.Name+" / "+own.Current.AutomationId);
        Check(dialog!=null,"Native AI connections menu opens the connections dialog");
        ((WindowPattern)dialog!.GetCurrentPattern(WindowPattern.Pattern)).Close();await opening.WaitAsync(TimeSpan.FromSeconds(3));
    }
    using(var anonymous=new HttpClient()){var denied=await anonymous.PostAsync($"http://127.0.0.1:{Storage.McpPort}/mcp",new StringContent("{}"));Check((int)denied.StatusCode==401,"Unauthenticated MCP caller rejected");}
    var created=await Tool("create_group",new(){["groupType"]="site",["patch"]=new JsonObject{["name"]="Windows UI contract"}});
    var groupId=created?["id"]?.GetValue<string>()??created?["group"]?["id"]?.GetValue<string>()??throw new Exception("create_group missing id");
    Check(groupId.Length>0,"Group created through canonical policy runtime");
    var cross=await Tool("set_group",new(){["id"]=groupId,["patch"]=new JsonObject{["scopes"]=new JsonArray(new JsonObject{["surface"]="site",["sites"]=new JsonArray("example.com"),["action"]="block"})}},true);
    Check(cross?["error"]?.GetValue<string>()=="browser-lines","Desktop tool refuses browser scope edits");
    var summary=await Tool("list_groups",new());
    Check((summary?["groups"] as JsonArray)?.OfType<JsonObject>().First(g=>g["id"]?.GetValue<string>()==groupId).Count==6,"list_groups returns canonical summary fields only");
    var missing=await Tool("get_group",new(),true);Check(missing?["error"]?.GetValue<string>()=="missing-argument:id","Missing required arguments refused before dispatch");
    Check((await Tool("set_group",new(){["id"]=groupId,["patch"]=new JsonObject()},true))?["error"]?.GetValue<string>()=="missing-patch","Empty group patch is refused like the editor tool contract");
    await Tool("set_settings",new(){["patch"]=new JsonObject{["quitRetryMinutes"]=1.5}},true);checks.Add("Fractional quit retry refused");
    var settings=await Tool("set_settings",new(){["patch"]=new JsonObject{["quitRetryMinutes"]=5,["quickAddGroupId"]=groupId}});
    Check(settings?["globalSettings"]?["quitRetryMinutes"]?.GetValue<int>()==5 && settings?["quickAddGroupId"]?.GetValue<string>()==groupId,"Editor settings response and selected quick-add target match Mac");
    await Tool("add_application",new(){["id"]=groupId,["appId"]="editor.exe"},true);checks.Add("Bare executable name refused");
    await Tool("add_application",new(){["id"]=groupId,["bundleId"]="legacy.exe"},true);checks.Add("Retired bundleId alias refused");
    var scopePatch=new JsonArray(new JsonObject{["surface"]="apps",["id"]="apps-a",["action"]="block",["apps"]=new JsonArray()},new JsonObject{["surface"]="apps",["id"]="apps-b",["action"]="block",["apps"]=new JsonArray(new JsonObject{["id"]=@"C:\Apps\other.exe",["name"]="Other"})});
    await Tool("set_group",new(){["id"]=groupId,["patch"]=new JsonObject{["scopes"]=scopePatch}});
    var added=await Tool("add_application",new(){["id"]=groupId,["appId"]=@"C:\Apps\editor.exe",["name"]="Editor"});
    await Tool("add_application",new(){["id"]=groupId,["appId"]=@"c:\apps\EDITOR.exe"});
    var preserved=await Tool("get_group",new(){["id"]=groupId});
    Check((preserved?["scopes"] as JsonArray)?.Count==2 && (preserved?["scopes"]?[0]?["apps"] as JsonArray)?.Count==1 && (preserved?["scopes"]?[1]?["apps"] as JsonArray)?.Count==1,"Application edit preserves other scope lines and deduplicates Windows identity case");
    var locked=await Tool("lock_group",new(){["id"]=groupId,["waitHours"]=0,["pin"]="123456"});
    Check(locked?["group"]?["locked"]?.GetValue<bool>()==true && locked?["group"]?["parentalPasswordHash"]==null,"Lock uses canonical PIN gates and public response omits verifier");
    await Tool("set_group",new(){["id"]=groupId,["patch"]=new JsonObject{["enabled"]=false}},true);
    checks.Add("Locked tool edit refused");
    var pending=await Tool("unlock_group",new(){["id"]=groupId,["pin"]="123456"});
    Check(pending?["confirmationsLeft"]?.GetValue<int>()==10,"Unlock begins 10-step confirmation after PIN");
    var tooSoon=await Tool("unlock_group",new(){["id"]=groupId,["confirm"]=true},true);
    Check(tooSoon?["error"]?.GetValue<string>().StartsWith("confirm-wait:")==true,"Premature confirmation cannot skip 5-second spacing");
    var waiting=await Tool("create_group",new(){["patch"]=new JsonObject{["name"]="Wait gate contract"}});var waitingId=waiting?["group"]?["id"]!.GetValue<string>()!;
    await Tool("lock_group",new(){["id"]=waitingId,["waitHours"]=1});
    Check((await Tool("unlock_group",new(){["id"]=waitingId},true))?["error"]?.GetValue<string>().StartsWith("wait:")==true,"A holding wait gate refuses unlock before confirmations");
    Check((await Tool("delete_all_groups",new(),true))?["error"]?.GetValue<string>().StartsWith("wait:")==true,"Delete all respects the union of active wait gates");
    var sandbox=await Tool("create_group",new(){["groupType"]="custom",["patch"]=new JsonObject{["name"]="Sandbox probes"}});
    var sandboxId=sandbox?["group"]?["id"]!.GetValue<string>()!;
    var safe=await Tool("run_custom_rule",new(){["id"]=sandboxId,["source"]="(on,v)=>{on('tick',()=>{v.state.noOpfs=typeof navigator.storage==='undefined';v.state.noBridge=typeof chrome.webview==='undefined';v.state.noFetch=false;try{fetch('https://invalid.example')}catch(_){v.state.noFetch=true;}});}"});
    Check(safe?["ran"]?.GetValue<bool>()==true,"Hostile capability probe loads without host privileges");
    await Task.Delay(1200);var sandboxStore=ReadStore();
    Check(sandboxStore?["cbRuleState"]?[sandboxId]?["noOpfs"]?.GetValue<bool>()==true && sandboxStore?["cbRuleState"]?[sandboxId]?["noBridge"]?.GetValue<bool>()==true && sandboxStore?["cbRuleState"]?[sandboxId]?["noFetch"]?.GetValue<bool>()==true,"Actual WebView2 rule sandbox seals OPFS, native bridge, and network capabilities");
    var custom=await Tool("create_group",new(){["groupType"]="custom",["patch"]=new JsonObject{["name"]="Native rule contract"}});
    var customId=custom?["id"]?.GetValue<string>()??custom?["group"]?["id"]?.GetValue<string>()??throw new Exception("custom id missing");
    var ruleSource="\n  (on,v)=>{on('tick',()=>{v.state.count=(v.state.count||0)+1;v.log('native-contract-log',v.state.count);});}  \n";
    var loaded=await Tool("run_custom_rule",new(){["id"]=customId,["source"]=ruleSource});
    Check(loaded?["ran"]?.GetValue<bool>()==true,"Current bare custom rule loads through actual WebView2 worker");
    await Task.Delay(2200);
    var store=ReadStore();
    Check((store?["blockedGroups"] as JsonArray)?.OfType<JsonObject>().First(g=>g["id"]?.GetValue<string>()==customId)["activeEventSource"]?.GetValue<string>()==ruleSource,"Run stores the user's source verbatim including whitespace");
    Check(store?["cbRuleState"]?[customId]?["count"]?.GetValue<int>()>=1,"Actual rule tick persists per-group state");
    var rerun=await Tool("run_custom_rule",new(){["id"]=customId});Check(rerun?["ran"]?.GetValue<bool>()==true,"Run without source uses saved current rule text");
    var failed=await Tool("run_custom_rule",new(){["id"]=customId,["source"]="(event)=>event.on('tickEvent',()=>{})"});
    Check(failed?["ran"]?.GetValue<bool>()==false,"Retired helper API is refused");
    store=ReadStore();var previous=store?["cbRuleState"]?[customId]?["count"]?.GetValue<int>()??0;await Task.Delay(1500);
    var progressDeadline=DateTimeOffset.UtcNow.AddSeconds(6);
    do { store=ReadStore(); if((store?["cbRuleState"]?[customId]?["count"]?.GetValue<int>()??0)>previous) break; await Task.Delay(200); }while(DateTimeOffset.UtcNow<progressDeadline);
    Check((store?["cbRuleState"]?[customId]?["count"]?.GetValue<int>()??0)>previous,"Failed Run preserves previous active rule");
    var deletable=await Tool("create_group",new(){["groupType"]="custom",["patch"]=new JsonObject{["name"]="Delete state fixture"}});
    var deleteId=deletable?["group"]?["id"]!.GetValue<string>()!;
    await Tool("run_custom_rule",new(){["id"]=deleteId,["source"]="(on,v)=>{on('tick',()=>{v.state.saved=1;});}"});
    await Task.Delay(1100); await Tool("delete_group",new(){["id"]=deleteId});
    store=ReadStore();Check(store?["cbRuleState"]?[deleteId]==null,"Deleting custom group clears native rule state and runtime");
    Check(store?["groupSnoozeTotalsMs"]?["time-finished"]?.GetValue<int>()==2017,"Later ticks do not count a finished time snooze twice");
    File.WriteAllText(resultFile,new JsonObject{["ok"]=true,["checks"]=new JsonArray(checks.Select(c=>(JsonNode)JsonValue.Create(c)!).ToArray())}.ToJsonString());
}
catch(Exception ex){File.WriteAllText(resultFile,new JsonObject{["ok"]=false,["error"]=ex.ToString(),["checks"]=new JsonArray(checks.Select(c=>(JsonNode)JsonValue.Create(c)!).ToArray())}.ToJsonString());Environment.ExitCode=1;Console.Error.WriteLine(ex);}
