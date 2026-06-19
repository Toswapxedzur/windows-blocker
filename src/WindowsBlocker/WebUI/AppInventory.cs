using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using WindowsBlocker.Enforcement;

namespace WindowsBlocker.WebUI;

// Supplies the editor's app picker with selectable Windows applications. macOS
// enumerates installed .app bundles; here we surface processes that currently
// own a visible main window (the apps a user would actually want to block),
// keyed by executable path so the enforcement matcher can resolve them.
public static class AppInventory
{
    public static string BuildJson()
    {
        var entries = new List<Dictionary<string, string>>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (process.MainWindowHandle == IntPtr.Zero || string.IsNullOrWhiteSpace(process.MainWindowTitle))
                {
                    continue;
                }
                var path = ProcessIdentity.PathForProcess((uint)process.Id);
                if (string.IsNullOrEmpty(path) || !seen.Add(path))
                {
                    continue;
                }
                entries.Add(new Dictionary<string, string>
                {
                    ["id"] = path,
                    ["name"] = FriendlyName(path, process),
                    ["icon"] = ""
                });
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

        entries.Sort((a, b) => string.Compare(a["name"], b["name"], StringComparison.OrdinalIgnoreCase));
        return JsonSerializer.Serialize(entries);
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
