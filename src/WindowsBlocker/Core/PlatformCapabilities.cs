namespace WindowsBlocker.Core;

// Ported from MacBlockerCore/PlatformCapabilities.swift, with a Windows profile
// added. Windows has no Screen Time framework; enforcement is window-close
// based (WM_CLOSE) plus a re-close monitor, so it advertises the floating
// status window and "arbitrary app UI" (window close/foreground control) but
// not Screen Time shielding. Custom rules are excluded from the port.
public sealed class PlatformCapabilities
{
    public bool ScreenTimeShielding { get; init; }
    public bool DeviceActivityMonitoring { get; init; }
    public bool CustomShieldUI { get; init; }
    public bool FloatingStatusWindow { get; init; }
    public bool AccessibilityControl { get; init; }
    public bool ScreenCaptureAssist { get; init; }
    public bool ArbitraryAppUIManipulation { get; init; }

    public static readonly PlatformCapabilities Windows = new()
    {
        ScreenTimeShielding = false,
        DeviceActivityMonitoring = false,
        CustomShieldUI = false,
        FloatingStatusWindow = true,
        AccessibilityControl = false,
        ScreenCaptureAssist = false,
        ArbitraryAppUIManipulation = true
    };
}
