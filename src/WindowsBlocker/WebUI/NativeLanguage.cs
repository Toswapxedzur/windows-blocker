using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WindowsBlocker.WebUI;

// Only bundled UI text and the interface preference are read here.
public static class NativeLanguage
{
    public static string Language {
        get {
            string preferred = CultureInfo.CurrentUICulture.Name;
            try { preferred = JsonNode.Parse(new WebStore().LoadRawJson() ?? "{}")?["vaultUiLanguage"]?.GetValue<string>() ?? preferred; } catch { }
            string code = preferred.ToLowerInvariant().Split('-')[0];
            return new[] {"en","ar","bn","de","es","fr","hi","id","it","ja","ko","nl","pa","pl","pt","ru","th","tr","vi","zh"}.Contains(code) ? code : "en";
        }
    }
    public static string Text(string key, string fallback, params (string Name, string Value)[] values) {
        string result = fallback;
        try {
            var path = Path.Combine(AppContext.BaseDirectory,"WebAssets","translation",Language+".json");
            var catalog = JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(path));
            if (catalog?.TryGetValue(key,out var translated) == true) result = translated;
        } catch { }
        foreach (var (name,value) in values) result = result.Replace("{"+name+"}",value);
        return result;
    }
}
