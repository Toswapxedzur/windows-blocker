using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using WindowsBlocker.Bridge;

namespace WindowsBlocker;

public sealed class McpConnectionsWindow : Window
{
    private readonly StackPanel _rows=new();
    private readonly TextBlock _status=new() { TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0) };
    public McpConnectionsWindow(Window owner)
    {
        Owner=owner; Title="AI connections"; Width=480; Height=570; MinWidth=400; MinHeight=320; WindowStartupLocation=WindowStartupLocation.CenterOwner;
        AutomationProperties.SetAutomationId(this,"McpConnectionsWindow");
        var body=new DockPanel {Margin=new Thickness(20)};
        var intro=new StackPanel(); intro.Children.Add(new TextBlock { Text="AI connections",FontSize=20,FontWeight=FontWeights.SemiBold });
        intro.Children.Add(new TextBlock { Text="AI apps use the same controls and gates as the Vault editor. Restart the AI app after changing its connection.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,12) });
        DockPanel.SetDock(intro,Dock.Top);body.Children.Add(intro);
        var footer=new StackPanel();footer.Children.Add(_status);
        var refresh=new Button {Content="Refresh",HorizontalAlignment=HorizontalAlignment.Right,Padding=new Thickness(12,5,12,5),Margin=new Thickness(0,12,0,0)};
        refresh.Click+=(_,_)=>Reload();footer.Children.Add(refresh);DockPanel.SetDock(footer,Dock.Bottom);body.Children.Add(footer);
        body.Children.Add(new ScrollViewer {Content=_rows,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});Content=body;Reload();
    }
    private void Reload()
    {
        var snapshot=McpConnectorRegistry.Snapshot();_rows.Children.Clear();
        foreach(var item in (snapshot["connectors"] as JsonArray ?? new()).OfType<JsonObject>())
        {
            var id=item["id"]!.GetValue<string>();var connected=item["connected"]?.GetValue<bool>()==true;var detected=item["detected"]?.GetValue<bool>()==true;
            var row=new DockPanel {Margin=new Thickness(0,0,0,12)};
            var button=new Button {Content=connected ? "Disconnect" : "Connect",IsEnabled=detected || connected,Padding=new Thickness(10,4,10,4),MinWidth=90};
            button.Click+=(_,_)=> { try { McpConnectorRegistry.Set(id,!connected);_status.Text="Restart the AI app to apply this change.";Reload(); } catch(Exception error) { _status.Text=error.Message; } };
            DockPanel.SetDock(button,Dock.Right);row.Children.Add(button);
            var label=new StackPanel();label.Children.Add(new TextBlock {Text=item["name"]?.GetValue<string>() ?? id,FontWeight=FontWeights.SemiBold});
            label.Children.Add(new TextBlock {Text=connected ? "Connected" : detected ? "Available" : "Not installed",Foreground=Brushes.Gray,FontSize=12});row.Children.Add(label);_rows.Children.Add(row);
        }
    }
}
