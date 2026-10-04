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
        var text=(ItemsControl)timer.FindName("Rows");
        string VisibleTimers()=>string.Join("\n",text.Items.Cast<object>().Select(row=>row.GetType().GetProperty("NameDisplay")!.GetValue(row)+" "+row.GetType().GetProperty("Duration")!.GetValue(row)));
        PumpUntil(()=>text.Items.Count>0,3000);
        var first=VisibleTimers();
        Check(first.StartsWith("Timer 0: 00:00:10") && first.Split('\n').Length<100,"10,000 timers render a bounded first page");
        PumpUntil(()=>VisibleTimers()!=first,6000);
        Check(text.Items.Count>0 && VisibleTimers()!=first,"Click-through timer advances automatically after five seconds");
        Check(!timer.IsHitTestVisible && !timer.Focusable && timer.ActualHeight<=SystemParameters.WorkArea.Height,"Rotating timer remains click-through and bounded");
        timer.UpdateRows(new[]{new TimerDisplayItem("long",new string('W',500),600)});
        PumpUntil(()=>text.Items.Count==1,1000);timer.UpdateLayout();
        IEnumerable<TextBlock> Labels(DependencyObject node) {
            if(node is TextBlock label)yield return label;
            for(var i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)
                foreach(var child in Labels(VisualTreeHelper.GetChild(node,i)))yield return child;
        }
        var duration=Labels(text).Single(t=>t.Text=="00:10:00");
        var name=Labels(text).Single(t=>t.Text.StartsWith("WWW"));
        var clockRect=duration.TransformToAncestor(timer).TransformBounds(new Rect(duration.RenderSize));
        Check(clockRect.Right<=timer.ActualWidth && duration.ActualWidth>50 && name.ActualWidth<1000,"Long timer names cannot hide the countdown column");
        timer.UpdateRows(Array.Empty<TimerDisplayItem>());
        PumpUntil(()=>!timer.IsVisible && text.Items.Count==0,1000);
        Check(!timer.IsVisible && text.Items.Count==0,"Empty timer roster hides and clears the overlay");timer.Close();
        var cardType=typeof(PanelOverlayWindow).Assembly.GetType("WindowsBlocker.PanelCard")!;
        var longLabel=string.Join(" ",Enumerable.Repeat("Long localized label",8));
        var fixture=new PanelSnapshot {Id="long",GroupId="fixture",Width="220",Title=longLabel,Controls=new List<PanelControl>{
            new(){Id="checkbox",Type="checkbox",Label=longLabel},
            new(){Id="radio",Type="radio",Options=new List<PanelOption>{new(){Value="a",Label=longLabel}}},
            new(){Id="button",Type="button",Label=longLabel},
            new(){Id="text",Type="textInput",Placeholder="Visible hint"},
            new(){Id="date",Type="date"}, new(){Id="time",Type="time"}}};
        var card=Activator.CreateInstance(cardType,new object[]{fixture,(PanelEventHandler)((_,_,_,_,_,_)=>{})})!;
        var cardRoot=(Border)cardType.GetProperty("Root")!.GetValue(card)!;
        window.Content=cardRoot;window.UpdateLayout();
        Check(Math.Abs(((SolidColorBrush)cardRoot.Background).Color.A/255.0-0.96)<0.002,"Rule panels use .96 opacity independently of timers");
        foreach(var label in Labels(cardRoot).Where(t=>t.Text==longLabel)) {
            var rect=label.TransformToAncestor(cardRoot).TransformBounds(new Rect(label.RenderSize));
            Check(rect.Right<=cardRoot.ActualWidth && label.ActualHeight>18,"Long panel labels wrap within narrow cards");
        }
        foreach(var hint in new[]{"Visible hint","YYYY-MM-DD","HH:MM"})
            Check(Labels(cardRoot).Any(t=>t.Text==hint && t.IsVisible && !t.IsHitTestVisible),"Empty input visibly displays noninteractive "+hint);
        var pinType=typeof(PanelOverlayWindow).Assembly.GetType("WindowsBlocker.NativePinField")!;
        var pin=(Grid)Activator.CreateInstance(pinType,new object[]{6,false,"12",(Action<string>)(v=>changes.Add(v))})!;
        var editor=pin.Children.OfType<TextBox>().Single();editor.Text="12x3456";
        Check(editor.Text=="123456" && pin.FlowDirection==FlowDirection.LeftToRight,"Drawn PIN accepts normalized digits in stable LTR order");
        window.Close();app.Shutdown();Console.WriteLine($"PASS {_checks} native overlay contracts");
    }
}
