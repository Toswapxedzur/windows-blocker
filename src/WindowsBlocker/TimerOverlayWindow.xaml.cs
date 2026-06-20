using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using WindowsBlocker.Core;
using WindowsBlocker.Enforcement;

namespace WindowsBlocker;

// The Windows analog of macOS's TimerOverlayPanel: a borderless, always-on-top,
// click-through HUD that floats the live "Name: MM:SS" countdown over whatever
// app is frontmost. It never steals focus and passes all input through to the
// window beneath it (WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_NOACTIVATE), and
// is hidden from Alt-Tab (WS_EX_TOOLWINDOW). Pixels float over other apps;
// nothing is injected into them.
public partial class TimerOverlayWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20;
    private const int WS_EX_LAYERED = 0x80000;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x80;
    private const double Inset = 16;

    public TimerOverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        SizeChanged += (_, _) => Reposition();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var ex = NativeMethods.GetWindowLong(hwnd, GWL_EXSTYLE);
        NativeMethods.SetWindowLong(hwnd, GWL_EXSTYLE,
            ex | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
    }

    // Replaces the visible rows. An empty set hides the HUD entirely.
    public void UpdateRows(IReadOnlyList<TimerDisplayItem> timers)
    {
        var rows = timers
            .Where(t => t.RemainingSeconds > 0)
            .Select(t => $"{t.Name}: {Format(t.RemainingSeconds)}")
            .ToList();

        if (rows.Count == 0)
        {
            Rows.ItemsSource = null;
            if (IsVisible)
            {
                Hide();
            }
            return;
        }

        Rows.ItemsSource = rows;
        if (!IsVisible)
        {
            Show();
        }
        Reposition();
    }

    private void Reposition()
    {
        // Top-left of the primary work area, matching the macOS HUD placement.
        var area = SystemParameters.WorkArea;
        Left = area.Left + Inset;
        Top = area.Top + Inset;
    }

    // H:MM:SS once an hour is involved, otherwise M:SS — matching the macOS
    // overlay's TimerOverlayRow.formattedRemaining.
    private static string Format(double seconds)
    {
        var total = (int)Math.Round(seconds);
        var hours = total / 3600;
        var minutes = (total % 3600) / 60;
        var secs = total % 60;
        return hours > 0
            ? $"{hours}:{minutes:D2}:{secs:D2}"
            : $"{minutes}:{secs:D2}";
    }
}
