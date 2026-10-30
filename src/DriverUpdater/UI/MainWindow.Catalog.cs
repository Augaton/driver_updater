using System.Windows;

namespace DriverUpdater.UI;

// "Pilotes récents" tab: makers' drivers from the Microsoft Update Catalog.
public partial class MainWindow
{
    List<CatalogDriver> catalogDrivers = [];

    async void OnSearchCatalog(object sender, RoutedEventArgs e) =>
        await RunBusy(L.T("Recherche dans le catalogue Microsoft Update…"), async stop =>
        {
            SetStatus(L.T("Inventaire des périphériques…"));
            var devices = await LoadInventory(required: true);
            ShowCatalog(await DriverCatalog.FindAsync(devices, new Progress<string>(SetStatus), cancel: stop));
            return catalogDrivers.Count == 0
                ? L.T("Le catalogue n'a pas de pilote plus récent que les vôtres.")
                : L.F("{0} pilote(s) plus récent(s) dans le catalogue.", catalogDrivers.Count);
        });

    void ShowCatalog(List<CatalogDriver> list)
    {
        catalogDrivers = list;
        CatalogGrid.ItemsSource = catalogDrivers;
        CatalogPlaceholder.Text = L.T("Aucun pilote plus récent dans le catalogue.");
        CatalogPlaceholder.Visibility = catalogDrivers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateButtons();
    }

    async void OnInstallCatalog(object sender, RoutedEventArgs e)
    {
        var selected = catalogDrivers.Where(d => d.Selected).ToList();
        if (selected.Count == 0)
        {
            SetStatus(L.T("Cochez au moins un pilote."));
            return;
        }
        long size = selected.Sum(d => d.Entry.Size);
        if (Ask(L.F("Installer {0} pilote(s) du catalogue ({1} à télécharger) ?\n\n", selected.Count, Bytes.Format(size)) +
                L.T("Un point de restauration système sera créé avant. Windows garde le pilote actuel s'il est plus adapté à votre PC."))
            != MessageBoxResult.Yes)
            return;

        await RunBusy(L.T("En attente de l'autorisation administrateur…"), async () =>
        {
            var report = await RunAdminAsync<CatalogInstallReport>(AdminCommand.InstallCatalog,
                selected.Select(d => d.Entry.UpdateId).ToArray());
            if (report.Cancelled) return L.T("Installation annulée.");

            var byId = selected.GroupBy(d => d.Entry.UpdateId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            string Name(string id) => byId.TryGetValue(id, out var d) ? $"{d.Device} {d.Entry.Version}" : id;
            var lines = report.Results.Select(r => L.F("{0} {1} : {2}", r.Succeeded ? "✔" : "✘", Name(r.UpdateId), r.Outcome));
            foreach (var r in report.Results.Where(r => r.Succeeded)) Journal.Add(Journal.Drivers, L.F("{0} : {1}", Name(r.UpdateId), r.Outcome));
            bool reboot = report.Results.Any(r => r.RebootRequired);
            MessageBox.Show(this, report.RestorePoint + "\n\n" + string.Join("\n", lines) +
                (reboot ? L.T("\n\nRedémarrez l'ordinateur pour terminer.") : ""), L.T("Résultat de l'installation"),
                MessageBoxButton.OK, MessageBoxImage.Information);

            var done = await ForgetInstalled(report.Results);
            return L.F("{0} pilote(s) sur {1} traité(s).", done.Count, selected.Count) + (reboot ? L.T(" Redémarrage requis.") : "");
        });
    }

    async void OnHideCatalog(object sender, RoutedEventArgs e)
    {
        var selected = catalogDrivers.Where(d => d.Selected).ToList();
        if (selected.Count == 0)
        {
            SetStatus(L.T("Cochez au moins un pilote."));
            return;
        }
        // The version shown, only: a newer one, published later, is offered again.
        var ids = selected.Select(d => d.Entry.UpdateId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Settings.CatalogHidden = Settings.CatalogHidden.Union(ids, StringComparer.OrdinalIgnoreCase).ToHashSet();
        foreach (var d in selected) Journal.Add(Journal.Drivers, L.F("{0} : {1}", $"{d.Device} {d.Entry.Version}", L.T("masqué dans les pilotes des fabricants")));
        await ForgetInstalled(ids, rescan: false);
        SetStatus(L.F("{0} pilote(s) masqué(s).", selected.Count));
    }

    void OnUnhideCatalog(object sender, RoutedEventArgs e)
    {
        int count = Settings.CatalogHidden.Count + Settings.CatalogKept.Count;
        Settings.CatalogHidden = new HashSet<string>();
        Settings.CatalogKept = new HashSet<string>();
        if (count > 0) Journal.Add(Journal.Drivers, L.F("{0} pilote(s) des fabricants réaffiché(s).", count));
        SetStatus(count == 0 ? L.T("Aucun pilote masqué.") : L.F("{0} pilote(s) réaffiché(s) : relancez la recherche pour les voir.", count));
    }
}
