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
            InventorySummary.Text = "Lecture impossible : " + Errors.Describe(e);
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
            $"{inventory.Count} périphériques  ·  {inventory.Count(d => d.NeedsAttention)} à problème  ·  " +
            $"{inventory.Count(d => !d.HasDriver)} sans pilote  ·  {inventory.Count(d => d.IsOlderThan(3, today))} pilotes de plus de 3 ans  ·  " +
            $"{inventory.Count(d => d.Signed == false)} non signés";
    }

    void OnInventoryFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded) ShowInventory();
    }

    async void OnRefreshInventory(object sender, RoutedEventArgs e) => await LoadInventory();

    // ---- Driver store ------------------------------------------------------------------

    async void OnStoreScan(object sender, RoutedEventArgs e) =>
        await RunBusy("En attente de l'autorisation administrateur…", async () =>
        {
            var removable = await RunAdminAsync<List<StorePackage>>(AdminCommand.DriverStore);
            storeChoices = removable.Select(p => new StoreChoice(p)).ToList();
            StoreGrid.ItemsSource = storeChoices;
            StoreGrid.Visibility = storeChoices.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateStoreSummary();
            return storeChoices.Count == 0 ? "Aucune ancienne version à supprimer." : $"{storeChoices.Count} ancienne(s) version(s) trouvée(s).";
        });

    void OnStoreChoiceChanged(object sender, RoutedEventArgs e) => UpdateStoreSummary();

    void UpdateStoreSummary()
    {
        var chosen = storeChoices.Where(c => c.Selected).ToList();
        StoreSummary.Text = storeChoices.Count == 0
            ? "Aucune ancienne version à supprimer : le magasin de pilotes est propre."
            : $"{chosen.Count} sur {storeChoices.Count} cochée(s) : {Bytes.Format(chosen.Sum(c => c.Package.Size))} à libérer.";
        UpdateButtons();
    }

    async void OnStoreDelete(object sender, RoutedEventArgs e)
    {
        var chosen = storeChoices.Where(c => c.Selected).ToList();
        if (chosen.Count == 0) return;
        if (Ask($"Supprimer {chosen.Count} ancienne(s) version(s) de pilotes ({Bytes.Format(chosen.Sum(c => c.Package.Size))}) ?\n\n" +
                "Windows refuse de supprimer un pilote utilisé. La version actuelle de chaque pilote reste en place.")
            != MessageBoxResult.Yes)
            return;

        await RunBusy("En attente de l'autorisation administrateur…", async () =>
        {
            var results = await RunAdminAsync<List<StoreDeleteResult>>(AdminCommand.DeleteDrivers,
                chosen.Select(c => c.Package.PublishedName).ToArray());
            var deleted = results.Where(r => r.Deleted).Select(r => r.PublishedName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            long freed = chosen.Where(c => deleted.Contains(c.Package.PublishedName)).Sum(c => c.Package.Size);
            storeChoices = storeChoices.Where(c => !deleted.Contains(c.Package.PublishedName)).ToList();
            StoreGrid.ItemsSource = storeChoices;
            UpdateStoreSummary();
            var refused = results.Where(r => !r.Deleted).ToList();
            return $"{deleted.Count} version(s) supprimée(s), {Bytes.Format(freed)} libérés." +
                   (refused.Count > 0 ? $" {refused.Count} refusée(s) : {refused[0].Detail}." : "");
        });
    }

    // ---- Live device watch -------------------------------------------------------------

    void StartDeviceWatch()
    {
        try
        {
            watcher = new DeviceWatcher();
        }
        catch (Exception e)
        {
            SetStatus("Surveillance des périphériques indisponible : " + Errors.Describe(e));
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
        var before = inventory.Select(d => d.DeviceId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool hadInventory = before.Count > 0;
        var now = await LoadInventory();
        await LoadHardware();
        if (!hadInventory) return; // first look: nothing to compare with

        var arrived = now.Where(d => !before.Contains(d.DeviceId)).ToList();
        newDevicesWithoutDriver = arrived.Where(d => !d.HasDriver).ToList();
        if (newDevicesWithoutDriver.Count > 0)
        {
            DeviceBannerText.Text = $"Nouveau périphérique sans pilote : {string.Join(", ", newDevicesWithoutDriver.Select(d => d.Name))}.";
            DeviceBanner.Visibility = Visibility.Visible;
        }
        else if (arrived.Count > 0)
        {
            SetStatus($"Nouveau périphérique : {string.Join(", ", arrived.Select(d => d.Name).Distinct().Take(3))}. Son pilote est en place.");
        }
    }

    async void OnDeviceBanner(object sender, RoutedEventArgs e)
    {
        DeviceBanner.Visibility = Visibility.Collapsed;
        Tabs.SelectedItem = CatalogTab;
        var devices = newDevicesWithoutDriver;
        await RunBusy("Recherche d'un pilote pour le nouveau périphérique…", async () =>
        {
            ShowCatalog(await DriverCatalog.FindAsync(devices, new Progress<string>(SetStatus)));
            return catalogDrivers.Count == 0
                ? "Le catalogue Microsoft n'a pas de pilote pour ce périphérique : essayez Windows Update (onglet Pilotes) ou le site du fabricant."
                : "Pilote trouvé : cochez-le puis installez-le.";
        });
    }
}
