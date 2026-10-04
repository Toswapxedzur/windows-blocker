using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using WindowsBlocker.Enforcement;
using WindowsBlocker.Rules;
using WindowsBlocker.WebUI;

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
                panel.GroupId = groupId; // The controller map owns routing identity.
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
    private readonly ObservableCollection<NativePanelItem> _panels=new();
    private readonly Dictionary<(string?,string),NativePanelItem> _items=new();

    public PanelOverlayWindow()
    {
        InitializeComponent();
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source=new Uri("/WindowsBlocker;component/NativeControls.xaml",UriKind.Relative) });
        Resources["VaultText"]=new SolidColorBrush(Color.FromRgb(248,250,252));
        Resources["VaultField"]=new SolidColorBrush(Color.FromRgb(51,65,85));
        FontFamily=new FontFamily("Arial");
        FontSize=13;
        Cards.ItemsSource=_panels;
        MaxWidth=Math.Max(1,SystemParameters.WorkArea.Width-2*Inset);
        MaxHeight=Math.Max(1,SystemParameters.WorkArea.Height-2*Inset);
        Cards.MaxHeight=MaxHeight; Cards.MaxWidth=MaxWidth;
        FlowDirection=NativeLanguage.Language=="ar" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        System.Windows.Automation.AutomationProperties.SetAutomationId(this, "VaultRulePanels");
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
        var keep=panels.Select(p=>(p.GroupId,p.Id)).ToHashSet();
        var desired=new List<NativePanelItem>(panels.Count);
        foreach(var panel in panels) {
            var id=(panel.GroupId,panel.Id);
            if(!_items.TryGetValue(id,out var item)) {
                item=new NativePanelItem(panel,Emit);_items[id]=item;
            } else item.Update(panel);
            desired.Add(item);
        }
        foreach(var id in _items.Keys.Where(id=>!keep.Contains(id)).ToList())_items.Remove(id);
        var differences=Math.Abs(_panels.Count-desired.Count);
        for(var i=0;i<Math.Min(_panels.Count,desired.Count);i++)if(_panels[i]!=desired[i])differences++;
        // Routine ticks keep the same item/container and its focused controls.
        // Large topology changes rebuild the source in linear time; only
        // viewport cards are realized. Small changes preserve neighboring cards.
        if(differences>40) {
            _panels.Clear();foreach(var item in desired)_panels.Add(item);
        } else {
            foreach(var item in _panels.Where(item=>!keep.Contains((item.Snapshot.GroupId,item.Snapshot.Id))).ToList())_panels.Remove(item);
            for(var i=0;i<desired.Count;i++) {
                if(i<_panels.Count && _panels[i]==desired[i])continue;
                var index=_panels.IndexOf(desired[i]);
                if(index>=0)_panels.Move(index,i);else _panels.Insert(i,desired[i]);
            }
        }
    }

    internal void Emit(string groupId,string panelId,string controlId,string eventName,string value,string valuesJson)
        =>OnEvent?.Invoke(groupId,panelId,controlId,eventName,value,valuesJson);

    public void RepositionFor(string position)
    {
        _position = position;
        var area = SystemParameters.WorkArea;
        MaxWidth=Math.Max(1,area.Width-2*Inset);
        MaxHeight=Math.Max(1,area.Height-2*Inset);
        Cards.MaxHeight=MaxHeight; Cards.MaxWidth=MaxWidth;
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

internal sealed class NativePanelItem
{
    public PanelSnapshot Snapshot {get;private set;}
    public PanelEventHandler OnEvent {get;}
    public event Action? Changed;
    public NativePanelItem(PanelSnapshot snapshot,PanelEventHandler onEvent) {Snapshot=snapshot;OnEvent=onEvent;}
    public void Update(PanelSnapshot snapshot) {Snapshot=snapshot;Changed?.Invoke();}
}

// Viewport hosts subscribe only while mounted. Routine ticks update the card
// without recreating its controls, preserving drafts and click attribution.
public sealed class NativePanelCardHost : ContentControl
{
    private PanelCard? _card;
    private NativePanelItem? _item;
    private void Refresh() {
        if(_item==null)return;
        if(_card==null) {_card=new PanelCard(_item.Snapshot,_item.OnEvent);Content=_card.Root;}
        else _card.Update(_item.Snapshot);
    }
    public NativePanelCardHost() {
        DataContextChanged+=(_,_)=> {
            if(_item!=null)_item.Changed-=Refresh;
            _item=DataContext as NativePanelItem;_card=null;Content=null;
            if(_item!=null){_item.Changed+=Refresh;Refresh();}
        };
        Unloaded+=(_,_)=>{if(_item!=null)_item.Changed-=Refresh;};
        Loaded+=(_,_)=>{if(_item!=null){_item.Changed-=Refresh;_item.Changed+=Refresh;Refresh();}};
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
    private readonly List<NativePanelSelect> _selects=new();
    private bool IsEditing=>Root.IsKeyboardFocusWithin || _selects.Any(select=>select.IsOpen);
    private void ApplyPending() {
        if(_pending!=null && !IsEditing) {var snapshot=_pending;_pending=null;Rebuild(snapshot);}
    }

    public PanelCard(PanelSnapshot snapshot, PanelEventHandler onEvent)
    {
        _onEvent = onEvent;
        Root = new Border { Margin = new Thickness(0, 0, 0, 8) };
        Root.LostKeyboardFocus += (_, _) =>ApplyPending();
        Rebuild(snapshot);
    }

    public void Update(PanelSnapshot snapshot)
    {
        var json = JsonSerializer.Serialize(snapshot);
        if (json == _lastJson)
        {
            return;
        }
        if (IsEditing)
        {
            _pending = snapshot;
            return;
        }
        Rebuild(snapshot);
    }

    private void Rebuild(PanelSnapshot snapshot)
    {
        _selects.Clear();
        _lastJson = JsonSerializer.Serialize(snapshot);
        Root.Background = Brush(null, "#f50f172a");
        Root.BorderThickness = new Thickness(0);
        Root.CornerRadius = new CornerRadius(14);
        Root.Padding = new Thickness(12);
        Root.Width = Math.Min(PanelWidth(snapshot.Width), Math.Max(1,SystemParameters.WorkArea.Width-2*16));
        Root.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, ShadowDepth = 5, Opacity = 0.32, Color = Colors.Black };

        var fg = Brush(null, "#f8fafc");
        var stack = new StackPanel();

        if (!string.IsNullOrEmpty(snapshot.Title))
        {
            stack.Children.Add(new TextBlock { Text = snapshot.Title, TextWrapping = TextWrapping.Wrap, FontSize = 14, FontWeight = FontWeights.Bold, Foreground = fg, Margin = new Thickness(0, 0, 0, 4) });
        }
        if (!string.IsNullOrEmpty(snapshot.Description))
        {
            stack.Children.Add(new TextBlock { Text = snapshot.Description, FontSize = 13, Foreground = fg, Opacity = 0.82, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
        }

        var groupId = snapshot.GroupId ?? "";
        var valuesJson = CollectValuesJson(snapshot);
        var accent = Brush(null, "#1e3a8a");
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
            case "html":
                return new TextBlock { Text = c.Text ?? (c.Html != null ? System.Net.WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(c.Html,"<[^>]*>","")) : null) ?? c.Label ?? "", FontSize = 13, Foreground = fg, Opacity = 0.85, TextWrapping = TextWrapping.Wrap };

            case "button":
            {
                var b = new Button { Content = c.Label ?? NativeLanguage.Text("contentPage.button", "Button"), Padding = new Thickness(14, 6, 14, 6), IsEnabled = c.Disabled != true };
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
                var hint = c.Type == "date" ? "YYYY-MM-DD" : c.Type == "time" ? "HH:MM" : c.Placeholder ?? "";
                NativeInputHints.SetHint(tb, hint);
                if (!string.IsNullOrEmpty(hint)) tb.ToolTip = hint;
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
                var select = Select(c,value=>Fire("change",value));
                return Labeled(c.Label, select, fg);
            }

            case "radio":
            {
                if ((c.Options?.Count ?? 0)>=6) return Labeled(c.Label, Select(c,value=>Fire("change",value)),fg);
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

            case "section":
            {
                var panel = new StackPanel { Margin = new Thickness(4, 0, 0, 0) };
                if (!string.IsNullOrEmpty(c.Text))
                {
                    panel.Children.Add(new TextBlock { Text = c.Text, TextWrapping = TextWrapping.Wrap, FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = fg, Opacity = 0.7, Margin = new Thickness(0, 0, 0, 4) });
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
                var field=new NativePinField(length,masked,c.ValueString,value=> {
                    Fire("change",value);
                    if(autoSubmit && value.Length==length)Fire("submit",value);
                }) { IsEnabled=c.Disabled!=true };
                return Labeled(c.Label, field, fg);
            }

            default:
                return new TextBlock { Text = c.Label ?? c.Text ?? "", FontSize = 13, Foreground = fg };
        }
    }

    private NativePanelSelect Select(PanelControl control,Action<string> changed) {
        var select=new NativePanelSelect(control.Options ?? new List<PanelOption>(),control.ValueString,changed) {IsEnabled=control.Disabled!=true};
        select.EditingEnded+=ApplyPending; _selects.Add(select); return select;
    }

    private static FrameworkElement Labeled(string? label, FrameworkElement control, Brush fg)
    {
        if (string.IsNullOrEmpty(label))
        {
            return control;
        }
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, FontSize = 11, FontWeight = FontWeights.Medium, Foreground = fg, Opacity = 0.7, Margin = new Thickness(0, 0, 0, 2) });
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
                    case "html": break;
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
