using System.Windows;

namespace DriverUpdater;

// "Logiciels" tab: updates through winget.
public partial class MainWindow
{
    List<SoftwareUpdate> software = [];

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
}
