using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WindowsBlocker.Bridge;
using WindowsBlocker.Enforcement;

namespace WindowsBlocker;

// The shared Settings switch/selected card drives the native floating +.
// NOACTIVATE preserves the frontmost app while the user presses the button.
public sealed class QuickAddWindow : Window
{
    private readonly Button _button;
    private readonly Func<string,AppIdentity,Task> _add;
    private string _groupId="",_groupName="";
    private int _revision;
    public QuickAddWindow(Func<string,AppIdentity,Task> add)
    {
        _add=add; Width=Height=18; WindowStyle=WindowStyle.None; ResizeMode=ResizeMode.NoResize; AllowsTransparency=true; Background=Brushes.Transparent; Topmost=true; ShowInTaskbar=false; ShowActivated=false;
        AutomationProperties.SetAutomationId(this,"VaultQuickAdd");
        _button=new Button { Content="+",FontFamily=new FontFamily("Arial"),FontSize=14,FontWeight=FontWeights.Bold,Foreground=new SolidColorBrush(Color.FromRgb(30,58,138)),Background=Brushes.Transparent,BorderThickness=new Thickness(0),Padding=new Thickness(0),Focusable=false };
        Content=new Border { CornerRadius=new CornerRadius(9),Background=new SolidColorBrush(Color.FromRgb(238,242,255)),Child=_button };
        _button.Click += async (_,_) =>
        {
            var app=ProcessIdentity.ForWindow(NativeMethods.GetForegroundWindow()); bool ok=false;
            try { if(_groupId.Length>0 && NativeAppSafety.CanControl(app)){await _add(_groupId,app);ok=true;} } catch { }
            var revision=++_revision; _button.Content=ok ? "✓" : "!"; Label(ok ? $"Added to {_groupName}" : $"Could not add the front app to {_groupName}");
            var timer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(1200)}; timer.Tick += (_,_)=>{timer.Stop();if(_revision==revision){_button.Content="+";Label($"Add the front app to {_groupName}");}};timer.Start();
        };
        SourceInitialized += (_,_) =>
        {
            var handle=new WindowInteropHelper(this).Handle;
            NativeMethods.SetWindowLong(handle,NativeMethods.GWL_EXSTYLE,NativeMethods.GetWindowLong(handle,NativeMethods.GWL_EXSTYLE)|0x08000000);
            HwndSource.FromHwnd(handle).AddHook((IntPtr hwnd,int message,IntPtr wParam,IntPtr lParam,ref bool handled)=>{if(message==0x0021){handled=true;return new IntPtr(3);}return IntPtr.Zero;});
        };
    }
    public static string? Target(JsonObject document)
    {
        if(document["globalSettings"] is not JsonObject settings || settings["quickAddEnabled"]?.GetValueKind()!=System.Text.Json.JsonValueKind.True) return null;
        var id=document["quickAddGroupId"]?.GetValueKind()==System.Text.Json.JsonValueKind.String ? document["quickAddGroupId"]!.GetValue<string>() : null;
        var group=(document["blockedGroups"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(g=>g["id"]?.GetValue<string>()==id);
        return group!=null && group["groupType"]?.GetValue<string>()!="custom" && !ConnectionHub.IsLocked(group) ? id : null;
    }
    public void Reload(JsonObject document)
    {
        var id=Target(document);
        if(id==null){_groupId="";_revision++;Hide();return;}
        if(_groupId!=id){_button.Content="+";_revision++;}
        _groupId=id;_groupName=(document["blockedGroups"] as JsonArray)?.OfType<JsonObject>().First(g=>g["id"]?.GetValue<string>()==id)["name"]?.GetValue<string>()??id;
        Label($"Add the front app to {_groupName}");var area=SystemParameters.WorkArea;Left=area.Right-Width-8;Top=area.Bottom-Height-8;if(!IsVisible)Show();
    }
    private void Label(string label){_button.ToolTip=label;AutomationProperties.SetName(_button,label);}
}
