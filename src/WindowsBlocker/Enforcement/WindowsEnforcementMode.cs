namespace WindowsBlocker.Enforcement;

// The Windows analog of macOS's MacEnforcementMode ladder. By design the
// Windows port uses exactly one action — CloseWindow (PostMessage WM_CLOSE),
// i.e. the same effect as clicking the red "X" — and relies on the WinEvent
// re-close loop to prevent reopening. The harder rungs (Suspend/Terminate,
// kernel launch-deny) from the macOS design are deliberately left unimplemented
// and listed here only to mark the extension point.
public enum WindowsEnforcementMode
{
    /// PostMessage WM_CLOSE to every top-level window of the blocked app.
    CloseWindow

    // Future (not implemented): Suspend (NtSuspendProcess),
    // Terminate (TerminateProcess), DenyLaunch (PsSetCreateProcessNotifyRoutineEx).
}
