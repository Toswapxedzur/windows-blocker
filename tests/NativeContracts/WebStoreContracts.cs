using System.Text.Json.Nodes;
using WindowsBlocker.WebUI;

internal static class WebStoreContracts
{
    public static void Run(Action<bool, string> check)
    {
        var store = new WebStore();
        Directory.CreateDirectory(Storage.RootDirectory);
        store.SaveRaw("{\"blockedGroups\":[],\"cbRuleState\":{\"other\":{\"kept\":true}}}");
        for (var i = 0; i < 64; i++)
        {
            // An IPC observer may hold the previous snapshot while the app
            // commits a new one. It explicitly allows replacement/deletion.
            using var reader = new FileStream(store.FilePath, FileMode.Open,
                FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var previous = JsonNode.Parse(reader);
            store.Update(root => root["sequence"] = i);
            reader.Position = 0;
            if (JsonNode.Parse(reader)?.ToJsonString() != previous?.ToJsonString())
                throw new Exception("Replacing native storage changed an open reader's snapshot");
        }
        var current = JsonNode.Parse(store.LoadRawJson()!);
        check(current?["sequence"]?.GetValue<int>() == 63
            && current?["cbRuleState"]?["other"]?["kept"]?.GetValue<bool>() == true,
            "Native storage commits every update while an observer holds the previous complete snapshot");
        using (var reader = new FileStream(store.FilePath, FileMode.Open,
            FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            store.WriteUsage(new() { ["usage"] = 1000 }, new());
            check(JsonNode.Parse(reader)?["usageTimersMs"] is null,
                "Usage saves replace the document without changing an observer's old snapshot");
        }
        check(JsonNode.Parse(store.LoadRawJson()!)?["sequence"]?.GetValue<int>() == 63,
            "Usage persistence preserves rule state and other native store keys");
        // Other contract fixtures expect an empty initial policy document.
        store.SaveRaw("{\"blockedGroups\":[]}");
    }
}
