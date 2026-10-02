using System.Text.Json.Nodes;
using WindowsBlocker.Bridge;
using WindowsBlocker.Core;
using WindowsBlocker.Enforcement;
using WindowsBlocker.WebUI;

Environment.SetEnvironmentVariable("VAULT_ENVIRONMENT", "development");
Environment.SetEnvironmentVariable("VAULT_STORAGE_ROOT", Path.Combine(Path.GetTempPath(), "vault-contracts-" + Guid.NewGuid()));
static void Check(bool passed, string description) { if (!passed) throw new Exception(description); Console.WriteLine("PASS " + description); }
var groups = ChromeExtensionImporter.ImportGroups("""
{"blockedGroups":[{"id":"a","enabled":true,"groupType":"site","activeDays":[],"scopes":[{"surface":"apps","appsExcept":true,"apps":[{"id":"C:\\Apps\\editor.exe","name":"Editor"}]}]},{"id":"b","enabled":true,"groupType":"custom","activeEventSource":"(on,v)=>{}","blockingRulesText":"retired helper","apps":[{"id":"stale.exe"}]}]}
""").Groups;
Check(groups[0].ActiveDays.Count == 0, "empty schedule means never");
Check(groups[0].ApplicationAllowlist && groups[0].Targets.Count == 1, "current appsExcept scope imports");
Check(groups[1].Targets.Count == 0 && groups[1].CustomRuleSource == "(on,v)=>{}", "stale app fields ignored and active source used");
var registry = new BlockedAppRegistry(); registry.Update([@"c:\apps\editor.exe"]);
Check(!registry.IsBlocked(new() { ProcessId=1, ExecutablePath=@"c:\other\editor.exe", ExecutableName="editor.exe" }), "different executable path is not widened by filename");
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
hub.SetRoster("windowsapp", new JsonArray(new JsonObject { ["id"]="local",["name"]="Same",["frozen"]=false }));
hub.SetRoster("chrome", new JsonArray(new JsonObject { ["id"]="browser",["name"]="Different",["frozen"]=false }));
Check(hub.Link("windowsapp","local","chrome","browser") == null, "explicit link joins differently named groups");
Check(hub.SharedUsage("Same") == null, "display name never identifies a link");
hub.ApplySync("windowsapp","local",new JsonObject { ["scalars"]=new JsonObject { ["name"]="Chosen",["allowedMinutes"]=10,["resetIntervalHours"]=24 },["scopes"]=new JsonArray(new JsonObject { ["id"]="apps-1",["surface"]="apps",["apps"]=new JsonArray() }),["ts"]=1 });
hub.ApplySync("chrome","browser",new JsonObject { ["scalars"]=new JsonObject { ["name"]="Browser" },["scopes"]=new JsonArray(new JsonObject { ["id"]="site-1",["surface"]="site",["sites"]=new JsonArray("example.com") }),["usageMs"]=5000,["ts"]=1 });
Check(hub.SharedUsage("local")?.Ms == 5000, "linked usage seeds once");
hub.ReportLocalUsage("local",1000,now.ToUnixTimeMilliseconds()); Check(hub.SharedUsage("local")?.Ms == 6000, "linked usage accumulates deltas");
hub.SetRoster("chrome",new JsonArray(new JsonObject { ["id"]="replacement",["name"]="Different" }));
Check(hub.ActiveClusterCount()==0, "delete and recreate does not rejoin by name");
Console.WriteLine("Native contract suite passed");
