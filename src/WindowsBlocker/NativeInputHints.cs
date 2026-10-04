using System.Windows;

namespace WindowsBlocker;

// The watermark is drawn by the TextBox template and never receives input.
internal static class NativeInputHints
{
    public static readonly DependencyProperty HintProperty = DependencyProperty.RegisterAttached(
        "Hint", typeof(string), typeof(NativeInputHints), new FrameworkPropertyMetadata(""));
    public static string GetHint(DependencyObject target) => (string)target.GetValue(HintProperty);
    public static void SetHint(DependencyObject target, string value) => target.SetValue(HintProperty, value);
}
