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
        VersionText.Text = $"version {typeof(MainWindow).Assembly.GetName().Version?.ToString(3)}";
        BuildNav();
        Loaded += async (_, _) =>
        {
            LoadWeeklyCheck();
            StartDeviceWatch();
            // The inventory too: new devices are spotted by comparing with it.
            await Task.WhenAll(LoadHardware(), LoadInventory(), CheckForUpdate());
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
        if (!openedTabs.Add(tab)) return;
        if (tab == DisksTab) await Task.WhenAll(LoadDisks(), LoadBatteries());
        else if (tab == SecurityTab) await (fullSecurity ??= LoadSecurity(askWindowsUpdate: true));
        else if ((tab == InventoryTab || tab == ProblemsTab) && inventory.Count == 0) await LoadInventory();
        else if (tab == GpuTab) await LoadGpus();
        else if (tab == ComponentsTab) await CheckComponents();
        else if (tab == GameTipsTab) await LoadGameTips();
        else if (tab == TuningTab) await LoadTuning();
        else if (tab == PrivacyTab || tab == CleanupTab) await LoadTweaks();
        else if (tab == HistoryTab) await LoadHistory();
        else if (tab == StartupTab) await LoadStartup();
        else if (tab == ProgramsTab) await LoadPrograms();
    }

    async Task CheckForUpdate()
    {
        try
        {
            var current = typeof(MainWindow).Assembly.GetName().Version!;
            if (await UpdateCheck.NewerReleaseAsync(current) is not { } release) return;
            UpdateBannerText.Text = $"La version {release.Version.ToString(3)} de Driver Updater est disponible.";
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
        if (Ask($"Installer la version {release.Version.ToString(3)} ?\n\nL'installateur est téléchargé depuis la page GitHub du projet, " +
                "vérifié, puis lancé ; l'application se ferme pour qu'il la remplace.") != MessageBoxResult.Yes)
            return;
        await RunBusy("Téléchargement de la mise à jour…", async () =>
        {
            var current = typeof(MainWindow).Assembly.GetName().Version!;
            using var installer = await UpdateCheck.DownloadInstallerAsync(release, current, new Progress<string>(SetStatus));
            try
            {
                installer.Start();
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                return "Mise à jour annulée.";
            }
            Application.Current.Shutdown();
            return "Installation de la mise à jour…";
        });
    }

    async void OnCopyDiagnostics(object sender, RoutedEventArgs e)
    {
        var lines = log.ToList();
        SetStatus("Préparation du diagnostic…");
        var report = await Task.Run(() => Diagnostics.Build(lines));
        try
        {
            Clipboard.SetText(report);
            SetStatus("Diagnostic copié : collez-le avec Ctrl+V. Relisez-le avant de le partager.");
        }
        catch (Exception ex)
        {
            // Another program can hold the clipboard for a moment.
            SetStatus("Presse-papiers occupé, réessayez : " + Errors.Describe(ex));
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

    protected override void OnClosing(CancelEventArgs e)
    {
        // The administrator helper reports here: closing now would cut it off mid-task. It may
        // also hang (a stuck DISM or Windows Update): the user decides.
        if (adminRuns > 0 && Ask("Une opération administrateur est en cours. Fermer quand même ?\n\n" +
                                 "Elle sera interrompue là où elle en est.", MessageBoxImage.Warning) != MessageBoxResult.Yes)
            e.Cancel = true;
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

    async Task RunBusy(string message, Func<Task<string>> work)
    {
        // One action at a time: a second one would turn every button back on when it ends,
        // while the first is still running.
        if (busy)
        {
            SetStatus("Une opération est déjà en cours : patientez qu'elle se termine.");
            return;
        }
        busy = true;
        UpdateButtons();
        BusyBar.Visibility = Visibility.Visible;
        SetStatus(message);
        try
        {
            SetStatus(await work());
        }
        catch (OperationCanceledException e)
        {
            SetStatus(e.Message);
        }
        catch (Exception e)
        {
            SetStatus("Erreur : " + Errors.Describe(e));
        }
        finally
        {
            busy = false;
            BusyBar.Visibility = Visibility.Collapsed;
            BusyBar.IsIndeterminate = true;
            UpdateButtons();
        }
    }

    void UpdateButtons()
    {
        foreach (var button in new[]
                 {
                     SearchButton, UnhideButton, SearchSoftwareButton, HealthDetailsButton, ScanTempButton,
                     CleanWindowsTempButton, SfcButton, DismRestoreButton, DismCleanupButton, BackupButton, RestoreBackupButton,
                     SearchCatalogButton, UninstallButton, FindLeftoversButton, RecycleButton, RefreshStartupButton,
                     BootReportButton, MeasureButton, ScanButton, RefreshInventoryButton, StoreScanButton, DeviceBannerButton,
                     GpuCheckButton, ComponentsCheckButton, TuneAllButton, TuningRefreshButton, RollbackButton, PrivacyAllButton,
                     RestorePointsButton, CreateRestorePointButton, UpdateInstallButton,
                 })
            button.IsEnabled = !busy;
        StoreDeleteButton.IsEnabled = !busy && storeChoices.Any(c => c.Selected);
        UpdateAllButton.IsEnabled = !busy && report.Any(r => r.Selected && r.CanInstall);
        GpuInstallButton.IsEnabled = !busy && gpuRows.Any(r => r.Newer is not null);
        ComponentsInstallButton.IsEnabled = !busy && componentRows.Any(r => r.CanInstall);
        InstallCatalogButton.IsEnabled = !busy && catalogDrivers.Count > 0;
        InstallButton.IsEnabled = HideButton.IsEnabled = !busy && drivers.Count > 0;
        UpgradeSoftwareButton.IsEnabled = !busy && software.Count > 0;
        CleanTempButton.IsEnabled = !busy && tempScan is { Files.Count: > 0 };
    }

    void SetStatus(string text)
    {
        StatusText.Text = text;
        log.Add($"{DateTime.Now:HH:mm:ss} {text}");
        if (log.Count > 200) log.RemoveAt(0);
        UIElementAutomationPeer.CreatePeerForElement(StatusText)
            .RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    MessageBoxResult Ask(string text, MessageBoxImage icon = MessageBoxImage.Question) =>
        MessageBox.Show(this, text, Title, MessageBoxButton.YesNo, icon);
}
