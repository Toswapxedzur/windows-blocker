namespace WindowsBlocker.SelfPreservation;

// Best-effort "stay alive" guard. Windows cannot intercept a hard
// TerminateProcess (Task Manager "End process") from user space without a
// driver / protected-process, which is out of scope. But a normal quit — the
// window X, Alt+F4, taskbar "Close window", and Task Manager's "End task"
// (which first posts WM_CLOSE to the main window) — surfaces as a cancelable
// WPF Window.Closing event. We warn exactly once and cancel that first attempt;
// any subsequent attempt is allowed through, so the user is never truly
// trapped.
public sealed class SelfPreservationGuard
{
    private bool _warned;

    public string WarningTitle { get; init; } = "Windows Vault is still running";

    public string WarningMessage { get; init; } =
        "Closing Windows Vault will stop blocking your selected apps.\n\n" +
        "If you really want to quit, close it again.";

    /// Returns true if the close should be CANCELLED (i.e. show the warning),
    /// false if the close should be allowed to proceed.
    public bool ShouldCancelClose()
    {
        if (_warned)
        {
            return false;
        }
        _warned = true;
        return true;
    }

    /// Resets the guard so the next close attempt warns again (e.g. after the
    /// window is reactivated and the user clearly intends to keep using it).
    public void Rearm() => _warned = false;
}
