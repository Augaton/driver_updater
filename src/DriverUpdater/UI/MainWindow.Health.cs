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
            DiskStatus.Text = L.T("Lecture impossible sans droits administrateur : utilisez « Usure et température ». ") + Errors.Describe(e);
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
            ShowSecurity(quick, askWindowsUpdate ? L.T("recherche en cours…") : L.T("non vérifiées"));
            if (!askWindowsUpdate) return quick;
            var full = await Task.Run(() => SecurityStatus.Read(askWindowsUpdate: true));
            ShowSecurity(full, L.T("Windows Update n'a pas répondu"));
            return full;
        }
        catch (Exception e)
        {
            SecuritySummary.Text = L.T("Lecture impossible : ") + Errors.Describe(e);
            return null;
        }
    }

    void ShowSecurity(SecurityState state, string updatesUnknown)
    {
        var lines = new List<string>();
        lines.AddRange(state.Antiviruses.Count == 0
            ? [L.T("Antivirus : aucun déclaré à Windows (Security Center absent, comme sur Windows Server).")]
            : state.Antiviruses.Select(a => L.F("{0} {1} : {2}, {3}", a.Enabled && a.UpToDate ? "✔" : "✘", a.Name,
                                                a.Enabled ? L.T("actif") : L.T("inactif"), a.UpToDate ? L.T("à jour") : L.T("pas à jour"))));
        lines.Add(state.FirewallOn switch
        {
            true => L.T("✔ Pare-feu Windows : actif"),
            false => L.T("✘ Pare-feu Windows : désactivé"),
            null => L.T("Pare-feu Windows : état illisible"),
        });
        lines.Add(state.PendingUpdates switch
        {
            0 => L.T("✔ Mises à jour de Windows : aucune en attente"),
            > 0 => L.F("✘ Mises à jour de Windows : {0} en attente", state.PendingUpdates),
            _ => L.T("Mises à jour de Windows : ") + updatesUnknown,
        });
        SecuritySummary.Text = string.Join("\n", lines);
    }

    async void OnRestorePoints(object sender, RoutedEventArgs e) =>
        await RunBusy(L.T("En attente de l'autorisation administrateur…"), async () =>
        {
            var points = await RunAdminAsync<List<RestorePointInfo>>(AdminCommand.RestorePoints);
            RestorePointList.ItemsSource = points;
            RestorePointsHint.Visibility = points.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            RestorePointsHint.Text = L.T("Aucun point de restauration : la protection du système est peut-être désactivée.");
            return points.Count == 0
                ? L.T("Aucun point de restauration : la protection du système est peut-être désactivée (Paramètres → Système → Informations système → Protection du système).")
                : L.F("{0} point(s) de restauration.", points.Count);
        });

    async void OnCreateRestorePoint(object sender, RoutedEventArgs e) =>
        await RunBusy(L.T("En attente de l'autorisation administrateur…"), async () =>
            (await RunAdminAsync<RestorePointResult>(AdminCommand.CreateRestorePoint)).Message);

    // Wear, temperature and often the battery are only readable as administrator.
    async void OnHealthDetails(object sender, RoutedEventArgs e) =>
        await RunBusy(L.T("En attente de l'autorisation administrateur…"), async () =>
        {
            var report = await RunAdminAsync<HealthReport>(AdminCommand.Health);
            DiskGrid.ItemsSource = report.Disks;
            DiskStatus.Text = "";
            ShowBatteries(report.Batteries);
            return L.T("Usure, température et batterie lues.");
        });

    async void OnScanTemp(object sender, RoutedEventArgs e) =>
        await RunBusy(L.T("Analyse des fichiers temporaires…"), async () =>
        {
            tempCutoff = DateTime.UtcNow - TempCleaner.MinAge;
            var cutoff = tempCutoff;
            tempScan = await Task.Run(() => TempCleaner.Find([UserTemp()], cutoff));
            TempStatus.Text = L.F("{0} fichier(s), {1}.", tempScan.Files.Count, Bytes.Format(tempScan.TotalBytes)) +
                string.Concat(tempScan.RefusedFolders.Select(f => L.F("\nIgnoré, car redirigé par un lien ou inaccessible : {0}", f)));
            return L.T("Analyse terminée.");
        });

    async void OnCleanTemp(object sender, RoutedEventArgs e) =>
        await RunBusy(L.T("Suppression des fichiers temporaires…"), async () =>
        {
            var files = tempScan!.Files;
            var cutoff = tempCutoff;
            var result = await Task.Run(() => TempCleaner.Delete(files, cutoff));
            tempScan = null;
            TempStatus.Text = Describe(result);
            return L.T("Nettoyage terminé.");
        });

    async void OnCleanWindowsTemp(object sender, RoutedEventArgs e) =>
        await RunBusy(L.T("En attente de l'autorisation administrateur…"), async () =>
        {
            var result = await RunAdminAsync<TempCleaner.CleanResult>(AdminCommand.CleanWindowsTemp);
            TempStatus.Text = L.T("C:\\Windows\\Temp : ") + Describe(result);
            return L.T("Nettoyage terminé.");
        });

    static string Describe(TempCleaner.CleanResult result) =>
        L.F("{0} libérés ({1} fichier(s)). ", Bytes.Format(result.FreedBytes), result.Deleted) +
        L.F("{0} laissé(s) en place : en cours d'utilisation, modifiés ou protégés.", result.Skipped);

    async void OnRepair(object sender, RoutedEventArgs e)
    {
        var command = (string)((Button)sender).Tag;
        await RunBusy(L.T("En attente de l'autorisation administrateur…"), async () =>
        {
            var report = await RunAdminAsync<RepairReport>(command);
            // The component cleanup lives on the "Nettoyage" page, the repairs on theirs.
            (command == AdminCommand.DismCleanup ? ComponentCleanupStatus : RepairStatus).Text = report.Summary;
            return report.ExitCode switch
            {
                0 => L.T("Terminé."),
                3010 => L.T("Terminé : redémarrez l'ordinateur."),
                _ => L.F("Terminé avec le code {0} : voir le résumé sur la page.", report.ExitCode),
            };
        });
    }

    async void OnBackup(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = L.T("Où enregistrer la sauvegarde des pilotes ?"),
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dialog.ShowDialog(this) != true) return;

        await RunBusy(L.T("En attente de l'autorisation administrateur…"), async () =>
        {
            var report = await RunAdminAsync<BackupReport>(AdminCommand.ExportDrivers, dialog.FolderName);
            BackupStatus.Text = L.F("{0} pilote(s) sauvegardé(s) dans {1}", report.Count, report.Folder);
            return L.T("Sauvegarde terminée.");
        });
    }

    async void OnRestoreBackup(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = L.T("Dossier de sauvegarde à restaurer") };
        if (dialog.ShowDialog(this) != true) return;
        if (Ask(L.F("Installer les pilotes de « {0} » ?\n\nUn point de restauration système sera créé avant.", dialog.FolderName)) != MessageBoxResult.Yes)
            return;

        await RunBusy(L.T("En attente de l'autorisation administrateur…"), async () =>
        {
            var report = await RunAdminAsync<ImportReport>(AdminCommand.ImportDrivers, dialog.FolderName);
            if (report.Cancelled) return L.T("Restauration annulée.");
            BackupStatus.Text = L.T("Pilotes restaurés.") + (report.RebootRequired ? L.T(" Redémarrez l'ordinateur pour terminer.") : "");
            await LoadHistory();
            return L.T("Restauration terminée.");
        });
    }
}
