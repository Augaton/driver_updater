using System.Windows;

namespace DriverUpdater.UI;

// "Pilotes récents" tab: makers' drivers from the Microsoft Update Catalog.
public partial class MainWindow
{
    List<CatalogDriver> catalogDrivers = [];

    async void OnSearchCatalog(object sender, RoutedEventArgs e) =>
        await RunBusy("Recherche dans le catalogue Microsoft Update…", async () =>
        {
            SetStatus("Inventaire des périphériques…");
            var devices = await LoadInventory();
            ShowCatalog(await DriverCatalog.FindAsync(devices, new Progress<string>(SetStatus)));
            return catalogDrivers.Count == 0
                ? "Le catalogue n'a pas de pilote plus récent que les vôtres."
                : $"{catalogDrivers.Count} pilote(s) plus récent(s) dans le catalogue.";
        });

    void ShowCatalog(List<CatalogDriver> list)
    {
        catalogDrivers = list;
        CatalogGrid.ItemsSource = catalogDrivers;
        CatalogPlaceholder.Text = "Aucun pilote plus récent dans le catalogue.";
        CatalogPlaceholder.Visibility = catalogDrivers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateButtons();
    }

    async void OnInstallCatalog(object sender, RoutedEventArgs e)
    {
        var selected = catalogDrivers.Where(d => d.Selected).ToList();
        if (selected.Count == 0)
        {
            SetStatus("Cochez au moins un pilote.");
            return;
        }
        long size = selected.Sum(d => d.Entry.Size);
        if (Ask($"Installer {selected.Count} pilote(s) du catalogue ({Bytes.Format(size)} à télécharger) ?\n\n" +
                "Un point de restauration système sera créé avant. Windows garde le pilote actuel s'il est plus adapté à votre PC.")
            != MessageBoxResult.Yes)
            return;

        await RunBusy("En attente de l'autorisation administrateur…", async () =>
        {
            var report = await RunAdminAsync<CatalogInstallReport>(AdminCommand.InstallCatalog,
                selected.Select(d => d.Entry.UpdateId).ToArray());
            if (report.Cancelled) return "Installation annulée.";

            var byId = selected.ToDictionary(d => d.Entry.UpdateId, StringComparer.OrdinalIgnoreCase);
            var lines = report.Results.Select(r =>
                $"{(r.Succeeded ? "✔" : "✘")} {(byId.TryGetValue(r.UpdateId, out var d) ? $"{d.Device} {d.Entry.Version}" : r.UpdateId)} : {r.Outcome}");
            bool reboot = report.Results.Any(r => r.RebootRequired);
            MessageBox.Show(this, report.RestorePoint + "\n\n" + string.Join("\n", lines) +
                (reboot ? "\n\nRedémarrez l'ordinateur pour terminer." : ""), "Résultat de l'installation",
                MessageBoxButton.OK, MessageBoxImage.Information);

            var done = report.Results.Where(r => r.Succeeded).Select(r => r.UpdateId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            ShowCatalog(catalogDrivers.Where(d => !done.Contains(d.Entry.UpdateId)).ToList());
            return $"{done.Count} pilote(s) sur {selected.Count} traité(s)." + (reboot ? " Redémarrage requis." : "");
        });
    }
}
