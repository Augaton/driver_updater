using System.Management;
using System.Windows;

namespace DriverUpdater;

// "Pilotes" and "Historique" tabs.
public partial class MainWindow
{
    List<DriverUpdate> drivers = [];

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
            ProblemStatus.Text = "Lecture impossible : " + Errors.Describe(e);
        }
    }

    async Task LoadHardware()
    {
        try
        {
            HardwareList.ItemsSource = await Task.Run(() => new[] { SystemInfo.Computer() }.Concat(SystemInfo.Gpus()).ToList());
        }
        catch (Exception e)
        {
            HardwareStatus.Text = "Lecture impossible : " + Errors.Describe(e);
        }
    }

    async Task LoadHistory()
    {
        try
        {
            var history = await Task.Run(() => WindowsUpdateDrivers.History());
            HistoryGrid.ItemsSource = history;
            HistoryPlaceholder.Text = "Windows Update n'a encore installé aucun pilote.";
            HistoryPlaceholder.Visibility = history.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception e)
        {
            HistoryPlaceholder.Text = "Lecture impossible : " + Errors.Describe(e);
        }
    }

    async void OnRefreshHistory(object sender, RoutedEventArgs e) => await LoadHistory();

    void LoadWeeklyCheck()
    {
        try
        {
            WeeklyCheckBox.IsChecked = WeeklyCheck.IsEnabled();
        }
        catch (Exception e)
        {
            WeeklyCheckBox.IsEnabled = false;
            WeeklyCheckNote.Text = "Planificateur de tâches indisponible : " + Errors.Describe(e);
        }
    }

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
            SetStatus("Erreur : " + Errors.Describe(ex));
        }
    }

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

        await RunBusy("En attente de l'autorisation administrateur…", async () =>
        {
            var report = await RunAdminAsync<InstallReport>(AdminCommand.Install, selected.Select(d => d.UpdateId).ToArray());
            if (report.Cancelled) return "Installation annulée.";

            var installed = report.Results.Where(r => r.Succeeded).Select(r => r.UpdateId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            await ShowUpdates(drivers.Where(d => !installed.Contains(d.UpdateId)).ToList());
            await LoadHistory();

            bool rebootRequired = report.Results.Any(r => r.RebootRequired);
            var lines = report.Results.Select(r => r.Succeeded ? "✔ " + r.Title : $"✘ {r.Title} ({r.Detail})");
            var text = report.RestorePoint + "\n\n" + string.Join("\n", lines);
            if (rebootRequired) text += "\n\nRedémarrez l'ordinateur pour terminer l'installation.";
            MessageBox.Show(this, text, "Résultat de l'installation", MessageBoxButton.OK, MessageBoxImage.Information);

            return $"{installed.Count} pilote(s) sur {selected.Count} installé(s)." + (rebootRequired ? " Redémarrage requis." : "");
        });
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

        await RunBusy("En attente de l'autorisation administrateur…", async () =>
        {
            int hidden = await RunAdminAsync<int>(AdminCommand.Hide, selected.Select(d => d.UpdateId).ToArray());
            await ShowUpdates(drivers.Except(selected).ToList());
            return $"{hidden} pilote(s) masqué(s).";
        });
    }

    async void OnUnhide(object sender, RoutedEventArgs e) =>
        await RunBusy("En attente de l'autorisation administrateur…", async () =>
        {
            int count = await RunAdminAsync<int>(AdminCommand.Unhide);
            return count == 0 ? "Aucun pilote masqué." : $"{count} pilote(s) réaffiché(s) : relancez la recherche pour les voir.";
        });
}
