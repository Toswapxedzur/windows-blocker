using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace QuitFixture;
public static class Program
{
    [STAThread] public static void Main(string[] args)
    {
        var report=args[0]; var application=new Application(); var window=new Window { Title="Vault test editor with unsaved work",Width=420,Height=200,Content=new TextBlock { Text="Unsaved fixture document",Margin=new Thickness(20) } };
        var count=0;
        Window? prompt=null;
        window.Closing += (_, e) =>
        {
            e.Cancel=true; count++; File.WriteAllText(report,count.ToString());
            if(prompt==null){ prompt=new Window { Owner=window,Title="Save changes?",Width=300,Height=150,Content=new TextBlock {Text="Save this fixture before closing?",Margin=new Thickness(20)} }; prompt.Closing += (_, evt) => evt.Cancel=true; prompt.Show(); }
        };
        application.Run(window);
    }
}
