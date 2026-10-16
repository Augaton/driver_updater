using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace DriverUpdater.UI;

/// <summary>An old driver package offered for deletion, with its tick box.</summary>
public sealed class StoreChoice(StorePackage package)
{
    public StorePackage Package { get; } = package;
    public bool Selected { get; set; } = true;
}

// "Inventaire" tab, the driver store, and the live device watch.
public partial class MainWindow
{
    List<DeviceDriver> inventory = [];
    List<StoreChoice> storeChoices = [];
    List<DeviceDriver> newDevicesWithoutDriver = [];
    DeviceWatcher? watcher;
    DispatcherTimer? deviceSettle;
    DateTime lastDeviceLook;

    async Task<List<DeviceDriver>> LoadInventory()
    {
        try
        {
            inventory = await Task.Run(DriverInventory.Scan);
            ShowInventory();
            ShowProblemDevices();
        }
        catch (Exception e)
        {
            InventorySummary.Text = L.T("Lecture impossible : ") + Errors.Describe(e);
        }
        return inventory;
    }

    void ShowInventory()
    {
        var today = DateTime.Today;
        IEnumerable<DeviceDriver> shown = InventoryFilter.SelectedIndex switch
        {
            1 => inventory.Where(d => d.NeedsAttention),
            2 => inventory.Where(d => !d.HasDriver),
            3 => inventory.Where(d => d.IsOlderThan(3, today)),
            4 => inventory.Where(d => d.Signed == false),
            5 => inventory.Where(d => d.HasDriver && !d.IsMicrosoft),
            _ => inventory,
        };
        InventoryGrid.ItemsSource = shown.ToList();
        InventorySummary.Text =
            L.F("{0} périphériques  ·  {1} à problème  ·  ", inventory.Count, inventory.Count(d => d.NeedsAttention)) +
            L.F("{0} sans pilote  ·  {1} pilotes de plus de 3 ans  ·  ", inventory.Count(d => !d.HasDriver), inventory.Count(d => d.IsOlderThan(3, today))) +
            L.F("{0} non signés", inventory.Count(d => d.Signed == false));
    }

    void OnInventoryFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded) ShowInventory();
    }

    async void OnRefreshInventory(object sender, RoutedEventArgs e) => await LoadInventory();

    /// <summary>Device Manager's "Roll back driver" for the selected device.</summary>
    async void OnRollback(object sender, RoutedEventArgs e)
    {
        if (InventoryGrid.SelectedItem is not DeviceDriver device)
        {
            SetStatus(L.T("Sélectionnez un périphérique dans la liste."));
            return;
        }
        if (!device.HasDriver)
        {
            SetStatus(L.F("{0} n'a pas de pilote : rien à remettre.", device.Name));
            return;
        }
        if (Ask(L.F("Remettre le pilote précédent de « {0} » ?\n\nWindows reprend la version qu'il utilisait avant la dernière ", device.Name) +
                L.T("mise à jour, s'il l'a gardée. Utile quand un nouveau pilote cause des problèmes.")) != MessageBoxResult.Yes)
            return;

        await RunBusy(L.T("En attente de l'autorisation administrateur…"), async () =>
        {
            bool reboot = await RunAdminAsync<bool>(AdminCommand.Rollback, device.DeviceId);
            await LoadInventory();
            await LoadHistory();
            return L.F("{0} : pilote précédent remis.", device.Name) + (reboot ? L.T(" Redémarrez l'ordinateur pour terminer.") : "");
        });
    }

    // ---- Driver store ------------------------------------------------------------------

    async void OnStoreScan(object sender, RoutedEventArgs e) =>
        await RunBusy(L.T("En attente de l'autorisation administrateur…"), async () =>
        {
            var removable = await RunAdminAsync<List<StorePackage>>(AdminCommand.DriverStore);
            storeChoices = removable.Select(p => new StoreChoice(p)).ToList();
            StoreGrid.ItemsSource = storeChoices;
            StoreGrid.Visibility = storeChoices.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateStoreSummary();
            return storeChoices.Count == 0 ? L.T("Aucune ancienne version à supprimer.") : L.F("{0} ancienne(s) version(s) trouvée(s).", storeChoices.Count);
        });

    void OnStoreChoiceChanged(object sender, RoutedEventArgs e) => UpdateStoreSummary();

    void UpdateStoreSummary()
    {
        var chosen = storeChoices.Where(c => c.Selected).ToList();
        StoreSummary.Text = storeChoices.Count == 0
            ? L.T("Aucune ancienne version à supprimer : le magasin de pilotes est propre.")
            : L.F("{0} sur {1} cochée(s) : {2} à libérer.", chosen.Count, storeChoices.Count, Bytes.Format(chosen.Sum(c => c.Package.Size)));
        UpdateButtons();
    }

    async void OnStoreDelete(object sender, RoutedEventArgs e)
    {
        var chosen = storeChoices.Where(c => c.Selected).ToList();
        if (chosen.Count == 0) return;
        if (Ask(L.F("Supprimer {0} ancienne(s) version(s) de pilotes ({1}) ?\n\n", chosen.Count, Bytes.Format(chosen.Sum(c => c.Package.Size))) +
                L.T("Windows refuse de supprimer un pilote utilisé. La version actuelle de chaque pilote reste en place."))
            != MessageBoxResult.Yes)
            return;

        await RunBusy(L.T("En attente de l'autorisation administrateur…"), async () =>
        {
            var results = await RunAdminAsync<List<StoreDeleteResult>>(AdminCommand.DeleteDrivers,
                chosen.Select(c => c.Package.PublishedName).ToArray());
            var deleted = results.Where(r => r.Deleted).Select(r => r.PublishedName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            long freed = chosen.Where(c => deleted.Contains(c.Package.PublishedName)).Sum(c => c.Package.Size);
            storeChoices = storeChoices.Where(c => !deleted.Contains(c.Package.PublishedName)).ToList();
            StoreGrid.ItemsSource = storeChoices;
            UpdateStoreSummary();
            var refused = results.Where(r => !r.Deleted).ToList();
            return L.F("{0} version(s) supprimée(s), {1} libérés.", deleted.Count, Bytes.Format(freed)) +
                   (refused.Count > 0 ? L.F(" {0} refusée(s) : {1}.", refused.Count, refused[0].Detail) : "");
        });
    }

    // ---- Live device watch -------------------------------------------------------------

    async Task StartDeviceWatch()
    {
        try
        {
            // Subscribing to WMI events can take a second: not on the window's thread.
            watcher = await Task.Run(() => new DeviceWatcher());
        }
        catch (Exception e)
        {
            SetStatus(L.T("Surveillance des périphériques indisponible : ") + Errors.Describe(e));
            return;
        }
        // One plug-in raises a burst of events: wait until things settle, then look once.
        deviceSettle = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        deviceSettle.Tick += async (_, _) =>
        {
            deviceSettle.Stop();
            // A flaky device (Bluetooth, a USB hub that reconnects) can fire all day: look at
            // most every 10 seconds, so the PC never spends its time re-reading the inventory.
            var wait = lastDeviceLook + TimeSpan.FromSeconds(10) - DateTime.UtcNow;
            if (wait > TimeSpan.Zero)
            {
                deviceSettle.Interval = wait;
                deviceSettle.Start();
                return;
            }
            // A scan or an install reads the inventory itself: look once it is done.
            if (busy)
            {
                deviceSettle.Interval = TimeSpan.FromSeconds(10);
                deviceSettle.Start();
                return;
            }
            deviceSettle.Interval = TimeSpan.FromSeconds(2);
            lastDeviceLook = DateTime.UtcNow;
            await OnDevicesChanged();
        };
        watcher.Changed += () => Dispatcher.BeginInvoke(() =>
        {
            deviceSettle.Stop();
            deviceSettle.Start();
        });
        Closed += (_, _) => watcher.Dispose();
    }

    async Task OnDevicesChanged()
    {
        var previous = inventory.ToList();
        var before = previous.Select(d => d.DeviceId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool hadInventory = before.Count > 0;
        var now = await LoadInventory();
        if (!hadInventory) return; // first look: nothing to compare with

        var arrived = now.Where(d => !before.Contains(d.DeviceId)).ToList();
        var after = now.Select(d => d.DeviceId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var left = previous.Where(d => !after.Contains(d.DeviceId));
        // The dashboard names the graphics cards: read it again only when one came or went.
        if (arrived.Concat(left).Any(d => d.Class.Equals("Display", StringComparison.OrdinalIgnoreCase))) await LoadHardware();
        newDevicesWithoutDriver = arrived.Where(d => !d.HasDriver).ToList();
        if (newDevicesWithoutDriver.Count > 0)
        {
            DeviceBannerText.Text = L.F("Nouveau périphérique sans pilote : {0}.", string.Join(", ", newDevicesWithoutDriver.Select(d => d.Name)));
            DeviceBanner.Visibility = Visibility.Visible;
        }
        else if (arrived.Count > 0)
        {
            SetStatus(L.F("Nouveau périphérique : {0}. Son pilote est en place.", string.Join(", ", arrived.Select(d => d.Name).Distinct().Take(3))));
        }
    }

    async void OnDeviceBanner(object sender, RoutedEventArgs e)
    {
        DeviceBanner.Visibility = Visibility.Collapsed;
        Tabs.SelectedItem = CatalogTab;
        var devices = newDevicesWithoutDriver;
        await RunBusy(L.T("Recherche d'un pilote pour le nouveau périphérique…"), async () =>
        {
            ShowCatalog(await DriverCatalog.FindAsync(devices, new Progress<string>(SetStatus)));
            return catalogDrivers.Count == 0
                ? L.T("Le catalogue Microsoft n'a pas de pilote pour ce périphérique : essayez la page Windows Update ou le site du fabricant.")
                : L.T("Pilote trouvé : cochez-le puis installez-le.");
        });
    }
}
