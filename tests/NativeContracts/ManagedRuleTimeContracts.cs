using System.Text.Json.Nodes;
using WindowsBlocker.Bridge;
using WindowsBlocker.WebUI;

// Screen-free native adapter checks. Pure RuleCore semantics are checked by the
// common JS suites; this controls the real async hub/persistence boundaries.
internal static class ManagedRuleTimeContracts
{
    public static async Task Run()
    {
        void Check(bool ok, string label) { if (!ok) throw new Exception(label); Console.WriteLine("PASS " + label); }
        var store = new WebStore();
        store.SaveRaw("{\"blockedGroups\":[{\"id\":\"w\",\"name\":\"Windows\",\"groupType\":\"custom\",\"enabled\":true}],\"cbRuleState\":{\"other\":{\"keep\":7}}}");
        store.CommitRuleDispatch(new Dictionary<string,string>{{"w","{\"done\":true}"}});
        var saved=File.ReadAllText(store.FilePath);
        Check(JsonNode.Parse(saved)?["cbRuleState"]?["w"]?["done"]?.GetValue<bool>()==true && JsonNode.Parse(saved)?["cbRuleState"]?["other"]?["keep"]?.GetValue<int>()==7,"native event writer saves completion and preserves unrelated state");
        using (var held=new FileStream(store.FilePath,FileMode.Open,FileAccess.Read,FileShare.Read)) {
            var refused=false;try {store.CommitRuleDispatch(new Dictionary<string,string>{{"w","{\"done\":false}"}});}catch(IOException){refused=true;}
            Check(refused && File.ReadAllText(store.FilePath)==saved,"native event writer exposes replacement failure and preserves completion bytes");
        }
        var rejected=false;try{store.CommitRuleDispatch(new Dictionary<string,string>{{"deleted","{}"}});}catch(InvalidDataException){rejected=true;}
        Check(rejected && File.ReadAllText(store.FilePath)==saved,"native event writer cannot resurrect a deleted group");
        JsonArray Roster(string id)=>new(new JsonObject{["id"]=id,["name"]=id});
        var hub=new ConnectionHub();hub.SetRoster("windowsapp",Roster("w"));hub.SetRoster("chrome",Roster("c"));
        Check(hub.Link("windowsapp","w","chrome","c")==null,"managed time adapter fixture links without starting a listener");
        var cluster=JsonNode.Parse(hub.ClustersJson())!["clusters"]![0]!["id"]!.GetValue<string>();
        var absent=await hub.SyncRuleTimeAsync("windowsapp","w",cluster,null,new());
        Check(absent["ready"]?.GetValue<bool>()==false,"native adapter fails closed until its trusted policy worker is available");
        var entered=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.RuleTimePolicy=async (method,args)=>{
            Check(method=="syncTime" && args[1] is JsonObject request && request["member"]?.GetValue<string>()=="windowsapp/w","native adapter binds the policy request to the pinned member");
            entered.TrySetResult(true);await release.Task.WaitAsync(TimeSpan.FromSeconds(5));
            return new(){["ok"]=true,["ready"]=true,["state"]=new JsonObject{["version"]=1,["objects"]=new JsonObject()},["acknowledgedSerial"]=1};
        };
        var pending=hub.SyncRuleTimeAsync("windowsapp","w",cluster,null,new());
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Check(hub.Unlink("chrome","c")==null,"membership can detach while policy work is awaiting its worker");
        release.TrySetResult(true);var stale=await pending.WaitAsync(TimeSpan.FromSeconds(5));
        Check(stale["ready"]?.GetValue<bool>()==false && stale["error"]?.GetValue<string>()=="custom-time-membership-changed","late worker result cannot acknowledge or resurrect a detached cluster");
        var remembered=new JsonObject{["version"]=1,["objects"]=new JsonObject(),["scope"]=cluster};
        var unknown=await hub.SyncRuleTimeAsync("windowsapp","w",null,remembered,new());
        Check(unknown["ready"]?.GetValue<bool>()==false && unknown["linked"]?.GetValue<bool>()==true,"missing registry cannot silently fork remembered shared time into a local allowance");
        Console.WriteLine("WINDOWS_MANAGED_TIME_ADAPTER_PASS");
    }
}
