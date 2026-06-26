using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WindowsBlocker.Enforcement;

namespace WindowsBlocker;

// The Windows analog of macOS's ToastOverlayPanelController: a borderless,
// always-on-top, click-through window that stacks colored log toasts in the
// bottom-right corner. Each toast auto-dismisses after ~5 seconds. Matches
// customBlocker's toast/log-feed style (colored card, 3px accent bar, meta line).
public partial class ToastOverlayWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20;
    private const int WS_EX_LAYERED = 0x80000;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x80;
    private const double Inset = 16;
    private const int MaxVisible = 8;
    private static readonly TimeSpan FadeAfter = TimeSpan.FromSeconds(5);

    public sealed class ToastItem
    {
        public string Meta { get; init; } = "";
        public string Message { get; init; } = "";
        public Brush Background { get; init; } = Brushes.Black;
        public Brush Foreground { get; init; } = Brushes.White;
        public Brush Accent { get; init; } = Brushes.SkyBlue;
        public Brush MetaBrush { get; init; } = Brushes.Gray;
    }

    private readonly ObservableCollection<ToastItem> _items = new();

    public ToastOverlayWindow()
    {
        InitializeComponent();
        Toasts.ItemsSource = _items;
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

    public void Show(string message, string level)
    {
        var (bg, fg, accent) = Palette(level);
        var meta = level == "log"
            ? DateTime.Now.ToString("HH:mm:ss")
            : $"{DateTime.Now:HH:mm:ss} \u00b7 {level.ToUpperInvariant()}";

        var item = new ToastItem
        {
            Meta = meta,
            Message = message,
            Background = bg,
            Foreground = fg,
            Accent = accent,
            MetaBrush = Fade(fg, 0.6)
        };

        while (_items.Count >= MaxVisible)
        {
            _items.RemoveAt(0);
        }
        _items.Add(item);

        if (!IsVisible)
        {
            base.Show();
        }
        Reposition();

        var timer = new DispatcherTimer { Interval = FadeAfter };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _items.Remove(item);
            if (_items.Count == 0)
            {
                Hide();
            }
            else
            {
                Reposition();
            }
        };
        timer.Start();
    }

    private void Reposition()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - Inset;
        Top = area.Bottom - ActualHeight - Inset;
    }

    private static (Brush bg, Brush fg, Brush accent) Palette(string level) => level switch
    {
        "error" => (Hex("#7f1d1d"), Hex("#fef2f2"), Hex("#ef4444")),
        "warn" => (Hex("#78350f"), Hex("#fffbeb"), Hex("#f59e0b")),
        _ => (Hex("#0f172a"), Hex("#f1f5f9"), Hex("#38bdf8")),
    };

    private static SolidColorBrush Hex(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }

    private static Brush Fade(Brush brush, double opacity)
    {
        if (brush is SolidColorBrush s)
        {
            var faded = new SolidColorBrush(s.Color) { Opacity = opacity };
            faded.Freeze();
            return faded;
        }
        return brush;
    }
}
