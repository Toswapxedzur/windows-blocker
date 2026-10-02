namespace WindowsBlocker.Enforcement;

public static class NativeAppSafety
{
    private static readonly HashSet<string> Browsers = new(StringComparer.OrdinalIgnoreCase) { "chrome.exe", "msedge.exe", "firefox.exe", "opera.exe", "brave.exe", "vivaldi.exe", "iexplore.exe" };
    public static bool CanControl(AppIdentity app)
    {
        if (app.IsEmpty || app.ProcessId == Environment.ProcessId || Browsers.Contains(app.ExecutableName)) return false;
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\') + "\\";
        return !app.ExecutablePath.StartsWith(windows, StringComparison.OrdinalIgnoreCase)
            && !app.ExecutableName.Equals("WindowsBlocker.exe", StringComparison.OrdinalIgnoreCase)
            && !app.ExecutableName.Equals("VaultClassifierWorker.exe", StringComparison.OrdinalIgnoreCase);
    }
}
