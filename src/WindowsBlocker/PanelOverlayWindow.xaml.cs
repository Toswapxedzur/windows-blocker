using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using WindowsBlocker.Enforcement;
using WindowsBlocker.Rules;

namespace WindowsBlocker;

/// (groupId, panelId, controlId, eventName, value, valuesJson)
public delegate void PanelEventHandler(string groupId, string panelId, string controlId, string eventName, string value, string valuesJson);

// Manager for the interactive panel overlay — the Windows analog of macOS's
// PanelOverlayPanelController. Like macOS it keeps one borderless, topmost,
// content-sized window per screen position (top-left … center), each stacking
// that position's panel cards. The rendered overlay is the authoritative merge
// of every group's panels (system panels included), so a panel disappears as
// soon as its group stops reporting it.
public sealed class PanelOverlay
{
    private readonly Dictionary<string, PanelOverlayWindow> _windows = new();
    public PanelEventHandler? OnEvent { get; set; }

    public void ReplaceAll(IReadOnlyDictionary<string, List<PanelSnapshot>> panelsByGroup)
    {
        var byPosition = new Dictionary<string, List<PanelSnapshot>>();
        foreach (var groupId in panelsByGroup.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            foreach (var panel in panelsByGroup[groupId])
            {
                if (panel.Visible == false)
                {
                    continue;
                }
                var pos = string.IsNullOrEmpty(panel.Position) ? "bottom-right" : panel.Position!;
                if (!byPosition.TryGetValue(pos, out var list))
                {
                    list = new List<PanelSnapshot>();
                    byPosition[pos] = list;
                }
                list.Add(panel);
            }
        }

        foreach (var pos in _windows.Keys.Union(byPosition.Keys).ToList())
        {
            var panels = byPosition.TryGetValue(pos, out var p) ? p : new List<PanelSnapshot>();
            if (panels.Count == 0)
            {
                if (_windows.TryGetValue(pos, out var w))
                {
                    w.SetCards(panels);
                    w.Hide();
                }
                continue;
            }
            var win = EnsureWindow(pos);
            win.SetCards(panels);
            if (!win.IsVisible)
            {
                win.Show();
            }
            win.RepositionFor(pos);
        }
    }

    public void Teardown()
    {
        foreach (var w in _windows.Values)
        {
            w.Close();
        }
        _windows.Clear();
    }

    private PanelOverlayWindow EnsureWindow(string position)
    {
        if (_windows.TryGetValue(position, out var existing))
        {
            return existing;
        }
        var win = new PanelOverlayWindow();
        win.OnEvent = (g, pid, cid, ev, val, vals) => OnEvent?.Invoke(g, pid, cid, ev, val, vals);
        new WindowInteropHelper(win).EnsureHandle();
        _windows[position] = win;
        return win;
    }
}

public partial class PanelOverlayWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x80;
    private const double Inset = 16;

    public PanelEventHandler? OnEvent { get; set; }
    private readonly Dictionary<string, PanelCard> _cards = new();

    public PanelOverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        SizeChanged += (_, _) => RepositionFor(_position);
    }

    private string _position = "bottom-right";

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        // Hidden from Alt-Tab, but still activatable so panel inputs (e.g. a
        // parental PIN field) can receive keyboard focus when clicked.
        var hwnd = new WindowInteropHelper(this).Handle;
        var ex = NativeMethods.GetWindowLong(hwnd, GWL_EXSTYLE);
        NativeMethods.SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TOOLWINDOW);
    }

    public void SetCards(List<PanelSnapshot> panels)
    {
        var keep = new HashSet<string>(panels.Select(p => p.Id));
        foreach (var id in _cards.Keys.ToList())
        {
            if (!keep.Contains(id))
            {
                Stack.Children.Remove(_cards[id].Root);
                _cards.Remove(id);
            }
        }

        foreach (var panel in panels)
        {
            if (_cards.TryGetValue(panel.Id, out var card))
            {
                card.Update(panel);
            }
            else
            {
                var newCard = new PanelCard(panel, (g, pid, cid, ev, val, vals) => OnEvent?.Invoke(g, pid, cid, ev, val, vals));
                _cards[panel.Id] = newCard;
                Stack.Children.Add(newCard.Root);
            }
        }

        // Order cards to match the incoming order.
        for (var i = 0; i < panels.Count; i++)
        {
            if (_cards.TryGetValue(panels[i].Id, out var card))
            {
                var current = Stack.Children.IndexOf(card.Root);
                if (current != i && current >= 0)
                {
                    Stack.Children.Remove(card.Root);
                    Stack.Children.Insert(Math.Min(i, Stack.Children.Count), card.Root);
                }
            }
        }
    }

    public void RepositionFor(string position)
    {
        _position = position;
        var area = SystemParameters.WorkArea;
        switch (position)
        {
            case "top-left":
                Left = area.Left + Inset; Top = area.Top + Inset; break;
            case "top-right":
                Left = area.Right - ActualWidth - Inset; Top = area.Top + Inset; break;
            case "bottom-left":
                Left = area.Left + Inset; Top = area.Bottom - ActualHeight - Inset; break;
            case "center":
                Left = area.Left + (area.Width - ActualWidth) / 2;
                Top = area.Top + (area.Height - ActualHeight) / 2;
                break;
            default: // bottom-right
                Left = area.Right - ActualWidth - Inset;
                Top = area.Bottom - ActualHeight - Inset;
                break;
        }
    }
}

// Renders one PanelSnapshot into a themed card. Rebuilds when the snapshot
// changes, but defers a rebuild while any of its inputs hold keyboard focus so
// in-progress typing (e.g. a PIN) is never wiped — the Windows counterpart of
// macOS's local-state input controls.
internal sealed class PanelCard
{
    public Border Root { get; }
    private readonly PanelEventHandler _onEvent;
    private string _lastJson = "";
    private PanelSnapshot? _pending;

    public PanelCard(PanelSnapshot snapshot, PanelEventHandler onEvent)
    {
        _onEvent = onEvent;
        Root = new Border { Margin = new Thickness(0, 0, 0, 8) };
        Root.LostKeyboardFocus += (_, _) =>
        {
            if (_pending != null && !Root.IsKeyboardFocusWithin)
            {
                var p = _pending;
                _pending = null;
                Rebuild(p);
            }
        };
        Rebuild(snapshot);
    }

    public void Update(PanelSnapshot snapshot)
    {
        var json = JsonSerializer.Serialize(snapshot);
        if (json == _lastJson)
        {
            return;
        }
        if (Root.IsKeyboardFocusWithin)
        {
            _pending = snapshot;
            return;
        }
        Rebuild(snapshot);
    }

    private void Rebuild(PanelSnapshot snapshot)
    {
        _lastJson = JsonSerializer.Serialize(snapshot);
        var theme = snapshot.Theme;
        Root.Background = Brush(theme?.Background, "#f50f172a");
        Root.BorderBrush = Brush(theme?.Border, "#73808080");
        Root.BorderThickness = new Thickness(1);
        Root.CornerRadius = new CornerRadius(14);
        Root.Padding = new Thickness(12);
        Root.Width = PanelWidth(snapshot.Width);
        Root.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, ShadowDepth = 5, Opacity = 0.32, Color = Colors.Black };

        var fg = Brush(theme?.Foreground, "#f8fafc");
        var stack = new StackPanel();

        if (!string.IsNullOrEmpty(snapshot.Title))
        {
            stack.Children.Add(new TextBlock { Text = snapshot.Title, FontSize = 14, FontWeight = FontWeights.Bold, Foreground = fg, Margin = new Thickness(0, 0, 0, 4) });
        }
        if (!string.IsNullOrEmpty(snapshot.Description))
        {
            stack.Children.Add(new TextBlock { Text = snapshot.Description, FontSize = 13, Foreground = fg, Opacity = 0.82, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
        }

        var groupId = snapshot.GroupId ?? "";
        var valuesJson = CollectValuesJson(snapshot);
        var accent = Brush(theme?.Accent, "#2563eb");
        foreach (var control in snapshot.Controls ?? new List<PanelControl>())
        {
            var el = BuildControl(control, snapshot.Id, groupId, valuesJson, fg, accent);
            if (el != null)
            {
                el.Margin = new Thickness(0, 3, 0, 3);
                stack.Children.Add(el);
            }
        }

        Root.Child = stack;
    }

    private FrameworkElement? BuildControl(PanelControl c, string panelId, string groupId, string valuesJson, Brush fg, Brush accent)
    {
        void Fire(string ev, string value) => _onEvent(groupId, panelId, c.Id, ev, value, valuesJson);

        switch (c.Type)
        {
            case "text":
                return new TextBlock { Text = c.Text ?? c.Label ?? "", FontSize = 13, Foreground = fg, Opacity = 0.85, TextWrapping = TextWrapping.Wrap };

            case "button":
            {
                var b = new Button { Content = c.Label ?? "Button", Padding = new Thickness(14, 6, 14, 6), IsEnabled = c.Disabled != true };
                b.Click += (_, _) => Fire("click", c.Action ?? "");
                return b;
            }

            case "checkbox":
            case "toggle":
            {
                var cb = new CheckBox { Content = c.Label ?? "", IsChecked = c.ValueBool, Foreground = fg, IsEnabled = c.Disabled != true };
                cb.Checked += (_, _) => Fire("change", "true");
                cb.Unchecked += (_, _) => Fire("change", "false");
                return Labeled(null, cb, fg);
            }

            case "textInput":
            case "numberInput":
            case "date":
            case "time":
            {
                var initial = c.Type == "numberInput" ? c.ValueDouble.ToString("g", CultureInfo.InvariantCulture) : c.ValueString;
                var tb = new TextBox { Text = initial, IsEnabled = c.Disabled != true };
                if (!string.IsNullOrEmpty(c.Placeholder)) tb.ToolTip = c.Placeholder;
                tb.TextChanged += (_, _) => Fire("change", tb.Text);
                return Labeled(c.Label, tb, fg);
            }

            case "textarea":
            {
                var tb = new TextBox { Text = c.ValueString, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = (c.Rows ?? 3) * 20, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, IsEnabled = c.Disabled != true };
                tb.TextChanged += (_, _) => Fire("change", tb.Text);
                return Labeled(c.Label, tb, fg);
            }

            case "range":
            {
                var slider = new Slider
                {
                    Minimum = c.Min ?? 0,
                    Maximum = Math.Max((c.Max ?? 100), (c.Min ?? 0) + (c.Step ?? 1)),
                    Value = Math.Min(Math.Max(c.ValueDouble, c.Min ?? 0), c.Max ?? 100),
                    IsEnabled = c.Disabled != true
                };
                if ((c.Step ?? 0) > 0) { slider.TickFrequency = c.Step!.Value; slider.IsSnapToTickEnabled = true; }
                slider.ValueChanged += (_, e) => Fire("change", e.NewValue.ToString(CultureInfo.InvariantCulture));
                return Labeled(c.Label, slider, fg);
            }

            case "select":
            {
                var combo = new ComboBox { IsEnabled = c.Disabled != true };
                foreach (var opt in c.Options ?? new List<PanelOption>())
                {
                    combo.Items.Add(new ComboBoxItem { Content = opt.Label, Tag = opt.Value, IsSelected = opt.Value == c.ValueString });
                }
                combo.SelectionChanged += (_, _) =>
                {
                    if (combo.SelectedItem is ComboBoxItem item && item.Tag is string v) Fire("change", v);
                };
                return Labeled(c.Label, combo, fg);
            }

            case "radio":
            {
                var panel = new StackPanel();
                var gn = $"{panelId}:{c.Id}:{Guid.NewGuid():N}";
                foreach (var opt in c.Options ?? new List<PanelOption>())
                {
                    var rb = new RadioButton { Content = opt.Label, GroupName = gn, IsChecked = c.ValueString == opt.Value, Foreground = fg, IsEnabled = c.Disabled != true };
                    var val = opt.Value;
                    rb.Checked += (_, _) => Fire("change", val);
                    panel.Children.Add(rb);
                }
                return Labeled(c.Label, panel, fg);
            }

            case "color":
            {
                var tb = new TextBox { Text = string.IsNullOrEmpty(c.ValueString) ? "#000000" : c.ValueString, IsEnabled = c.Disabled != true };
                tb.TextChanged += (_, _) => Fire("change", tb.Text);
                return Labeled(c.Label, tb, fg);
            }

            case "timer":
            {
                var ms = c.Timer?.CurrentMs ?? 0;
                var name = c.Timer?.DisplayName ?? c.Label ?? c.Id;
                var row = new DockPanel { LastChildFill = false };
                var nameBlock = new TextBlock { Text = name, FontSize = 13, FontWeight = FontWeights.Medium, Foreground = fg };
                var timeBlock = new TextBlock { Text = FormatTimerMs(ms, c.Format ?? "mm:ss"), FontSize = 13, FontFamily = new FontFamily("Consolas"), Foreground = ms <= 0 ? Brushes.OrangeRed : fg };
                DockPanel.SetDock(nameBlock, Dock.Left);
                DockPanel.SetDock(timeBlock, Dock.Right);
                row.Children.Add(nameBlock);
                row.Children.Add(timeBlock);
                return row;
            }

            case "section":
            {
                var panel = new StackPanel { Margin = new Thickness(4, 0, 0, 0) };
                if (!string.IsNullOrEmpty(c.Text))
                {
                    panel.Children.Add(new TextBlock { Text = c.Text, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = fg, Opacity = 0.7, Margin = new Thickness(0, 0, 0, 4) });
                }
                foreach (var child in c.Controls ?? new List<PanelControl>())
                {
                    var el = BuildControl(child, panelId, groupId, valuesJson, fg, accent);
                    if (el != null) { el.Margin = new Thickness(0, 2, 0, 2); panel.Children.Add(el); }
                }
                return panel;
            }

            case "pin":
            {
                var length = Math.Max(3, Math.Min(12, c.Length ?? 6));
                var masked = c.Masked ?? true;
                var autoSubmit = c.AutoSubmit ?? false;
                FrameworkElement field;
                if (masked)
                {
                    var pb = new PasswordBox { MaxLength = length, FontFamily = new FontFamily("Consolas"), FontSize = 18, IsEnabled = c.Disabled != true, Width = length * 26 };
                    pb.PasswordChanged += (_, _) =>
                    {
                        var digits = new string(pb.Password.Where(char.IsDigit).ToArray());
                        Fire("change", digits);
                        if (autoSubmit && digits.Length == length) Fire("submit", digits);
                    };
                    field = pb;
                }
                else
                {
                    var tb = new TextBox { MaxLength = length, FontFamily = new FontFamily("Consolas"), FontSize = 18, Text = c.ValueString, IsEnabled = c.Disabled != true, Width = length * 26 };
                    tb.TextChanged += (_, _) =>
                    {
                        var digits = new string(tb.Text.Where(char.IsDigit).ToArray());
                        if (digits != tb.Text) { tb.Text = digits; return; }
                        Fire("change", digits);
                        if (autoSubmit && digits.Length == length) Fire("submit", digits);
                    };
                    field = tb;
                }
                return Labeled(c.Label, field, fg);
            }

            default:
                return new TextBlock { Text = c.Label ?? c.Text ?? "", FontSize = 13, Foreground = fg };
        }
    }

    private static FrameworkElement Labeled(string? label, FrameworkElement control, Brush fg)
    {
        if (string.IsNullOrEmpty(label))
        {
            return control;
        }
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = label, FontSize = 11, FontWeight = FontWeights.Medium, Foreground = fg, Opacity = 0.7, Margin = new Thickness(0, 0, 0, 2) });
        stack.Children.Add(control);
        return stack;
    }

    private static string CollectValuesJson(PanelSnapshot snapshot)
    {
        var values = new Dictionary<string, string>();
        void Visit(List<PanelControl>? controls)
        {
            if (controls == null) return;
            foreach (var c in controls)
            {
                switch (c.Type)
                {
                    case "section": Visit(c.Controls); break;
                    case "button":
                    case "text":
                    case "timer": break;
                    default:
                        if (c.Value.HasValue) values[c.Id] = c.ValueString;
                        break;
                }
            }
        }
        Visit(snapshot.Controls);
        return JsonSerializer.Serialize(values);
    }

    private static double PanelWidth(string? width) => width switch
    {
        "small" => 220,
        "medium" => 280,
        "large" => 360,
        _ when !string.IsNullOrEmpty(width) && double.TryParse(width!.Replace("px", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out var n) => Math.Max(180, Math.Min(520, n)),
        _ => 300
    };

    private static string FormatTimerMs(double ms, string format)
    {
        var totalMs = Math.Max(0, (int)ms);
        var totalSec = totalMs / 1000;
        switch (format)
        {
            case "ms": return totalMs.ToString();
            case "ss": return totalSec.ToString();
            case "hh:mm:ss":
                return $"{totalSec / 3600}:{(totalSec % 3600) / 60:D2}:{totalSec % 60:D2}";
            default:
                return $"{totalSec / 60}:{totalSec % 60:D2}";
        }
    }

    // Accepts #rrggbb or #aarrggbb; falls back to the given default on failure.
    private static SolidColorBrush Brush(string? hex, string fallback)
    {
        var value = string.IsNullOrWhiteSpace(hex) ? fallback : hex!;
        try
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
            brush.Freeze();
            return brush;
        }
        catch
        {
            var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fallback));
            b.Freeze();
            return b;
        }
    }
}
