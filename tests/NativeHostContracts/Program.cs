using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using VaultNativeHost;

static void Check(bool ok, string description) { if (!ok) throw new Exception(description); Console.WriteLine("PASS " + description); }
Check(NativeBrowserParent.BrowserIdentity("msedge.exe","Microsoft Edge","Microsoft Corporation",false)=="edge", "Edge identity requires its signed product and publisher");
Check(NativeBrowserParent.BrowserIdentity("msedge.exe","Malicious Edge","Microsoft Corporation",true)==null && NativeBrowserParent.BrowserIdentity("msedge.exe","Microsoft Edge","Untrusted",true)==null, "Name-only and wrong-publisher browser identities refused");
Check(NativeBrowserParent.BrowserIdentity("chrome.exe","Google Chrome for Testing","Google LLC",true)=="chrome" && NativeBrowserParent.BrowserIdentity("chrome.exe","Google Chrome for Testing","Google LLC",false)==null, "Signed Chrome for Testing is development-only");
Check(NativeBrowserParent.CurrentProgram(true)==null, "Ordinary test launcher is not trusted as a browser parent");
var snapshots = new[] { new NativeBrowserParent.ProcessSnapshot(20, 10, @"c:\vault\host.exe", 200), new NativeBrowserParent.ProcessSnapshot(10, 1, @"c:\browser\chrome.exe", 100) };
var observed = new NativeBrowserParent.BrowserLaunch("chrome", @"c:\browser\chrome.exe", snapshots);
Check(NativeBrowserParent.SameLaunch(observed, observed with { Chain = snapshots.ToArray() }), "Repeated native creator snapshots preserve exact launch identity");
Check(!NativeBrowserParent.SameLaunch(observed, null) && !NativeBrowserParent.SameLaunch(observed, observed with { Program = "edge" }), "Unavailable or different native browser family cannot reuse a launch attestation");
foreach(var field in new[]{"path","birth","parent","id"})
{
    var changed = snapshots.ToArray();
    changed[1] = field switch { "path" => changed[1] with { Path = @"c:\other\chrome.exe" }, "birth" => changed[1] with { Created = 101 }, "parent" => changed[1] with { ParentId = 2 }, _ => changed[1] with { Id = 11 } };
    Check(!NativeBrowserParent.SameLaunch(observed, observed with { Chain = changed }), "Native request recheck refuses changed creator " + field);
}
Check(!NativeBrowserParent.SameLaunch(observed, observed with { ExecutablePath = @"c:\other\chrome.exe" }), "Same browser family at another canonical path is a distinct native identity");
Check(NativeBrowserParent.HashExecutableIdentity(@"C:\Browser\sub\..\CHROME.EXE") == NativeBrowserParent.HashExecutableIdentity(@"c:\browser\chrome.exe"), "Executable descriptor hashes existing full-path normalization and lowercase exactly");
Check(NativeBrowserParent.HashExecutableIdentity(@"c:\other\chrome.exe") != observed.Value, "Two same-named browser installs have different executable descriptors");
foreach(var invalid in new[]{"chrome.exe","Package_123!App", "C:\\browser\\chrome.exe\n"})
{
    var refused = false; try { _ = NativeBrowserParent.HashExecutableIdentity(invalid); } catch(ArgumentException) { refused = true; }
    Check(refused, "Browser executable identity never widens to an unsupported identifier: " + invalid.Replace("\n","newline"));
}
Environment.SetEnvironmentVariable("VAULT_ENVIRONMENT","development");
Check(!NativeHostProtocol.DevelopmentAlias(@"C:\Vault\vault-local-hub-native-host.exe") && NativeHostProtocol.DevelopmentAlias(@"C:\Vault\vault-local-hub-native-host-development.exe") && !NativeHostProtocol.DevelopmentAlias(@"C:\Vault\other-development.exe"), "Registered helper alias binds environment independently of inherited development variables");
Check(NativeHostProtocol.AllowedOrigin("chrome-extension://mcbmcmephdaapjepopobikobjmfdeamm/",false) && !NativeHostProtocol.AllowedOrigin("chrome-extension://fjichnkbaoilbfbjcjkggllmbicmeegk/",false) && !NativeHostProtocol.AllowedOrigin("chrome-extension://mcbmcmephdaapjepopobikobjmfdeamm:444/",false), "Production browser helper refuses development and altered origins");
Check(NativeHostProtocol.MaximumFrameLength==65_536,"Native browser frame cap matches the shared 64KiB protocol");
var edge = @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe";
Check(NativeBrowserParent.BrowserFile(edge,false)=="edge", "Actual installed Edge passes Windows Authenticode and signed PE metadata");
Check(NativeBrowserParent.VerifiedSigner(Path.Combine(Environment.SystemDirectory,"cmd.exe"))=="Microsoft Windows", "Actual system cmd intermediary has a verified Microsoft Windows signature");
var damaged = Path.Combine(Path.GetTempPath(),"vault-untrusted-browser-"+Guid.NewGuid()+".exe");
try
{
    File.Copy(edge,damaged);
    using(var file = new FileStream(damaged,FileMode.Open,FileAccess.ReadWrite)) { file.Position=0x400; var b=file.ReadByte();file.Position=0x400;file.WriteByte((byte)(b^1)); }
    Check(NativeBrowserParent.BrowserFile(damaged,true)==null, "Modified browser binary fails verified signature even in development");
}
finally { File.Delete(damaged); }
if (args.Contains("--browser-identity-only")) return; // Screen-free identity checks; the separate caller-refusal fixture owns its child process.
var start = new ProcessStartInfo(args[0]) {UseShellExecute=false,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
start.Environment["VAULT_ENVIRONMENT"]="development";start.ArgumentList.Add("chrome-extension://mcbmcmephdaapjepopobikobjmfdeamm/");
using(var child=Process.Start(start)!)
{
    var request=Encoding.UTF8.GetBytes("{\"kind\":\"local-hub-challenge\",\"v\":4,\"program\":\"edge\",\"challenge\":\""+new string('A',43)+"\"}");
    var header=new byte[4];BinaryPrimitives.WriteUInt32LittleEndian(header,(uint)request.Length);
    try { await child.StandardInput.BaseStream.WriteAsync(header);await child.StandardInput.BaseStream.WriteAsync(request);await child.StandardInput.BaseStream.FlushAsync();child.StandardInput.Close(); } catch(IOException) { }
    await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
    Check((await child.StandardOutput.ReadToEndAsync()).Length==0,"Nonbrowser caller receives no proof despite a valid extension origin and challenge");
}
