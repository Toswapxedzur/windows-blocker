using System.Net.Http.Headers;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;
using System.Windows.Automation;
using System.Runtime.InteropServices;
using WindowsBlocker.Bridge;
using WindowsBlocker.WebUI;

Environment.SetEnvironmentVariable("VAULT_ENVIRONMENT","development");
Environment.SetEnvironmentVariable("VAULT_STORAGE_ROOT",args[0]);
var resultFile=args[1]; var checks=new List<string>();bool cloudPlaceholderVerified=false;
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
AutomationElement? NativeWindow(int processId,string automationId)
{
    foreach(var hwnd in NativeInteraction.Windows(processId))
    {
        var window=AutomationElement.FromHandle(hwnd);
        if(window.Current.AutomationId==automationId)return window;
    }
    return null;
}

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
    var appPid=int.Parse(args[3]);
    async Task<AutomationElement?> WaitElement(Func<AutomationElement?> find,int seconds=6)
    {
        var until=DateTimeOffset.UtcNow.AddSeconds(seconds);
        do {var item=find();if(item!=null)return item;await Task.Delay(100);}while(DateTimeOffset.UtcNow<until);
        return null;
    }
    AutomationElement? OwnButton(string name)
    {
        foreach(var handle in NativeInteraction.Windows(appPid))
        {
            var window=AutomationElement.FromHandle(handle);if(window.Current.AutomationId!="VaultRulePanels")continue;
            var button=window.FindFirst(TreeScope.Descendants,new AndCondition(new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button),new PropertyCondition(AutomationElement.NameProperty,name)));
            if(button!=null)return button;
        }
        return null;
    }
    var quick=await Tool("create_group",new(){["patch"]=new JsonObject{["name"]="Quick add fixture",["enabled"]=false}});
    var quickId=quick!["group"]!["id"]!.GetValue<string>();
    await Tool("set_settings",new(){["patch"]=new JsonObject{["quickAddEnabled"]=true,["quickAddGroupId"]=quickId}});
    var quickWindow=await WaitElement(()=>NativeWindow(appPid,"VaultQuickAdd"));
    Check(quickWindow!=null && !quickWindow.Current.IsOffscreen,"Selected unlocked group displays the native floating quick-add");
    using(var target=Process.Start(new ProcessStartInfo(args[4]){UseShellExecute=false,ArgumentList={Path.Combine(args[0],"quick-close-count.txt")}})!)
    {
        try
        {
            var targetDeadline=DateTimeOffset.UtcNow.AddSeconds(5);
            do {target.Refresh();if(target.MainWindowHandle!=IntPtr.Zero)break;await Task.Delay(100);}while(DateTimeOffset.UtcNow<targetDeadline);
            var hwnd=target.MainWindowHandle;NativeInteraction.BringToFront(hwnd);
            var targetBounds=AutomationElement.FromHandle(hwnd).Current.BoundingRectangle;
            var focusDeadline=DateTimeOffset.UtcNow.AddSeconds(5);
            do {NativeInteraction.Click(targetBounds.Left+targetBounds.Width/2,targetBounds.Top+targetBounds.Height/2);await Task.Delay(250);}while(NativeInteraction.GetForegroundWindow()!=hwnd && DateTimeOffset.UtcNow<focusDeadline);
            Check(hwnd!=IntPtr.Zero && NativeInteraction.GetForegroundWindow()==hwnd,"Harmless test editor is the frontmost app before quick-add");
            var quickButton=quickWindow!.FindFirst(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button));
            var bounds=quickButton.Current.BoundingRectangle;NativeInteraction.Click(bounds.Left+bounds.Width/2,bounds.Top+bounds.Height/2);
            var addedDeadline=DateTimeOffset.UtcNow.AddSeconds(5);bool didAdd;
            do {var saved=ReadStore();didAdd=(saved?["blockedGroups"] as JsonArray)?.OfType<JsonObject>().First(g=>g["id"]!.GetValue<string>()==quickId)["scopes"] is JsonArray lines && lines.OfType<JsonObject>().Any(line=>(line["apps"] as JsonArray)?.OfType<JsonObject>().Any(a=>string.Equals(a["id"]?.GetValue<string>(),args[4],StringComparison.OrdinalIgnoreCase))==true);if(didAdd)break;await Task.Delay(100);}while(DateTimeOffset.UtcNow<addedDeadline);
            Check(didAdd && NativeInteraction.GetForegroundWindow()==hwnd,"Real mouse quick-add stores the front app identity without taking focus");
        }
        finally {if(!target.HasExited)target.Kill(true);}
    }
    await Tool("lock_group",new(){["id"]=quickId,["waitHours"]=0,["pin"]="654321"});await Task.Delay(150);
    Check(NativeWindow(appPid,"VaultQuickAdd")?.Current.IsOffscreen!=false,"Locking the selected target hides the native quick-add");
    var panelGroups=new List<string>();
    foreach(var label in new[]{"First","Second"})
    {
        var answer=await Tool("create_group",new(){["groupType"]="custom",["patch"]=new JsonObject{["name"]="Panel "+label}});var panelGroup=answer!["group"]!["id"]!.GetValue<string>();panelGroups.Add(panelGroup);
        var panelSource="(on,v)=>{v.panel('same',{title:'Panel "+label+"',position:'top-left',controls:[{id:'done',type:'button',label:'Mark "+label+" done'}]});on('panel',ev=>{v.state.clicked=ev.data.controlId;v.panel('same',null);});}";
        await Tool("run_custom_rule",new(){["id"]=panelGroup,["source"]=panelSource});
    }
    var firstButton=await WaitElement(()=>OwnButton("Mark First done"));var secondButton=await WaitElement(()=>OwnButton("Mark Second done"));
    Check(firstButton!=null && secondButton!=null,"Different custom groups with the same panel ID both render native controls");
    ((InvokePattern)firstButton!.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
    var panelDeadline=DateTimeOffset.UtcNow.AddSeconds(15);
    do {if(ReadStore()?["cbRuleState"]?[panelGroups[0]]?["clicked"]?.GetValue<string>()=="done")break;await Task.Delay(100);}while(DateTimeOffset.UtcNow<panelDeadline);
    Check(ReadStore()?["cbRuleState"]?[panelGroups[0]]?["clicked"]?.GetValue<string>()=="done" && ReadStore()?["cbRuleState"]?[panelGroups[1]]?["clicked"]==null && OwnButton("Mark Second done")!=null,"Native panel click routes only to its owning group and preserves the other card");
    ((InvokePattern)OwnButton("Mark Second done")!.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
    await Tool("set_settings",new(){["patch"]=new JsonObject{["quickAddGroupId"]=panelGroups[0]}});await Task.Delay(100);
    Check(NativeWindow(appPid,"VaultQuickAdd")?.Current.IsOffscreen!=false,"Custom rule targets do not show native quick-add");
    await Tool("set_settings",new(){["patch"]=new JsonObject{["quickAddEnabled"]=false}});
    using(var ownApp=Process.GetProcessById(appPid))
    {
        var editor=NativeInteraction.Windows(appPid).Select(AutomationElement.FromHandle).First(window=>window.Current.Name=="Windows Vault");
        AutomationElement? EditorButton(string name)=>editor.FindFirst(TreeScope.Descendants,new AndCondition(new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button),new PropertyCondition(AutomationElement.NameProperty,name)));
        var settingsButton=await WaitElement(()=>EditorButton("Settings"));Check(settingsButton!=null,"Shared Settings button is reachable in the actual WebView accessibility tree");
        ((InvokePattern)settingsButton!.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
        var choose=await WaitElement(()=>EditorButton("Choose folder"));Check(choose!=null,"Custom-rule folder chooser is reachable from shared Settings");
        NativeInteraction.BringToFront(new IntPtr(editor.Current.NativeWindowHandle));
        NativeInteraction.SetForegroundWindow(new IntPtr(editor.Current.NativeWindowHandle));
        ((InvokePattern)choose!.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
        var picker=await WaitElement(()=>{var hwnd=NativeInteraction.NamedWindow(appPid,"Choose folder");return hwnd==IntPtr.Zero ? null : AutomationElement.FromHandle(hwnd);},20);
        if(picker==null)foreach(var hwnd in NativeInteraction.Windows(appPid)){var window=AutomationElement.FromHandle(hwnd);Console.WriteLine("Native window: "+window.Current.Name+" / "+window.Current.AutomationId);}
        Check(picker!=null,"Choose folder opens the normal native folder dialog");
        var folder=Path.Combine(args[0],"chosen-rule-folder");Directory.CreateDirectory(folder);File.WriteAllBytes(Path.Combine(folder,"invalid-utf8.txt"),new byte[]{0xff,0xfe});File.WriteAllText(Path.Combine(folder,"hidden.txt"),"hidden");File.SetAttributes(Path.Combine(folder,"hidden.txt"),FileAttributes.Hidden);
        File.WriteAllBytes(Path.Combine(folder,"too-large.txt"),new byte[1_048_577]);
        using var lockedRead=new FileStream(Path.Combine(folder,"locked.txt"),FileMode.Create,FileAccess.ReadWrite,FileShare.None);
        var outside=Path.Combine(args[0],"outside-rule-folder");Directory.CreateDirectory(outside);File.WriteAllText(Path.Combine(outside,"secret.txt"),"outside");
        using(var junction=Process.Start(new ProcessStartInfo("cmd.exe"){UseShellExecute=false,CreateNoWindow=true,ArgumentList={"/c","mklink","/J",Path.Combine(folder,"escape"),outside}})!){await junction.WaitForExitAsync();Check(junction.ExitCode==0,"Normal user creates an owned junction escape fixture");}
        var deletedTarget=Path.Combine(args[0],"deleted-junction-target");Directory.CreateDirectory(deletedTarget);
        using(var junction=Process.Start(new ProcessStartInfo("cmd.exe"){UseShellExecute=false,CreateNoWindow=true,ArgumentList={"/c","mklink","/J",Path.Combine(folder,"dangling"),deletedTarget}})!){await junction.WaitForExitAsync();Check(junction.ExitCode==0,"Normal user creates an owned dangling-parent junction fixture");}
        Directory.Delete(deletedTarget);File.WriteAllText(Path.Combine(folder,"bom.txt"),"Windows 中文",new UTF8Encoding(true));File.WriteAllText(Path.Combine(folder,"bom.json"),"{\"name\":\"中文\"}",new UTF8Encoding(true));
        var selectedAlias=Path.Combine(args[0],"selected-root-junction");
        using(var junction=Process.Start(new ProcessStartInfo("cmd.exe"){UseShellExecute=false,CreateNoWindow=true,ArgumentList={"/c","mklink","/J",selectedAlias,folder}})!){await junction.WaitForExitAsync();Check(junction.ExitCode==0,"Normal user creates an owned explicit root junction selection");}
        using var cloud=new CloudFolderFixture(Path.Combine(folder,"cloud"));cloudPlaceholderVerified=(cloud.ReparseTag & 0xffff0fff)==0x9000001a;
        if(cloudPlaceholderVerified)Check(true,"Normal user creates an owned actual cloud placeholder");
        else Console.WriteLine("LIMIT Cloud Files API accepted this owned fixture but exposed no cloud reparse tag; live placeholder coverage is unavailable on this rig.");
        var folderField=await WaitElement(()=>{var hwnd=NativeInteraction.NamedWindow(appPid,"Choose folder");return hwnd==IntPtr.Zero ? null : AutomationElement.FromHandle(hwnd).FindFirst(TreeScope.Descendants,new AndCondition(new PropertyCondition(AutomationElement.AutomationIdProperty,"1152"),new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Edit)));},20);
        if(folderField==null)foreach(AutomationElement element in picker!.FindAll(TreeScope.Descendants,Condition.TrueCondition))Console.WriteLine("Picker: "+element.Current.Name+" / "+element.Current.AutomationId+" / "+element.Current.ControlType.ProgrammaticName);
        Check(folderField!=null,"Native folder dialog exposes its path field");
        ((ValuePattern)folderField!.GetCurrentPattern(ValuePattern.Pattern)).SetValue(selectedAlias);
        var grant=Path.Combine(args[0],"local-folder-grant.txt");var grantDeadline=DateTimeOffset.UtcNow.AddSeconds(15);
        // A typed path can first navigate the native dialog into that folder.
        // Reacquire its native button after navigation changes its UIA element.
        while(!File.Exists(grant) && DateTimeOffset.UtcNow<grantDeadline)
        {
            try
            {
                var pickerHandle=NativeInteraction.NamedWindow(appPid,"Choose folder");
                if(pickerHandle!=IntPtr.Zero)
                {
                    var activePicker=AutomationElement.FromHandle(pickerHandle);
                    var select=activePicker.FindFirst(TreeScope.Descendants,new AndCondition(new PropertyCondition(AutomationElement.AutomationIdProperty,"1"),new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button)));
                    if(select!=null && select.Current.IsEnabled) {var click=select.GetClickablePoint();NativeInteraction.Click(click.X,click.Y);}
                }
            }
            catch(ElementNotAvailableException) { } // The normal picker may close between enumeration and its click.
            await Task.Delay(500);
        }
        Check(File.Exists(grant) && File.ReadAllText(grant)==folder,"Actual junction-root selection persists its canonical granted target");
        var files=await Tool("create_group",new(){["groupType"]="custom",["patch"]=new JsonObject{["name"]="Chosen folder rule fixture"}});var fileId=files!["group"]!["id"]!.GetValue<string>();
        var fileRule="(on,v)=>{on('tick',()=>{if(v.state.started)return;v.state.started=true;v.file('write','nested/facts.json','plain text 中文');v.file('exists','cloud/cloud.txt');v.file('read','cloud/hydrated.txt');v.file('readJson','bom.json');for(const path of ['../outside.txt','C:/outside.txt','CON.txt','.hidden.txt','escape/secret.txt','dangling/secret.txt','bom.txt','invalid-utf8.txt','too-large.txt','locked.txt'])v.file('read',path);});on('file',ev=>{const a=ev.data;v.state.answers=v.state.answers||{};v.state.answers[a.op+':'+a.path]=a;if(a.path==='nested/facts.json'&&a.ok){if(a.op==='write')v.file('append',a.path,' appended');else if(a.op==='append')v.file('read',a.path);else if(a.op==='read')v.file('exists',a.path);else if(a.op==='exists')v.file('list','nested');}});}";
        Check((await Tool("run_custom_rule",new(){["id"]=fileId,["source"]=fileRule}))?["ran"]?.GetValue<bool>()==true,"Selected-folder rule loads in the actual isolated WebView worker");
        // The VM may share mini1 with the second OS runtime test. Wait for the
        // complete asynchronous write/append/read/exists/list chain without
        // changing the required results.
        var fileDeadline=DateTimeOffset.UtcNow.AddSeconds(45);JsonNode? answers;
        do {answers=ReadStore()?["cbRuleState"]?[fileId]?["answers"];if(answers?["list:nested"]?["ok"]?.GetValue<bool>()==true && answers?["read:locked.txt"]!=null)break;await Task.Delay(100);}while(DateTimeOffset.UtcNow<fileDeadline);
        Check(answers?["read:nested/facts.json"]?["text"]?.GetValue<string>()=="plain text 中文 appended" && answers?["exists:nested/facts.json"]?["exists"]?.GetValue<bool>()==true,"Native v.file creates parents, writes plain .json text, appends and reads exact UTF-8");
        Check(answers?["list:nested"]?["entries"]?[0]?["extension"]?.GetValue<string>()==".json" && File.ReadAllText(Path.Combine(folder,"nested","facts.json"))=="plain text 中文 appended","Native list returns canonical relative paths and file extensions");
        Check(new[]{"../outside.txt","C:/outside.txt","CON.txt",".hidden.txt","escape/secret.txt","dangling/secret.txt"}.All(path=>answers?["read:"+path]?["error"]?.GetValue<string>()=="invalid-path"),"Native folder boundary rejects traversal, absolute paths, hidden names, Windows device aliases and existing/dangling junction escapes");
        Check(answers?["read:bom.txt"]?["text"]?.GetValue<string>()=="Windows 中文" && answers?["readJson:bom.json"]?["ok"]?.GetValue<bool>()==true && answers?["readJson:bom.json"]?["text"]?.GetValue<string>()=="{\"name\":\"中文\"}","Actual rule file replies decode Windows UTF-8 BOM text and JSON like the browser broker");
        Check(answers?["read:invalid-utf8.txt"]?["error"]?.GetValue<string>()=="invalid-text" && answers?["read:too-large.txt"]?["error"]?.GetValue<string>()=="file-too-large","Native reads refuse malformed UTF-8 and files exceeding one MiB");
        Check(answers?["read:locked.txt"]?["error"]?.GetValue<string>()=="local-file-error","Actual sharing violation returns a canonical error token without an absolute path or Windows prose");
        Check(answers?["exists:cloud/cloud.txt"]?["ok"]?.GetValue<bool>()==true && answers?["exists:cloud/cloud.txt"]?["exists"]?.GetValue<bool>()==true && answers?["read:cloud/hydrated.txt"]?["text"]?.GetValue<string>()=="available cloud content",cloudPlaceholderVerified ? "Actual Cloud Files placeholder lookup and available sync-root text remain accessible inside the selected folder" : "Owned Cloud Files API root lookup and available text remain accessible inside the selected folder");
        var revoke=EditorButton("Remove folder access");Check(revoke?.Current.IsEnabled==true,"Selected-folder revoke control is enabled");((InvokePattern)revoke!.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
        var revokeDeadline=DateTimeOffset.UtcNow.AddSeconds(3);while(File.Exists(grant)&&DateTimeOffset.UtcNow<revokeDeadline)await Task.Delay(100);
        Check(!File.Exists(grant),"Shared Settings revokes the native folder grant");
        await Tool("run_custom_rule",new(){["id"]=fileId,["source"]="(on,v)=>{on('tick',()=>{v.file('read','nested/facts.json');});on('file',ev=>{v.state.revoked=ev.data;});}"});
        var deniedDeadline=DateTimeOffset.UtcNow.AddSeconds(4);do{if(ReadStore()?["cbRuleState"]?[fileId]?["revoked"]?["error"]?.GetValue<string>()=="local-folder-not-available")break;await Task.Delay(100);}while(DateTimeOffset.UtcNow<deniedDeadline);
        Check(ReadStore()?["cbRuleState"]?[fileId]?["revoked"]?["ok"]?.GetValue<bool>()==false && ReadStore()?["cbRuleState"]?[fileId]?["revoked"]?["error"]?.GetValue<string>()=="local-folder-not-available","Revocation immediately denies subsequent actual rule file requests");
        var close=EditorButton("Close");if(close!=null)((InvokePattern)close.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
    }
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
    File.WriteAllText(resultFile,new JsonObject{["ok"]=true,["cloudPlaceholderVerified"]=cloudPlaceholderVerified,["checks"]=new JsonArray(checks.Select(c=>(JsonNode)JsonValue.Create(c)!).ToArray())}.ToJsonString());
}
catch(Exception ex){File.WriteAllText(resultFile,new JsonObject{["ok"]=false,["error"]=ex.ToString(),["checks"]=new JsonArray(checks.Select(c=>(JsonNode)JsonValue.Create(c)!).ToArray())}.ToJsonString());Environment.ExitCode=1;Console.Error.WriteLine(ex);}

static class NativeInteraction
{
    private delegate bool EnumWindow(IntPtr hwnd,IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindow callback,IntPtr data);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint id);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd,StringBuilder text,int capacity);
    internal static IntPtr NamedWindow(int processId,string title){foreach(var hwnd in Windows(processId)){var text=new StringBuilder(256);GetWindowText(hwnd,text,text.Capacity);if(text.ToString()==title)return hwnd;}return IntPtr.Zero;}
    internal static List<IntPtr> Windows(int processId){var windows=new List<IntPtr>();EnumWindows((hwnd,_)=>{GetWindowThreadProcessId(hwnd,out var id);if(id==processId)windows.Add(hwnd);return true;},IntPtr.Zero);return windows;}
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
    internal static void BringToFront(IntPtr hwnd)=>SetWindowPos(hwnd,new IntPtr(-1),0,0,0,0,3);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags,uint dx,uint dy,uint data,UIntPtr extra);
    internal static void Click(double x,double y){SetCursorPos((int)x,(int)y);mouse_event(2,0,0,0,UIntPtr.Zero);mouse_event(4,0,0,0,UIntPtr.Zero);}
}
