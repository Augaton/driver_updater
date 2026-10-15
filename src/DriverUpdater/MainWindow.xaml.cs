using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace DriverUpdater;

/// <summary>
/// The main window. It runs without administrator rights; each tab's actions live in their
/// own partial file (MainWindow.Drivers.cs, .Software.cs, .Health.cs).
/// </summary>
public partial class MainWindow : Window
{
    readonly List<string> log = [];
    bool busy;
    bool adminRunning;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) =>
        {
            LoadWeeklyCheck();
            await Task.WhenAll(LoadProblemDevices(), LoadHardware(), LoadDisks(), LoadBatteries(), LoadHistory(), CheckForUpdate());
        };
    }

    async Task CheckForUpdate()
    {
        try
        {
            var current = typeof(MainWindow).Assembly.GetName().Version!;
            if (await UpdateCheck.NewerReleaseAsync(current) is not { } release) return;
            UpdateBannerText.Text = $"La version {release.Version.ToString(3)} de Driver Updater est disponible.";
            UpdateBannerButton.Tag = release.Url;
            UpdateBanner.Visibility = Visibility.Visible;
        }
        catch (Exception)
        {
            // Offline or GitHub unreachable: simply no banner.
        }
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
        if (!target.StartsWith("ms-settings:", StringComparison.Ordinal))
            target = Path.Combine(Environment.SystemDirectory, target);
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // The administrator helper reports here: closing now would cut it off mid-task.
        if (adminRunning)
        {
            e.Cancel = true;
            SetStatus("Une opération administrateur est en cours : patientez avant de fermer.");
        }
        base.OnClosing(e);
    }

    /// <summary>Runs one administrator action; Windows asks the user first.</summary>
    async Task<T> RunAdminAsync<T>(string command, params string[] args)
    {
        adminRunning = true;
        try
        {
            return await Elevation.RunAsync<T>(command, args, new Progress<string>(SetStatus),
                question => Ask(question, MessageBoxImage.Warning) == MessageBoxResult.Yes);
        }
        finally
        {
            adminRunning = false;
        }
    }

    async Task RunBusy(string message, Func<Task<string>> work)
    {
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
                 })
            button.IsEnabled = !busy;
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
