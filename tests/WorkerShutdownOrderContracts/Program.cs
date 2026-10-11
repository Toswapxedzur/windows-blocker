using System.Text.Json.Nodes;
using WindowsBlocker.Bridge;
Environment.SetEnvironmentVariable("VAULT_ENVIRONMENT","development");
var storageRoot=Path.Combine(Path.GetTempPath(),"vault-order-contract-"+Guid.NewGuid());
Environment.SetEnvironmentVariable("VAULT_STORAGE_ROOT",storageRoot);
var path=Path.Combine(Path.GetTempPath(),"vault-order-proof-"+Guid.NewGuid()+".json");
try
{
    using var client=new ClassifierWorkerClient(args[0]);
    await client.Request("shutdown-proof",new JsonObject{["path"]=path,["flushHang"]=true});
    var observations=Enumerable.Range(0,32).Select(i=>client.Request("remember",new JsonObject{["text"]="观察"+i})).ToArray();
    // Deliberately ignore the return value: this exact runner compiles against
    // both the old Task-returning API and the new typed-result API.
    await client.ShutdownAsync();
    if(!File.Exists(path))throw new Exception("Shutdown did not persist EOF evidence");
    var proof=JsonNode.Parse(File.ReadAllText(path))!;
    if(proof["eof"]?.GetValue<bool>()!=true || proof["flushAttempted"]!=null)throw new Exception("Pre-EOF flush blocked ordinary EOF shutdown");
    await Task.WhenAll(observations);
    var actual=proof["remembered"]!.AsArray().Select(x=>x!.GetValue<string>());
    if(!actual.SequenceEqual(Enumerable.Range(0,32).Select(i=>"观察"+i)))throw new Exception("An admitted observation was lost/reordered before EOF");
    Console.WriteLine("PASS Exact EOF-order regression preserves32 admitted observations without flush prerequisite");
}
finally
{
    // Preserve measured EOF/flush-attempt evidence before removing the exact
    // randomly owned fixture paths. This runs for baseline failure too.
    try {if(args.Length>1 && File.Exists(path))File.Copy(path,args[1],false);}
    finally
    {
        if(File.Exists(path))File.Delete(path);
        if(Directory.Exists(storageRoot))Directory.Delete(storageRoot,true);
    }
}
