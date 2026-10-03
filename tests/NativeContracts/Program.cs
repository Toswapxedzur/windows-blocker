using System.Text.Json.Nodes;
using WindowsBlocker.Bridge;
using WindowsBlocker.Core;
using WindowsBlocker.Enforcement;
using WindowsBlocker.WebUI;

Environment.SetEnvironmentVariable("VAULT_ENVIRONMENT", "development");
Environment.SetEnvironmentVariable("VAULT_STORAGE_ROOT", Path.Combine(Path.GetTempPath(), "vault-contracts-" + Guid.NewGuid()));
static void Check(bool passed, string description) { if (!passed) throw new Exception(description); Console.WriteLine("PASS " + description); }
NativeLanguageContracts.Run(Check);
var sourceSeed = "{\"blockedGroups\":[]}";
var seed = NativeEditorContract.SeedScript(sourceSeed);
using (var seeded = System.Text.Json.JsonDocument.Parse(seed["window.__cbNativeStoreSeed = ".Length..^1])) Check(seeded.RootElement.ValueKind == System.Text.Json.JsonValueKind.String && seeded.RootElement.GetString() == sourceSeed, "canonical editor seed is JSON string literal");
Check(NativeEditorContract.TrustedUri("https://appassets.windowsblocker/popup.html") && !NativeEditorContract.TrustedUri("https://appassets.windowsblocker.evil/popup.html") && !NativeEditorContract.TrustedUri("https://appassets.windowsblocker:444/popup.html"), "native bridge accepts only exact local editor origin");
Check(NativeEditorContract.ExternalLink("https://example.com/source", true) && NativeEditorContract.ExternalLink("http://example.com/help",true) && !NativeEditorContract.ExternalLink("https://example.com/source",false), "External source and help links require a user gesture");
Check(!NativeEditorContract.ExternalLink("file:///C:/Windows/cmd.exe",true) && !NativeEditorContract.ExternalLink("javascript:alert(1)",true) && !NativeEditorContract.ExternalLink("https://user:pass@example.com/",true) && !NativeEditorContract.ExternalLink("https://appassets.windowsblocker/popup.html",true), "External link policy rejects unsafe schemes, credentials and embedded editor URLs");
var groups = ChromeExtensionImporter.ImportGroups("""
{"blockedGroups":[{"id":"a","enabled":true,"groupType":"site","activeDays":[],"scopes":[{"surface":"apps","appsExcept":true,"apps":[{"id":"C:\\Apps\\editor.exe","name":"Editor"}]}]},{"id":"b","enabled":true,"groupType":"custom","activeEventSource":"(on,v)=>{}","blockingRulesText":"retired helper","apps":[{"id":"stale.exe"}]}]}
""").Groups;
Check(groups[0].ActiveDays.Count == 0, "empty schedule means never");
Check(groups[0].ApplicationAllowlist && groups[0].Targets.Count == 1, "current appsExcept scope imports");
Check(groups[1].Targets.Count == 0 && groups[1].CustomRuleSource == "(on,v)=>{}", "stale app fields ignored and active source used");
var staleBudget=ChromeExtensionImporter.ImportGroups("""{"blockedGroups":[{"id":"old","enabled":true,"mode":"timer","allowedMinutes":"broken"}]}""").Groups[0];
Check(staleBudget.Mode==BlockingMode.Instant && staleBudget.AllowedMinutes==15,"Retired timer mode is not restored and malformed numeric state is bounded");
var registry = new BlockedAppRegistry(); registry.Update([@"c:\apps\editor.exe"]);
Check(!registry.IsBlocked(new() { ProcessId=1, ExecutablePath=@"c:\other\editor.exe", ExecutableName="editor.exe" }), "different executable path is not widened by filename");
registry.Update(["editor.exe"]); Check(!registry.HasAny,"retired filename identities are ignored safely");
Check(WindowsAppId.Normalize(@"C:\Apps\editor.exe")!=null && WindowsAppId.Normalize("Package_123!App")!=null && WindowsAppId.Normalize("editor.exe")==null,"Windows application IDs require exact paths or AUMIDs");
var longId=@"C:\Apps\"+string.Join("\\",Enumerable.Repeat(new string('x',100),4))+"\\editor.exe";
Check(WindowsAppId.Normalize(longId)==longId,"Exact Windows app IDs preserve supported paths above 255 characters");
Check(ConnectionHub.ValidClassifierRequest("abc","collect",new JsonObject()) && !ConnectionHub.ValidClassifierRequest("abc","arbitrary-native-action",new JsonObject()) && !ConnectionHub.ValidClassifierRequest("a b","collect",new JsonObject()),"Browser classifier operation vocabulary and request IDs bounded");
var quit = new QuitRequestScheduler(); var now = DateTimeOffset.Now;
Check(quit.ShouldRequest("12:1", now, TimeSpan.Zero), "first process quit requested");
Check(!quit.ShouldRequest("12:1", now.AddHours(5), TimeSpan.Zero), "zero retry preserves refusal forever while process lives");
Check(!quit.ShouldRequest("12:1", now.AddMinutes(1), TimeSpan.FromMinutes(2)), "save prompt untouched before retry");
Check(quit.ShouldRequest("12:1", now.AddMinutes(2), TimeSpan.FromMinutes(2)), "configured retry requested on deadline");
Check(quit.ShouldRequest("12:2", now, TimeSpan.Zero), "PID reuse gets independent quit");
quit.Reconcile(new HashSet<string>()); Check(quit.ShouldRequest("12:1", now, TimeSpan.Zero), "exited process forgotten");
var challenge = LocalHubAuthentication.Challenge(); var secret = Enumerable.Range(0,32).Select(n => (byte)n).ToArray();
var proof = LocalHubAuthentication.Proof("chrome", challenge, secret);
Check(LocalHubAuthentication.Verify("chrome", challenge, proof, secret), "protocol4 proof validates");
Check(!LocalHubAuthentication.Verify("edge", challenge, proof, secret), "proof bound to browser identity");
var hello = new JsonObject { ["v"]=4,["program"]="chrome",["challenge"]=challenge,["proof"]=proof };
Check(ConnectionHub.HelloRejectionReason(hello,challenge,secret) == null, "protocol4 hello accepted");
hello["v"]=2; Check(ConnectionHub.HelloRejectionReason(hello,challenge,secret)=="protocol-mismatch", "retired protocol refused");
var hub = new ConnectionHub();
Check(!(JsonNode.Parse(hub.CurrentStatusJson())!["peers"] as JsonArray)!.OfType<JsonObject>().Any(p=>p["program"]?.GetValue<string>()=="classifier"),"Absent embedded Classifier handler is not advertised to browsers");
hub.ClassifierRequest=(_,_,_,_)=>Task.FromResult(new JsonObject());
Check((JsonNode.Parse(hub.CurrentStatusJson())!["peers"] as JsonArray)!.OfType<JsonObject>().Any(p=>p["program"]?.GetValue<string>()=="classifier" && p["connected"]?.GetValue<bool>()==true),"Embedded shared Classifier route is discoverable by canonical browser readiness");
var routePeer=Guid.NewGuid();var otherPeer=Guid.NewGuid();
Check(hub.BeginClassifierRequest(routePeer,"same",out var originalLease)==null && hub.BeginClassifierRequest(routePeer,"same",out _)=="duplicate-classifier-request","Duplicate authenticated-peer Classifier correlation cannot dispatch twice");
Check(hub.BeginClassifierRequest(otherPeer,"same",out var otherLease)==null,"Independent authenticated browsers keep separate request identities");
for(var i=0;i<30;i++) if(hub.BeginClassifierRequest(routePeer,"slot-"+i,out _)!=null) throw new Exception("Classifier capacity filled early");
Check(hub.BeginClassifierRequest(otherPeer,"overflow",out _)=="classifier-busy","Embedded Classifier relay admits at most 32 outstanding requests");
hub.EndClassifierRequest(otherPeer,"same",otherLease);
Check(hub.BeginClassifierRequest(otherPeer,"replacement",out var replacementLease)==null,"Completed or timed-out Classifier lease releases relay capacity");
hub.RemoveClassifierRequests(routePeer);
Check(hub.BeginClassifierRequest(routePeer,"same",out var restartedLease)==null,"Peer disconnect clears its pending Classifier correlations");
hub.EndClassifierRequest(routePeer,"same",originalLease);
Check(hub.BeginClassifierRequest(routePeer,"same",out _)=="duplicate-classifier-request","Late old generation completion cannot remove a replacement Classifier request");
hub.EndClassifierRequest(routePeer,"same",restartedLease);hub.EndClassifierRequest(otherPeer,"replacement",replacementLease);
hub.SetRoster("windowsapp", new JsonArray(new JsonObject { ["id"]="local",["name"]="Same",["frozen"]=false }));
hub.SetRoster("chrome", new JsonArray(new JsonObject { ["id"]="browser",["name"]="Different",["frozen"]=false }));
Check(hub.Link("windowsapp","local","chrome","browser") == null, "explicit link joins differently named groups");
Check(JsonNode.Parse(hub.ClustersJson())?["clusters"]?[0]?["shared"]?["scopes"]==null,"Unseeded linked group does not overwrite scope lines");
Check(hub.SharedUsage("Same") == null, "display name never identifies a link");
hub.ApplySync("windowsapp","local",new JsonObject { ["scalars"]=new JsonObject { ["name"]="Chosen",["allowedMinutes"]=10,["resetIntervalHours"]=24 },["scopes"]=new JsonArray(new JsonObject { ["id"]="apps-1",["surface"]="apps",["apps"]=new JsonArray() }),["ts"]=1 });
hub.ApplySync("chrome","browser",new JsonObject { ["scalars"]=new JsonObject { ["name"]="Browser" },["scopes"]=new JsonArray(new JsonObject { ["id"]="site-1",["surface"]="site",["sites"]=new JsonArray("example.com") }),["usageMs"]=5000,["ts"]=1 });
Check(hub.SharedUsage("local")?.Ms == 5000, "linked usage seeds once");

hub.ReportLocalUsage("local",1000,now.ToUnixTimeMilliseconds()); Check(hub.SharedUsage("local")?.Ms == 6000, "linked usage accumulates deltas");
hub.SetRoster("chrome",new JsonArray(new JsonObject { ["id"]="replacement",["name"]="Different" }));
Check(hub.ActiveClusterCount()==0, "delete and recreate does not rejoin by name");
var repeatedHub = new ConnectionHub();
repeatedHub.SetRoster("chrome",new JsonArray(new JsonObject { ["id"]="browser",["name"]="Chrome" }));
repeatedHub.SetRoster("windowsapp",new JsonArray(new JsonObject { ["id"]="local",["name"]="Native" }));
repeatedHub.Link("windowsapp","local","chrome","browser");
// A second browser's first contribution replaces only its matching entry,
// keeping a separately configured Shorts entry for the same website.
repeatedHub.ApplySync("chrome","browser",new JsonObject { ["scopes"]=new JsonArray(
    new JsonObject { ["surface"]="items",["platform"]="youtube",["entryID"]="youtube:shorts",["form"]="short" },
    new JsonObject { ["surface"]="items",["platform"]="youtube",["entryID"]="youtube:creator",["sources"]=new JsonArray("old") }),["ts"]=2 });
repeatedHub.SetRoster("edge",new JsonArray(new JsonObject { ["id"]="edge-browser",["name"]="Edge",["frozen"]=false }));
Check(repeatedHub.Link("chrome","browser","edge","edge-browser")==null,"another browser joins the linked group");
repeatedHub.ApplySync("edge","edge-browser",new JsonObject { ["scopes"]=new JsonArray(
    new JsonObject { ["surface"]="items",["platform"]="youtube",["entryID"]="youtube:creator",["sources"]=new JsonArray("new") }),["ts"]=3 });
var repeatedScopes=(JsonNode.Parse(repeatedHub.ClustersJson())?["clusters"]?[0]?["shared"]?["scopes"] as JsonArray)!.OfType<JsonObject>().ToList();
Check(repeatedScopes.Any(line=>line["entryID"]?.GetValue<string>()=="youtube:shorts" && line["form"]?.GetValue<string>()=="short") && repeatedScopes.Count(line=>line["entryID"]?.GetValue<string>()=="youtube:creator")==1 && repeatedScopes.Single(line=>line["entryID"]?.GetValue<string>()=="youtube:creator")["sources"]?[0]?.GetValue<string>()=="new","linked repeated websites merge independently by stable entry ID");

var budgets=new ConnectionHub(); budgets.SetRoster("windowsapp",new JsonArray(new JsonObject { ["id"]="fixed",["name"]="Fixed" },new JsonObject { ["id"]="rolling",["name"]="Rolling" })); budgets.SetRoster("chrome",new JsonArray(new JsonObject { ["id"]="fixed-web",["name"]="Fixed" },new JsonObject { ["id"]="rolling-web",["name"]="Rolling" }));
budgets.Link("windowsapp","fixed","chrome","fixed-web"); budgets.Link("windowsapp","rolling","chrome","rolling-web");
var localUsage=new WebStore.UsageTimers(); localUsage.TimersMs["fixed"]=12_000;localUsage.ResetAtMs["fixed"]=now.ToUnixTimeMilliseconds();
var minute=UsageBudget.BucketStartMs(now.ToUnixTimeMilliseconds());localUsage.BucketsMs["rolling"]=new(){[minute]=7_000};localUsage.TimersMs["rolling"]=7_000;
var budgetGroups=new[]{new BlockGroup{Id="fixed",Mode=BlockingMode.AfterMinutes},new BlockGroup{Id="rolling",Mode=BlockingMode.AfterMinutes,RollingLimit=true}};
var seededBudgets=new HashSet<string>();var usageWrites=new Dictionary<string,double>();var resetWrites=new Dictionary<string,double>();var bucketWrites=new Dictionary<string,Dictionary<double,double>>();
EnforcementEngine.AdoptLinkedUsage(budgets,budgetGroups,localUsage,seededBudgets,now,usageWrites,resetWrites,bucketWrites);
Check(budgets.SharedUsage("fixed")?.Ms==12_000 && localUsage.TimersMs["fixed"]==12_000,"Joining an inactive fixed budget preserves local usage before shared adoption");
Check(budgets.SharedUsage("rolling")?.Buckets.GetValueOrDefault(minute)==7_000 && localUsage.TimersMs["rolling"]==7_000,"Joining a rolling budget preserves local minute history before shared adoption");
budgets.ReportLocalUsage("fixed",1_000,now.ToUnixTimeMilliseconds());EnforcementEngine.AdoptLinkedUsage(budgets,budgetGroups,localUsage,seededBudgets,now,usageWrites,resetWrites,bucketWrites);
Check(localUsage.TimersMs["fixed"]==13_000 && usageWrites["fixed"]==13_000,"Shared budget changes persist without reseeding the old local total");
double SnoozeTotal(ConnectionHub source,string groupId) => (JsonNode.Parse(source.ClustersJson())?["clusters"] as JsonArray)!.OfType<JsonObject>().First(c=>(c["members"] as JsonArray)!.OfType<JsonObject>().Any(m=>m["program"]?.GetValue<string>()=="windowsapp" && m["groupId"]?.GetValue<string>()==groupId))["shared"]!["snoozeTotalMs"]!.GetValue<double>();
var at=DateTimeOffset.Now.ToUnixTimeMilliseconds();
budgets.ApplySync("windowsapp","fixed",new(){["scalars"]=new JsonObject{["allowedMinutes"]=1,["resetIntervalHours"]=24},["ts"]=1});
budgets.ReportLocalUsage("fixed",46_000,at);
budgets.ApplySync("chrome","fixed-web",new(){["snooze"]=new JsonObject{["kind"]="budget",["startsAtMs"]=at-1_000,["untilMs"]=at+120_000,["extraMs"]=60_000},["snoozeTs"]=at});
budgets.ApplySync("chrome","fixed-web",new(){["usageDeltaMs"]=2_000,["usageDeltaAnchorMs"]=budgets.SharedUsage("fixed")!.Value.ResetAtMs});budgets.ReportLocalUsage("fixed",500,at);
Check(SnoozeTotal(budgets,"fixed")==1_500,"Cross-device fixed budget snooze counts only accepted usage above the plain allowance");
budgets.ApplySync("chrome","fixed-web",new(){["usageMs"]=61_500});budgets.ApplySync("chrome","fixed-web",new(){["usageDeltaMs"]=1_000,["usageDeltaAnchorMs"]=at-100_000});
Check(SnoozeTotal(budgets,"fixed")==1_500,"Absolute adoption and stale-period fixed deltas do not echo snooze consumption");
budgets.ApplySync("chrome","fixed-web",new(){["snooze"]=new JsonObject{["kind"]="budget",["startsAtMs"]=at-1_000,["untilMs"]=at-1,["extraMs"]=60_000},["snoozeTs"]=at+1});
Check(SnoozeTotal(budgets,"fixed")==1_500,"Ending a linked budget snooze adds no elapsed clock time");
var minuteKey=minute.ToString(System.Globalization.CultureInfo.InvariantCulture);var expiredKey=(minute-2*3_600_000).ToString(System.Globalization.CultureInfo.InvariantCulture);
budgets.ApplySync("windowsapp","rolling",new(){["scalars"]=new JsonObject{["allowedMinutes"]=1,["resetIntervalHours"]=1,["rollingLimit"]=true},["ts"]=1,["usageBucketsSeed"]=new JsonObject{[minuteKey]=58_000,[expiredKey]=900_000}});
budgets.ApplySync("chrome","rolling-web",new(){["snooze"]=new JsonObject{["kind"]="budget",["startsAtMs"]=at-1_000,["untilMs"]=at+120_000,["extraMs"]=60_000},["snoozeTs"]=at});
budgets.ApplySync("chrome","rolling-web",new(){["usageBuckets"]=new JsonObject{[minuteKey]=4_000,[expiredKey]=10_000}});budgets.ReportLocalUsage("rolling",0,0,bucketDeltas:new Dictionary<double,double>{[minute]=1_000});
Check(SnoozeTotal(budgets,"rolling")==3_000,"Rolling linked snooze counts both devices against the pruned in-window budget");
budgets.ApplySync("chrome","rolling-web",new(){["usageBucketsSeed"]=new JsonObject{[minuteKey]=63_000},["usageDeltaMs"]=1_000});
Check(SnoozeTotal(budgets,"rolling")==3_000,"Rolling seeds and unrelated fixed-delta field do not echo snooze consumption");
var restored=new ConnectionHub();restored.Start();
try { Check(restored.SharedUsage("fixed")?.Ms==61_500 && SnoozeTotal(restored,"fixed")==1_500 && SnoozeTotal(restored,"rolling")==3_000,"Usage-only contributions and snooze consumption survive a hub restart"); }
finally { await restored.StopAsync(); }
Check(WindowsBlocker.Rules.LocalFolderPathPolicy.RedirectsName(0xA000000C) && WindowsBlocker.Rules.LocalFolderPathPolicy.RedirectsName(0xA0000003),"Windows junction and symlink tags are name redirections");
Check(!WindowsBlocker.Rules.LocalFolderPathPolicy.RedirectsName(0x9000001A) && !WindowsBlocker.Rules.LocalFolderPathPolicy.RedirectsName(0x9000F01A) && !WindowsBlocker.Rules.LocalFolderPathPolicy.RedirectsName(0x80000021),"Cloud placeholders and OneDrive tags retain ordinary selected-folder access");
var historicalIcons=new JsonObject{["kind"]="known-items",["items"]=new JsonArray(new JsonObject{["id"]=@"app|C:\History\closed.exe"},new JsonObject{["id"]="app|family_name!App"},new JsonObject{["id"]="web|example.com"}),["icons"]=new JsonObject{["example.com"]="browser-cache"}};
ActivityNativeIcons.Enrich(historicalIcons,key=>"native:"+key);
Check(historicalIcons["icons"]?[@"C:\History\closed.exe"]?.GetValue<string>()==@"native:C:\History\closed.exe" && historicalIcons["icons"]?["family_name!App"]!=null && historicalIcons["icons"]?["example.com"]?.GetValue<string>()=="browser-cache","Historical Activity icons resolve exact executable/AUMID identities without changing browser icons");
var groupedIcons=new JsonObject{["snapshot"]=new JsonObject{["groups"]=new JsonArray(new JsonObject{["members"]=new JsonArray(@"app|C:\History\closed.exe")})},["icons"]=new JsonObject{[@"C:\History\closed.exe"]="current-icon"}};
ActivityNativeIcons.Enrich(groupedIcons,key=>throw new Exception("Existing icon must not be resolved again"));
Check(groupedIcons["icons"]?[@"C:\History\closed.exe"]?.GetValue<string>()=="current-icon","Merged Activity groups retain existing native icons");
Console.WriteLine("Native contract suite passed");
