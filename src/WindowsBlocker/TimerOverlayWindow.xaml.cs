using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WindowsBlocker.Core;
using WindowsBlocker.Enforcement;

namespace WindowsBlocker;

// The Windows analog of macOS's TimerOverlayPanel: a borderless, always-on-top,
// click-through HUD that floats the live "Name: HH:MM:SS" countdown over whatever
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

    private readonly DispatcherTimer _rotation;
    private IReadOnlyList<TimerDisplayItem> _active=Array.Empty<TimerDisplayItem>();
    private int _page;
    private int _revision;

    public TimerOverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        SizeChanged += (_, _) => Reposition();
        _rotation=new DispatcherTimer {Interval=TimeSpan.FromSeconds(5)};
        _rotation.Tick+=(_,_)=>{_page++;RenderPage();};
        Closed+=(_,_)=>{_revision++;_rotation.Stop();};
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var ex = NativeMethods.GetWindowLong(hwnd, GWL_EXSTYLE);
        NativeMethods.SetWindowLong(hwnd, GWL_EXSTYLE,
            ex | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
    }

    // Keep roster processing off the UI thread for large active-group sets;
    // the screen realizes a single bounded page, never one widget per group.
    public async void UpdateRows(IReadOnlyList<TimerDisplayItem> timers)
    {
        var revision=System.Threading.Interlocked.Increment(ref _revision);
        var snapshot=timers.ToArray();
        var active=snapshot.Length>40
            ? await System.Threading.Tasks.Task.Run(()=>snapshot.Where(t=>t.RemainingSeconds>0).ToArray())
            : snapshot.Where(t=>t.RemainingSeconds>0).ToArray();
        await Dispatcher.InvokeAsync(()=> {
            if(revision!=_revision)return;
            _active=active;
            if(active.Length==0) {
                _page=0; _rotation.Stop(); Rows.ItemsSource=null;
                if(IsVisible)Hide();
                return;
            }
            RenderPage();
            if(!IsVisible)Show();
            Reposition();
        });
    }

    private void RenderPage()
    {
        var area=SystemParameters.WorkArea;
        var capacity=Math.Max(1,(int)Math.Floor((area.Height-2*Inset-16)/18));
        var pages=Math.Max(1,(_active.Count+capacity-1)/capacity);
        _page%=pages;
        Rows.MaxWidth=Math.Max(1,area.Width-2*Inset-20);
        Rows.MaxHeight=Math.Max(1,area.Height-2*Inset-16);
        var visible=_active.Skip(_page*capacity).Take(capacity).Select(t=>new TimerLine(t.Name+":",Format(t.RemainingSeconds))).ToArray();
        var font=new Typeface("Arial");
        var natural=visible.Select(row=>new FormattedText(row.NameDisplay+" "+row.Duration,
            System.Globalization.CultureInfo.CurrentCulture,FlowDirection.LeftToRight,font,13,Brushes.White,
            VisualTreeHelper.GetDpi(this).PixelsPerDip).Width+8).DefaultIfEmpty(100).Max();
        Rows.Width=Math.Min(Rows.MaxWidth,Math.Max(100,natural));
        Rows.ItemsSource=visible;
        if(pages>1) {if(!_rotation.IsEnabled)_rotation.Start();} else _rotation.Stop();
    }

    private sealed record TimerLine(string NameDisplay, string Duration);

    private void Reposition()
    {
        // Top-left of the primary work area, matching the macOS HUD placement.
        var area = SystemParameters.WorkArea;
        Left = area.Left + Inset;
        Top = area.Top + Inset;
    }

    // Match the shared HUD: round remaining time up and always show HH:MM:SS.
    internal static string Format(double seconds)
    {
        var total = double.IsFinite(seconds) ? (long)Math.Min(int.MaxValue,Math.Ceiling(Math.Max(0,seconds))) : 0;
        return $"{total/3600:D2}:{total/60%60:D2}:{total%60:D2}";
    }
}
