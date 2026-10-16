using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace DriverUpdater.UI;

// "Accueil" tab: the big button analyses every driver; the report says what to do.
public partial class MainWindow
{
    List<ReportRow> report = [];
    List<(string HardwareId, DriverOffer Offer)> windowsUpdateOffers = [];
    Dictionary<string, DriverOffer> catalogOffers = [];
    Dictionary<string, DriverOffer> vendorOffers = []; // NVIDIA's site, by device instance ID

    void OnTile(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tab } && FindName(tab) is TabItem item) Tabs.SelectedItem = item;
    }

    /// <summary>
    /// Every device against both sources at once: Windows Update and the makers' drivers in
    /// the Microsoft Update Catalog. The rest of the PC (software, startup, disks) is checked
    /// alongside, without holding up the report.
    /// </summary>
    async void OnScanAll(object sender, RoutedEventArgs e) =>
        await RunBusy("Analyse de tous les pilotes…", async () =>
        {
            ScanSpinning(true);
            try
            {
                foreach (var number in new[] { ReportTotal, ReportOk, ReportUpdates, ReportIssues }) number.Text = "…";
                ScanStatus.Text = "Inventaire des périphériques…";
                var devices = await LoadInventory();
                ReportTotal.Text = devices.Count.ToString();
                var otherChecks = Task.WhenAll(CheckSoftwareTile(), CheckStartupTile(), CheckDisksTile(), CheckSecurityTile());

                ScanStatus.Text = "Comparaison avec Windows Update et les fabricants : une à deux minutes…";
                var windowsUpdate = Task.Run(WindowsUpdateDrivers.Search);
                var catalog = DriverCatalog.FindAsync(devices, new Progress<string>(m => ScanStatus.Text = m));
                var nvidia = NvidiaDrivers.LatestAsync(devices.Where(IsNvidia), Machine.HasBattery);
                var failures = new List<string>();
                try
                {
                    var found = await windowsUpdate;
                    await ShowUpdates(found);
                    windowsUpdateOffers = found.Select(u => (u.HardwareId ?? "", OfferOf(u))).ToList();
                }
                catch (Exception ex)
                {
                    failures.Add("Windows Update : " + Errors.Describe(ex));
                }
                try
                {
                    var found = await catalog;
                    ShowCatalog(found);
                    catalogOffers = found
                        .GroupBy(c => c.HardwareId, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => OfferOf(g.First().Entry), StringComparer.OrdinalIgnoreCase);
                }
                catch (Exception ex)
                {
                    failures.Add("catalogue : " + Errors.Describe(ex));
                }
                nvidiaReleases = await nvidia; // an unreachable NVIDIA is simply no offer
                vendorOffers = devices.Where(d => nvidiaReleases.ContainsKey(d.DeviceId))
                    .ToDictionary(d => d.DeviceId, d => NvidiaModel.Offer(nvidiaReleases[d.DeviceId], d.ParsedVersion ?? new Version(32, 0, 0, 0)),
                        StringComparer.OrdinalIgnoreCase);

                report = DriverReport.Build(devices, windowsUpdateOffers, catalogOffers, vendorOffers);
                ShowReport();
                ScanStatus.Text = failures.Count == 0
                    ? $"Analyse du {DateTime.Now:dd/MM à HH:mm}. Tout ce qui est à mettre à jour est coché."
                    : "Analyse incomplète, " + string.Join(" ; ", failures);
                await otherChecks;
                int updates = report.Count(r => r.Offer is not null);
                bool graphics = report.Any(r => r.Offer?.Source == DriverOffer.Nvidia);
                return (updates == 0 ? "Analyse terminée : vos pilotes sont à jour." : $"Analyse terminée : {updates} pilote(s) à mettre à jour.")
                       + (graphics ? " Pilote NVIDIA plus récent : il s'installe depuis la page Carte graphique." : "");
            }
            finally
            {
                ScanSpinning(false);
            }
        });

    static DriverOffer OfferOf(DriverUpdate update)
    {
        var version = Catalog.VersionOf("", update.Title);
        var label = version is null ? update.Title : $"{version}" + (update.Date is { } d ? $" — {d:dd/MM/yyyy}" : "");
        return new DriverOffer(update.UpdateId, DriverOffer.WindowsUpdate, version, update.Date, label);
    }

    static DriverOffer OfferOf(CatalogEntry entry) =>
        new(entry.UpdateId, DriverOffer.Catalog, entry.Version, entry.Date, entry.Label);

    void ShowReport()
    {
        var shown = ShowUpToDate.IsChecked == true ? report : report.Where(r => r.Verdict != Verdict.UpToDate).ToList();
        ReportGrid.ItemsSource = shown;
        ReportTotal.Text = report.Count.ToString();
        ReportOk.Text = report.Count(r => r.Verdict is Verdict.UpToDate or Verdict.Disabled).ToString();
        ReportUpdates.Text = report.Count(r => r.Offer is not null).ToString();
        ReportIssues.Text = report.Count(r => !r.CanInstall && r.Verdict is Verdict.NoDriver or Verdict.Problem).ToString();
        ReportPlaceholder.Text = report.Count == 0 ? "Le rapport apparaîtra ici." : "Tous vos pilotes sont à jour.";
        ReportPlaceholder.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateReportButton();
    }

    void OnShowUpToDate(object sender, RoutedEventArgs e) => ShowReport();

    void OnReportChoiceChanged(object sender, RoutedEventArgs e) => UpdateReportButton();

    void UpdateReportButton()
    {
        int chosen = report.Where(r => r.Selected && r.CanInstall).Select(r => r.Offer!.UpdateId).Distinct().Count();
        UpdateAllButton.Content = chosen == 0 ? "Mettre à jour la sélection" : $"Mettre à jour ({chosen})";
        UpdateButtons();
    }

    /// <summary>Everything ticked, from both sources, under a single administrator consent.</summary>
    async void OnUpdateAll(object sender, RoutedEventArgs e)
    {
        var offers = report.Where(r => r.Selected && r.CanInstall).Select(r => r.Offer!).DistinctBy(o => o.UpdateId).ToList();
        if (offers.Count == 0) return;
        if (Ask($"Installer {offers.Count} pilote(s) ?\n\nUn point de restauration système sera créé avant. " +
                "Windows garde un pilote actuel s'il est plus adapté à votre PC.") != MessageBoxResult.Yes)
            return;

        await RunBusy("En attente de l'autorisation administrateur…", async () =>
        {
            var items = offers.Select(o => (o.Source == DriverOffer.WindowsUpdate ? AdminCommand.FromWindowsUpdate : AdminCommand.FromCatalog) + o.UpdateId);
            var result = await RunAdminAsync<CatalogInstallReport>(AdminCommand.InstallAll, items.ToArray());
            if (result.Cancelled) return "Installation annulée.";

            var names = report.Where(r => r.Offer is not null)
                .GroupBy(r => r.Offer!.UpdateId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => string.Join(", ", g.Select(r => r.Device.Name).Distinct()), StringComparer.OrdinalIgnoreCase);
            var lines = result.Results.Select(r => $"{(r.Succeeded ? "✔" : "✘")} {names.GetValueOrDefault(r.UpdateId, r.UpdateId)} : {r.Outcome}");
            bool reboot = result.Results.Any(r => r.RebootRequired);
            MessageBox.Show(this, result.RestorePoint + "\n\n" + string.Join("\n", lines) +
                (reboot ? "\n\nRedémarrez l'ordinateur pour terminer." : ""), "Résultat des mises à jour",
                MessageBoxButton.OK, MessageBoxImage.Information);

            // Installed offers leave the report; the rest stays as found.
            var done = result.Results.Where(r => r.Succeeded).Select(r => r.UpdateId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            windowsUpdateOffers = windowsUpdateOffers.Where(w => !done.Contains(w.Offer.UpdateId)).ToList();
            catalogOffers = catalogOffers.Where(c => !done.Contains(c.Value.UpdateId)).ToDictionary(StringComparer.OrdinalIgnoreCase);
            report = DriverReport.Build(await LoadInventory(), windowsUpdateOffers, catalogOffers, vendorOffers);
            ShowReport();
            await LoadHistory();
            return $"{done.Count} pilote(s) sur {offers.Count} mis à jour." + (reboot ? " Redémarrage requis." : "");
        });
    }

    Task CheckSoftwareTile() => Tile(TileSoftwareValue, TileSoftwareText, async () =>
    {
        if (!WinGet.IsAvailable) return ("—", "winget absent");
        ShowSoftware(await SoftwareUpgrades());
        return (software.Count.ToString(), software.Count == 0 ? "logiciels à jour" : "logiciel(s) à mettre à jour");
    });

    Task CheckStartupTile() => Tile(TileStartupValue, TileStartupText, async () =>
    {
        await LoadStartup();
        return (startupEntries.Count(s => s.Enabled).ToString(), "programmes lancés avec Windows");
    });

    Task CheckDisksTile() => Tile(TileDisksValue, TileDisksText, async () =>
    {
        var list = await Task.Run(SystemInfo.Disks);
        DiskGrid.ItemsSource = list;
        var bad = list.Where(d => d.Health is not "Bon").ToList();
        return bad.Count == 0 ? ("OK", $"{list.Count} disque(s) en bon état") : (bad.Count.ToString(), "disque(s) à surveiller");
    });

    Task CheckSecurityTile() => Tile(TileSecurityValue, TileSecurityText, async () =>
    {
        // A new analysis asks again; the "Santé" card shows the same answer.
        if (await (fullSecurity = LoadSecurity(askWindowsUpdate: true)) is not { } state) return ("!", "lecture impossible");
        return state.Problems.Count == 0 ? ("OK", "antivirus, pare-feu et Windows à jour") : (state.Problems.Count.ToString(), "point(s) à régler");
    });

    /// <summary>Runs one check for one tile; a failure shows on that tile and spares the others.</summary>
    static async Task Tile(TextBlock value, TextBlock text, Func<Task<(string Value, string Text)>> check)
    {
        try
        {
            value.Text = "…";
            (value.Text, text.Text) = await check();
        }
        catch (Exception e)
        {
            value.Text = "!";
            text.Text = Errors.Describe(e);
        }
    }

    void ScanSpinning(bool on)
    {
        ScanRing.Opacity = on ? 1 : 0;
        ScanRotation.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty, on
            ? new DoubleAnimation(0, 360, TimeSpan.FromSeconds(3)) { RepeatBehavior = RepeatBehavior.Forever }
            : null);
    }
}
