using System.Windows;
using System.Windows.Threading;

namespace DriverUpdater;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        UseLanguage(L.Resolve(Settings.Language, System.Globalization.CultureInfo.CurrentUICulture));
        ApplyTheme();
        if (e.Args.Length > 0 && e.Args[0] == Elevation.Argument)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = AdminHelper.RunAsync(this, e.Args);
        }
        else if (e.Args.Contains(WeeklyCheck.Argument))
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
    /// The texts' language, chosen before anything is read or shown: the administrator helper
    /// and the weekly check speak it too.
    /// </summary>
    public static void UseLanguage(string language) => L.Use(language);

    /// <summary>
    /// Windows 11 controls in their dark variant, under the app's neon palette. Set here rather than in App.xaml so it
    /// merges into the resources App.xaml has already loaded instead of racing them.
    /// </summary>
    public void ApplyTheme() => ThemeMode = ThemeMode.Dark;

    void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(e.Exception.Message, "Driver Updater — erreur", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
