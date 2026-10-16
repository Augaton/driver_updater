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
        ProblemHeading.Text = L.F("Périphériques en erreur ({0})", devices.Count);
        ProblemStatus.Text = L.T("Aucun : tous les périphériques fonctionnent.");
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
                L.F("{0} de mémoire, {1} cœurs logiques", Bytes.Format((long)memory.TotalPhysicalMemory), Environment.ProcessorCount),
            }.Where(part => part.Length > 0));
        }
        catch (Exception e)
        {
            HardwareStatus.Text = PcSummary.Text = L.T("Lecture du matériel impossible : ") + Errors.Describe(e);
        }
    }

    async Task LoadHistory()
    {
        try
        {
            var history = await Task.Run(() => WindowsUpdateDrivers.History());
            HistoryGrid.ItemsSource = history;
            HistoryPlaceholder.Text = L.T("Windows Update n'a encore installé aucun pilote.");
            HistoryPlaceholder.Visibility = history.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception e)
        {
            HistoryPlaceholder.Text = L.T("Lecture impossible : ") + Errors.Describe(e);
        }
    }

    async void OnRefreshHistory(object sender, RoutedEventArgs e) => await LoadHistory();

    async Task LoadWeeklyCheck()
    {
        try
        {
            // Task Scheduler answers slowly: never on the window's thread while it opens.
            bool enabled = await Task.Run(() =>
            {
                bool on = WeeklyCheck.IsEnabled();
                if (on) WeeklyCheck.FollowThisCopy(Environment.ProcessPath!);
                return on;
            });
            WeeklyCheckBox.IsChecked = enabled;
            WeeklyNvidiaBox.IsEnabled = WeeklySoftwareBox.IsEnabled = enabled;
        }
        catch (Exception e)
        {
            WeeklyCheckBox.IsEnabled = false;
            WeeklyCheckNote.Text = L.T("Planificateur de tâches indisponible : ") + Errors.Describe(e);
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
                ? L.T("Vérification activée : chaque mercredi à midi, ou au démarrage suivant si le PC était éteint.")
                : L.T("Vérification hebdomadaire désactivée."));
        }
        catch (Exception ex)
        {
            WeeklyCheckBox.IsChecked = !enable;
            SetStatus(L.T("Erreur : ") + Errors.Describe(ex));
        }
        WeeklyNvidiaBox.IsEnabled = WeeklySoftwareBox.IsEnabled = WeeklyCheckBox.IsChecked == true;
    }

    async void OnSearch(object sender, RoutedEventArgs e) =>
        await RunBusy(L.T("Recherche sur Windows Update, cela peut prendre quelques minutes…"), async () =>
        {
            var found = await Task.Run(WindowsUpdateDrivers.Search);
            await ShowUpdates(found);
            return drivers.Count == 0 ? L.T("Vos pilotes sont à jour.") : L.F("{0} mise(s) à jour trouvée(s).", drivers.Count);
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
        DriverPlaceholder.Text = L.T("Aucune mise à jour de pilote disponible.");
        DriverPlaceholder.Visibility = drivers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateButtons();
        if (status is not null) SetStatus(status);
    }

    async void OnInstall(object sender, RoutedEventArgs e)
    {
        var selected = drivers.Where(d => d.Selected).ToList();
        if (selected.Count == 0)
        {
            SetStatus(L.T("Cochez au moins un pilote."));
            return;
        }
        if (Ask(L.F("Installer {0} pilote(s) ?\n\nUn point de restauration système sera créé avant.", selected.Count)) != MessageBoxResult.Yes)
            return;

        await RunBusy(L.T("En attente de l'autorisation administrateur…"), async () =>
        {
            var report = await RunAdminAsync<InstallReport>(AdminCommand.Install, selected.Select(d => d.UpdateId).ToArray());
            if (report.Cancelled) return L.T("Installation annulée.");

            var installed = report.Results.Where(r => r.Succeeded).Select(r => r.UpdateId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            await ShowUpdates(drivers.Where(d => !installed.Contains(d.UpdateId)).ToList());
            await LoadHistory();

            bool rebootRequired = report.Results.Any(r => r.RebootRequired);
            var lines = report.Results.Select(r => r.Succeeded ? "✔ " + r.Title : $"✘ {r.Title} ({r.Detail})");
            var text = report.RestorePoint + "\n\n" + string.Join("\n", lines);
            if (rebootRequired) text += L.T("\n\nRedémarrez l'ordinateur pour terminer l'installation.");
            MessageBox.Show(this, text, L.T("Résultat de l'installation"), MessageBoxButton.OK, MessageBoxImage.Information);

            return L.F("{0} pilote(s) sur {1} installé(s).", installed.Count, selected.Count) + (rebootRequired ? L.T(" Redémarrage requis.") : "");
        });
    }

    async void OnHide(object sender, RoutedEventArgs e)
    {
        var selected = drivers.Where(d => d.Selected).ToList();
        if (selected.Count == 0)
        {
            SetStatus(L.T("Cochez au moins un pilote."));
            return;
        }
        if (Ask(L.F("Masquer {0} pilote(s) ?\n\nWindows Update ne les proposera plus, ici comme dans les Paramètres. ", selected.Count) +
                L.T("« Réafficher les pilotes masqués » les fait revenir.")) != MessageBoxResult.Yes)
            return;

        await RunBusy(L.T("En attente de l'autorisation administrateur…"), async () =>
        {
            int hidden = await RunAdminAsync<int>(AdminCommand.Hide, selected.Select(d => d.UpdateId).ToArray());
            await ShowUpdates(drivers.Except(selected).ToList());
            return L.F("{0} pilote(s) masqué(s).", hidden);
        });
    }

    async void OnUnhide(object sender, RoutedEventArgs e) =>
        await RunBusy(L.T("En attente de l'autorisation administrateur…"), async () =>
        {
            int count = await RunAdminAsync<int>(AdminCommand.Unhide);
            return count == 0 ? L.T("Aucun pilote masqué.") : L.F("{0} pilote(s) réaffiché(s) : relancez la recherche pour les voir.", count);
        });
}
