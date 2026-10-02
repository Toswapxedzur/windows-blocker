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
await client.ShutdownAsync();
