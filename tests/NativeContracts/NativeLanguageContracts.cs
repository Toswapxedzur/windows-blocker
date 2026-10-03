using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using WindowsBlocker.WebUI;

internal static class NativeLanguageContracts
{
    public static void Run(Action<bool, string> check)
    {
        var previousCulture = CultureInfo.CurrentUICulture;
        var previous = File.Exists(Storage.WebStorePath) ? File.ReadAllBytes(Storage.WebStorePath) : null;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        var locales = new[] { "en", "ar", "bn", "de", "es", "fr", "hi", "id", "it", "ja", "ko", "nl", "pa", "pl", "pt", "ru", "th", "tr", "vi", "zh" };
        try
        {
            foreach (var code in locales.Concat(new[] { "ar-EG", "zh-CN", "zz" }).Concat(locales.Reverse()))
            {
                var selected = code == "ar-EG" ? "ar" : code == "zh-CN" ? "zh" : code == "zz" ? "en" : code;
                var raw = new JsonObject { ["vaultUiLanguage"] = code, ["blockedGroups"] = new JsonArray(new JsonObject { ["name"] = "My group 我的分组" }) }.ToJsonString();
                File.WriteAllText(Storage.WebStorePath, raw);
                var expected = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "WebAssets", "translation", selected + ".json")))!;
                check(NativeLanguage.Language == selected, code + ": saved native language takes priority over OS language");
                foreach (var key in expected.Keys.Where(key => key.StartsWith("native.") || key.StartsWith("windows.")).Concat(new[] { "contentPage.button", "settings.localFolderChoose" }))
                    if (NativeLanguage.Text(key, "UNEXPECTED FALLBACK") != expected[key]) throw new Exception(code + ": native text differs for " + key);
                check(true, code + ": native menu, prompt, folder and panel strings match the bundled catalog");
                var name = "My group 我的分组 <x>";
                check(NativeLanguage.Text("native.quickAdd", "UNEXPECTED", ("group", name)) == expected["native.quickAdd"].Replace("{group}", name), code + ": user names remain literal during interpolation");
                check(NativeLanguage.Text("unknown", "Literal diagnostic <x>") == "Literal diagnostic <x>" && File.ReadAllText(Storage.WebStorePath) == raw, code + ": diagnostic fallback and persisted policy remain untouched");
            }
            File.WriteAllText(Storage.WebStorePath, "not JSON");
            check(NativeLanguage.Language == "en", "Corrupt preference safely falls back to the OS language");
            File.Delete(Storage.WebStorePath);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");
            check(NativeLanguage.Language == "fr", "Absent preference follows a supported OS language");
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousCulture;
            if (previous is null) File.Delete(Storage.WebStorePath); else File.WriteAllBytes(Storage.WebStorePath, previous);
        }
    }
}
