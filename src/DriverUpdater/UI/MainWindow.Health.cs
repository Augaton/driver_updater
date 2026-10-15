using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace DriverUpdater.UI;

// "Santé et nettoyage" and "Sauvegarde" tabs.
public partial class MainWindow
{
    TempCleaner.ScanResult? tempScan;
    // Windows Update takes up to a minute to count: one search serves the tile and the card.
    Task<SecurityState?>? fullSecurity;
    DateTime tempCutoff;

    // Built from the profile folder rather than from %TEMP%, which any program can repoint.
    static TempCleaner.Target UserTemp() =>
        new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Local", "Temp"), Recursive: true);

    async Task LoadDisks()
    {
        try
        {
            DiskGrid.ItemsSource = await Task.Run(SystemInfo.Disks);
        }
        catch (Exception e)
        {
            DiskStatus.Text = "Lecture impossible sans droits administrateur : utilisez « Usure et température ». " + Errors.Describe(e);
        }
    }

    async Task LoadBatteries()
    {
        try
        {
            ShowBatteries(await Task.Run(SystemInfo.Batteries));
        }
        catch (Exception)
        {
            // Battery data is a bonus: without it the card simply stays hidden.
        }
    }

    void ShowBatteries(List<Battery> batteries)
    {
        BatteryGrid.ItemsSource = batteries;
        BatteryCard.Visibility = batteries.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Antivirus and firewall at once; the Windows updates waiting take Windows Update up to a
    /// minute to count, so they follow. Null when nothing could be read.
    /// </summary>
    async Task<SecurityState?> LoadSecurity(bool askWindowsUpdate)
    {
        try
        {
            var quick = await Task.Run(() => SecurityStatus.Read(askWindowsUpdate: false));
            ShowSecurity(quick, askWindowsUpdate ? "recherche en cours…" : "non vérifiées");
            if (!askWindowsUpdate) return quick;
            var full = await Task.Run(() => SecurityStatus.Read(askWindowsUpdate: true));
            ShowSecurity(full, "Windows Update n'a pas répondu");
            return full;
        }
        catch (Exception e)
        {
            SecuritySummary.Text = "Lecture impossible : " + Errors.Describe(e);
            return null;
        }
    }

    void ShowSecurity(SecurityState state, string updatesUnknown)
    {
        var lines = new List<string>();
        lines.AddRange(state.Antiviruses.Count == 0
            ? ["Antivirus : aucun déclaré à Windows (Security Center absent, comme sur Windows Server)."]
            : state.Antiviruses.Select(a => $"{(a.Enabled && a.UpToDate ? "✔" : "✘")} {a.Name} : " +
                                            $"{(a.Enabled ? "actif" : "inactif")}, {(a.UpToDate ? "à jour" : "pas à jour")}"));
        lines.Add(state.FirewallOn switch
        {
            true => "✔ Pare-feu Windows : actif",
            false => "✘ Pare-feu Windows : désactivé",
            null => "Pare-feu Windows : état illisible",
        });
        lines.Add(state.PendingUpdates switch
        {
            0 => "✔ Mises à jour de Windows : aucune en attente",
            > 0 => $"✘ Mises à jour de Windows : {state.PendingUpdates} en attente",
            _ => "Mises à jour de Windows : " + updatesUnknown,
        });
        SecuritySummary.Text = string.Join("\n", lines);
    }

    async void OnRestorePoints(object sender, RoutedEventArgs e) =>
        await RunBusy("En attente de l'autorisation administrateur…", async () =>
        {
            var points = await RunAdminAsync<List<RestorePointInfo>>(AdminCommand.RestorePoints);
            RestorePointList.ItemsSource = points;
            return points.Count == 0
                ? "Aucun point de restauration : la protection du système est peut-être désactivée (Paramètres → Système → Informations système → Protection du système)."
                : $"{points.Count} point(s) de restauration.";
        });

    async void OnCreateRestorePoint(object sender, RoutedEventArgs e) =>
        await RunBusy("En attente de l'autorisation administrateur…", async () =>
            (await RunAdminAsync<RestorePointResult>(AdminCommand.CreateRestorePoint)).Message);

    // Wear, temperature and often the battery are only readable as administrator.
    async void OnHealthDetails(object sender, RoutedEventArgs e) =>
        await RunBusy("En attente de l'autorisation administrateur…", async () =>
        {
            var report = await RunAdminAsync<HealthReport>(AdminCommand.Health);
            DiskGrid.ItemsSource = report.Disks;
            DiskStatus.Text = "";
            ShowBatteries(report.Batteries);
            return "Usure, température et batterie lues.";
        });

    async void OnScanTemp(object sender, RoutedEventArgs e) =>
        await RunBusy("Analyse des fichiers temporaires…", async () =>
        {
            tempCutoff = DateTime.UtcNow - TempCleaner.MinAge;
            var cutoff = tempCutoff;
            tempScan = await Task.Run(() => TempCleaner.Find([UserTemp()], cutoff));
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
            TempStatus.Text = Describe(result);
            return "Nettoyage terminé.";
        });

    async void OnCleanWindowsTemp(object sender, RoutedEventArgs e) =>
        await RunBusy("En attente de l'autorisation administrateur…", async () =>
        {
            var result = await RunAdminAsync<TempCleaner.CleanResult>(AdminCommand.CleanWindowsTemp);
            TempStatus.Text = @"C:\Windows\Temp : " + Describe(result);
            return "Nettoyage terminé.";
        });

    static string Describe(TempCleaner.CleanResult result) =>
        $"{Bytes.Format(result.FreedBytes)} libérés ({result.Deleted} fichier(s)). " +
        $"{result.Skipped} laissé(s) en place : en cours d'utilisation, modifiés ou protégés.";

    async void OnRepair(object sender, RoutedEventArgs e)
    {
        var command = (string)((Button)sender).Tag;
        await RunBusy("En attente de l'autorisation administrateur…", async () =>
        {
            var report = await RunAdminAsync<RepairReport>(command);
            RepairStatus.Text = report.Summary;
            return report.ExitCode switch
            {
                0 => "Réparation terminée.",
                3010 => "Réparation terminée : redémarrez l'ordinateur.",
                _ => $"Terminé avec le code {report.ExitCode} : voir le résumé dans la carte « Réparer Windows ».",
            };
        });
    }

    async void OnBackup(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Où enregistrer la sauvegarde des pilotes ?",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dialog.ShowDialog(this) != true) return;

        await RunBusy("En attente de l'autorisation administrateur…", async () =>
        {
            var report = await RunAdminAsync<BackupReport>(AdminCommand.ExportDrivers, dialog.FolderName);
            BackupStatus.Text = $"{report.Count} pilote(s) sauvegardé(s) dans {report.Folder}";
            return "Sauvegarde terminée.";
        });
    }

    async void OnRestoreBackup(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Dossier de sauvegarde à restaurer" };
        if (dialog.ShowDialog(this) != true) return;
        if (Ask($"Installer les pilotes de « {dialog.FolderName} » ?\n\nUn point de restauration système sera créé avant.") != MessageBoxResult.Yes)
            return;

        await RunBusy("En attente de l'autorisation administrateur…", async () =>
        {
            var report = await RunAdminAsync<ImportReport>(AdminCommand.ImportDrivers, dialog.FolderName);
            if (report.Cancelled) return "Restauration annulée.";
            BackupStatus.Text = "Pilotes restaurés." + (report.RebootRequired ? " Redémarrez l'ordinateur pour terminer." : "");
            await LoadHistory();
            return "Restauration terminée.";
        });
    }
}
