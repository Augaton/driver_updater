using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace DriverUpdater;

public partial class App : Application
{
    // One window per user session: two would write the same journal at once, and could run
    // two administrator actions side by side. A second start brings the first to the front.
    const string WindowLockName = @"Local\DriverUpdater-Window", ShowName = @"Local\DriverUpdater-Show";
    static Mutex? windowLock;
    static EventWaitHandle? showSignal;
    static RegisteredWaitHandle? showWait;

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
            KeepCrashes();
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = WeeklyCheck.RunAsync(this);
        }
        else if (ClaimWindow())
        {
            KeepCrashes();
            try
            {
                new MainWindow().Show();
            }
            catch (Exception ex)
            {
                // Without a window, nothing would ever end the process.
                Diagnostics.SaveCrash(ex);
                MessageBox.Show(ex.Message, L.T("Driver Updater — erreur"), MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
        }
        else
        {
            Shutdown();
        }
    }

    /// <summary>
    /// A crash ends the process: what happened is kept for the next "Copier le diagnostic".
    /// Never in the administrator helper, which must not write into the user's folders.
    /// </summary>
    static void KeepCrashes() =>
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Diagnostics.SaveCrash(args.ExceptionObject);

    /// <summary>
    /// Whether this process may open the window. If another one already has it, that one is
    /// brought to the front instead, and this one should end.
    /// </summary>
    public bool ClaimWindow()
    {
        bool first;
        try
        {
            windowLock = new Mutex(initiallyOwned: true, WindowLockName, out first);
        }
        catch (UnauthorizedAccessException)
        {
            // Started "as administrator", the open one keeps its lock out of reach, and could not be brought forward either.
            MessageBox.Show(L.T("Driver Updater est déjà ouvert, lancé en administrateur : revenez à sa fenêtre, ou fermez-la d'abord."),
                "Driver Updater", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        if (!first)
        {
            windowLock.Dispose();
            windowLock = null;
            try
            {
                using var show = EventWaitHandle.OpenExisting(ShowName);
                // Started by the user, this process may give the foreground away; the other may not take it.
                AllowSetForegroundWindow(AnyProcess);
                show.Set();
            }
            catch (Exception ex) when (ex is WaitHandleCannotBeOpenedException or UnauthorizedAccessException)
            {
                // The other one is still starting: it shows on its own.
            }
            return false;
        }
        showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowName);
        showWait = ThreadPool.RegisterWaitForSingleObject(showSignal, (_, _) => Dispatcher.BeginInvoke(BringToFront), null, Timeout.Infinite, executeOnlyOnce: false);
        return true;
    }

    /// <summary>Lets a new copy open the window, for "Redémarrer maintenant": called on the thread that claimed it.</summary>
    public static void ReleaseWindow()
    {
        showWait?.Unregister(null);
        showSignal?.Dispose();
        windowLock?.ReleaseMutex();
        windowLock?.Dispose();
        (showWait, showSignal, windowLock) = (null, null, null);
    }

    void BringToFront()
    {
        if (MainWindow is not { } window) return;
        // As a click on its taskbar button: back to its size, maximized if it was.
        if (window.WindowState == WindowState.Minimized) SystemCommands.RestoreWindow(window);
        window.Activate();
    }

    const int AnyProcess = -1;

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool AllowSetForegroundWindow(int processId);

    /// <summary>
    /// The texts' language, chosen before anything is read or shown: the administrator helper
    /// and the weekly check speak it too.
    /// </summary>
    public static void UseLanguage(string language) => L.Use(language);

    /// <summary>
    /// Windows 11 controls in their dark variant, under the app's neon palette. Set here rather than in App.xaml so it
    /// merges into the resources App.xaml has already loaded instead of racing them. With high contrast on, Windows'
    /// own colors instead: the user chose them to be able to read at all.
    /// </summary>
    public void ApplyTheme()
    {
        if (!SystemParameters.HighContrast)
        {
            ThemeMode = ThemeMode.Dark;
            return;
        }
        ThemeMode = ThemeMode.System; // the Fluent controls in their high contrast variant
        // The palette, before any window reads it: each page's text, surfaces and states.
        foreach (var key in new[] { "Void", "Panel", "PanelRaised" }) Resources[key] = SystemColors.WindowBrush;
        foreach (var key in new[] { "Ink", "InkMuted", "Edge" }) Resources[key] = SystemColors.WindowTextBrush;
        foreach (var key in new[] { "NeonCyan", "NeonViolet", "NeonGreen", "NeonAmber", "NeonPink", "ChartCyan", "NeonGradient" })
            Resources[key] = SystemColors.HotTrackBrush;
        Resources["Glow"] = new System.Windows.Media.Effects.DropShadowEffect { Opacity = 0 };
        // The Fluent theme's own high contrast colors, rather than the neon repaint of them.
        foreach (var key in new[]
                 {
                     "AccentFillColorDefaultBrush", "AccentFillColorSecondaryBrush", "AccentFillColorTertiaryBrush",
                     "TextOnAccentFillColorPrimaryBrush", "CardBackgroundFillColorDefaultBrush", "CardStrokeColorDefaultBrush",
                     "TextFillColorSecondaryBrush", "DataGridRowSelectedBackgroundThemeBrush", "DataGridRowSelectedForegroundThemeBrush",
                 })
            Resources.Remove(key);
    }

    void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Diagnostics.Remember(e.Exception);
        MessageBox.Show(e.Exception.Message + L.T("\n\n« Copier le diagnostic » en joint le détail."), L.T("Driver Updater — erreur"),
            MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
