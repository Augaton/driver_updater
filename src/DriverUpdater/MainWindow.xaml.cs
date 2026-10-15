using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using Microsoft.Win32;

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
        Loaded += async (_, _) =>
        {
            LoadWeeklyCheck();
            await Task.WhenAll(LoadProblemDevices(), LoadGpus(), LoadDisks(), LoadBatteries());
        };
    }

    // Built from the profile folder, which a user cannot redirect, rather than from %TEMP%,
    // which any program running as the user can point at C:\Windows\System32.
    static TempCleaner.Target[] TempTargets() =>
    [
        new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Local", "Temp"), Recursive: true),
        new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"), Recursive: true),
    ];

    // ---- Loading -----------------------------------------------------------------------

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

    async Task LoadGpus()
    {
        try
        {
            var gpus = await Task.Run(SystemInfo.Gpus);
            GpuList.ItemsSource = gpus;
            GpuStatus.Text = gpus.Count == 0 ? "Aucune carte graphique détectée." : "";
        }
        catch (Exception e)
        {
            GpuStatus.Text = "Lecture impossible : " + Describe(e);
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

    async Task LoadBatteries()
    {
        try
        {
            var batteries = await Task.Run(SystemInfo.Batteries);
            BatteryGrid.ItemsSource = batteries;
            BatteryCard.Visibility = batteries.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception)
        {
            // Battery data is a bonus: without it the card simply stays hidden.
        }
    }

    void LoadWeeklyCheck()
    {
        if (!WeeklyCheck.InstalledInProgramFiles)
        {
            WeeklyCheckBox.IsEnabled = false;
            WeeklyCheckNote.Text = "Disponible une fois l'application installée avec son installateur.";
            return;
        }
        try
        {
            WeeklyCheckBox.IsChecked = WeeklyCheck.IsEnabled();
        }
        catch (Exception e)
        {
            WeeklyCheckBox.IsEnabled = false;
            WeeklyCheckNote.Text = "Planificateur de tâches indisponible : " + Describe(e);
        }
    }

    // ---- Drivers -----------------------------------------------------------------------

    async void OnSearch(object sender, RoutedEventArgs e) =>
        await RunBusy("Recherche sur Windows Update, cela peut prendre quelques minutes…", async () =>
        {
            var found = await Task.Run(WindowsUpdateDrivers.Search);
            await ShowUpdates(found);
            return drivers.Count == 0 ? "Vos pilotes sont à jour." : $"{drivers.Count} mise(s) à jour trouvée(s).";
        });

    /// <summary>Shows updates found here or by the weekly check, with the driver each one would replace.</summary>
    public async Task ShowUpdates(List<DriverUpdate> found, string? status = null)
    {
        try
        {
            var installed = await Task.Run(SystemInfo.InstalledDrivers);
            foreach (var driver in found)
                if (driver.HardwareId is { } id && installed.TryGetValue(id, out var match))
                    driver.Installed = match.ToString();
        }
        catch (ManagementException)
        {
            // Installed versions are a bonus; the updates are still worth showing.
        }
        drivers = found;
        DriverGrid.ItemsSource = drivers;
        DriverPlaceholder.Text = "Aucune mise à jour de pilote disponible.";
        DriverPlaceholder.Visibility = drivers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateButtons();
        if (status is not null) SetStatus(status);
    }

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
            var point = await SafetyRestorePoint();
            if (point is null) return "Installation annulée.";

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
            await ShowUpdates(drivers.Where(d => !installed.Contains(d)).ToList());

            var lines = outcomes.Select(o => o.Succeeded ? "✔ " + o.Driver.Title : $"✘ {o.Driver.Title} ({o.Detail})");
            var report = point.Message + "\n\n" + string.Join("\n", lines);
            if (rebootRequired) report += "\n\nRedémarrez l'ordinateur pour terminer l'installation.";
            MessageBox.Show(this, report, "Résultat de l'installation", MessageBoxButton.OK, MessageBoxImage.Information);

            return $"{installed.Count} pilote(s) sur {selected.Count} installé(s)." + (rebootRequired ? " Redémarrage requis." : "");
        });
        installing = false;
    }

    async void OnHide(object sender, RoutedEventArgs e)
    {
        var selected = drivers.Where(d => d.Selected).ToList();
        if (selected.Count == 0)
        {
            SetStatus("Cochez au moins un pilote.");
            return;
        }
        if (Ask($"Masquer {selected.Count} pilote(s) ?\n\nWindows Update ne les proposera plus, ici comme dans les Paramètres. " +
                "« Réafficher les pilotes masqués » les fait revenir.") != MessageBoxResult.Yes)
            return;

        await RunBusy("Masquage…", async () =>
        {
            await Task.Run(() => selected.ForEach(WindowsUpdateDrivers.Hide));
            await ShowUpdates(drivers.Except(selected).ToList());
            return $"{selected.Count} pilote(s) masqué(s).";
        });
    }

    async void OnUnhide(object sender, RoutedEventArgs e) =>
        await RunBusy("Recherche des pilotes masqués…", async () =>
        {
            int count = await Task.Run(WindowsUpdateDrivers.UnhideAll);
            return count == 0 ? "Aucun pilote masqué." : $"{count} pilote(s) réaffiché(s) : relancez la recherche pour les voir.";
        });

    void OnWeeklyCheckClick(object sender, RoutedEventArgs e)
    {
        bool enable = WeeklyCheckBox.IsChecked == true;
        try
        {
            if (enable) WeeklyCheck.Enable(Environment.ProcessPath!);
            else WeeklyCheck.Disable();
            SetStatus(enable
                ? "Vérification activée : chaque mercredi à midi, ou au démarrage suivant si le PC était éteint."
                : "Vérification hebdomadaire désactivée.");
        }
        catch (Exception ex)
        {
            WeeklyCheckBox.IsChecked = !enable;
            SetStatus("Erreur : " + Describe(ex));
        }
    }

    // Through explorer.exe, so the browser opens with normal rights instead of ours.
    void OnOpenVendor(object sender, RoutedEventArgs e) =>
        Process.Start(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"),
            (string)((Button)sender).Tag);

    // ---- Backup ------------------------------------------------------------------------

    async void OnBackup(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Où enregistrer la sauvegarde des pilotes ?",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dialog.ShowDialog(this) != true) return;

        await RunBusy("Sauvegarde des pilotes…", async () =>
        {
            var (folder, count) = await DriverBackup.ExportAsync(dialog.FolderName);
            BackupStatus.Text = $"{count} pilote(s) sauvegardé(s) dans {folder}";
            return "Sauvegarde terminée.";
        });
    }

    async void OnRestoreBackup(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Dossier de sauvegarde à restaurer" };
        if (dialog.ShowDialog(this) != true) return;
        if (Ask($"Installer les pilotes de « {dialog.FolderName} » ?\n\nUn point de restauration système sera créé avant.") != MessageBoxResult.Yes)
            return;

        await RunBusy("Création d'un point de restauration, cela peut prendre une minute…", async () =>
        {
            if (await SafetyRestorePoint() is null) return "Restauration annulée.";
            installing = true;
            SetStatus("Installation des pilotes sauvegardés…");
            bool reboot = await DriverBackup.ImportAsync(dialog.FolderName);
            installing = false;
            BackupStatus.Text = "Pilotes restaurés." + (reboot ? " Redémarrez l'ordinateur pour terminer." : "");
            return "Restauration terminée.";
        });
        installing = false;
    }

    /// <summary>
    /// Creates a restore point before touching drivers. Returns null if that failed and the
    /// user chose not to go on without one.
    /// </summary>
    async Task<RestorePointResult?> SafetyRestorePoint()
    {
        var point = await Task.Run(SystemInfo.CreateRestorePoint);
        if (!point.Ok && Ask(point.Message + "\n\nContinuer sans point de restauration ?", MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return null;
        return point;
    }

    // ---- Temp files --------------------------------------------------------------------

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

    // ---- Plumbing ----------------------------------------------------------------------

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
        HideButton.IsEnabled = !busy && drivers.Count > 0;
        UnhideButton.IsEnabled = !busy;
        ScanTempButton.IsEnabled = !busy;
        CleanTempButton.IsEnabled = !busy && tempScan is { Files.Count: > 0 };
        BackupButton.IsEnabled = !busy;
        RestoreBackupButton.IsEnabled = !busy;
    }

    void SetStatus(string text)
    {
        StatusText.Text = text;
        UIElementAutomationPeer.CreatePeerForElement(StatusText)
            .RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    MessageBoxResult Ask(string text, MessageBoxImage icon = MessageBoxImage.Question) =>
        MessageBox.Show(this, text, Title, MessageBoxButton.YesNo, icon);

    // Windows errors are only meaningful with their code (e.g. 0x8024402C = no network), and
    // late-bound COM can raise them as any exception type. Codes in 0x8013xxxx are .NET's own.
    static string Describe(Exception e) =>
        (uint)e.HResult >> 16 == 0x8013 ? e.Message : $"{e.Message} (0x{e.HResult:X8})";
}
