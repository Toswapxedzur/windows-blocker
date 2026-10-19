using System.Text.Json.Nodes;
using WindowsBlocker.Rules;
using WindowsBlocker.WebUI;
using WindowsBlocker.Bridge;

// Actual isolated WebView2 group and trusted policy workers. The controller
// supplies a fresh admitted interactive actor/UDF; these checks start no hub
// listener, touch no browser profile and use explicit event clocks, not sleeps.
internal static class ManagedTimeContracts
{
    public static async Task Run(CustomRuleRuntime runtime, Action<bool,string> Check)
    {
        const string source = """
        (on,v)=>{
          const down=v.timer("down",{durationMs:5000});
          const up=v.timer("up",{direction:"up"});
          on("start",()=>{down.start();up.start();});
          on("pause",()=>{down.pause();up.pause();});
          on("resume",()=>{down.resume();up.resume();});
          on("reset",()=>{down.reset();up.reset();});
          on("timerEnded",()=>{v.state.ends=(v.state.ends||0)+1;});
          on("tick",()=>{v.state.down=down.snapshot();v.state.up=up.snapshot();});
        }
        """;
        var store = new WebStore();
        store.SaveRaw("{\"blockedGroups\":[{\"id\":\"timers\",\"name\":\"Timers\",\"groupType\":\"custom\",\"enabled\":true}],\"cbRuleState\":{}}");
        JsonObject state = new();
        void Save() => store.Merge(new JsonObject { ["cbRuleState"] = new JsonObject { ["timers"] = state.DeepClone() } });
        var loaded = await runtime.LoadAsync("timers", source, "{}");
        Check(loaded?.Ok == true, "Actual worker registers countdown and count-up factories");
        if (loaded!.States.TryGetValue("timers", out var initial)) state = JsonNode.Parse(initial)!.AsObject();
        Save();
        var at = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 1000000;
        async Task Send(string type, long now)
        {
            var result = await runtime.DispatchAsync(new() { Type = type, GroupId = "timers", Now = now, Data = new { } });
            Check(result != null && result.StateFailures.Count == 0 && result.Quarantine == null, "Actual timer worker accepts " + type + " at " + (now-at));
            if (result!.States.TryGetValue("timers", out var saved)) state = JsonNode.Parse(saved)!.AsObject();
            Save();
        }
        await Send("start",at); await Send("pause",at+2000); await Send("tick",at+4000);
        Check(state["down"]!["elapsedMs"]!.GetValue<long>() == 2000 && state["up"]!["elapsedMs"]!.GetValue<long>() == 2000 && !state["down"]!["running"]!.GetValue<bool>(), "Pause freezes both actual worker clocks");
        await Send("resume",at+4000); await Send("tick",at+6999);
        Check(state["down"]!["remainingMs"]!.GetValue<long>() == 1 && state["ends"] == null, "Countdown remains unfinished one millisecond before expiry");
        await Send("tick",at+7000); await Send("tick",at+8000);
        Check(state["ends"]!.GetValue<int>() == 1 && state["down"]!["completed"]!.GetValue<bool>() && state["up"]!["elapsedMs"]!.GetValue<long>() == 6000, "Actual worker delivers countdown completion once while count-up continues");
        await runtime.UnloadAsync("timers");
        state = JsonNode.Parse(store.LoadRawJson()!)!["cbRuleState"]!["timers"]!.AsObject();
        loaded = await runtime.LoadAsync("timers",source,state.ToJsonString());
        Check(loaded?.Ok == true, "Actual worker reloads persisted managed timer state");
        await Send("tick",at+9000);
        Check(state["ends"]!.GetValue<int>() == 1, "Restart preserves completion acknowledgment without replay");
        await Send("reset",at+9000); await Send("tick",at+9000);
        Check(state["down"]!["elapsedMs"]!.GetValue<long>() == 0 && !state["down"]!["running"]!.GetValue<bool>() && state["up"]!["elapsedMs"]!.GetValue<long>() == 0, "Reset clears both actual worker timers and pauses them");
        await runtime.UnloadAsync("timers");

        // The real trusted worker executes pure core methods with exact clocks.
        async Task<JsonObject> Policy(string method, params object?[] args)
        {
            var reply = await runtime.PolicyAsync("RuleCore", method, args);
            Check(reply != null && reply.TryGetValue("value",out _), "Actual trusted policy worker routes RuleCore." + method);
            return JsonNode.Parse(reply!["value"].GetRawText())!.AsObject();
        }
        var tracker = new JsonObject { ["kind"]="rolling",["windowMs"]=10000,["limitMs"]=3000,["websites"]=new JsonArray("example.org"),["apps"]=new JsonArray(),
            ["bucketsMs"]=new JsonObject(),["segments"]=new JsonArray(),["accruedUntilMs"]=0,["resetAtMs"]=at,["fault"]=false };
        var seed = new JsonObject { ["version"]=1,["objects"]=new JsonObject{["budget"]=tracker},["ownTargets"]=new JsonObject{["budget"]=new JsonObject{["websites"]=new JsonArray("example.org"),["apps"]=new JsonArray()}} };
        var merged=await Policy("mergeTime",null,new object[]{new {member="chrome/c",state=seed}},"chrome/c",at);
        Check(merged["ok"]!.GetValue<bool>(),"Actual policy worker merges rolling declarations");
        var shared=merged["state"]!.AsObject();
        var reduced=await Policy("reduceTime",shared,new {member="chrome/c",commands=Array.Empty<object>(),nowMs=at+5000,usage=new {type="visible",now=at+5000,data=new {url="https://example.org",elapsedMs=5000}}});
        Check(reduced["ok"]!.GetValue<bool>(),"Actual policy worker accepts trusted visible usage");
        shared=reduced["state"]!.AsObject();
        Check(Used(shared)==5000,"Actual worker counts observed usage beyond the allowance");
        var before=await Policy("reduceTime",shared,new {member="chrome/c",commands=Array.Empty<object>(),nowMs=at+12000});
        Check(Used(before["state"]!.AsObject())==3000,"Exact rolling expiry retains exhausted allowance at boundary");
        var after=await Policy("reduceTime",shared,new {member="chrome/c",commands=Array.Empty<object>(),nowMs=at+12001});
        Check(Used(after["state"]!.AsObject())==2999,"Exact rolling expiry returns one millisecond of allowance without minute delay");
        shared["confirmed"]=new JsonObject{["chrome/c"]=true};
        var detached=await Policy("detachTime",seed,shared,"chrome/c","chrome/c","cluster","token",at+5000,false);
        Check(detached["ok"]!.GetValue<bool>() && detached["state"]!["detachedReceipt"]!["token"]!.GetValue<string>()=="token" && Used(detached["state"]!.AsObject())==5000,"Actual policy worker routes final shared snapshot transfer");
        var acknowledged=await Policy("detachTime",detached["state"],shared,"chrome/c","chrome/c","cluster","token",at+5000,true);
        Check(acknowledged["ok"]!.GetValue<bool>() && acknowledged["state"]!["detachedReceipt"]==null && Used(acknowledged["state"]!.AsObject())==5000,"Actual policy worker acknowledges saved handover without discarding usage");
        await HandoverInitializer(runtime,Check);
        await HandoverActive(runtime,Check,false);
        await HandoverActive(runtime,Check,true);
        await DiagnosticFeed(runtime,Check);
    }
    private static async Task HandoverInitializer(CustomRuleRuntime runtime, Action<bool,string> Check)
    {
        var store = new WebStore();
        var timer = new JsonObject { ["kind"]="timer",["direction"]="down",["durationMs"]=10000,["elapsedMs"]=0,["startedAtMs"]=null,["generation"]=0,["incarnation"]="handover-fixture",["notified"]=false };
        var seed = new JsonObject { ["version"]=1,["objects"]=new JsonObject{["clock"]=timer} };
        store.SaveRaw(new JsonObject { ["blockedGroups"]=new JsonArray(new JsonObject{["id"]="handover",["name"]="Handover",["groupType"]="custom",["enabled"]=true}),
            ["cbRuleState"]=new JsonObject{["handover"]=new JsonObject{["keep"]=7,["$vaultTime"]=seed.DeepClone()}} }.ToJsonString());
        var hub = new ConnectionHub(store);
        hub.RuleTimePolicy = async (method,args) => {
            var reply=await runtime.PolicyAsync("RuleCore",method,args);
            return reply!=null && reply.TryGetValue("value",out var value) ? JsonNode.Parse(value.GetRawText()) as JsonObject : null;
        };
        JsonArray Roster(string id) => new(new JsonObject{["id"]=id,["name"]=id,["timeState"]=seed.DeepClone()});
        hub.SetRoster("windowsapp",Roster("handover")); hub.SetRoster("chrome",Roster("browser"));
        Check(hub.Link("windowsapp","handover","chrome","browser")==null,"Actual worker handover initializer fixture links without a listener");
        var cluster=JsonNode.Parse(hub.ClustersJson())!["clusters"]![0]!["id"]!.GetValue<string>();
        await hub.SyncRuleTimeAsync("windowsapp","handover",cluster,seed,new());
        var ready=await hub.SyncRuleTimeAsync("chrome","browser",cluster,seed,new());
        Check(ready["ready"]?.GetValue<bool>()==true,"Actual policy worker confirms both linked original seeds");
        var command=new JsonArray(new JsonObject{["id"]="clock",["op"]="reset",["serial"]=1,["scope"]=cluster,["atMs"]=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()});
        var reset=await hub.SyncRuleTimeAsync("windowsapp","handover",cluster,seed,command);
        Check(reset["state"]?["objects"]?["clock"]?["generation"]?.GetValue<int>()==1,"Actual policy worker advances authoritative timer generation");
        Check(hub.Unlink("chrome","browser")==null,"Actual native unlink retains final managed state for initializer");
        var engine=new RuleEngine(store,hub);engine.AttachRuntime(runtime);
        var loaded=await engine.RunRuleAsync("handover","(on,v)=>{const t=v.timer('clock',{durationMs:10000});v.state.initialized=true;}");
        var saved=JsonNode.Parse(store.LoadRawJson()!)!["cbRuleState"]!["handover"]!;
        Check(loaded["ok"]?.GetValue<bool>()==true && saved["initialized"]?.GetValue<bool>()==true && saved["keep"]?.GetValue<int>()==7 && saved["$vaultTime"]?["objects"]?["clock"]?["generation"]?.GetValue<int>()==1,
            "Initializer reads saved post-handover state and cannot overwrite it with its stale preflight seed");
        await engine.UnloadGroupAsync("handover");
    }
    private static async Task HandoverActive(CustomRuleRuntime runtime, Action<bool,string> Check, bool disabled)
    {
        var id=disabled?"handover-disabled":"handover-active";
        var store=new WebStore();
        store.SaveRaw(new JsonObject { ["blockedGroups"]=new JsonArray(new JsonObject { ["id"]=id,["name"]=id,["groupType"]="custom",["enabled"]=true }) }.ToJsonString());
        var hub=new ConnectionHub(store);
        hub.RuleTimePolicy=async(method,args)=>{
            var reply=await runtime.PolicyAsync("RuleCore",method,args);
            return reply!=null && reply.TryGetValue("value",out var value)?JsonNode.Parse(value.GetRawText()) as JsonObject:null;
        };
        var engine=new RuleEngine(store,hub);engine.AttachRuntime(runtime);
        const string source="""
        (on,v)=>{
          const clock=v.timer("clock",{durationMs:10000});
          const budget=v.rolling("budget",{windowMs:10000,limitMs:3000});
          budget.assign({apps:["C:\\Apps\\owned.exe"]});
          on("tick",()=>{v.state.clock=clock.snapshot();v.state.budget=budget.snapshot();});
          on("localReset",()=>{clock.reset();v.state.localReset=true;});
        }
        """;
        Check((await engine.RunRuleAsync(id,source))["ok"]?.GetValue<bool>()==true,"Active handover fixture loads "+id);
        JsonObject Saved()=>JsonNode.Parse(store.LoadRawJson()!)!["cbRuleState"]![id]!.AsObject();
        var seed=Saved()["$vaultTime"]!.AsObject();
        var browser=(JsonObject)seed.DeepClone();
        browser["ownTargets"]=new JsonObject{["budget"]=new JsonObject{["websites"]=new JsonArray("example.org"),["apps"]=new JsonArray()}};
        browser["objects"]!["budget"]!["websites"]=new JsonArray("example.org");browser["objects"]!["budget"]!["apps"]=new JsonArray();
        JsonArray Roster(string group,JsonObject time)=>new(new JsonObject{["id"]=group,["name"]=group,["timeState"]=time.DeepClone()});
        hub.SetRoster("windowsapp",Roster(id,seed));hub.SetRoster("chrome",Roster(id+"-browser",browser));
        Check(hub.Link("windowsapp",id,"chrome",id+"-browser")==null,"Active handover fixture links "+id);
        var cluster=JsonNode.Parse(hub.ClustersJson())!["clusters"]!.AsArray().OfType<JsonObject>().Single(c=>c["members"]!.AsArray().OfType<JsonObject>().Any(m=>m["groupId"]?.GetValue<string>()==id))["id"]!.GetValue<string>();
        await hub.SyncRuleTimeAsync("windowsapp",id,cluster,seed,new());
        await hub.SyncRuleTimeAsync("chrome",id+"-browser",cluster,browser,new());
        await engine.TickAsync(new(),new());
        Check(Saved()["clock"]?["ready"]?.GetValue<bool>()==true && Saved()["$vaultTime"]?["scope"]?.GetValue<string>()==cluster,"Active worker receives shared context before unlink "+id);
        var reset=await hub.SyncRuleTimeAsync("chrome",id+"-browser",cluster,browser,new JsonArray(new JsonObject{["id"]="clock",["op"]="reset",["serial"]=1,["scope"]=cluster,["atMs"]=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}));
        Check(reset["state"]?["objects"]?["clock"]?["generation"]?.GetValue<int>()==1,"Shared final timer generation advances "+id);
        if(disabled) store.Update(root=>root["blockedGroups"]![0]!["enabled"]=false);
        Check(hub.Unlink("chrome",id+"-browser")==null,"Active worker fixture unlinks "+id);
        await engine.TickAsync(new(),new());
        var final=Saved()["$vaultTime"]!;
        Check(final["scope"]==null && final["objects"]?["clock"]?["generation"]?.GetValue<int>()==1 && final["objects"]?["budget"]?["apps"]?[0]?.GetValue<string>()=="c:\\apps\\owned.exe" && final["objects"]?["budget"]?["websites"]?.AsArray().Count==0,
            "Saved final handover retains generation and native own targets "+id);
        if(disabled) {store.Update(root=>root["blockedGroups"]![0]!["enabled"]=true);await engine.TickAsync(new(),new());}
        Check(Saved()["clock"]?["ready"]?.GetValue<bool>()==true && Saved()["budget"]?["ready"]?.GetValue<bool>()==true,"Live worker becomes locally ready after unlink "+id);
        await engine.FireUserEventAsync("localReset",id,new(),new());
        final=Saved();
        Check(final["localReset"]?.GetValue<bool>()==true && final["$vaultTime"]?["objects"]?["clock"]?["generation"]?.GetValue<int>()==2 && final["$vaultTime"]?["pending"]?.AsArray().Count==0,
            "Post-unlink timer control executes locally without stale shared scope "+id);
        await engine.UnloadGroupAsync(id);
    }
    private static async Task DiagnosticFeed(CustomRuleRuntime runtime, Action<bool,string> Check)
    {
        var store=new WebStore();store.SaveRaw("{\"blockedGroups\":[{\"id\":\"diagnostics\",\"name\":\"Diagnostics\",\"groupType\":\"custom\",\"enabled\":true}]}");
        var engine=new RuleEngine(store);engine.AttachRuntime(runtime);
        const string source="""
        (on,v)=>{
          on("fail",()=>{v.state.calls=(v.state.calls||0)+1;v.log("ordinary-log");throw new Error("actual-handler-error");});
          on("invalid",()=>{v.state.large="x".repeat(70000);throw new Error("actual-unsaved-handler-error");});
        }
        """;
        Check((await engine.RunRuleAsync("diagnostics",source))["ok"]?.GetValue<bool>()==true,"Actual diagnostic fixture loads");
        engine.DrainLogJson();
        await engine.FireUserEventAsync("fail","diagnostics",new(),new());
        var logs=JsonNode.Parse(engine.DrainLogJson()!)!.AsArray().OfType<JsonObject>().ToArray();
        Check(logs.Count(e=>e["source"]?.GetValue<string>()=="page-error" && e["level"]?.GetValue<string>()=="error" && e["message"]!.GetValue<string>().Contains("actual-handler-error"))==1 &&
            logs.Count(e=>e["source"]?.GetValue<string>()=="v.log" && e["level"]?.GetValue<string>()=="log" && e["message"]?.GetValue<string>()=="ordinary-log")==1,
            "Actual uncaught handler error and ordinary v.log remain distinct visible-feed entries");
        await engine.TickAsync(new(),new());
        Check(JsonNode.Parse(store.LoadRawJson()!)?["cbRuleState"]?["diagnostics"]?["calls"]?.GetValue<int>()==1 && engine.DrainLogJson()==null,"Uncaught error is not replayed on the next tick");
        await engine.FireUserEventAsync("invalid","diagnostics",new(),new());
        logs=JsonNode.Parse(engine.DrainLogJson()!)!.AsArray().OfType<JsonObject>().ToArray();
        Check(logs.Any(e=>e["source"]?.GetValue<string>()=="page-error" && e["level"]?.GetValue<string>()=="error" && e["message"]!.GetValue<string>().Contains("actual-unsaved-handler-error")) &&
            logs.Any(e=>e["message"]!.GetValue<string>().Contains("Event state could not be saved")),"Handler error remains visible when its resulting state is refused");
        Check(JsonNode.Parse(store.LoadRawJson()!)?["cbRuleState"]?["diagnostics"]?["large"]==null,"Invalid diagnostic-event state is preserved unsaved");
        await engine.UnloadGroupAsync("diagnostics");
    }
    private static double Used(JsonObject state) => ((JsonObject)state["objects"]!["budget"]!["bucketsMs"]!).Sum(p=>p.Value!.GetValue<double>());
}
