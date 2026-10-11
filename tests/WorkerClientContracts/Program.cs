using System.Diagnostics;
using System.Text.Json.Nodes;
using WindowsBlocker.Bridge;
Environment.SetEnvironmentVariable("VAULT_ENVIRONMENT","development");
Environment.SetEnvironmentVariable("VAULT_STORAGE_ROOT",Path.Combine(Path.GetTempPath(),"vault-worker-contracts-"+Guid.NewGuid()));
using var client=new ClassifierWorkerClient(args[0]);
var unicode="标签 café 🔒";
var roundtrip=await client.Request("echo",new JsonObject { ["text"]=unicode });
if(roundtrip?["text"]?.GetValue<string>()!=unicode) throw new Exception("Worker JSONL did not preserve Unicode");
Console.WriteLine("PASS Worker JSONL preserves non-ASCII tags and titles");
using var diagnostic=new StringWriter();using var listener=new TextWriterTraceListener(diagnostic);Trace.Listeners.Add(listener);
var background=new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
client.Event+=message=>{if(message["event"]?.GetValue<string>()=="error")background.TrySetResult(message);};
await client.Request("background-error",new());var refused=await background.Task.WaitAsync(TimeSpan.FromSeconds(5));
if(refused["error"]?.GetValue<string>()!="worker-response-too-large" || !diagnostic.ToString().Contains("worker-response-too-large (activity)"))throw new Exception("Background worker refusal was silently ignored");
Trace.Listeners.Remove(listener);Console.WriteLine("PASS Bounded background event refusal reaches diagnostics and the native event adapter");
using var release=new ManualResetEventSlim();
var held=new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
client.Event+=message=> { if(message["event"]?.GetValue<string>()=="hold") { held.TrySetResult(message["pid"]!.GetValue<int>());release.Wait(TimeSpan.FromSeconds(8)); } };
var stale=client.Request("crash",new());
var oldPid=await held.Task.WaitAsync(TimeSpan.FromSeconds(5));
try { using var old=Process.GetProcessById(oldPid);await old.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); } catch(ArgumentException) { }
var fresh=client.Request("delay",new JsonObject { ["text"]=unicode });release.Set();
var answer=await fresh.WaitAsync(TimeSpan.FromSeconds(5));
if(answer?["text"]?.GetValue<string>()!=unicode) throw new Exception("Old child cleanup disrupted a replacement child's pending request");
Console.WriteLine("PASS Crashed child cleanup leaves replacement requests intact");
try { await stale;throw new Exception("Crashed child's request unexpectedly succeeded"); } catch(InvalidOperationException) { }
Console.WriteLine("PASS Crashed child's pending request fails promptly");
var deleted=await client.Request("activity",ActivityMcpRequest.Create("delete_activity_group",new JsonObject { ["id"]="fixture" }));
if(deleted?.GetValue<string>()!="Deleted.")throw new Exception("Activity scalar success was not preserved");
Console.WriteLine("PASS Activity MCP scalar payload survives native worker JSONL client");
var canonicalRefused=false;
try { await client.Request("activity",ActivityMcpRequest.Create("delete_activity_group",new JsonObject { ["id"]="missing" })); }
catch(InvalidOperationException ex) { canonicalRefused=ex.Message=="Activity group not found"; }
if(!canonicalRefused)throw new Exception("Activity canonical refusal was converted into success");
Console.WriteLine("PASS Activity MCP canonical refusal reaches native caller as error");
var normal=await client.ShutdownAsync();
if(!normal.Clean)throw new Exception("Healthy worker shutdown was not a clean exit");
Console.WriteLine("PASS Healthy worker EOF/exit reported distinctly from fallback");

// The old client sends native-flush before EOF and gets stuck in this exact
// fixture. The new client must close stdin without requesting a flush reply.
var evidence=Path.Combine(Path.GetTempPath(),"vault-worker-shutdown-"+Guid.NewGuid()+".json");
using(var closing=new ClassifierWorkerClient(args[0]))
{
    await closing.Request("shutdown-proof",new JsonObject{["path"]=evidence,["flushHang"]=true});
    var admitted=Enumerable.Range(0,32).Select(i=>closing.Request("remember",new JsonObject{["text"]=unicode+i})).ToArray();
    var first=closing.ShutdownAsync();var second=closing.ShutdownAsync();
    if(!ReferenceEquals(first,second))throw new Exception("Concurrent shutdown calls did not share one lifecycle task");
    var refusedAfterStop=false;
    try {await closing.Request("echo",new());}catch(InvalidOperationException ex){refusedAfterStop=ex.Message=="classifier-worker-stopping";}
    if(!refusedAfterStop)throw new Exception("A request after shutdown started was admitted");
    await Task.WhenAll(admitted);
    var outcome=await first;
    if(outcome.Kind!=WorkerShutdownKind.CleanExit || outcome.ExitCode!=0)throw new Exception("Busy/pre-flush fixture did not exit normally via EOF");
    var proof=JsonNode.Parse(File.ReadAllText(evidence))!;
    if(proof["eof"]?.GetValue<bool>()!=true || proof["flushAttempted"]!=null || proof["remembered"]?.AsArray().Count!=32)throw new Exception("EOF did not persist every admitted frame without pre-flush request");
    var expected=Enumerable.Range(0,32).Select(i=>unicode+i);
    if(!proof["remembered"]!.AsArray().Select(n=>n!.GetValue<string>()).SequenceEqual(expected))throw new Exception("Admitted writes were reordered before EOF");
    var pid=proof["pid"]!.GetValue<int>();
    try {using var child=Process.GetProcessById(pid);if(!child.HasExited)throw new Exception("Clean shutdown left its exact child running");}catch(ArgumentException){}
    closing.Dispose();
    if(closing.LastShutdownResult?.Kind!=WorkerShutdownKind.CleanExit)throw new Exception("Dispose altered a clean lifecycle outcome");
}
File.Delete(evidence);
Console.WriteLine("PASS EOF drains admitted FIFO frames, avoids hanging flush, and rejects restart after stop");

var failedEvidence=Path.Combine(Path.GetTempPath(),"vault-worker-fallback-"+Guid.NewGuid()+".json");
using(var failed=new ClassifierWorkerClient(args[0]))
{
    await failed.Request("shutdown-proof",new JsonObject{["path"]=failedEvidence,["hangOnEof"]=true});
    var outcome=await failed.ShutdownAsync();
    if(outcome.Kind!=WorkerShutdownKind.ForcedTermination || outcome.Clean || outcome.Error!="classifier-worker-shutdown-timeout")throw new Exception("Timed-out EOF was mislabeled clean or forced fallback was hidden");
}
if(File.Exists(failedEvidence))File.Delete(failedEvidence);
Console.WriteLine("PASS Unresponsive EOF fallback is explicit and cannot report clean exit");

using(var neverStarted=new ClassifierWorkerClient(args[0]))
{
    if((await neverStarted.ShutdownAsync()).Kind!=WorkerShutdownKind.AlreadyStopped)throw new Exception("Unused client launched a child during shutdown");
    try {await neverStarted.Request("echo",new());throw new Exception("Stopped unused client restarted");}catch(InvalidOperationException ex)when(ex.Message=="classifier-worker-stopping"){}
}
Console.WriteLine("PASS Unused shutdown and disposal never create a replacement child");

using(var failedExit=new ClassifierWorkerClient(args[0]))
{
    await failedExit.Request("shutdown-proof",new JsonObject{["exitCode"]=9});
    var result=await failedExit.ShutdownAsync();
    if(result.Kind!=WorkerShutdownKind.Failed || result.ExitCode!=9 || result.Clean)throw new Exception("Nonzero worker exit was hidden as successful persistence");
}
Console.WriteLine("PASS Nonzero worker exit remains explicit failure");
