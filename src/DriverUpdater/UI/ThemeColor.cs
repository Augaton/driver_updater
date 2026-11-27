using System.Windows;
using System.Windows.Documents;

namespace DriverUpdater.UI;

/// <summary>
/// A palette color chosen by data, by its key (a row's state: "Success", "Caution"…). The element keeps following
/// the key, so a change of theme in Windows repaints it; a brush looked up once would keep the old theme's color.
/// </summary>
public static class ThemeColor
{
    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.RegisterAttached(
        "Foreground", typeof(string), typeof(ThemeColor), new PropertyMetadata(null, OnForegroundChanged));

    public static string? GetForeground(DependencyObject target) => (string?)target.GetValue(ForegroundProperty);

    public static void SetForeground(DependencyObject target, string? value) => target.SetValue(ForegroundProperty, value);

    static void OnForegroundChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is FrameworkElement element && e.NewValue is string key)
            element.SetResourceReference(TextElement.ForegroundProperty, key);
    }
}
