using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace WindowsBlocker.Rules;

// The Windows analog of MacBlockerCore/CustomJavaScriptPolicyRuntime.swift.
//
// macOS runs the verbatim custom-rule-runtime.js (which defines a self-contained
// `MacBlockerRuntime`) in JavaScriptCore. The user picked WebView2 as the JS
// host on Windows, so instead of a second engine we run that exact same script
// inside the editor's WebView2 page (injected at document-creation time) and
// call MacBlockerRuntime.load/unload/dispatch over ExecuteScriptAsync. The
// runtime is pure JS (no DOM), so it behaves identically to the macOS JSContext.
//
// All methods must be invoked on the UI thread (ExecuteScriptAsync requires it);
// the enforcement tick already runs on the dispatcher, so awaiting here is safe.
public sealed class CustomRuleRuntime
{
    private readonly CoreWebView2 _web;

    private static readonly JsonSerializerOptions SerializeOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly JsonSerializerOptions DeserializeOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public CustomRuleRuntime(CoreWebView2 web) => _web = web;

    /// True once the injected custom-rule-runtime.js has defined MacBlockerRuntime.
    public async Task<bool> IsReadyAsync()
    {
        var raw = await _web.ExecuteScriptAsync(
            "(typeof MacBlockerRuntime !== 'undefined' && !!MacBlockerRuntime.dispatch)");
        return raw == "true";
    }

    public async Task<LoadResult?> LoadAsync(string groupId, string source)
    {
        var gid = JsonSerializer.Serialize(groupId);
        var src = JsonSerializer.Serialize(source);
        var expr =
            $"(function(){{try{{return JSON.parse(MacBlockerRuntime.load({gid},{src}));}}" +
            $"catch(e){{return {{__error:String(e)}};}}}})()";
        return await EvalAsync<LoadResult>(expr);
    }

    public async Task UnloadAsync(string groupId)
    {
        var gid = JsonSerializer.Serialize(groupId);
        try
        {
            await _web.ExecuteScriptAsync(
                $"(function(){{try{{MacBlockerRuntime.unload({gid});}}catch(e){{}}}})()");
        }
        catch
        {
            // Page may be navigating; the runtime resets on reload anyway.
        }
    }

    public async Task<DispatchResult?> DispatchAsync(CustomRuleEvent ev)
    {
        var eventJson = JsonSerializer.Serialize(ev, SerializeOptions);
        // Embed the event JSON as a JS string literal and JSON.parse it inside the
        // page, exactly as CustomJavaScriptPolicyRuntime.dispatch does in Swift.
        var literal = JsonSerializer.Serialize(eventJson);
        var expr =
            $"(function(){{try{{return JSON.parse(MacBlockerRuntime.dispatch(JSON.parse({literal})));}}" +
            $"catch(e){{return {{__error:String(e)}};}}}})()";
        return await EvalAsync<DispatchResult>(expr);
    }

    private async Task<T?> EvalAsync<T>(string expr) where T : class
    {
        string raw;
        try
        {
            raw = await _web.ExecuteScriptAsync(expr);
        }
        catch
        {
            return null;
        }
        if (string.IsNullOrEmpty(raw) || raw == "null")
        {
            return null;
        }
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("__error", out _))
            {
                return null;
            }
            return JsonSerializer.Deserialize<T>(raw, DeserializeOptions);
        }
        catch
        {
            return null;
        }
    }
}
