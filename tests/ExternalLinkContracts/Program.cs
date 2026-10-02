using System.Text.Json.Nodes;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using WindowsBlocker.WebUI;

internal static class Program
{
    [STAThread] private static void Main(string[] args)
    {
        var app = new Application(); var web = new WebView2(); var window = new Window { Title="Vault owned external-link fixture", Width=600, Height=400, Content=web };
        window.Loaded += async (_,_) =>
        {
            var checks = new List<string>(); var opened = new List<string>();
            void Check(bool ok,string what) { if(!ok) throw new Exception(what); checks.Add(what); }
            try
            {
                var folder = Path.Combine(args[0],"assets");Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder,"link-fixture.html"),"<a id='source' style='position:absolute;left:30px;top:30px' href='https://example.com/source'>Creator source</a><a id='help' style='position:absolute;left:30px;top:90px' target='_blank' href='https://example.com/help'>Help</a><script>setTimeout(()=>{window.open('https://example.com/programmatic');location.href='https://example.com/scripted';},500)</script>");
                web.CreationProperties = new CoreWebView2CreationProperties {UserDataFolder=Path.Combine(args[0],"webview")};await web.EnsureCoreWebView2Async();var core=web.CoreWebView2!;
                core.SetVirtualHostNameToFolderMapping(NativeEditorContract.Host,folder,CoreWebView2HostResourceAccessKind.DenyCors);
                ExternalLinkHandler.Attach(core,url=>opened.Add(url));
                var loaded=new TaskCompletionSource();core.NavigationCompleted+=(_,e)=> {if(e.IsSuccess) loaded.TrySetResult();};core.Navigate("https://appassets.windowsblocker/link-fixture.html");await loaded.Task.WaitAsync(TimeSpan.FromSeconds(15));
                await Task.Delay(1000);
                Check(opened.Count==0 && (await core.ExecuteScriptAsync("location.href")).Contains("link-fixture.html"),"Programmatic external navigation and window opens cannot invoke the browser: "+string.Join(",",opened)+"; source="+core.Source);
                async Task Click(string id)
                {
                    var position=JsonNode.Parse(await core.ExecuteScriptAsync($"(()=>{{const r=document.getElementById('{id}').getBoundingClientRect();return {{x:r.x+5,y:r.y+5}}}})()"))!;
                    await core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",new JsonObject{["type"]="mousePressed",["button"]="left",["clickCount"]=1,["x"]=position["x"]!.DeepClone(),["y"]=position["y"]!.DeepClone()}.ToJsonString());
                    await core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",new JsonObject{["type"]="mouseReleased",["button"]="left",["clickCount"]=1,["x"]=position["x"]!.DeepClone(),["y"]=position["y"]!.DeepClone()}.ToJsonString());await Task.Delay(250);
                }
                await Click("source");Check(opened.SequenceEqual(new[]{"https://example.com/source"}) && (await core.ExecuteScriptAsync("location.href")).Contains("link-fixture.html"),"Actual WebView source-link click opens externally while preserving the embedded editor");
                await Click("help");Check(opened.SequenceEqual(new[]{"https://example.com/source","https://example.com/help"}),"Actual target-blank help click uses the same external policy");
                File.WriteAllText(args[1],new JsonObject{["ok"]=true,["checks"]=new JsonArray(checks.Select(c=>(JsonNode)JsonValue.Create(c)!).ToArray())}.ToJsonString());
            }
            catch(Exception ex) {File.WriteAllText(args[1],new JsonObject{["ok"]=false,["error"]=ex.ToString()}.ToJsonString());}
            finally { web.Dispose();window.Close();app.Shutdown(); }
        };
        app.Run(window);
    }
}
