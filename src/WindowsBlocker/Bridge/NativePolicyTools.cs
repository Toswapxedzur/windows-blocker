using System.Text.Json;
using System.Text.Json.Nodes;
using WindowsBlocker.Rules;
using WindowsBlocker.WebUI;

namespace WindowsBlocker.Bridge;

// Tools validate through the same canonical JavaScript the editor runs. The
// native store owns writes; a concurrent edit causes a refusal, not stale data.
public sealed class NativePolicyTools(WebStore store, ConnectionHub hub, RuleEngine rules)
{
    public Func<string,string,object?[],Task<JsonNode?>>? Policy { get; set; }
    public Action<RuleTickOutput>? RulesEvent { get; set; }
    private readonly SemaphoreSlim _mutation = new(1);
    private readonly Dictionary<string,Confirmation> _confirmations = new();
    private sealed record Confirmation(string Tag, JsonNode State, DateTimeOffset Started);
    public static readonly string[] Names = ["list_groups","get_group","set_group","set_settings","add_application","remove_application","create_group","lock_group","unlock_group","set_lock_gates","delete_group","delete_all_groups","move_group","snooze_group","end_snooze","run_custom_rule","get_connection_status","list_links","link_groups","unlink_group"];
    private async Task<JsonNode?> Call(string method, params object?[] args) => Policy != null ? await Policy("CBGroupActions",method,args) : throw new InvalidOperationException("policy-runtime-unavailable");
    private async Task<JsonNode?> Scope(string method, params object?[] args) => Policy != null ? await Policy("CBGroupScopes",method,args) : throw new InvalidOperationException("policy-runtime-unavailable");
    private async Task<JsonNode?> Pin(string method, params object?[] args) => Policy != null ? await Policy("CBParentalPin",method,args) : throw new InvalidOperationException("policy-runtime-unavailable");
    private static void Refuse(JsonNode? result) { if (result is JsonObject obj && obj["error"] != null) throw new InvalidOperationException(obj["error"]!.GetValue<string>()); }
    private JsonObject Document() => JsonNode.Parse(store.LoadRawJson() ?? "{}") as JsonObject ?? new();
    private static JsonArray Groups(JsonObject doc) => doc["blockedGroups"] as JsonArray ?? new();
    private static JsonObject Group(JsonObject doc,string id) => Groups(doc).OfType<JsonObject>().FirstOrDefault(g => Text(g["id"]) == id) ?? throw new InvalidOperationException("group-not-found");
    private static string Text(JsonNode? node) => node?.GetValueKind() == JsonValueKind.String ? node.GetValue<string>().Trim() : "";
    private static string Source(JsonNode? node) => node?.GetValueKind() == JsonValueKind.String ? node.GetValue<string>() : "";
    private static JsonObject Public(JsonObject g)
    {
        var copy = (JsonObject)g.DeepClone(); copy["locked"] = ConnectionHub.IsLocked(g); copy["hasParentalPin"] = g["parentalPasswordHash"] != null && g["parentalPasswordSalt"] != null;
        copy.Remove("parentalPasswordHash"); copy.Remove("parentalPasswordSalt"); return copy;
    }
    private void Replace(string id,JsonObject expected,JsonObject replacement)
    {
        store.Update(doc => { var current = Group(doc,id); if (current.ToJsonString() != expected.ToJsonString()) throw new InvalidOperationException("store-changed-retry"); var groups = Groups(doc); var index = groups.IndexOf(current); groups[index] = replacement.DeepClone(); });
    }
    public async Task<JsonNode?> Invoke(string name,JsonObject args)
    {
        await _mutation.WaitAsync();
        try
        {
            hub.ReconcileLocal(store);
            var document = Document(); var id = Text(args["id"]); var now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            if (name == "list_groups") return new JsonObject { ["groups"] = new JsonArray(Groups(document).OfType<JsonObject>().Select(g => (JsonNode)new JsonObject { ["id"]=g["id"]?.DeepClone(),["name"]=g["name"]?.DeepClone(),["enabled"]=g["enabled"]?.DeepClone() ?? JsonValue.Create(true),["mode"]=g["mode"]?.DeepClone() ?? JsonValue.Create("instant"),["locked"]=ConnectionHub.IsLocked(g),["apps"]=(g["scopes"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(l => Text(l["surface"])=="apps")?["apps"] is JsonArray apps ? apps.Count : 0 }).ToArray()) };
            if (name == "get_group") return Public(Group(document,id));
            if (name == "get_connection_status") return JsonNode.Parse(hub.CurrentStatusJson());
            if (name == "list_links") return JsonNode.Parse(hub.ClustersJson());
            if (name == "link_groups" || name == "unlink_group")
            {
                var reason = name == "link_groups" ? hub.Link(Text(args["program"]),Text(args["groupId"]),Text(args["targetProgram"]),Text(args["targetGroupId"])) : hub.Unlink(Text(args["program"]),Text(args["groupId"]));
                if (reason != null) throw new InvalidOperationException(reason); return JsonNode.Parse(hub.ClustersJson());
            }
            if (name == "create_group")
            {
                var result = await Scope("createToolGroup",Groups(document),Text(args["groupType"]) is {Length:>0} type ? type : "site",args["patch"] ?? new JsonObject(),"desktop"); Refuse(result);
                var created = result?["group"] as JsonObject ?? throw new InvalidOperationException("invalid-group");
                store.Update(doc => { var groups = Groups(doc); if (groups.OfType<JsonObject>().Any(g => Text(g["name"]).Trim().Equals(Text(created["name"]).Trim(),StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("duplicate-name"); if (doc["blockedGroups"] == null) doc["blockedGroups"] = groups; groups.Add(created.DeepClone()); }); return new JsonObject { ["group"] = Public(created) };
            }
            if (name == "set_settings")
            {
                var patch = args["patch"] as JsonObject ?? throw new InvalidOperationException("missing-patch"); if (patch.Count == 0) throw new InvalidOperationException("missing-patch");
                foreach (var key in patch.Select(p => p.Key)) if (key is not ("quickAddEnabled" or "quitRetryMinutes" or "quickAddGroupId")) throw new InvalidOperationException("not-an-editor-setting:" + key);
                var validation = (JsonObject)patch.DeepClone(); validation.Remove("quickAddGroupId");
                var problem = await Call("validateSettingsPatch",validation); if (problem?.GetValueKind() == JsonValueKind.String) throw new InvalidOperationException(problem.GetValue<string>());
                if (patch["quickAddGroupId"] != null && Text(patch["quickAddGroupId"]).Length > 0) _ = Group(document,Text(patch["quickAddGroupId"]));
                var settings = document["globalSettings"] as JsonObject ?? new(); var merged = (JsonObject)settings.DeepClone(); foreach (var (k,v) in validation) merged[k]=v?.DeepClone();
                var sanitized = await Call("sanitizeGlobalSettings",merged) as JsonObject ?? throw new InvalidOperationException("invalid-settings");
                store.Update(doc => { doc["globalSettings"]=sanitized.DeepClone(); if (patch.ContainsKey("quickAddGroupId")) doc["quickAddGroupId"]=Text(patch["quickAddGroupId"]); });
                return new JsonObject { ["globalSettings"]=new JsonObject { ["quickAddEnabled"]=sanitized["quickAddEnabled"]?.DeepClone(),["quitRetryMinutes"]=sanitized["quitRetryMinutes"]?.DeepClone() },["quickAddGroupId"]=Text(Document()["quickAddGroupId"]) };
            }
            if (name == "delete_all_groups")
            {
                var plan = await Call("deleteAllPlan",Groups(document),now); Refuse(plan);
                var confirmationTag = plan?["pinHashes"]?.ToJsonString() ?? "no-pin";
                foreach (var pinGroup in (NewConfirmation("delete-all", confirmationTag) ? plan?["pinGroups"] as JsonArray ?? new() : new JsonArray()).OfType<JsonObject>())
                {
                    var pins = args["pins"] as JsonArray ?? new(); var index = (plan!["pinGroups"] as JsonArray)!.IndexOf(pinGroup);
                    await CheckPin(document,pinGroup,index < pins.Count ? Text(pins[index]) : "",now);
                }
                if (plan?["needsConfirmation"]?.GetValue<bool>() == true)
                {
                    var pending = await Confirm("delete-all",confirmationTag,args["confirm"]?.GetValue<bool>() == true,now);
                    if (pending != null) return pending;
                }
                var ids = Groups(document).OfType<JsonObject>().Select(g=>Text(g["id"])).ToArray();
                store.Update(doc => { if (Groups(doc).ToJsonString() != Groups(document).ToJsonString()) throw new InvalidOperationException("store-changed-retry"); doc["blockedGroups"] = new JsonArray(); foreach (var key in PerGroupKeys) doc[key] = new JsonObject(); });
                foreach (var deletedId in ids) { await rules.UnloadGroupAsync(deletedId); rules.ClearLog(deletedId); } hub.ReconcileLocal(store);
                return new JsonObject { ["deleted"] = ids.Length };
            }
            var group = Group(document,id);
            if (name == "run_custom_rule")
            {
                var source = args.ContainsKey("source") ? Source(args["source"]) : Source(group["blockingRulesText"] ?? group["activeEventSource"]);
                var run = await rules.RunRuleAsync(id,source); var error=Text(run["error"]);
                if (error is "group-not-found" or "group-locked" or "rules-not-running") throw new InvalidOperationException(error);
                return new JsonObject { ["ran"]=run["ok"]?.DeepClone(),["handlers"]=run["handlers"]?.DeepClone(),["error"]=run["error"]?.DeepClone() };
            }
            if (name is "lock_group" or "set_lock_gates")
            {
                var gates = new JsonObject(); if (args.ContainsKey("waitHours")) gates["waitHours"] = args["waitHours"]?.DeepClone();
                if (args["clearPin"]?.GetValue<bool>() == true) { if (group["parentalPasswordHash"] != null) await CheckPin(document,group,Text(args["pin"]),now); gates["pinFields"] = null; }
                else if (args.ContainsKey("pin"))
                {
                    if (group["parentalPasswordHash"] != null) throw new InvalidOperationException("pin-already-set");
                    if ((await Pin("isValidParentalPin",Text(args["pin"])))?.GetValue<bool>() != true) throw new InvalidOperationException("invalid-pin");
                    gates["pinFields"] = await Pin("newPinFieldsSync",Text(args["pin"]));
                }
                var result = await Call(name == "set_lock_gates" ? "setGates" : ConnectionHub.IsLocked(group) ? "tighten" : "lockWithGates",group,gates,now); Refuse(result);
                var replacement = result?["group"] as JsonObject ?? throw new InvalidOperationException("invalid-group"); Replace(id,group,replacement); return new JsonObject { ["group"]=Public(replacement) };
            }
            if (name == "unlock_group")
            {
                var plan = await Call("unlockPlan",group,now); Refuse(plan);
                if (NewConfirmation("unlock:" + id,group["lockVersion"]?.ToJsonString() ?? "0") && plan?["needsPin"]?.GetValue<bool>() == true) await CheckPin(document,group,Text(args["pin"]),now);
                var pending = await Confirm("unlock:" + id,group["lockVersion"]?.ToJsonString() ?? "0",args["confirm"]?.GetValue<bool>() == true,now);
                if (pending != null) { pending["unlocked"]=false; return pending; }
                var replacement = await Call("unlock",group) as JsonObject ?? throw new InvalidOperationException("invalid-group"); Replace(id,group,replacement); return new JsonObject { ["unlocked"]=true,["group"]=Public(replacement) };
            }
            if (name is "snooze_group" or "end_snooze")
            {
                if (name == "snooze_group" && Text(group["groupType"]) == "custom")
                {
                    if (group["allowSnooze"]?.GetValueKind() == JsonValueKind.False) throw new InvalidOperationException("snooze-disabled");
                    var effects = await rules.FireUserEventAsync("snoozePress",id,new(),new()); RulesEvent?.Invoke(effects); return new JsonObject { ["snoozePressed"]=true };
                }
                var snoozes = document["groupSnoozes"] as JsonObject ?? new(); var entry = snoozes[id];
                JsonNode? next;
                if (name == "end_snooze") { var result = await Call("endSnoozeEntry",entry,now); Refuse(result); next = result?["entry"]?.DeepClone(); }
                else
                {
                    var plan = await Call("snoozePlan",group,entry,now); Refuse(plan);
                    var count = plan?["confirmations"]?.GetValue<int>() ?? 0;
                    if (count > 0) { var pending = await Confirm("snooze:" + id,group.ToJsonString(),args["confirm"]?.GetValue<bool>() == true,now,count); if (pending != null) { pending["snoozed"]=false; return pending; } }
                    next = await Call("snoozeEntry",group,now,document["usageResetAtMs"]?[id],document["usageTimersMs"]?[id] ?? JsonValue.Create(0));
                }
                store.Merge(new() { ["groupSnoozes"] = new JsonObject { [id] = next } }); hub.ReconcileLocal(store); return new JsonObject { [name == "end_snooze" ? "ended" : "snoozed"] = true,["snooze"]=next?.DeepClone() };
            }
            if (ConnectionHub.IsLocked(group)) throw new InvalidOperationException("group-locked");
            if (name == "delete_group")
            {
                store.Update(doc => { var current = Group(doc,id); if (ConnectionHub.IsLocked(current)) throw new InvalidOperationException("group-locked"); Groups(doc).Remove(current); foreach (var key in PerGroupKeys) if (doc[key] is JsonObject map) map.Remove(id); }); await rules.UnloadGroupAsync(id); rules.ClearLog(id); hub.ReconcileLocal(store); return new JsonObject { ["deleted"] = id };
            }
            if (name == "move_group")
            {
                var target = args["index"]?.GetValue<int>() ?? -1;
                store.Update(doc => { var groups = Groups(doc); if (target < 0 || target >= groups.Count) throw new InvalidOperationException("invalid-index"); var current = Group(doc,id); if (ConnectionHub.IsLocked(current)) throw new InvalidOperationException("group-locked"); var index = groups.IndexOf(current); groups.RemoveAt(index); groups.Insert(target,current); }); return new JsonObject { ["order"]=new JsonArray(Groups(Document()).OfType<JsonObject>().Select(g=>(JsonNode)JsonValue.Create(Text(g["id"]))!).ToArray()) };
            }
            JsonObject patchGroup;
            if (name is "add_application" or "remove_application")
            {
                var appId = WindowsAppId.Normalize(Text(args["appId"])) ?? throw new InvalidOperationException("invalid-appId");
                var scopes = group["scopes"] is JsonArray oldScopes ? (JsonArray)oldScopes.DeepClone() : new();
                var appLine = scopes.OfType<JsonObject>().FirstOrDefault(l => Text(l["surface"]) == "apps");
                if (appLine == null) { appLine = new() { ["surface"]="apps",["id"]="apps-1",["platform"]=null,["action"]="block",["apps"]=new JsonArray(),["appsExcept"]=false }; scopes.Add(appLine); }
                var apps = appLine["apps"] as JsonArray ?? new(); appLine["apps"]=apps;
                var matching=apps.OfType<JsonObject>().Where(a=>Text(a["id"]).Equals(appId,StringComparison.OrdinalIgnoreCase)).ToList();
                if (name == "remove_application") foreach(var app in matching) apps.Remove(app);
                else if (matching.Count==0) apps.Add(new JsonObject { ["id"]=appId,["name"]=Text(args["name"]) is {Length:>0} display ? display : appId });
                patchGroup=new() { ["scopes"]=scopes };

            }
            else if (name == "set_group") { patchGroup = args["patch"] as JsonObject ?? throw new InvalidOperationException("missing-patch"); if(patchGroup.Count==0) throw new InvalidOperationException("missing-patch"); }
            else throw new InvalidOperationException("unknown-tool");
            var edited = await Scope("applyToolEdit",group,patchGroup,"desktop"); Refuse(edited);
            var replacementGroup = edited?["group"] as JsonObject ?? throw new InvalidOperationException("invalid-group");
            if ((await Call("nameTaken",Groups(document),Text(replacementGroup["name"]),id))?.GetValue<bool>() == true) throw new InvalidOperationException("duplicate-name");
            var restart = (await Call("budgetRestarts",group,replacementGroup))?.GetValue<bool>() == true;
            Replace(id,group,replacementGroup);
            if (restart) store.Update(doc => { foreach (var key in new[] { "usageTimersMs","usageResetAtMs","usageBucketsMs","groupSnoozes" }) if (doc[key] is JsonObject map) map.Remove(id); });
            return new JsonObject { ["group"]=Public(replacementGroup) };
        }
        finally { _mutation.Release(); }
    }
    public async Task<bool> SettleSnoozesAsync()
    {
        if(Policy==null) return false;
        await _mutation.WaitAsync();
        try
        {
            var document=Document(); if(document["groupSnoozes"] is not JsonObject snoozes) return false;
            var now=DateTimeOffset.Now.ToUnixTimeMilliseconds(); var settledEntries=new JsonObject(); var increments=new Dictionary<string,double>();
            foreach(var (id,value) in snoozes)
            {
                if(value is not JsonObject entry) continue;
                var current=(JsonObject)entry.DeepClone();
                if(Text(entry["kind"])=="budget") { var group=Groups(document).OfType<JsonObject>().FirstOrDefault(g=>Text(g["id"])==id); var settled=await Call("settleBudgetSnooze",entry,group,document["usageTimersMs"]?[id] ?? JsonValue.Create(0),now); if(settled is JsonObject end) current=end; }
                if(current["activeMsApplied"]?.GetValueKind()!=JsonValueKind.True && current["untilMs"]?.GetValueKind()==JsonValueKind.Number && current["untilMs"]!.GetValue<double>()<=now)
                {
                    increments[id]=(await Call("snoozeCountedMs",current))?.GetValue<double>() ?? 0; current["activeMsApplied"]=true;
                }
                if(current.ToJsonString()!=entry.ToJsonString()) settledEntries[id]=current;
            }
            if(settledEntries.Count==0) return false;
            store.Update(root=> { var entries=root["groupSnoozes"] as JsonObject ?? new(); root["groupSnoozes"]=entries; var totals=root["groupSnoozeTotalsMs"] as JsonObject ?? new(); root["groupSnoozeTotalsMs"]=totals;
                foreach(var (id,entry) in settledEntries) if(entries[id]?.ToJsonString()==snoozes[id]?.ToJsonString()) { entries[id]=entry?.DeepClone(); if(increments.TryGetValue(id,out var delta)) totals[id]=(totals[id]?.GetValue<double>() ?? 0)+Math.Max(0,delta); } });
            return true;
        }
        finally { _mutation.Release(); }
    }
    private async Task CheckPin(JsonObject document,JsonObject group,string pin,long now)
    {
        var attempts = document["parentalPinAttempts"] as JsonObject ?? new();
        var result = await Pin("checkSync",attempts,group,pin,now) as JsonObject ?? throw new InvalidOperationException("invalid-pin-result");
        if (result["attempts"] is JsonObject updated) store.Merge(new() { ["parentalPinAttempts"] = updated.DeepClone() });
        if (result["ok"]?.GetValue<bool>() != true) throw new InvalidOperationException((result["waiting"]?.GetValue<bool>() == true ? "pin-wait:" : "pin-wrong:") + Math.Ceiling(result["waitMs"]?.GetValue<double>()/1000 ?? 0));
        if(result["upgradedHash"] is JsonNode upgraded) { var updatedGroup=(JsonObject)group.DeepClone(); updatedGroup["parentalPasswordHash"]=upgraded.DeepClone(); Replace(Text(group["id"]),group,updatedGroup); group["parentalPasswordHash"]=upgraded.DeepClone(); }
    }
    private bool NewConfirmation(string key,string tag) => !_confirmations.TryGetValue(key,out var existing) || existing.Tag != tag || DateTimeOffset.Now - existing.Started > TimeSpan.FromMinutes(5);
    private async Task<JsonObject?> Confirm(string key,string tag,bool clicked,long now,int count=10)
    {
        if (!_confirmations.TryGetValue(key,out var confirmation) || confirmation.Tag != tag || DateTimeOffset.Now - confirmation.Started > TimeSpan.FromMinutes(5))
        {
            var state = await Call("confirmStart",now,count) ?? throw new InvalidOperationException("invalid-confirmation");
            _confirmations[key] = new(tag,state,DateTimeOffset.Now); return new() { ["confirmationsLeft"] = count,["confirmAfterSeconds"] = 5,["next"] = "call again with confirm: true every 5 s until confirmationsLeft is 0 (within 5 minutes)" };
        }
        if (!clicked) { _confirmations.Remove(key); return await Confirm(key,tag,false,now,count); }
        var step = await Call("confirmStep",confirmation.State,now) as JsonObject ?? throw new InvalidOperationException("invalid-confirmation");
        if (step["waitMs"] is JsonNode wait && wait.GetValue<double>() > 0) throw new InvalidOperationException("confirm-wait:" + Math.Ceiling(wait.GetValue<double>()/1000));
        if (step["done"]?.GetValue<bool>() == true) { _confirmations.Remove(key); return null; }
        _confirmations[key] = confirmation with { State = step["state"]!.DeepClone() }; return new() { ["confirmationsLeft"] = step["state"]?["left"]?.DeepClone(),["waitMs"] = step["waitMs"]?.DeepClone(),["confirmAfterSeconds"] = 5,["next"] = "call again with confirm: true every 5 s until confirmationsLeft is 0 (within 5 minutes)" };
    }
    private static readonly string[] PerGroupKeys = ["usageTimersMs","usageResetAtMs","usageBucketsMs","groupSnoozes","groupSnoozeTotalsMs","cbRuleState","cbRuleLog","cbRuleQuarantine","parentalPinAttempts"];
}
