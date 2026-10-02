using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using WindowsBlocker.Rules;

var fixture = Path.Combine(Path.GetTempPath(), "vault-folder-contracts-" + Guid.NewGuid().ToString("N"));
Environment.SetEnvironmentVariable("VAULT_ENVIRONMENT", "development");
Environment.SetEnvironmentVariable("VAULT_STORAGE_ROOT", fixture);
var root = Path.Combine(fixture, "root");
var outside = Path.Combine(fixture, "outside");
Directory.CreateDirectory(root); Directory.CreateDirectory(outside);
var checks = new List<string>();
void Check(bool ok, string label) { if (!ok) throw new Exception(label); checks.Add(label); Console.WriteLine("PASS " + label); }
JsonObject FileAction(string op, string path) => LocalFolderGrant.Handle(new RuleAction { Kind = "file", Op = op, Path = path, RequestId = Guid.NewGuid().ToString("N") });
void Junction(string path, string target)
{
    using var command = Process.Start(new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, ArgumentList = { "/c", "mklink", "/J", path, target } })!;
    command.WaitForExit(); if (command.ExitCode != 0) throw new Exception("Owned normal-user junction creation failed");
}
try
{
    var alias = Path.Combine(fixture, "root-alias"); Junction(alias, root);
    Check(LocalFolderPathPolicy.CanonicalRoot(alias).Equals(root, StringComparison.OrdinalIgnoreCase), "Explicitly selected junction root grants its canonical target");
    File.WriteAllText(Path.Combine(fixture, "local-folder-grant.txt"), root);
    File.WriteAllText(Path.Combine(outside, "secret.txt"), "outside-private");
    Junction(Path.Combine(root, "escape"), outside);
    Check(FileAction("read", "escape/secret.txt")["error"]?.GetValue<string>() == "invalid-path", "Existing interior junction cannot reach outside the grant");
    var missingTarget = Path.Combine(fixture, "deleted-target"); Directory.CreateDirectory(missingTarget);
    var dangling = Path.Combine(root, "dangling"); Junction(dangling, missingTarget); Directory.Delete(missingTarget);
    Console.WriteLine("Observed dangling .NET Path.Exists=" + Path.Exists(dangling) + "; Directory.Exists=" + Directory.Exists(dangling));
    Check(FileAction("read", "dangling/secret.txt")["error"]?.GetValue<string>() == "invalid-path" && FileAction("write", "dangling/new.txt")["error"]?.GetValue<string>() == "invalid-path", "Dangling interior junction is refused before reads or parent creation");
    var bom = new UTF8Encoding(true); File.WriteAllText(Path.Combine(root, "bom.txt"), "Windows 中文", bom); File.WriteAllText(Path.Combine(root, "bom.json"), "{\"name\":\"中文\"}", bom);
    Check(FileAction("read", "bom.txt")["text"]?.GetValue<string>() == "Windows 中文", "UTF-8 BOM text matches browser File.text decoding");
    var json = FileAction("readJson", "bom.json");
    Check(json["ok"]?.GetValue<bool>() == true && json["text"]?.GetValue<string>() == "{\"name\":\"中文\"}", "BOM-prefixed JSON reads match browser text and JSON validation");
    using (var locked = new FileStream(Path.Combine(root, "locked.txt"), FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        Check(FileAction("read", "locked.txt")["error"]?.GetValue<string>() == "local-file-error", "Actual sharing violation exposes only the canonical error token");
    LocalFolderGrant.Revoke(); Check(FileAction("read", "bom.txt")["error"]?.GetValue<string>() == "local-folder-not-available", "Revoked native grant refuses subsequent file operations");
    Console.WriteLine(new JsonObject { ["ok"] = true, ["checks"] = new JsonArray(checks.Select(s => (JsonNode?)JsonValue.Create(s)).ToArray()) }.ToJsonString());
}
finally
{
    // Delete owned junction objects first, never recurse through their targets.
    foreach (var path in new[] { Path.Combine(root, "escape"), Path.Combine(root, "dangling"), Path.Combine(fixture, "root-alias") })
        if (Directory.Exists(path) || Path.Exists(path)) Directory.Delete(path);
    if (Directory.Exists(fixture)) Directory.Delete(fixture, true);
}
