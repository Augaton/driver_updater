using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace DriverUpdater.UI;

/// <summary>
/// "Garder cet affichage ?" with a countdown, as Windows asks after a display change: with no
/// answer (the screen stayed black), the answer is no.
/// </summary>
public sealed class KeepDisplayDialog : Window
{
    readonly DispatcherTimer countdown = new() { Interval = TimeSpan.FromSeconds(1) };
    int secondsLeft = 15;
    public bool Keep { get; private set; }

    public KeepDisplayDialog(Window owner, string change)
    {
        Owner = owner;
        Title = "Driver Updater";
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (System.Windows.Media.Brush)FindResource("Panel");
        Foreground = (System.Windows.Media.Brush)FindResource("Ink");

        var message = new TextBlock { Text = L.F("{0}. L'image s'affiche-t-elle bien ?", change), TextWrapping = TextWrapping.Wrap, MaxWidth = 420, FontSize = 14 };
        var timer = new TextBlock { Foreground = (System.Windows.Media.Brush)FindResource("InkMuted"), Margin = new Thickness(0, 6, 0, 16) };
        var keep = new Button { Content = L.T("Garder"), IsDefault = true, Margin = new Thickness(0, 0, 8, 0), MinWidth = 100 };
        var revert = new Button { Content = L.T("Revenir"), IsCancel = true, MinWidth = 100 };
        keep.SetResourceReference(StyleProperty, "AccentButtonStyle");
        keep.Click += (_, _) => Close(true);
        revert.Click += (_, _) => Close(false);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(keep);
        buttons.Children.Add(revert);
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(message);
        panel.Children.Add(timer);
        panel.Children.Add(buttons);
        Content = panel;

        void Show() => timer.Text = L.F("Retour à l'affichage précédent dans {0} s.", secondsLeft);
        Show();
        countdown.Tick += (_, _) =>
        {
            if (--secondsLeft <= 0) Close(false);
            else Show();
        };
        Loaded += (_, _) => countdown.Start();
        Closed += (_, _) => countdown.Stop();
    }

    void Close(bool keep)
    {
        Keep = keep;
        countdown.Stop();
        Close();
    }
}
