using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace DriverUpdater.UI;

/// <summary>
/// The main window. It runs without administrator rights. One page per feature, reached from
/// the menu tree (MainWindow.Nav.cs); each area's actions live in their own partial file
/// (MainWindow.Home.cs, .Drivers.cs, .Catalog.cs, .Inventory.cs, .Games.cs, .Tuning.cs,
/// .Software.cs, .Startup.cs, .Performance.cs, .Health.cs).
/// </summary>
public partial class MainWindow : Window
{
    readonly List<string> log = [];
    bool busy;
    int adminRuns; // administrator helpers still running
    AppRelease? newerRelease;

    public MainWindow()
    {
        InitializeComponent();
        RestorePlacement();
        Translator.Translate(this); // before the menu copies the pages' titles
        VersionText.Text = $"version {typeof(MainWindow).Assembly.GetName().Version?.ToString(3)}";
        BuildNav();
        Loaded += async (_, _) =>
        {
            ShowLastScan();
            // The inventory too: new devices are spotted by comparing with it. All of it off the
            // window's thread, so the window answers from the first moment.
            await Task.WhenAll(LoadWeeklyCheck(), StartDeviceWatch(), LoadHardware(), LoadInventory(), CheckForUpdate(), CheckLocalTiles());
        };
    }

    /// <summary>
    /// Everything the app reads locally, without the network or administrator rights: lets the
    /// screenshot test show each section with real content.
    /// </summary>
    internal async Task LoadLocalData()
    {
        await Task.WhenAll(LoadHardware(), LoadInventory(), LoadStartup(), LoadPrograms(), LoadDisks(), LoadBatteries(), LoadHistory(),
            LoadTuning(), LoadSecurity(askWindowsUpdate: false));
        report = DriverReport.Build(inventory, [], new Dictionary<string, DriverOffer>());
        ShowReport();
        LoadSettings();
        await LoadGames();
    }

    readonly HashSet<TabItem> openedTabs = [];

    // Each page reads the system only when first opened: WMI, Windows Update and the registry
    // are slow to query, and most visits use one or two pages.
    async void OnTabChanged(object sender, SelectionChangedEventArgs e)
    {
        // Lists inside the pages raise SelectionChanged too, and it bubbles up here.
        if (e.OriginalSource != Tabs || Tabs.SelectedItem is not TabItem tab) return;
        ShowInNav(tab);
        if (tab == JournalTab) await ShowJournal(); // changes come from every page: read again each time
        if (!openedTabs.Add(tab)) return;
        if (tab == DisksTab) await Task.WhenAll(LoadDisks(), LoadBatteries());
        else if (tab == SecurityTab) await (fullSecurity ??= LoadSecurity(askWindowsUpdate: true));
        else if ((tab == InventoryTab || tab == ProblemsTab) && inventory.Count == 0) await LoadInventory();
        else if (tab == GpuTab) await LoadGpus();
        else if (tab == ComponentsTab) await CheckComponents();
        else if (tab == GameTipsTab) await LoadGameTips();
        else if (tab == TuningTab) await LoadTuning();
        else if (tab == PrivacyTab || tab == CleanupTab || tab == FakeBoostsTab) await LoadTweaks();
        else if (tab == HistoryTab) await LoadHistory();
        else if (tab == StartupTab) await LoadStartup();
        else if (tab == ProgramsTab) await LoadPrograms();
        else if (tab == SettingsTab) LoadSettings();
    }

    async Task CheckForUpdate()
    {
        try
        {
            var current = typeof(MainWindow).Assembly.GetName().Version!;
            if (await UpdateCheck.NewerReleaseAsync(current) is not { } release) return;
            UpdateBannerText.Text = L.F("La version {0} de Driver Updater est disponible.", release.Version.ToString(3));
            UpdateBannerButton.Tag = release.Url;
            UpdateInstallButton.Visibility = release.CanInstall ? Visibility.Visible : Visibility.Collapsed;
            newerRelease = release;
            UpdateBanner.Visibility = Visibility.Visible;
        }
        catch (Exception)
        {
            // Offline or GitHub unreachable: simply no banner.
        }
    }

    /// <summary>
    /// Downloads the new installer, checks it against the release's SHA-256 list, starts it and
    /// closes the app so the installer can replace it.
    /// </summary>
    async void OnInstallAppUpdate(object sender, RoutedEventArgs e)
    {
        if (newerRelease is not { CanInstall: true } release) return;
        if (Ask(L.F("Installer la version {0} ?\n\nL'installateur est téléchargé depuis la page GitHub du projet, ", release.Version.ToString(3)) +
                L.T("vérifié, puis lancé ; l'application se ferme pour qu'il la remplace.")) != MessageBoxResult.Yes)
            return;
        await RunBusy(L.T("Téléchargement de la mise à jour…"), async () =>
        {
            var current = typeof(MainWindow).Assembly.GetName().Version!;
            using var installer = await UpdateCheck.DownloadInstallerAsync(release, current, new Progress<string>(SetStatus));
            try
            {
                installer.Start();
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                return L.T("Mise à jour annulée.");
            }
            Application.Current.Shutdown();
            return L.T("Installation de la mise à jour…");
        });
    }

    async void OnCopyDiagnostics(object sender, RoutedEventArgs e)
    {
        var lines = log.ToList();
        SetStatus(L.T("Préparation du diagnostic…"));
        var report = await Task.Run(() => Diagnostics.Build(lines));
        try
        {
            Clipboard.SetText(report);
            SetStatus(L.T("Diagnostic copié : collez-le avec Ctrl+V. Relisez-le avant de le partager."));
        }
        catch (Exception ex)
        {
            // Another program can hold the clipboard for a moment.
            SetStatus(L.T("Presse-papiers occupé, réessayez : ") + Errors.Describe(ex));
        }
    }

    void OnOpenLink(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo((string)((Button)sender).Tag) { UseShellExecute = true });

    void OnOpenSetting(object sender, RoutedEventArgs e)
    {
        var target = (string)((Button)sender).Tag;
        // A bare file name would also be looked up in the current folder, where anyone could
        // drop a fake rstrui.exe.
        if (!target.StartsWith("ms-settings:", StringComparison.Ordinal) && !target.StartsWith("windowsdefender:", StringComparison.Ordinal))
            target = Path.Combine(Environment.SystemDirectory, target);
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
    }

    /// <summary>
    /// Where the window was left, if that is still on a screen; otherwise centred, and never
    /// larger than the screen: a laptop at 150 % has fewer than 700 units of height.
    /// </summary>
    void RestorePlacement()
    {
        var area = SystemParameters.WorkArea;
        MinWidth = Math.Min(MinWidth, area.Width);
        MinHeight = Math.Min(MinHeight, area.Height);
        var screens = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        // Its title bar must be on a screen, or it could not be moved: a screen unplugged since, for one.
        if (Settings.Window is { } saved && screens.Contains(new Point(saved.Left + 60, saved.Top + 16)))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            (Left, Top) = (saved.Left, saved.Top);
            Width = Math.Clamp(saved.Width, MinWidth, Math.Max(MinWidth, screens.Width));
            Height = Math.Clamp(saved.Height, MinHeight, Math.Max(MinHeight, screens.Height));
            if (saved.Maximized) WindowState = WindowState.Maximized;
        }
        else
        {
            Width = Math.Min(Width, area.Width);
            Height = Math.Min(Height, area.Height);
        }
    }

    void SavePlacement()
    {
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, ActualWidth, ActualHeight) : RestoreBounds;
        if (!bounds.IsEmpty) Settings.Window = new(bounds.Left, bounds.Top, bounds.Width, bounds.Height, WindowState == WindowState.Maximized);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // The administrator helper reports here: closing now would cut it off mid-task. It may
        // also hang (a stuck DISM or Windows Update): the user decides.
        if (adminRuns > 0 && Ask(L.T("Une opération administrateur est en cours. Fermer quand même ?\n\n") +
                                 L.T("Elle sera interrompue là où elle en est."), MessageBoxImage.Warning) != MessageBoxResult.Yes)
            e.Cancel = true;
        // The others too: an update winget is still running would finish without reaching the journal.
        else if (adminRuns == 0 && busy && Ask(L.T("Une opération est en cours. Fermer quand même ?\n\n") +
                                               L.T("Ce qui est déjà fait le reste ; la suite n'aura pas lieu, ou pas dans le journal."),
                     MessageBoxImage.Warning) != MessageBoxResult.Yes)
            e.Cancel = true;
        if (!e.Cancel) SavePlacement();
        base.OnClosing(e);
    }

    /// <summary>Runs one administrator action; Windows asks the user first.</summary>
    async Task<T> RunAdminAsync<T>(string command, params string[] args)
    {
        adminRuns++;
        try
        {
            return await Elevation.RunAsync<T>(command, args, new Progress<string>(SetStatus),
                question => Ask(question, MessageBoxImage.Warning) == MessageBoxResult.Yes);
        }
        finally
        {
            adminRuns--;
        }
    }

    CancellationTokenSource? stopping; // the running operation's, when it can be stopped

    Task RunBusy(string message, Func<Task<string>> work) => RunBusy(message, _ => work(), canStop: false);

    /// <summary>The same, with "Arrêter" in the status bar: <paramref name="work"/> stops where its token says.</summary>
    Task RunBusy(string message, Func<CancellationToken, Task<string>> work) => RunBusy(message, work, canStop: true);

    async Task RunBusy(string message, Func<CancellationToken, Task<string>> work, bool canStop)
    {
        // One action at a time: a second one would turn every button back on when it ends,
        // while the first is still running.
        if (busy)
        {
            SetStatus(L.T("Une opération est déjà en cours : patientez qu'elle se termine."));
            return;
        }
        busy = true;
        using var stop = new CancellationTokenSource();
        stopping = canStop ? stop : null;
        StopButton.IsEnabled = true;
        StopButton.Visibility = canStop ? Visibility.Visible : Visibility.Collapsed;
        UpdateButtons();
        BusyBar.Visibility = Visibility.Visible;
        SetStatus(message);
        try
        {
            SetStatus(await work(stop.Token));
        }
        catch (OperationCanceledException e)
        {
            SetStatus(stop.IsCancellationRequested ? L.T("Arrêté.") : e.Message);
        }
        catch (Exception e)
        {
            Diagnostics.Remember(e); // where it came from, for a bug report
            SetStatus(L.T("Erreur : ") + Errors.Describe(e));
        }
        finally
        {
            busy = false;
            stopping = null;
            StopButton.Visibility = Visibility.Collapsed;
            BusyBar.Visibility = Visibility.Collapsed;
            BusyBar.IsIndeterminate = true;
            UpdateButtons();
        }
    }

    void OnStopBusy(object sender, RoutedEventArgs e)
    {
        stopping?.Cancel();
        StopButton.IsEnabled = false;
        SetStatus(L.T("Arrêt demandé…"));
    }

    void UpdateButtons()
    {
        foreach (var button in new[]
                 {
                     SearchButton, UnhideButton, SearchSoftwareButton, HealthDetailsButton, ScanTempButton,
                     CleanWindowsTempButton, SfcButton, DismRestoreButton, DismCleanupButton, BackupButton, RestoreBackupButton,
                     SearchCatalogButton, UnhideCatalogButton, UninstallButton, FindLeftoversButton, RecycleButton, RefreshStartupButton,
                     BootReportButton, MeasureButton, ScanButton, RefreshInventoryButton, StoreScanButton, DeviceBannerButton,
                     GpuCheckButton, ComponentsCheckButton, TuneAllButton, TuningRefreshButton, RollbackButton, PrivacyAllButton, FakeBoostsScanButton, FakeBoostsAllButton,
                     RestorePointsButton, CreateRestorePointButton, UpdateInstallButton,
                 })
            button.IsEnabled = !busy;
        StoreDeleteButton.IsEnabled = !busy && storeChoices.Any(c => c.Selected);
        UpdateAllButton.IsEnabled = !busy && report.Any(r => r.Selected && r.CanInstall);
        GpuInstallButton.IsEnabled = !busy && gpuRows.Any(r => r.Newer is not null);
        ComponentsInstallButton.IsEnabled = !busy && componentRows.Any(r => r.CanInstall);
        InstallCatalogButton.IsEnabled = HideCatalogButton.IsEnabled = !busy && catalogDrivers.Count > 0;
        InstallButton.IsEnabled = HideButton.IsEnabled = !busy && drivers.Count > 0;
        UpgradeSoftwareButton.IsEnabled = !busy && software.Count > 0;
        CleanTempButton.IsEnabled = !busy && tempScan is { Files.Count: > 0 };
    }

    void SetStatus(string text)
    {
        StatusText.Text = text;
        StatusText.ToolTip = text; // one line in the bar: a long error is read whole here
        log.Add($"{DateTime.Now:HH:mm:ss} {text}");
        if (log.Count > 200) log.RemoveAt(0);
        UIElementAutomationPeer.CreatePeerForElement(StatusText)
            .RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    MessageBoxResult Ask(string text, MessageBoxImage icon = MessageBoxImage.Question) =>
        MessageBox.Show(this, text, Title, MessageBoxButton.YesNo, icon);
}
