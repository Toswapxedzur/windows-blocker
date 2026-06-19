using System;

namespace WindowsBlocker.Enforcement;

// Closes windows by posting WM_CLOSE — the exact effect of clicking the red "X".
// This is the only enforcement action (per design). It is cooperative: a tray
// app minimizes instead of exiting (re-caught by the WinEvent monitor when it
// reappears) and an app with an unsaved-changes prompt may stay up.
public static class WindowCloser
{
    /// Sweeps every top-level, visible, non-tool window and posts WM_CLOSE to
    /// those whose owning app matches the registry. Returns the count closed.
    public static int CloseBlockedWindows(BlockedAppRegistry registry, IntPtr selfWindow)
    {
        if (!registry.HasAny)
        {
            return 0;
        }
        var closed = 0;
        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!IsCloseableTopLevel(hwnd) || hwnd == selfWindow)
            {
                return true;
            }
            var identity = ProcessIdentity.ForWindow(hwnd);
            if (registry.IsBlocked(identity))
            {
                CloseWindow(hwnd);
                closed++;
            }
            return true;
        }, IntPtr.Zero);
        return closed;
    }

    /// Closes a single window if its owning app is blocked. Used by the
    /// WinEvent monitor on the low-latency show/foreground path.
    public static bool CloseIfBlocked(IntPtr hwnd, BlockedAppRegistry registry, IntPtr selfWindow)
    {
        if (hwnd == selfWindow || !registry.HasAny || !IsCloseableTopLevel(hwnd))
        {
            return false;
        }
        var identity = ProcessIdentity.ForWindow(hwnd);
        if (!registry.IsBlocked(identity))
        {
            return false;
        }
        CloseWindow(hwnd);
        return true;
    }

    public static void CloseWindow(IntPtr hwnd)
    {
        // PostMessage (not SendMessage): non-blocking, never hangs us if the
        // target app is unresponsive.
        NativeMethods.PostMessage(hwnd, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
    }

    private static bool IsCloseableTopLevel(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd) || !NativeMethods.IsWindowVisible(hwnd))
        {
            return false;
        }
        // Only true top-level windows (the ones that own a red X).
        if (NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT) != hwnd)
        {
            return false;
        }
        // Skip tool windows (tray helpers, tooltips) — they have no user-facing X.
        var exStyle = (long)(uint)NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        if ((exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0)
        {
            return false;
        }
        // A real app window has a title.
        return NativeMethods.GetWindowTextLength(hwnd) > 0;
    }
}
