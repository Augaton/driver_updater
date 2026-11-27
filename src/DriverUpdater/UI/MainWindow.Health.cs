using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace DriverUpdater.UI;

public enum SecurityLevel { Good, ToDo, Bad, Neutral }

/// <summary>
/// One line of the "Sécurité" page: what Windows reports, how it stands, and the page of
/// Windows where to act. The state is said in words too, never by the color alone.
/// </summary>
public sealed record SecurityRow(string Title, string Detail, SecurityLevel Level, string ActionText, string Target)
{
    public string Glyph => Level switch
    {
        SecurityLevel.Good => "", // check mark
        SecurityLevel.ToDo => "", // warning
        SecurityLevel.Bad => "", // cross
        _ => "", // info
    };

    /// <summary>The palette key of the state's color (UI/ThemeColor.cs).</summary>
    public string ColorKey => Level switch
    {
        SecurityLevel.Good => "Success",
        SecurityLevel.ToDo => "Caution",
        SecurityLevel.Bad => "Critical",
        _ => "InkMuted",
    };
}

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
            ShowSecurity(quick, askWindowsUpdate ? L.T("Recherche en cours…") : L.T("Pas encore demandées à Windows Update."));
            if (!askWindowsUpdate) return quick;
            var full = await Task.Run(() => SecurityStatus.Read(askWindowsUpdate: true));
            ShowSecurity(full, L.T("Windows Update n'a pas répondu."));
            return full;
        }
        catch (Exception e)
        {
            SecurityMessage.Text = L.T("Lecture impossible : ") + Errors.Describe(e);
            SecurityMessage.Visibility = Visibility.Visible;
            SecurityList.ItemsSource = null;
            return null;
        }
    }

    /// <summary>One line per antivirus, then the firewall and the Windows updates, each with where to act.</summary>
    void ShowSecurity(SecurityState state, string updatesUnknown)
    {
        const string Threats = "windowsdefender://threat", Network = "windowsdefender://network", Updates = "ms-settings:windowsupdate";
        var rows = new List<SecurityRow>();
        bool anyActive = state.Antiviruses.Any(a => a.Enabled);
        if (state.Antiviruses.Count == 0)
            rows.Add(new(L.T("Antivirus"), L.T("Aucun déclaré à Windows (Security Center absent, comme sur Windows Server)."),
                         SecurityLevel.Neutral, L.T("Protection antivirus"), Threats));
        foreach (var antivirus in state.Antiviruses)
            rows.Add(antivirus switch
            {
                { Enabled: true, UpToDate: true } => new(antivirus.Name, L.T("Actif, à jour."), SecurityLevel.Good, L.T("Protection antivirus"), Threats),
                { Enabled: true } => new(antivirus.Name, L.T("Actif, mais ses définitions de virus ne sont pas à jour."),
                                         SecurityLevel.ToDo, L.T("Protection antivirus"), Threats),
                _ when anyActive => new(antivirus.Name, L.T("Inactif : un autre antivirus protège le PC."),
                                        SecurityLevel.Neutral, L.T("Protection antivirus"), Threats),
                _ => new(antivirus.Name, L.T("Inactif : aucun antivirus ne protège le PC."), SecurityLevel.Bad, L.T("Protection antivirus"), Threats),
            });
        rows.Add(state.FirewallOn switch
        {
            true => new(L.T("Pare-feu Windows"), L.T("Actif."), SecurityLevel.Good, L.T("Pare-feu"), Network),
            _ when state.OtherFirewalls is { Count: > 0 } others =>
                new(string.Join(", ", others), L.T("Pare-feu actif : celui de Windows lui laisse la place."), SecurityLevel.Good, L.T("Pare-feu"), Network),
            false => new(L.T("Pare-feu Windows"), L.T("Désactivé."), SecurityLevel.Bad, L.T("Pare-feu"), Network),
            null => new(L.T("Pare-feu Windows"), L.T("État illisible."), SecurityLevel.Neutral, L.T("Pare-feu"), Network),
        });
        rows.Add(state.PendingUpdates switch
        {
            0 => new(L.T("Mises à jour de Windows"), L.T("Aucune en attente."), SecurityLevel.Good, L.T("Windows Update"), Updates),
            > 0 => new(L.T("Mises à jour de Windows"), L.F("{0} en attente.", state.PendingUpdates), SecurityLevel.ToDo, L.T("Windows Update"), Updates),
            _ => new(L.T("Mises à jour de Windows"), updatesUnknown, SecurityLevel.Neutral, L.T("Windows Update"), Updates),
        });
        if (state.RestartPending)
            rows.Add(new(L.T("Redémarrage en attente"), L.T("Windows attend un redémarrage pour finir d'installer des mises à jour : elles ne protègent qu'après."),
                         SecurityLevel.ToDo, L.T("Windows Update"), Updates));
        SecurityMessage.Visibility = Visibility.Collapsed;
        SecurityList.ItemsSource = rows;
    }

    /// <summary>Whether restore points can be made at all, read without administrator rights, and the way to turn them on.</summary>
    void ShowRestoreProtection()
    {
        var state = SystemInfo.ReadRestoreProtection();
        RestoreProtectionText.Text = state switch
        {
            RestoreProtection.On => L.T("Protection du système activée : Windows peut créer des points de restauration."),
            RestoreProtection.Off => L.T("Protection du système désactivée : aucun point de restauration ne peut être créé, pas même avant l'installation d'un pilote."),
            RestoreProtection.OffByPolicy => L.T("Protection du système coupée par une stratégie : si elle ne vient pas de votre entreprise, la page Faux boosts la retire."),
            _ => "",
        };
        RestoreProtectionText.Visibility = state == RestoreProtection.Unknown ? Visibility.Collapsed : Visibility.Visible;
        EnableRestoreButton.Visibility = state == RestoreProtection.Off ? Visibility.Visible : Visibility.Collapsed;
    }

    async void OnEnableRestore(object sender, RoutedEventArgs e) =>
        await RunBusy(L.T("En attente de l'autorisation administrateur…"), async () =>
        {
            var result = await RunAdminAsync<RestorePointResult>(AdminCommand.RestoreEnable);
            ShowRestoreProtection();
            if (result.Ok) Journal.Add(Journal.Maintenance, result.Message);
            return result.Message;
        });

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
        {
            var result = await RunAdminAsync<RestorePointResult>(AdminCommand.CreateRestorePoint);
            return result.Ok ? Logged(Journal.Maintenance, result.Message) : result.Message;
        });

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
            if (result.Deleted > 0) Journal.Add(Journal.Cleanup, L.T("Fichiers temporaires : ") + Describe(result));
            return L.T("Nettoyage terminé.");
        });

    async void OnCleanWindowsTemp(object sender, RoutedEventArgs e) =>
        await RunBusy(L.T("En attente de l'autorisation administrateur…"), async () =>
        {
            var result = await RunAdminAsync<TempCleaner.CleanResult>(AdminCommand.CleanWindowsTemp);
            TempStatus.Text = L.T("C:\\Windows\\Temp : ") + Describe(result);
            if (result.Deleted > 0) Journal.Add(Journal.Cleanup, TempStatus.Text);
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
            var outcome = report.ExitCode switch
            {
                0 => L.T("Terminé."),
                3010 => L.T("Terminé : redémarrez l'ordinateur."),
                _ => L.F("Terminé avec le code {0} : voir le résumé sur la page.", report.ExitCode),
            };
            Journal.Add(command == AdminCommand.DismCleanup ? Journal.Cleanup : Journal.Maintenance,
                L.F("{0} : {1}", (sender as Button)?.Content as string ?? command, outcome));
            return outcome;
        });
    }

    async void OnRepairWindows(object sender, RoutedEventArgs e) =>
        await RunBusy(L.T("En attente de l'autorisation administrateur…"), async () =>
        {
            var pass = await RunAdminAsync<RepairPass>(AdminCommand.RepairWindows);
            RepairStatus.Text = L.F("Image de Windows (DISM) :\n{0}\n\nFichiers système (SFC) :\n{1}", pass.Image.Summary, pass.Files.Summary);
            Journal.Add(Journal.Maintenance, L.F("{0} : {1}", L.T("Réparer Windows"), pass.Verdict));
            return pass.Verdict;
        });

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
            BackupStatus.Text = L.F("{0} pilote(s) sauvegardé(s) dans {1}", report.Count, report.Folder)
                                + (report.Failed.Count > 0 ? L.T(". Non sauvegardé(s) : ") + string.Join(", ", report.Failed) : "");
            return report.Failed.Count == 0 ? L.T("Sauvegarde terminée.") : L.F("Sauvegarde terminée, sauf {0} pilote(s).", report.Failed.Count);
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
            Journal.Add(Journal.Drivers, L.F("Pilotes réinstallés depuis « {0} ».", dialog.FolderName));
            await LoadHistory();
            return L.T("Restauration terminée.");
        });
    }
}
