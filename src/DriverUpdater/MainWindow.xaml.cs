using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;

namespace DriverUpdater;

public partial class MainWindow : Window
{
    // Younger temp files may still belong to a running installer or app.
    static readonly TimeSpan TempMinAge = TimeSpan.FromDays(2);

    List<DriverUpdate> drivers = [];
    TempCleaner.ScanResult? tempScan;
    DateTime tempCutoff;
    bool busy;
    bool installing;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await Task.WhenAll(LoadProblemDevices(), LoadDisks());
    }

    // Built from the profile folder, which a user cannot redirect, rather than from %TEMP%,
    // which any program running as the user can point at C:\Windows\System32.
    static TempCleaner.Target[] TempTargets() =>
    [
        new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Local", "Temp"), Recursive: true),
        new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"), Recursive: true),
    ];

    async Task LoadProblemDevices()
    {
        try
        {
            var devices = await Task.Run(SystemInfo.ProblemDevices);
            ProblemList.ItemsSource = devices;
            ProblemHeading.Text = $"Périphériques en erreur ({devices.Count})";
            ProblemStatus.Text = "Aucun : tous les périphériques fonctionnent.";
            ProblemStatus.Visibility = devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception e)
        {
            ProblemStatus.Text = "Lecture impossible : " + Describe(e);
        }
    }

    async Task LoadDisks()
    {
        try
        {
            DiskGrid.ItemsSource = await Task.Run(SystemInfo.Disks);
        }
        catch (Exception e)
        {
            DiskStatus.Text = "Lecture impossible : " + Describe(e);
        }
    }

    async void OnSearch(object sender, RoutedEventArgs e) =>
        await RunBusy("Recherche sur Windows Update, cela peut prendre quelques minutes…", async () =>
        {
            var installed = Task.Run(SystemInfo.InstalledDrivers);
            drivers = await Task.Run(WindowsUpdateDrivers.Search);
            try
            {
                var current = await installed;
                foreach (var driver in drivers)
                    if (driver.HardwareId is { } id && current.TryGetValue(id, out var match))
                        driver.Installed = match.ToString();
            }
            catch (ManagementException)
            {
                // Installed versions are a bonus; the updates are still worth showing.
            }
            ShowDrivers();
            return drivers.Count == 0 ? "Vos pilotes sont à jour." : $"{drivers.Count} mise(s) à jour trouvée(s).";
        });

    async void OnInstall(object sender, RoutedEventArgs e)
    {
        var selected = drivers.Where(d => d.Selected).ToList();
        if (selected.Count == 0)
        {
            SetStatus("Cochez au moins un pilote.");
            return;
        }
        if (Ask($"Installer {selected.Count} pilote(s) ?\n\nUn point de restauration système sera créé avant.") != MessageBoxResult.Yes)
            return;

        await RunBusy("Création d'un point de restauration, cela peut prendre une minute…", async () =>
        {
            var point = await Task.Run(SystemInfo.CreateRestorePoint);
            if (!point.Ok && Ask(point.Message + "\n\nContinuer sans point de restauration ?", MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return "Installation annulée.";

            installing = true;
            BusyBar.IsIndeterminate = false;
            BusyBar.Maximum = selected.Count;
            var outcomes = new List<InstallOutcome>();
            foreach (var driver in selected)
            {
                SetStatus($"Pilote {outcomes.Count + 1} sur {selected.Count} : {driver.Title}…");
                outcomes.Add(await Task.Run(() => WindowsUpdateDrivers.Install(driver)));
                BusyBar.Value = outcomes.Count;
            }
            installing = false;
            bool rebootRequired = outcomes.Any(o => o.RebootRequired);

            var installed = outcomes.Where(o => o.Succeeded).Select(o => o.Driver).ToHashSet();
            drivers = drivers.Where(d => !installed.Contains(d)).ToList();
            ShowDrivers();

            var lines = outcomes.Select(o => o.Succeeded ? "✔ " + o.Driver.Title : $"✘ {o.Driver.Title} ({o.Detail})");
            var report = point.Message + "\n\n" + string.Join("\n", lines);
            if (rebootRequired) report += "\n\nRedémarrez l'ordinateur pour terminer l'installation.";
            MessageBox.Show(this, report, "Résultat de l'installation", MessageBoxButton.OK, MessageBoxImage.Information);

            return $"{installed.Count} pilote(s) sur {selected.Count} installé(s)." + (rebootRequired ? " Redémarrage requis." : "");
        });
        installing = false;
    }

    async void OnScanTemp(object sender, RoutedEventArgs e) =>
        await RunBusy("Analyse des fichiers temporaires…", async () =>
        {
            tempCutoff = DateTime.UtcNow - TempMinAge;
            var cutoff = tempCutoff;
            tempScan = await Task.Run(() => TempCleaner.Find(TempTargets(), cutoff));
            TempStatus.Text = $"{tempScan.Files.Count} fichier(s), {Bytes.Format(tempScan.TotalBytes)}." +
                string.Concat(tempScan.RefusedFolders.Select(f => $"\nIgnoré, car redirigé par un lien ou inaccessible : {f}"));
            return "Analyse terminée.";
        });

    async void OnCleanTemp(object sender, RoutedEventArgs e) =>
        await RunBusy("Suppression des fichiers temporaires…", async () =>
        {
            var files = tempScan!.Files;
            var cutoff = tempCutoff;
            var result = await Task.Run(() => TempCleaner.Delete(files, cutoff));
            tempScan = null;
            TempStatus.Text = $"{Bytes.Format(result.FreedBytes)} libérés ({result.Deleted} fichier(s)). " +
                              $"{result.Skipped} laissé(s) en place : en cours d'utilisation, modifiés ou protégés.";
            return "Nettoyage terminé.";
        });

    void OnOpenSetting(object sender, RoutedEventArgs e)
    {
        var target = (string)((Button)sender).Tag;
        // A bare file name would also be looked up in the current folder, where anyone could
        // drop a fake rstrui.exe that we would then start as administrator.
        if (!target.StartsWith("ms-settings:", StringComparison.Ordinal))
            target = Path.Combine(Environment.SystemDirectory, target);
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Quitting mid-install could leave a driver half installed.
        if (installing)
        {
            e.Cancel = true;
            SetStatus("Installation en cours : patientez avant de fermer.");
        }
        base.OnClosing(e);
    }

    void ShowDrivers()
    {
        DriverGrid.ItemsSource = drivers;
        DriverPlaceholder.Text = "Aucune mise à jour de pilote disponible.";
        DriverPlaceholder.Visibility = drivers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
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
        catch (Exception e)
        {
            SetStatus("Erreur : " + Describe(e));
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
        SearchButton.IsEnabled = !busy;
        InstallButton.IsEnabled = !busy && drivers.Count > 0;
        ScanTempButton.IsEnabled = !busy;
        CleanTempButton.IsEnabled = !busy && tempScan is { Files.Count: > 0 };
    }

    void SetStatus(string text)
    {
        StatusText.Text = text;
        UIElementAutomationPeer.CreatePeerForElement(StatusText)
            .RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    MessageBoxResult Ask(string text, MessageBoxImage icon = MessageBoxImage.Question) =>
        MessageBox.Show(this, text, Title, MessageBoxButton.YesNo, icon);

    // Windows Update errors are only meaningful with their HRESULT (e.g. 0x8024402C = no network).
    static string Describe(Exception e) =>
        e is COMException ? $"{e.Message} (0x{e.HResult:X8})" : e.Message;
}
