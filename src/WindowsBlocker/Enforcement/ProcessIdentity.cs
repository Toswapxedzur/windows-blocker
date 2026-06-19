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

    public bool IsEmpty => ProcessId == 0 && ExecutablePath.Length == 0;
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

        return new AppIdentity { ProcessId = pid, ExecutablePath = path, ExecutableName = name };
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
                        ExecutableName = Path.GetFileName(path).ToLowerInvariant()
                    };
                    return false; // stop enumerating
                }
            }
            return true;
        }, IntPtr.Zero);
        return found;
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
