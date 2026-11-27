using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

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
                FollowWindowsTheme();
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
    /// The palette, each color with its dark and light value: Windows 11's neutral grays, the overlays of a hovered
    /// or chosen item, and the state colors (on the light side a step deeper than Windows' own, so they keep 4.5:1
    /// on a chosen table row too). Accent and Chart come from the accent chosen in Windows (<see cref="ApplyTheme"/>).
    /// </summary>
    static readonly (string Key, string? Dark, string? Light)[] Palette =
    [
        ("Void", "#202020", "#F3F3F3"), // window and pages
        ("Chrome", "#1B1B1B", "#EBEBEB"), // menu and status bar
        ("Panel", "#2B2B2B", "#FBFBFB"), // cards
        ("Edge", "#383838", "#E0E0E0"), // borders and dividers
        ("Ink", "#F3F3F3", "#1B1B1B"),
        ("InkMuted", "#ADADAD", "#5C5C5C"),
        ("SubtleHover", "#0AFFFFFF", "#09000000"),
        ("SubtleSelected", "#14FFFFFF", "#0F000000"),
        ("SubtlePressed", "#06FFFFFF", "#05000000"),
        ("Success", "#6CCB5F", "#0E6E0E"),
        ("Caution", "#FCE100", "#8A5100"),
        ("Critical", "#FF99A4", "#B42719"),
    ];

    /// <summary>
    /// The Fluent theme's own keys, matched to the palette: its cards, and a chosen table row in a neutral gray rather
    /// than the accent fill, on which the states drawn in their own color could not be read. On light, its disabled
    /// controls too: Windows fades them below 3:1, where they can no longer be read. The theme copies a base key into
    /// each control's own when it loads, so those are named one by one. Null keeps the theme's own.
    /// </summary>
    static readonly (string Key, string? Dark, string? Light)[] FluentKeys =
    [
        ("CardBackgroundFillColorDefaultBrush", "#2B2B2B", "#FBFBFB"),
        ("CardStrokeColorDefaultBrush", "#383838", "#E0E0E0"),
        ("TextFillColorSecondaryBrush", "#ADADAD", "#5C5C5C"),
        ("DataGridRowSelectedBackgroundThemeBrush", "#3D3D3D", "#E5E5E5"),
        ("DataGridRowSelectedForegroundThemeBrush", "#F3F3F3", "#1B1B1B"),
        ("TextFillColorDisabledBrush", null, "#767676"),
        ("ButtonForegroundDisabled", null, "#767676"),
        ("CheckBoxForegroundUncheckedDisabled", null, "#767676"),
        ("AccentButtonBackgroundDisabled", null, "#E0E0E0"),
        ("AccentButtonForegroundDisabled", null, "#6E6E6E"),
    ];

    (bool HighContrast, bool Light, Color Accent)? appliedTheme;

    /// <summary>
    /// The Windows 11 controls and the palette in the theme Windows gives apps, light or dark, with the accent chosen
    /// in Windows. Set here rather than in App.xaml so it merges into the resources App.xaml has already loaded
    /// instead of racing them; the palette goes in the application's own resources, where its keys win over the
    /// Fluent theme's dictionary. With high contrast on, Windows' own colors instead: the user chose them to be able
    /// to read at all.
    /// </summary>
    /// <param name="light">Light or dark whatever Windows says (the window test); null follows Windows.</param>
    public void ApplyTheme(bool? light = null)
    {
        (bool HighContrast, bool Light, Color Accent) theme = (SystemParameters.HighContrast, light ?? WindowsUsesLightTheme(), SystemColors.AccentColor);
        if (theme == appliedTheme) return;
        appliedTheme = theme;
        if (theme.HighContrast)
        {
            ThemeMode = ThemeMode.System; // the Fluent controls in their high contrast variant
            foreach (var key in new[] { "Void", "Chrome", "Panel" }) Resources[key] = SystemColors.WindowBrush;
            foreach (var key in new[] { "Ink", "InkMuted", "Edge" }) Resources[key] = SystemColors.WindowTextBrush;
            foreach (var key in new[] { "Accent", "Chart", "Success", "Caution", "Critical" }) Resources[key] = SystemColors.HotTrackBrush;
            foreach (var key in new[] { "SubtleHover", "SubtleSelected", "SubtlePressed" }) Resources[key] = Brushes.Transparent;
            foreach (var (key, _, _) in FluentKeys) Resources.Remove(key); // the theme's own high contrast colors
            return;
        }
        ThemeMode = theme.Light ? ThemeMode.Light : ThemeMode.Dark;
        foreach (var (key, dark, lightValue) in Palette.Concat(FluentKeys))
            if ((theme.Light ? lightValue : dark) is { } hex) Resources[key] = Frozen(hex);
            else Resources.Remove(key);
        // The variants of the accent Windows 11 itself draws on that surface: its lighter ones on dark, its darker
        // ones on light. The first is the theme's accent button, so the menu mark and the states match the buttons.
        Resources["Accent"] = Frozen(theme.Light ? SystemColors.AccentColorDark1 : SystemColors.AccentColorLight2);
        Resources["Chart"] = Frozen(theme.Light ? SystemColors.AccentColorDark1 : SystemColors.AccentColorLight1);
    }

    /// <summary>The app mode chosen in Windows (Personalization, Colors); light when unset, as for Windows itself.</summary>
    static bool WindowsUsesLightTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
    }

    /// <summary>Raised once a change of theme in Windows is applied, for what draws itself (the gauges).</summary>
    public static event Action? ThemeChanged;

    /// <summary>
    /// A change of theme, accent or high contrast in Windows repaints the open window, which reads the palette as
    /// DynamicResource. Windows announces it as a change of "general" settings (ImmersiveColorSet), of colors, or of
    /// accessibility; <see cref="ApplyTheme"/> skips the many that change nothing here.
    /// </summary>
    void FollowWindowsTheme() =>
        SystemEvents.UserPreferenceChanged += (_, args) =>
        {
            if (args.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.Accessibility)
                Dispatcher.BeginInvoke(() =>
                {
                    var before = appliedTheme;
                    ApplyTheme();
                    if (appliedTheme != before) ThemeChanged?.Invoke();
                });
        };

    static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    static SolidColorBrush Frozen(string hex) => Frozen((Color)ColorConverter.ConvertFromString(hex));

    void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Diagnostics.Remember(e.Exception);
        MessageBox.Show(e.Exception.Message + L.T("\n\n« Copier le diagnostic » en joint le détail."), L.T("Driver Updater — erreur"),
            MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
