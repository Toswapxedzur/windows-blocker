using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using WindowsBlocker.Enforcement;
using WindowsBlocker.Bridge;

namespace WindowsBlocker.WebUI;

// Supplies the editor's app picker with selectable Windows applications. Like
// macOS (which lists installed .app bundles), this lists every installed,
// launchable app — even ones not running right now — via the Shell AppsFolder
// (InstalledAppCatalog), then folds in any currently-running unpackaged app that
// has no AppsFolder entry (e.g. portable exes). Each entry is { id, name, icon }
// where id is an AUMID (UWP/Store) or an executable path (Win32), matching how
// the enforcement layer identifies a running window.
public static class AppInventory
{
    private static volatile string _cachedJson = "[]";
    private static readonly Dictionary<string,InstalledAppCatalog.AppEntry> _activityApps=new(StringComparer.OrdinalIgnoreCase);
    public static (string Name,string? Icon) DescribeActivity(AppIdentity identity)
    {
        if(identity.IsEmpty) return ("Unknown",null);
        var key=identity.Canonical;
        lock(_activityApps)
        {
            if(_activityApps.TryGetValue(key,out var cached)) return (cached.Name,cached.Icon);
            var entry=(JsonNode.Parse(_cachedJson) as JsonArray)?.OfType<JsonObject>().FirstOrDefault(a=>a["id"]?.GetValue<string>().Equals(key,StringComparison.OrdinalIgnoreCase)==true);
            var name=entry?["name"]?.GetValue<string>() ?? identity.DisplayName; var icon=entry?["icon"]?.GetValue<string>();
            if(icon==null && identity.ExecutablePath.Length>0) { var described=InstalledAppCatalog.DescribeExecutable(identity.ExecutablePath,name); if(described.HasValue) { name=described.Value.Name;icon=described.Value.Icon; } }
            if(_activityApps.Count>=500) _activityApps.Clear();
            _activityApps[key]=new(key,name,icon ?? ""); return (name,icon);
        }
    }

    public static string? IconForStoredId(string value)
    {
        var id=WindowsAppId.Normalize(value);if(id==null)return null;
        var identity=Path.IsPathFullyQualified(id) ? new AppIdentity {ExecutablePath=id,ExecutableName=Path.GetFileName(id)} : new AppIdentity {Aumid=id};
        var icon=DescribeActivity(identity).Icon;
        return icon?.StartsWith("data:image/",StringComparison.Ordinal)==true && icon.Length<=24_000 ? icon : null;
    }

    /// The most recently built inventory JSON (or "[]"). Non-blocking; used as a
    /// fallback for the WebResourceRequested handler.
    public static string CachedJson() => _cachedJson;

    /// Builds the inventory on a dedicated STA thread (Shell COM + icon
    /// extraction require single-threaded-apartment COM) and caches the result.
    public static Task<string> BuildJsonAsync()
    {
        var tcs = new TaskCompletionSource<string>();
        var thread = new Thread(() =>
        {
            try { tcs.SetResult(BuildJsonSta()); }
            catch { tcs.TrySetResult(_cachedJson); }
        })
        {
            IsBackground = true,
            Name = "AppInventory"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }

    private static string BuildJsonSta()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<InstalledAppCatalog.AppEntry>();

        // 1) Every installed/launchable app (includes not-running + Store apps).
        foreach (var entry in InstalledAppCatalog.EnumerateInstalled())
        {
            if (string.IsNullOrEmpty(entry.Id) || !seen.Add(entry.Id))
            {
                continue;
            }
            entries.Add(entry);
        }

        // 2) Fold in currently-running, windowed, UNPACKAGED apps that aren't in
        //    the AppsFolder (packaged apps are already covered by their AUMID).
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.MainWindowHandle == IntPtr.Zero || !NativeMethods.IsWindowVisible(process.MainWindowHandle))
                {
                    continue;
                }
                var pid = (uint)process.Id;
                if (!string.IsNullOrEmpty(ProcessIdentity.AumidForProcess(pid)))
                {
                    continue; // packaged → already listed via AppsFolder
                }
                var path = ProcessIdentity.PathForProcess(pid);
                if (string.IsNullOrEmpty(path) || !seen.Add(path))
                {
                    continue;
                }
                var entry = InstalledAppCatalog.DescribeExecutable(path, FriendlyName(path, process));
                if (entry.HasValue)
                {
                    entries.Add(entry.Value);
                }
            }
            catch
            {
                // Inaccessible/elevated processes are skipped.
            }
            finally
            {
                process.Dispose();
            }
        }

        entries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

        var array = new JsonArray();
        foreach (var entry in entries)
        {
            array.Add(new JsonObject
            {
                ["id"] = entry.Id,
                ["name"] = entry.Name,
                ["icon"] = entry.Icon
            });
        }
        var json = array.ToJsonString();
        _cachedJson = json;
        lock(_activityApps) _activityApps.Clear();
        return json;
    }

    private static string FriendlyName(string path, Process process)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            if (!string.IsNullOrWhiteSpace(info.FileDescription))
            {
                return info.FileDescription!;
            }
        }
        catch
        {
            // Fall through to the file name.
        }
        try
        {
            return Path.GetFileNameWithoutExtension(path);
        }
        catch
        {
            return process.ProcessName;
        }
    }
}
