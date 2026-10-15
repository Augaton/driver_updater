using System.Windows;
using System.Windows.Threading;

namespace DriverUpdater;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ApplyTheme();
        if (e.Args.Contains(WeeklyCheck.Argument))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = WeeklyCheck.RunAsync(this);
        }
        else
        {
            new MainWindow().Show();
        }
    }

    /// <summary>
    /// Windows 11 look, light or dark like Windows. Set here rather than in App.xaml so it
    /// merges into the resources App.xaml has already loaded instead of racing them.
    /// </summary>
    public void ApplyTheme() => ThemeMode = ThemeMode.System;

    void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(e.Exception.Message, "Driver Updater — erreur", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
