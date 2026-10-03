using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace WindowsBlocker;

// Drawn digit boxes share one native editor, preserving normal paste, delete
// and caret behavior. Digits always read left-to-right in RTL interfaces.
internal sealed class NativePinField : Grid
{
    public NativePinField(int length, bool masked, string initial, Action<string> changed)
    {
        FlowDirection=FlowDirection.LeftToRight;
        var digits=new TextBlock[length];
        var boxes=new UniformGrid {Columns=length};
        for(var i=0;i<length;i++) {
            digits[i]=new TextBlock {FontFamily=new FontFamily("Arial"),FontSize=18,Foreground=Brushes.White,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
            var box=new Border {CornerRadius=new CornerRadius(8),Margin=new Thickness(2),Padding=new Thickness(2,8,2,8),Child=digits[i]};
            box.SetResourceReference(Border.BackgroundProperty,"VaultField"); boxes.Children.Add(box);
        }
        Children.Add(boxes);
        void Render(string value) {for(var i=0;i<length;i++)digits[i].Text=i<value.Length ? masked ? "●" : value[i].ToString() : "";}
        string Normalize(string value)=>new string(value.Where(c=>c is >= '0' and <= '9').Take(length).ToArray());
        if(masked) {
            var editor=new PasswordBox {MaxLength=length,Opacity=0,Password=Normalize(initial)};
            Render(editor.Password);
            editor.PasswordChanged+=(_,_)=> {var value=Normalize(editor.Password);if(value!=editor.Password){editor.Password=value;return;} Render(value);changed(value);};
            Children.Add(editor);
        } else {
            var editor=new TextBox {MaxLength=length,Opacity=0,Text=Normalize(initial)};
            Render(editor.Text);
            editor.TextChanged+=(_,_)=> {var value=Normalize(editor.Text);if(value!=editor.Text){editor.Text=value;editor.CaretIndex=value.Length;return;}Render(value);changed(value);};
            Children.Add(editor);
        }
        MaxWidth=length*38; HorizontalAlignment=HorizontalAlignment.Left;
    }
}
