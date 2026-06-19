using System;

namespace WindowsBlocker.Enforcement;

// Low-latency "user attempted to open a blocked app" detector. Hooks window
// show + foreground events system-wide (the Windows analog of macOS's
// NSWorkspace.didActivateApplicationNotification used by BrowserFocusObserver),
// so a relaunched or tray-restored window is caught the instant it appears
// rather than waiting for the 1-second tick.
public sealed class WinEventMonitor : IDisposable
{
    // Held as a field so the GC never collects the delegate while the unmanaged
    // hook still points at it (a classic SetWinEventHook crash otherwise).
    private readonly NativeMethods.WinEventDelegate _callback;
    private IntPtr _showHook;
    private IntPtr _foregroundHook;
    private readonly Action<IntPtr> _onWindowEvent;

    public WinEventMonitor(Action<IntPtr> onWindowEvent)
    {
        _onWindowEvent = onWindowEvent;
        _callback = OnWinEvent;
    }

    /// Must be called on a thread with a running message loop (the WPF UI thread
    /// qualifies) because the hook is WINEVENT_OUTOFCONTEXT.
    public void Start()
    {
        _showHook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_OBJECT_SHOW, NativeMethods.EVENT_OBJECT_SHOW,
            IntPtr.Zero, _callback, 0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT);
        _foregroundHook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_FOREGROUND, NativeMethods.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _callback, 0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT);
    }

    private void OnWinEvent(
        IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        // Only window-level objects, not child controls/menus.
        if (hwnd == IntPtr.Zero || idObject != NativeMethods.OBJID_WINDOW)
        {
            return;
        }
        _onWindowEvent(hwnd);
    }

    public void Dispose()
    {
        if (_showHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_showHook);
            _showHook = IntPtr.Zero;
        }
        if (_foregroundHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_foregroundHook);
            _foregroundHook = IntPtr.Zero;
        }
    }
}
