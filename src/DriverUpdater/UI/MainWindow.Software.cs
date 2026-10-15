using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Microsoft.VisualBasic.FileIO;

namespace DriverUpdater.UI;

// "Logiciels" tab: updates through winget, installed programs, uninstalling and leftovers.
public partial class MainWindow
{
    List<SoftwareUpdate> software = [];
    List<InstalledProgram> programs = [];
    List<Leftover> leftovers = [];
    InstalledProgram? uninstalled; // the last program uninstalled here, whose leftovers we look for

    async void OnSearchSoftware(object sender, RoutedEventArgs e) =>
        await RunBusy("Recherche des mises à jour de logiciels…", async () =>
        {
            if (!WinGet.IsAvailable)
                return "winget est introuvable : installez « Programme d'installation d'application » depuis le Microsoft Store.";
            ShowSoftware(await WinGet.ListUpgradesAsync());
            return software.Count == 0 ? "Vos logiciels sont à jour." : $"{software.Count} logiciel(s) à mettre à jour.";
        });

    void ShowSoftware(List<SoftwareUpdate> list)
    {
        software = list;
        SoftwareGrid.ItemsSource = software;
        SoftwarePlaceholder.Text = "Aucun logiciel à mettre à jour.";
        SoftwarePlaceholder.Visibility = software.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateButtons();
    }

    async void OnUpgradeSoftware(object sender, RoutedEventArgs e)
    {
        var selected = software.Where(s => s.Selected).ToList();
        if (selected.Count == 0)
        {
            SetStatus("Cochez au moins un logiciel.");
            return;
        }

        await RunBusy("Mise à jour des logiciels…", async () =>
        {
            BusyBar.IsIndeterminate = false;
            BusyBar.Maximum = selected.Count;
            var done = new List<SoftwareUpdate>();
            var failed = new List<string>();
            for (int i = 0; i < selected.Count; i++)
            {
                var app = selected[i];
                SetStatus($"Logiciel {i + 1} sur {selected.Count} : {app.Name}…");
                int code = await WinGet.UpgradeAsync(app.Id);
                if (code == 0) done.Add(app);
                else failed.Add($"{app.Name} (0x{code:X8})");
                BusyBar.Value = i + 1;
            }
            ShowSoftware(software.Except(done).ToList());
            return failed.Count == 0
                ? $"{done.Count} logiciel(s) mis à jour."
                : $"{done.Count} sur {selected.Count} mis à jour. Échec : {string.Join(", ", failed)}.";
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
            SetStatus("Lecture des programmes installés impossible : " + Errors.Describe(e));
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
            SetStatus("Sélectionnez un programme dans la liste.");
            return;
        }
        if (Ask($"Désinstaller « {program.Name} » ?\n\nSon propre désinstalleur va s'ouvrir.") != MessageBoxResult.Yes) return;

        await RunBusy($"Désinstallation de {program.Name} : suivez son désinstalleur…", async () =>
        {
            try
            {
                using var process = InstalledPrograms.Uninstall(program);
                if (process is not null) await process.WaitForExitAsync();
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                return "Désinstallation annulée.";
            }
            uninstalled = program;
            await LoadPrograms();
            if (InstalledPrograms.StillInstalled(program))
                return $"{program.Name} est toujours installé (désinstallation annulée ou pas terminée). " +
                       "Une fois terminée, cliquez sur « Chercher les restes ».";
            await ShowLeftovers(program);
            return $"{program.Name} est désinstallé.";
        });
    }

    async void OnFindLeftovers(object sender, RoutedEventArgs e)
    {
        if (uninstalled is null)
        {
            SetStatus("Désinstallez d'abord un programme ici : ses restes seront alors cherchés.");
            return;
        }
        if (InstalledPrograms.StillInstalled(uninstalled))
        {
            SetStatus($"{uninstalled.Name} est encore installé : ses dossiers servent toujours.");
            return;
        }
        await ShowLeftovers(uninstalled);
    }

    async Task ShowLeftovers(InstalledProgram program)
    {
        leftovers = await Task.Run(() => Leftovers.Find(program.Name, program.Publisher, program.InstallLocation, LeftoverRoots.Current));
        LeftoverList.ItemsSource = leftovers;
        LeftoverHeading.Text = leftovers.Count == 0
            ? $"« {program.Name} » n'a laissé aucun dossier reconnaissable."
            : $"Dossiers laissés par « {program.Name} » :";
        RecycleButton.Visibility = leftovers.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        LeftoverCard.Visibility = Visibility.Visible;
    }

    async void OnRecycleLeftovers(object sender, RoutedEventArgs e)
    {
        var selected = leftovers.Where(l => l.Selected).ToList();
        if (selected.Count == 0)
        {
            SetStatus("Cochez au moins un dossier.");
            return;
        }
        if (Ask($"Mettre {selected.Count} dossier(s) à la corbeille ?\n\n" + string.Join("\n", selected.Select(l => l.Path)))
            != MessageBoxResult.Yes)
            return;

        await RunBusy("Mise à la corbeille…", async () =>
        {
            // The user's own folders need no rights; done here, on the window's thread,
            // because the Recycle Bin's dialogs need it.
            foreach (var leftover in selected.Where(l => !l.NeedsAdmin))
                if (Leftovers.IsSafeToRecycle(leftover.Path, LeftoverRoots.Current, out _))
                    FileSystem.DeleteDirectory(leftover.Path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            var machine = selected.Where(l => l.NeedsAdmin).Select(l => l.Path).ToArray();
            if (machine.Length > 0)
            {
                SetStatus("En attente de l'autorisation administrateur…");
                await RunAdminAsync<int>(AdminCommand.Recycle, machine);
            }
            leftovers = leftovers.Except(selected).ToList();
            LeftoverList.ItemsSource = leftovers;
            RecycleButton.Visibility = leftovers.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            return $"{selected.Count} dossier(s) mis à la corbeille : vous pouvez les y récupérer.";
        });
    }
}
