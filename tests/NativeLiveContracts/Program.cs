using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;
using WindowsBlocker.Enforcement;

var resultFile=args[1]; var assertions=new List<string>(); Process? fixture=null;
try
{
    if(new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)) throw new Exception("The live suite must run with a normal user token");
    var report=Path.Combine(Path.GetTempPath(),"vault-quit-"+Guid.NewGuid()+".txt");
    fixture=Process.Start(new ProcessStartInfo(args[0],"\""+report+"\"") {UseShellExecute=false})!;
    var started=Stopwatch.StartNew(); while(fixture.MainWindowHandle==IntPtr.Zero && started.Elapsed<TimeSpan.FromSeconds(10)){await Task.Delay(100);fixture.Refresh();}
    if(fixture.MainWindowHandle==IntPtr.Zero) throw new Exception("Fixture window was not created");
    var identity=ProcessIdentity.ForWindow(fixture.MainWindowHandle); var scheduler=new QuitRequestScheduler(); var now=DateTimeOffset.Now;
    if(scheduler.ShouldRequest(identity.ProcessInstance,now,TimeSpan.Zero)) WindowCloser.RequestProcessQuit(identity.ProcessId,IntPtr.Zero);
    for(var attempt=0;attempt<25 && !File.Exists(report);attempt++) await Task.Delay(100);
    if(!File.Exists(report) || File.ReadAllText(report)!="1") throw new Exception("First cooperative quit was not delivered exactly once");
    assertions.Add("One WM_CLOSE reached the blocked process");
    for(var attempt=0;attempt<5;attempt++){ if(scheduler.ShouldRequest(identity.ProcessInstance,now.AddSeconds(attempt+1),TimeSpan.Zero)) WindowCloser.RequestProcessQuit(identity.ProcessId,IntPtr.Zero); await Task.Delay(100); }
    if(File.ReadAllText(report)!="1" || fixture.HasExited) throw new Exception("Zero retry did not preserve the unsaved-work prompt");
    assertions.Add("Save prompt remains and zero retry sends no additional WM_CLOSE");
    if(scheduler.ShouldRequest(identity.ProcessInstance,now.AddMinutes(2),TimeSpan.FromMinutes(2))) WindowCloser.RequestProcessQuit(identity.ProcessId,IntPtr.Zero);
    await Task.Delay(300);
    if(File.ReadAllText(report)!="2") throw new Exception("Configured retry did not deliver exactly one new request");
    assertions.Add("Configured interval delivers one retry while preserving process");
    File.WriteAllText(resultFile,JsonSerializer.Serialize(new{ok=true,normalUser=true,assertions}));
}
catch(Exception ex){File.WriteAllText(resultFile,JsonSerializer.Serialize(new{ok=false,error=ex.Message,assertions}));Environment.ExitCode=1;}
finally{if(fixture is {HasExited:false})fixture.Kill(true);fixture?.Dispose();}
