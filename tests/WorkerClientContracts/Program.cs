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
await client.ShutdownAsync();
