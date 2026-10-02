using System.Diagnostics;
using Microsoft.Web.WebView2.Core;

namespace WindowsBlocker.WebUI;

public static class ExternalLinkHandler
{
    // Only the visible trusted editor receives this handler. The hidden rule
    // runtime keeps its unconditional external-navigation rejection.
    public static void Attach(CoreWebView2 core, Action<string>? open = null)
    {
        open ??= value => Process.Start(new ProcessStartInfo(value) { UseShellExecute = true });
        void Open(string value, bool userInitiated)
        {
            if (!NativeEditorContract.ExternalLink(value, userInitiated)) return;
            try { open(value); } catch { /* A missing browser must not crash Vault. */ }
        }
        core.NewWindowRequested += (_, args) => { args.Handled = true; Open(args.Uri, args.IsUserInitiated); };
        core.NavigationStarting += (_, args) =>
        {
            if (NativeEditorContract.TrustedUri(args.Uri)) return;
            args.Cancel = true;
            // A redirect is not a new user gesture and cannot open another app.
            Open(args.Uri, args.IsUserInitiated && !args.IsRedirected);
        };
    }
}
