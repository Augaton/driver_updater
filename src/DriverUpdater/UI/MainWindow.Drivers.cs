using System.Windows;

namespace DriverUpdater.UI;

// "Pilotes" and "Historique" tabs.
public partial class MainWindow
{
    List<DriverUpdate> drivers = [];

    // From the inventory already read, rather than another pass over WMI.
    void ShowProblemDevices()
    {
        var devices = inventory
            .Where(d => d.NeedsAttention && d.Problem != 45) // 45: unplugged right now, not a problem
            .Select(d => new ProblemDevice(d.Name, SystemInfo.ErrorMeaning(d.Problem)))
            .ToList();
        ProblemList.ItemsSource = devices;
        ProblemHeading.Text = $"Périphériques en erreur ({devices.Count})";
        ProblemStatus.Text = "Aucun : tous les périphériques fonctionnent.";
        ProblemStatus.Visibility = devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The PC, its BIOS and graphics cards, read once for the dashboard and this tab.</summary>
    async Task LoadHardware()
    {
        try
        {
            var (computer, gpus) = await Task.Run(() => (SystemInfo.Computer(), SystemInfo.Gpus()));
            // The graphics cards have their own page, with their driver.
            HardwareList.ItemsSource = new[] { computer };
            PcTitle.Text = computer.Name;
            var memory = new Microsoft.VisualBasic.Devices.ComputerInfo();
            PcSummary.Text = string.Join("\n", new[]
            {
                computer.Detail,
                string.Join(" + ", gpus.Select(g => g.Name)),
                $"{Bytes.Format((long)memory.TotalPhysicalMemory)} de mémoire, {Environment.ProcessorCount} cœurs logiques",
            }.Where(part => part.Length > 0));
        }
        catch (Exception e)
        {
            HardwareStatus.Text = PcSummary.Text = "Lecture du matériel impossible : " + Errors.Describe(e);
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
            bool enabled = WeeklyCheck.IsEnabled();
            WeeklyCheckBox.IsChecked = enabled;
            if (enabled) WeeklyCheck.FollowThisCopy(Environment.ProcessPath!);
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
        var installed = InstalledDrivers.ByHardwareId(inventory.Count > 0 ? inventory : await LoadInventory());
        foreach (var driver in found)
            if (driver.HardwareId is { } id && installed.TryGetValue(id, out var match))
                driver.Installed = match.ToString();
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
