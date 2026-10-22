using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace DriverUpdater.UI;

// "Logiciels" tab: updates through winget, installed programs, uninstalling and leftovers.
public partial class MainWindow
{
    List<SoftwareUpdate> software = [];
    List<InstalledProgram> programs = [];
    List<Leftover> leftovers = [];
    InstalledProgram? uninstalled; // the last program uninstalled here, whose leftovers we look for

    async void OnSearchSoftware(object sender, RoutedEventArgs e) =>
        await RunBusy(L.T("Recherche des mises à jour de logiciels…"), async () =>
        {
            if (!WinGet.IsAvailable)
                return L.T("winget est introuvable : installez « Programme d'installation d'application » depuis le Microsoft Store.");
            ShowSoftware(await SoftwareUpgrades(fresh: true));
            return software.Count == 0 ? L.T("Vos logiciels sont à jour.") : L.F("{0} logiciel(s) à mettre à jour.", software.Count);
        });

    Task<List<SoftwareUpdate>>? upgrades;
    DateTime upgradesAt;

    /// <summary>
    /// What winget can update. It takes several seconds, and the dashboard and the game
    /// components ask for the same list: one run serves both for two minutes. A search the
    /// user asks for, or an update just done, always asks again.
    /// </summary>
    Task<List<SoftwareUpdate>> SoftwareUpgrades(bool fresh = false)
    {
        if (fresh || upgrades is null || upgrades.IsFaulted || upgrades.IsCanceled || DateTime.UtcNow - upgradesAt > TimeSpan.FromMinutes(2))
        {
            upgrades = WinGet.ListUpgradesAsync();
            upgradesAt = DateTime.UtcNow;
        }
        return upgrades;
    }

    void ShowSoftware(List<SoftwareUpdate> list)
    {
        software = list;
        SoftwareGrid.ItemsSource = software;
        SoftwarePlaceholder.Text = L.T("Aucun logiciel à mettre à jour.");
        SoftwarePlaceholder.Visibility = software.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateButtons();
    }

    async void OnUpgradeSoftware(object sender, RoutedEventArgs e)
    {
        var selected = software.Where(s => s.Selected).ToList();
        if (selected.Count == 0)
        {
            SetStatus(L.T("Cochez au moins un logiciel."));
            return;
        }

        await RunBusy(L.T("Mise à jour des logiciels…"), async () =>
        {
            BusyBar.IsIndeterminate = false;
            BusyBar.Maximum = selected.Count;
            var done = new List<SoftwareUpdate>();
            var failed = new List<string>();
            for (int i = 0; i < selected.Count; i++)
            {
                var app = selected[i];
                SetStatus(L.F("Logiciel {0} sur {1} : {2}…", i + 1, selected.Count, app.Name));
                int code = await WinGet.UpgradeAsync(app.Id);
                if (code == 0)
                {
                    done.Add(app);
                    Journal.Add(Journal.Software, L.F("{0} : {1} → {2}", app.Name, app.Version, app.Available));
                }
                else
                {
                    failed.Add($"{app.Name} (0x{code:X8})");
                }
                BusyBar.Value = i + 1;
            }
            ShowSoftware(software.Except(done).ToList());
            upgrades = null; // the next look asks winget again
            return failed.Count == 0
                ? L.F("{0} logiciel(s) mis à jour.", done.Count)
                : L.F("{0} sur {1} mis à jour. Échec : {2}.", done.Count, selected.Count, string.Join(", ", failed));
        });
    }

    async Task LoadPrograms()
    {
        try
        {
            programs = await Task.Run(InstalledPrograms.List);
            ApplyProgramFilter();
        }
        catch (Exception e)
        {
            SetStatus(L.T("Lecture des programmes installés impossible : ") + Errors.Describe(e));
        }
    }

    void OnProgramFilterChanged(object sender, TextChangedEventArgs e) => ApplyProgramFilter();

    void ApplyProgramFilter()
    {
        var filter = ProgramFilter.Text.Trim();
        ProgramGrid.ItemsSource = filter.Length == 0
            ? programs
            : programs.Where(p => p.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
                                  || p.Publisher.Contains(filter, StringComparison.CurrentCultureIgnoreCase)).ToList();
    }

    async void OnUninstall(object sender, RoutedEventArgs e)
    {
        if (ProgramGrid.SelectedItem is not InstalledProgram program)
        {
            SetStatus(L.T("Sélectionnez un programme dans la liste."));
            return;
        }
        if (Ask(L.F("Désinstaller « {0} » ?\n\nSon propre désinstalleur va s'ouvrir.", program.Name)) != MessageBoxResult.Yes) return;

        await RunBusy(L.F("Désinstallation de {0} : suivez son désinstalleur…", program.Name), async () =>
        {
            try
            {
                using var process = InstalledPrograms.Uninstall(program);
                if (process is not null) await process.WaitForExitAsync();
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                return L.T("Désinstallation annulée.");
            }
            uninstalled = program;
            await LoadPrograms();
            if (InstalledPrograms.StillInstalled(program))
                return L.F("{0} est toujours installé (désinstallation annulée ou pas terminée). ", program.Name) +
                       L.T("Une fois terminée, cliquez sur « Chercher les restes ».");
            await ShowLeftovers(program);
            return Logged(Journal.Software, L.F("{0} est désinstallé.", program.Name));
        });
    }

    async void OnFindLeftovers(object sender, RoutedEventArgs e)
    {
        if (uninstalled is null)
        {
            SetStatus(L.T("Désinstallez d'abord un programme ici : ses restes seront alors cherchés."));
            return;
        }
        if (InstalledPrograms.StillInstalled(uninstalled))
        {
            SetStatus(L.F("{0} est encore installé : ses dossiers servent toujours.", uninstalled.Name));
            return;
        }
        await ShowLeftovers(uninstalled);
    }

    async Task ShowLeftovers(InstalledProgram program)
    {
        leftovers = await Task.Run(() => Leftovers.Find(program.Name, program.Publisher, program.InstallLocation, LeftoverRoots.Current,
            InstalledPrograms.List().Select(p => p.InstallLocation)));
        LeftoverList.ItemsSource = leftovers;
        LeftoverHeading.Text = leftovers.Count == 0
            ? L.F("« {0} » n'a laissé aucun dossier reconnaissable.", program.Name)
            : L.F("Dossiers laissés par « {0} » :", program.Name);
        RecycleButton.Visibility = leftovers.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        LeftoverCard.Visibility = Visibility.Visible;
    }

    async void OnRecycleLeftovers(object sender, RoutedEventArgs e)
    {
        var selected = leftovers.Where(l => l.Selected).ToList();
        if (selected.Count == 0)
        {
            SetStatus(L.T("Cochez au moins un dossier."));
            return;
        }
        if (Ask(L.F("Mettre {0} dossier(s) à la corbeille ?\n\n", selected.Count) + string.Join("\n", selected.Select(l => l.Path)))
            != MessageBoxResult.Yes)
            return;

        await RunBusy(L.T("Mise à la corbeille…"), async () =>
        {
            // The user's own folders need no rights; done here, on the window's thread,
            // because the Recycle Bin's dialogs need it.
            var owner = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            int recycled = 0;
            foreach (var leftover in selected.Where(l => !l.NeedsAdmin))
                if (Leftovers.IsSafeToRecycle(leftover.Path, LeftoverRoots.Current, out _) && NativeFiles.Recycle(leftover.Path, owner))
                    recycled++;
            var machine = selected.Where(l => l.NeedsAdmin).Select(l => l.Path).ToArray();
            if (machine.Length > 0)
            {
                SetStatus(L.T("En attente de l'autorisation administrateur…"));
                recycled += await RunAdminAsync<int>(AdminCommand.Recycle, machine);
            }
            leftovers = leftovers.Where(l => System.IO.Directory.Exists(l.Path)).ToList();
            LeftoverList.ItemsSource = leftovers;
            RecycleButton.Visibility = leftovers.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (recycled > 0) Journal.Add(Journal.Cleanup, L.F("{0} dossier(s) mis à la corbeille : vous pouvez les y récupérer.", recycled));
            return recycled == selected.Count
                ? L.F("{0} dossier(s) mis à la corbeille : vous pouvez les y récupérer.", recycled)
                : L.F("{0} dossier(s) sur {1} mis à la corbeille ; les autres sont restés en place.", recycled, selected.Count);
        });
    }
}
