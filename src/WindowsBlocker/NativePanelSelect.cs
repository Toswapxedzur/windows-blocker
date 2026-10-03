using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using WindowsBlocker.Rules;
using WindowsBlocker.WebUI;

namespace WindowsBlocker;

// One bounded, virtualized chooser. Search is shown only for six or more
// options; filtering never changes the rule's selected value or roster.
internal sealed class NativePanelSelect : StackPanel
{
    private readonly IReadOnlyList<PanelOption> _options;
    private readonly Button _button;
    private readonly ListBox _list;
    private readonly Popup _popup;
    private readonly TextBox _search;
    private readonly Action<string> _changed;
    private string _value;
    private bool _selecting;
    public bool IsOpen=>_popup.IsOpen;
    public event Action? EditingEnded;
    public NativePanelSelect(IReadOnlyList<PanelOption> options, string value, Action<string> changed)
    {
        _options=options; _value=value; _changed=changed;
        _button=new Button { Content=Label(value)+"  ▾", HorizontalContentAlignment=HorizontalAlignment.Left };
        _list=new ListBox { MaxHeight=240, MinHeight=32, DisplayMemberPath=nameof(PanelOption.Label) };
        _search=new TextBox { Margin=new Thickness(0,0,0,6), Visibility=options.Count>=6 ? Visibility.Visible : Visibility.Collapsed };
        var hint=NativeLanguage.Text("contentPage.searchOptions", "Search options");
        _search.ToolTip=hint;
        System.Windows.Automation.AutomationProperties.SetName(_search,hint);
        var contents=new StackPanel(); contents.Children.Add(_search); contents.Children.Add(_list);
        var surface=new Border { Padding=new Thickness(8), CornerRadius=new CornerRadius(12), Child=contents };
        surface.SetResourceReference(Border.BackgroundProperty,"VaultField");
        _popup=new Popup { PlacementTarget=_button, Placement=PlacementMode.Bottom, StaysOpen=false, AllowsTransparency=true, Child=surface };
        _button.Click+=(_,_)=> {
            _selecting=true; _search.Text=""; _list.ItemsSource=_options;
            _list.SelectedItem=_options.FirstOrDefault(o=>o.Value==_value); _selecting=false;
            surface.Width=Math.Min(480,Math.Max(180,_button.ActualWidth));
            _popup.IsOpen=true;
            if(_search.Visibility==Visibility.Visible) _search.Focus(); else _list.Focus();
        };
        _search.TextChanged+=(_,_)=> {
            if(_selecting) return;
            _selecting=true;
            var query=_search.Text.Trim();
            _list.ItemsSource=string.IsNullOrEmpty(query) ? _options : _options.Where(o=>o.Label.Contains(query,StringComparison.CurrentCultureIgnoreCase) || o.Value.Contains(query,StringComparison.CurrentCultureIgnoreCase)).ToList();
            _list.SelectedItem=(_list.ItemsSource as System.Collections.IEnumerable)?.Cast<PanelOption>().FirstOrDefault(o=>o.Value==_value);
            _selecting=false;
        };
        _list.SelectionChanged+=(_,_)=> {
            if(_selecting || _list.SelectedItem is not PanelOption option) return;
            _value=option.Value; _button.Content=Label(_value)+"  ▾"; _popup.IsOpen=false; _changed(_value);
        };
        _popup.Closed+=(_,_)=>EditingEnded?.Invoke();
        Children.Add(_button); Children.Add(_popup);
        Unloaded+=(_,_)=>_popup.IsOpen=false;
    }
    private string Label(string value)=>_options.FirstOrDefault(o=>o.Value==value)?.Label ?? value;
}
