using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WindowsBlocker.Core;
using WindowsBlocker;
using WindowsBlocker.Rules;

internal static class Program
{
    private static int _checks;
    private static void Check(bool value,string message) {if(!value)throw new Exception(message);Console.WriteLine("PASS "+message);_checks++;}
    [STAThread] private static void Main() {
        var app=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};
        var format=typeof(TimerOverlayWindow).GetMethod("Format",BindingFlags.Static|BindingFlags.NonPublic)!;
        Check((string)format.Invoke(null,new object[]{1.01})! == "00:00:02", "Remaining duration rounds upward with HH:MM:SS");
        Check((string)format.Invoke(null,new object[]{3600.0})! == "01:00:00", "Hour durations retain padded hours");
        Check((string)format.Invoke(null,new object[]{-3.0})! == "00:00:00", "Negative duration is bounded");
        var old=JsonSerializer.Deserialize<PanelSnapshot>("{\"id\":\"old\",\"theme\":{\"background\":\"red\"},\"controls\":[]}")!;
        Check(old.Id=="old" && !JsonSerializer.Serialize(old).Contains("theme"), "Retired stored theme is ignored without recreating it");
        var window=new PanelOverlayWindow();
        var list=(ItemsControl)window.FindName("Cards");
        Check(window.FontFamily.Source=="Arial" && window.MaxHeight<=SystemParameters.WorkArea.Height,"Panel uses shared font and work-area bound");
        var roster=Enumerable.Range(0,10000).Select(i=>new PanelSnapshot { Id=i.ToString(),GroupId="fixture",Title="Card "+i,Controls=new List<PanelControl>{new(){Id="button",Type="button",Label="Click"}} }).ToList();
        window.SetCards(roster);window.Show();window.UpdateLayout();
        Check(list.Items.Count==10000,"All panel definitions remain stored");
        int Realized(DependencyObject root) {var n=root is NativePanelCardHost?1:0;for(var i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)n+=Realized(VisualTreeHelper.GetChild(root,i));return n;}
        Check(Realized(list)>0 && Realized(list)<80,"Only viewport cards are realized for 10,000 panels");
        Check(window.ActualHeight<=SystemParameters.WorkArea.Height && window.Top>=SystemParameters.WorkArea.Top,"Many-card panel remains on-screen");
        var selectType=typeof(PanelOverlayWindow).Assembly.GetType("WindowsBlocker.NativePanelSelect")!;
        var changes=new List<string>();
        var choices=Enumerable.Range(0,10000).Select(i=>new PanelOption {Value=i.ToString(),Label="Option "+i}).ToList();
        var chooser=(FrameworkElement)Activator.CreateInstance(selectType,new object[]{choices,"42",(Action<string>)(v=>changes.Add(v))})!;
        var small=(FrameworkElement)Activator.CreateInstance(selectType,new object[]{choices.Take(3).ToList(),"1",(Action<string>)(v=>changes.Add(v))})!;
        var searchField=selectType.GetField("_search",BindingFlags.NonPublic|BindingFlags.Instance)!;
        Check(((TextBox)searchField.GetValue(chooser)!).Visibility==Visibility.Visible && ((TextBox)searchField.GetValue(small)!).Visibility==Visibility.Collapsed,"Only long option lists expose search");
        var button=(Button)selectType.GetField("_button",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(chooser)!;
        var options=(ListBox)selectType.GetField("_list",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(chooser)!;
        window.SetCards(new());window.Content=chooser;window.UpdateLayout();button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        ((TextBox)searchField.GetValue(chooser)!).Text="Option 9999";
        Check(options.Items.Count==1 && changes.Count==0,"Search filters without committing a new value");
        options.SelectedIndex=0;
        Check(changes.SequenceEqual(new[]{"9999"}),"Choosing a search result commits the exact option value once");
        var timer=new TimerOverlayWindow();
        timer.UpdateRows(Enumerable.Range(0,10000).Select(i=>new TimerDisplayItem(i.ToString(),"Timer "+i,10)).ToArray());
        void PumpUntil(Func<bool> done,int milliseconds) {
            var frame=new DispatcherFrame();var timeout=DateTime.UtcNow.AddMilliseconds(milliseconds);
            var poll=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(20)};
            poll.Tick+=(_,_)=>{if(done()||DateTime.UtcNow>=timeout){poll.Stop();frame.Continue=false;}};
            poll.Start();Dispatcher.PushFrame(frame);
        }
        var text=(TextBlock)timer.FindName("Rows");PumpUntil(()=>text.Text.Length>0,3000);
        var first=text.Text;
        Check(first.StartsWith("Timer 0: 00:00:10") && first.Split('\n').Length<100,"10,000 timers render a bounded first page");
        PumpUntil(()=>text.Text!=first,6000);
        Check(text.Text.Length>0 && text.Text!=first,"Click-through timer advances automatically after five seconds");
        Check(!timer.IsHitTestVisible && !timer.Focusable && timer.ActualHeight<=SystemParameters.WorkArea.Height,"Rotating timer remains click-through and bounded");
        timer.UpdateRows(Array.Empty<TimerDisplayItem>());
        PumpUntil(()=>!timer.IsVisible && text.Text=="",1000);
        Check(!timer.IsVisible && text.Text=="","Empty timer roster hides and clears the overlay");timer.Close();
        var pinType=typeof(PanelOverlayWindow).Assembly.GetType("WindowsBlocker.NativePinField")!;
        var pin=(Grid)Activator.CreateInstance(pinType,new object[]{6,false,"12",(Action<string>)(v=>changes.Add(v))})!;
        var editor=pin.Children.OfType<TextBox>().Single();editor.Text="12x3456";
        Check(editor.Text=="123456" && pin.FlowDirection==FlowDirection.LeftToRight,"Drawn PIN accepts normalized digits in stable LTR order");
        window.Close();app.Shutdown();Console.WriteLine($"PASS {_checks} native overlay contracts");
    }
}
