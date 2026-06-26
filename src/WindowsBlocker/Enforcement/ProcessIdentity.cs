using System;
using System.IO;
using System.Text;

namespace WindowsBlocker.Enforcement;

// Resolves a window handle to the owning application's identity. This is the
// Windows answer to macOS's stable bundle identifier: there is none, so we
// derive a composite identity (full executable path + file name). For UWP/Store
// apps the visible window is hosted by ApplicationFrameHost.exe, so we descend
// into the child windows to find the real owning process.
public sealed class AppIdentity
{
    public uint ProcessId { get; init; }
    public string ExecutablePath { get; init; } = "";
    public string ExecutableName { get; init; } = "";
    // Application User Model ID for packaged (UWP/Store) apps, lowercased. Empty
    // for unpackaged Win32 apps. This is the only stable identity a Store app the
    // user picked while it wasn't running can be matched by, since its on-disk
    // executable path under WindowsApps isn't knowable from the picker.
    public string Aumid { get; init; } = "";

    public bool IsEmpty => ProcessId == 0 && ExecutablePath.Length == 0 && Aumid.Length == 0;

    // A single stable string a custom rule / log can refer to this app by. Prefers
    // the AUMID for packaged apps, then the full path, then the bare exe name.
    public string Canonical =>
        !string.IsNullOrEmpty(Aumid) ? Aumid :
        !string.IsNullOrEmpty(ExecutablePath) ? ExecutablePath :
        ExecutableName;

    // A short human-readable name (exe file name without extension, title-cased
    // first letter), used as the rule event's appName.
    public string DisplayName
    {
        get
        {
            if (string.IsNullOrEmpty(ExecutableName))
            {
                return Aumid;
            }
            var stem = ExecutableName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? ExecutableName[..^4]
                : ExecutableName;
            return stem.Length > 0 ? char.ToUpperInvariant(stem[0]) + stem[1..] : stem;
        }
    }
}

public static class ProcessIdentity
{
    private const string FrameHost = "applicationframehost.exe";

    public static AppIdentity ForWindow(IntPtr hwnd)
    {
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0)
        {
            return new AppIdentity();
        }

        var path = PathForProcess(pid);
        var name = string.IsNullOrEmpty(path) ? "" : Path.GetFileName(path).ToLowerInvariant();

        // UWP frame host indirection: the real app lives in a child window owned
        // by a different process. Walk children to find it.
        if (name == FrameHost)
        {
            var real = RealOwnerOfFrameHost(hwnd, pid);
            if (!real.IsEmpty)
            {
                return real;
            }
        }

        return new AppIdentity
        {
            ProcessId = pid,
            ExecutablePath = path,
            ExecutableName = name,
            Aumid = AumidForProcess(pid)
        };
    }

    private static AppIdentity RealOwnerOfFrameHost(IntPtr frameWindow, uint frameHostPid)
    {
        AppIdentity found = new();
        NativeMethods.EnumChildWindows(frameWindow, (child, _) =>
        {
            NativeMethods.GetWindowThreadProcessId(child, out var childPid);
            if (childPid != 0 && childPid != frameHostPid)
            {
                var path = PathForProcess(childPid);
                if (!string.IsNullOrEmpty(path))
                {
                    found = new AppIdentity
                    {
                        ProcessId = childPid,
                        ExecutablePath = path,
                        ExecutableName = Path.GetFileName(path).ToLowerInvariant(),
                        Aumid = AumidForProcess(childPid)
                    };
                    return false; // stop enumerating
                }
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    // Resolves the lowercased Application User Model ID of a packaged process, or
    // "" when the process is unpackaged (plain Win32) or inaccessible.
    public static string AumidForProcess(uint pid)
    {
        var handle = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == IntPtr.Zero)
        {
            return "";
        }
        try
        {
            uint length = 0;
            // First call sizes the buffer (returns ERROR_INSUFFICIENT_BUFFER); a
            // length of 0 means the process has no app model identity.
            NativeMethods.GetApplicationUserModelId(handle, ref length, null);
            if (length == 0)
            {
                return "";
            }
            var buffer = new char[length];
            var rc = NativeMethods.GetApplicationUserModelId(handle, ref length, buffer);
            if (rc != 0)
            {
                return "";
            }
            return new string(buffer, 0, (int)Math.Max(0, length - 1)).TrimEnd('\0').ToLowerInvariant();
        }
        catch
        {
            return "";
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }

    public static string PathForProcess(uint pid)
    {
        var handle = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == IntPtr.Zero)
        {
            return "";
        }
        try
        {
            var capacity = 1024u;
            var sb = new StringBuilder((int)capacity);
            if (NativeMethods.QueryFullProcessImageName(handle, 0, sb, ref capacity))
            {
                return sb.ToString().ToLowerInvariant();
            }
            return "";
        }
        finally
        {
            NativeMethods.CloseHandle(handle);
        }
    }
}
