using System.Reflection;
using System.Text.Json.Nodes;
using WindowsBlocker.Bridge;
using WindowsBlocker.WebUI;

// Uses actual canonical storage and hub operations. The sole phase seam is
// compiled out of the runnable app and does not modify baseline locking.
internal static class NativeDefinitionIntentContracts
{
    static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static JsonObject Apps() => new() { ["id"]="apps-1", ["surface"]="apps", ["apps"]=new JsonArray(new JsonObject { ["id"]=@"C:\Fixture\editor.exe", ["name"]="Editor" }) };
    static JsonObject Frame(double allowance, double ts) => new() {
        ["scalars"]=new JsonObject { ["name"]="Fixture", ["enabled"]=true, ["mode"]="budget", ["allowedMinutes"]=allowance, ["resetIntervalHours"]=24 },
        ["scopes"]=new JsonArray(new JsonObject { ["surface"]="site", ["sites"]=new JsonArray("example.com") }), ["ts"]=ts
    };
    static JsonObject Shared(ConnectionHub hub) => (JsonObject)JsonNode.Parse(hub.ClustersJson())!["clusters"]![0]!["shared"]!;
    static JsonObject Local(WebStore store) => (JsonObject)JsonNode.Parse(store.LoadRawJson()!)!["blockedGroups"]![0]!;
    static double Allowance(ConnectionHub hub) => Shared(hub)["scalars"]!["allowedMinutes"]!.GetValue<double>();
    static double StoredAllowance(WebStore store) => Local(store)["allowedMinutes"]!.GetValue<double>();
    static void Edit(WebStore store, string key, JsonNode? value) => store.Update(doc => doc["blockedGroups"]![0]![key]=value?.DeepClone());
    static (WebStore Store, ConnectionHub Hub) Seed(bool reconcile=true)
    {
        var store=new WebStore();
        store.SaveRaw(new JsonObject { ["blockedGroups"]=new JsonArray(new JsonObject {
            ["id"]="w", ["name"]="Fixture", ["enabled"]=true, ["mode"]="budget", ["allowedMinutes"]=20, ["resetIntervalHours"]=24, ["scopes"]=new JsonArray(Apps()), ["unknownField"]="keep"
        }) }.ToJsonString());
        // Construct a persisted already-contributed link without a local ledger,
        // then attach its actual canonical owner as normal startup does.
        var original=new ConnectionHub();
        original.SetRoster("windowsapp",new JsonArray(new JsonObject { ["id"]="w", ["name"]="Fixture" }));
        original.SetRoster("chrome",new JsonArray(new JsonObject { ["id"]="c", ["name"]="Fixture" }));
        Check(original.Link("chrome","c","windowsapp","w")==null,"Seed link refused");
        original.ApplySync("chrome","c",Frame(20,0));
        var native=Frame(20,0);native["scopes"]=new JsonArray(Apps());original.ApplySync("windowsapp","w",native);
        var hub=new ConnectionHub(store);
        typeof(ConnectionHub).GetMethod("Restore",Private)!.Invoke(hub,null);
        if(reconcile)hub.ReconcileLocal(store);
        return (store,hub);
    }
    public static async Task Run()
    {
        var previousRoot=Environment.GetEnvironmentVariable("VAULT_STORAGE_ROOT");
        var parent=Path.Combine(Path.GetTempPath(),"vault-definition-intent-"+Guid.NewGuid());
        Directory.CreateDirectory(parent);
        var failures=new List<string>();
        async Task Case(string name, Func<Task> action)
        {
            Environment.SetEnvironmentVariable("VAULT_STORAGE_ROOT",Path.Combine(parent,name));
            try { await action(); Console.WriteLine("PASS "+name); }
            catch(Exception ex) { failures.Add(name);Console.WriteLine("FAIL "+name+": "+ex.Message); }
        }
        try
        {
            await Case("unchanged-stale-raw",()=> {
                var (store,hub)=Seed();hub.ApplySync("chrome","c",Frame(24,2));var ts=Shared(hub)["ts"]!.GetValue<double>();
                hub.ReconcileLocal(store);hub.ReconcileLocal(store);
                Check(Allowance(hub)==24 && StoredAllowance(store)==24 && Shared(hub)["ts"]!.GetValue<double>()==ts,"Unchanged local storage echoed over newer browser allowance");
                return Task.CompletedTask;
            });
            await Case("enabled-only-on-lagging-raw",()=> {
                var (store,hub)=Seed();hub.ApplySync("chrome","c",Frame(24,2));Edit(store,"enabled",JsonValue.Create(false));hub.ReconcileLocal(store);
                Check(Allowance(hub)==24 && StoredAllowance(store)==24 && Shared(hub)["scalars"]!["enabled"]!.GetValue<bool>()==false,"Enabled-only edit republished untouched stale allowance");
                Check(Local(store)["unknownField"]!.GetValue<string>()=="keep" && Shared(hub)["scopes"]!.AsArray().Count==2,"Intent rebasing erased unknown data or Website scope");
                return Task.CompletedTask;
            });
            await Case("apps-only-on-lagging-raw",()=> {
                var (store,hub)=Seed();hub.ApplySync("chrome","c",Frame(24,2));Edit(store,"scopes",new JsonArray(new JsonObject { ["surface"]="apps", ["apps"]=new JsonArray() }));hub.ReconcileLocal(store);
                Check(Allowance(hub)==24 && StoredAllowance(store)==24,"Apps-only edit republished untouched stale allowance");
                Check(Shared(hub)["scopes"]!.AsArray().OfType<JsonObject>().Single(s=>s["surface"]!.GetValue<string>()=="apps")["apps"]!.AsArray().Count==0,"Explicit native Apps clear was lost");return Task.CompletedTask;
            });
            await Case("sequential-native30-then-enabled",()=> {
                var (store,hub)=Seed();Edit(store,"allowedMinutes",JsonValue.Create(30));hub.ReconcileLocal(store);
                Edit(store,"enabled",JsonValue.Create(false));hub.ReconcileLocal(store);
                Check(Allowance(hub)==30 && StoredAllowance(store)==30 && Shared(hub)["scalars"]!["enabled"]!.GetValue<bool>()==false,"Later native enabled edit discarded pending native allowance30");return Task.CompletedTask;
            });
            await Case("failed-save-does-not-acknowledge",()=> {
                var (store,hub)=Seed();hub.ApplySync("chrome","c",Frame(24,2));var before=File.ReadAllText(store.FilePath);var refused=false;
                using(var reader=new FileStream(store.FilePath,FileMode.Open,FileAccess.Read,FileShare.Read))
                {
                    try {hub.ReconcileLocal(store);}catch(IOException){refused=true;}
                    Check(refused && File.ReadAllText(store.FilePath)==before,"Refused Replace was not an honest preserved-file failure");
                }
                hub.ReconcileLocal(store);
                Check(Allowance(hub)==24 && StoredAllowance(store)==24,"Failed adoption acknowledged unsaved24, then retry republished raw20");return Task.CompletedTask;
            });
            await Case("native30-between-read-and-adoption",async()=> {
                var (store,hub)=Seed();using var captured=new ManualResetEventSlim();using var release=new ManualResetEventSlim();var count=0;
                hub.LocalReconciliationPhaseForTests=phase=> {if(phase=="before-adoption" && Interlocked.Increment(ref count)==1){captured.Set();if(!release.Wait(TimeSpan.FromSeconds(5)))throw new Exception("Owned adoption barrier timed out");}};
                var reconcile=Task.Run(()=>hub.ReconcileLocal(store));
                try
                {
                    Check(captured.Wait(TimeSpan.FromSeconds(5)),"Actual reconciliation did not reach phase barrier");
                    var storeGate=typeof(WebStore).GetField("_gate",Private)!.GetValue(store)!;
                    var unlocked=Monitor.TryEnter(storeGate);if(unlocked)Monitor.Exit(storeGate);
                    var edit=Task.Run(()=>Edit(store,"allowedMinutes",JsonValue.Create(30)));
                    if(unlocked)await edit.WaitAsync(TimeSpan.FromSeconds(5));
                    release.Set();await Task.WhenAll(reconcile,edit).WaitAsync(TimeSpan.FromSeconds(5));
                    hub.LocalReconciliationPhaseForTests=null;hub.ReconcileLocal(store);
                    Check(Allowance(hub)==30 && StoredAllowance(store)==30,"Actual native30 write was overwritten between contribution and adoption");
                }
                finally {release.Set();await reconcile.WaitAsync(TimeSpan.FromSeconds(5));hub.LocalReconciliationPhaseForTests=null;}
            });
            await Case("startup-stale-raw-is-not-intent",()=> {
                var (store,hub)=Seed(false);hub.ApplySync("chrome","c",Frame(24,2));hub.ReconcileLocal(store);
                Check(Allowance(hub)==24 && StoredAllowance(store)==24,"Startup stale raw became a fresh native edit");return Task.CompletedTask;
            });
            await Case("startup-native-write-before-first-reconcile",()=> {
                var (store,hub)=Seed(false);hub.ApplySync("chrome","c",Frame(24,2));Edit(store,"allowedMinutes",JsonValue.Create(30));hub.ReconcileLocal(store);
                Check(Allowance(hub)==30 && StoredAllowance(store)==30,"Successful native write before first reconcile was discarded as startup stale storage");return Task.CompletedTask;
            });
            await Case("native30-then-apps-on-newer-shared",()=> {
                var (store,hub)=Seed();Edit(store,"allowedMinutes",JsonValue.Create(30));hub.ReconcileLocal(store);
                var newer=Frame(30,Shared(hub)["ts"]!.GetValue<double>());hub.ApplySync("chrome","c",newer);
                Edit(store,"scopes",new JsonArray(new JsonObject { ["surface"]="apps", ["apps"]=new JsonArray() }));hub.ReconcileLocal(store);
                Check(Allowance(hub)==30 && StoredAllowance(store)==30,"Apps clear erased previous native allowance30");return Task.CompletedTask;
            });
            await Case("object-key-order-does-not-publish",()=> {
                var (store,hub)=Seed();hub.ApplySync("chrome","c",Frame(24,2));hub.ReconcileLocal(store);var ts=Shared(hub)["ts"]!.GetValue<double>();
                store.Update(doc=> {var g=doc["blockedGroups"]![0]!.AsObject();var pairs=g.Reverse().Select(k=>KeyValuePair.Create(k.Key,k.Value?.DeepClone())).ToArray();doc["blockedGroups"]![0]=new JsonObject(pairs);});
                hub.ReconcileLocal(store);Check(Shared(hub)["ts"]!.GetValue<double>()==ts && Allowance(hub)==24,"Object property ordering was mistaken for native edit");return Task.CompletedTask;
            });
            await Case("unchanged-usage-and-rule-state-preserved",()=> {
                var (store,hub)=Seed();store.Merge(new JsonObject { ["cbRuleState"]=new JsonObject { ["w"]=new JsonObject { ["count"]=7 } } });
                hub.ReportLocalUsage("w",1234,Shared(hub)["usageResetAtMs"]!.GetValue<double>());hub.ApplySync("chrome","c",Frame(20,2));
                Edit(store,"enabled",JsonValue.Create(false));hub.ReconcileLocal(store);
                var doc=JsonNode.Parse(store.LoadRawJson()!)!;Check(doc["cbRuleState"]!["w"]!["count"]!.GetValue<int>()==7 && Shared(hub)["usageMs"]!.GetValue<double>()==1234,"Non-budget intent damaged usage/custom-rule state");return Task.CompletedTask;
            });
            await Case("scalar-removal-is-explicit-intent",()=> {
                var (store,hub)=Seed();Edit(store,"fallbackUrl",JsonValue.Create("https://example.com/"));hub.ReconcileLocal(store);hub.ApplySync("chrome","c",Frame(24,Shared(hub)["ts"]!.GetValue<double>()));
                store.Update(doc=>doc["blockedGroups"]![0]!.AsObject().Remove("fallbackUrl"));hub.ReconcileLocal(store);
                Check(!Local(store).ContainsKey("fallbackUrl") && !Shared(hub)["scalars"]!.AsObject().ContainsKey("fallbackUrl") && Allowance(hub)==24,"Scalar clear republished stale allowance or failed to remove value");return Task.CompletedTask;
            });
            await Case("matching-lock-cas-applied",()=> {
                var (store,hub)=Seed();var frame=Frame(20,2);frame["lock"]=new JsonObject { ["lockVersion"]=1 };hub.ApplySync("chrome","c",frame);hub.ReconcileLocal(store);
                store.Update(doc=> {doc["blockedGroups"]![0]!["lockVersion"]=2;doc["blockedGroups"]![0]!["parentalPasswordHash"]="fixture";});hub.ReconcileLocal(store);
                Check(Shared(hub)["lock"]!["lockVersion"]!.GetValue<double>()==2 && Local(store)["lockSyncedVersion"]!.GetValue<double>()==2,"Matching lock CAS lost native atomic lock unit");return Task.CompletedTask;
            });
            await Case("stale-lock-cas-rejected",()=> {
                var (store,hub)=Seed();var first=Frame(20,2);first["lock"]=new JsonObject { ["lockVersion"]=1 };hub.ApplySync("chrome","c",first);hub.ReconcileLocal(store);
                var newer=Frame(24,3);newer["lock"]=new JsonObject { ["lockVersion"]=3, ["parentalPasswordHash"]="remote" };newer["lockBase"]=1;hub.ApplySync("chrome","c",newer);
                Edit(store,"lockVersion",JsonValue.Create(2));hub.ReconcileLocal(store);
                Check(Allowance(hub)==24 && Shared(hub)["lock"]!["lockVersion"]!.GetValue<double>()==3 && Local(store)["lockSyncedVersion"]!.GetValue<double>()==3,"Stale lock CAS overwrote current lock or unrelated browser allowance");return Task.CompletedTask;
            });
            await Case("missing-canonical-file-refused",()=> {
                var (store,hub)=Seed();var before=hub.ClustersJson();File.Delete(store.FilePath);var refused=false;
                try {hub.ReconcileLocal(store);}catch(InvalidDataException){refused=true;}
                Check(refused && !File.Exists(store.FilePath) && hub.ClustersJson()==before,"Missing canonical file fabricated empty roster/adoption");return Task.CompletedTask;
            });
            await Case("same-owner-reattach-does-not-reset-intent",()=> {
                var (store,hub)=Seed(false);Edit(store,"allowedMinutes",JsonValue.Create(30));hub.ReconcileLocal(store);hub.ReconcileLocal(store);
                Check(Allowance(hub)==30 && StoredAllowance(store)==30,"Same-owner reconciliation recaptured changed raw as startup baseline");return Task.CompletedTask;
            });
            await Case("mixed-scope-array-refused",()=> {
                var (store,hub)=Seed();var before=hub.ClustersJson();Edit(store,"scopes",new JsonArray(Apps(),JsonValue.Create("broken")));var bytes=File.ReadAllText(store.FilePath);var refused=false;
                try {hub.ReconcileLocal(store);}catch(InvalidDataException){refused=true;}
                Check(refused && hub.ClustersJson()==before && File.ReadAllText(store.FilePath)==bytes,"Mixed nested scopes mutated canonical state");return Task.CompletedTask;
            });
            foreach (var invalid in new[] { "missing-collection", "missing-id", "empty-id", "numeric-id", "duplicate-id" })
                await Case("malformed-identity-"+invalid,()=> {
                    var (store,hub)=Seed();var before=hub.ClustersJson();
                    store.Update(doc=> {
                        var group=doc["blockedGroups"]![0]!.AsObject();
                        if (invalid=="missing-collection") doc.Remove("blockedGroups");
                        else if (invalid=="missing-id") group.Remove("id");
                        else if (invalid=="empty-id") group["id"]="";
                        else if (invalid=="numeric-id") group["id"]=12;
                        else doc["blockedGroups"]!.AsArray().Add(group.DeepClone());
                    });
                    var bytes=File.ReadAllText(store.FilePath);var refused=false;
                    try {hub.ReconcileLocal(store);}catch(InvalidDataException){refused=true;}
                    Check(refused && hub.ClustersJson()==before && File.ReadAllText(store.FilePath)==bytes,"Malformed collection/identity detached or changed shared state");return Task.CompletedTask;
                });
            await Case("future-schema-refuses-hub-mutation",()=> {
                var (store,hub)=Seed();var before=hub.ClustersJson();var doc=JsonNode.Parse(store.LoadRawJson()!)!.AsObject();
                doc["schemaVersion"]=99;doc["blockedGroups"]![0]!["allowedMinutes"]=30;File.WriteAllText(store.FilePath,doc.ToJsonString());var bytes=File.ReadAllText(store.FilePath);
                try {hub.ReconcileLocal(store);}catch(InvalidDataException){}
                Check(hub.ClustersJson()==before && File.ReadAllText(store.FilePath)==bytes,"Future storage altered shared definition before refusing physical write");return Task.CompletedTask;
            });
            await Case("malformed-nested-scopes-refused",()=> {
                var (store,hub)=Seed();var before=hub.ClustersJson();Edit(store,"scopes",JsonValue.Create("broken"));var bytes=File.ReadAllText(store.FilePath);
                try {hub.ReconcileLocal(store);}catch(InvalidDataException){}
                Check(hub.ClustersJson()==before && File.ReadAllText(store.FilePath)==bytes,"Malformed nested scopes published/overlaid instead of preserving file and shared state");return Task.CompletedTask;
            });
        }
        finally {Environment.SetEnvironmentVariable("VAULT_STORAGE_ROOT",previousRoot);Directory.Delete(parent,true);}
        Console.WriteLine($"Native definition intent cases: {24-failures.Count} passed, {failures.Count} failed");
        if(failures.Count>0)throw new Exception("Native definition intent failures: "+string.Join(", ",failures));
    }
}
