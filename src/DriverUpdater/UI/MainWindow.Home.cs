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
    string? reportGaps; // the sources the last analysis could not reach, if any

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
        await RunBusy(L.T("Analyse de tous les pilotes…"), async () =>
        {
            ScanSpinning(true);
            try
            {
                foreach (var number in new[] { ReportTotal, ReportOk, ReportUpdates, ReportIssues }) number.Text = "…";
                // Windows Update is the slowest source and needs no inventory: asked at once. The
                // security tile asks it too (pending updates), only afterwards: Windows runs one
                // search at a time, and the drivers' report must not wait behind that one.
                var windowsUpdate = Task.Run(WindowsUpdateDrivers.Search);
                async Task SecurityAfterDrivers()
                {
                    await Task.WhenAny(windowsUpdate); // its own failure is reported below
                    await CheckSecurityTile();
                }
                ScanStatus.Text = L.T("Inventaire des périphériques…");
                var devices = await LoadInventory(required: true);
                ReportTotal.Text = devices.Count.ToString();
                var otherChecks = Task.WhenAll(CheckSoftwareTile(), CheckStartupTile(), CheckDisksTile(), SecurityAfterDrivers());

                ScanStatus.Text = L.T("Comparaison avec Windows Update et les fabricants : une à deux minutes…");
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
                    failures.Add(L.T("Windows Update : ") + Errors.Describe(ex));
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
                    failures.Add(L.T("catalogue : ") + Errors.Describe(ex));
                }
                nvidiaReleases = await nvidia; // an unreachable NVIDIA is simply no offer
                vendorOffers = devices.Where(d => nvidiaReleases.ContainsKey(d.DeviceId))
                    .ToDictionary(d => d.DeviceId, d => NvidiaModel.Offer(nvidiaReleases[d.DeviceId], d.ParsedVersion ?? new Version(32, 0, 0, 0)),
                        StringComparer.OrdinalIgnoreCase);

                report = DriverReport.Build(devices, windowsUpdateOffers, catalogOffers, vendorOffers);
                reportGaps = failures.Count == 0 ? null : L.T("Analyse incomplète, ") + string.Join(" ; ", failures);
                ShowReport();
                SaveLastScan(DateTime.Now);
                ScanStatus.Text = reportGaps ?? L.F("Analyse du {0:dd/MM à HH:mm}. Tout ce qui est à mettre à jour est coché.", DateTime.Now);
                await otherChecks;
                // A source out of reach says nothing about the drivers it would have checked.
                if (reportGaps is not null) return reportGaps;
                int updates = report.Count(r => r.Offer is not null);
                bool graphics = report.Any(r => r.Offer?.Source == DriverOffer.Nvidia);
                return (updates == 0 ? L.T("Analyse terminée : vos pilotes sont à jour.") : L.F("Analyse terminée : {0} pilote(s) à mettre à jour.", updates))
                       + (graphics ? L.T(" Pilote NVIDIA plus récent : il s'installe depuis la page Carte graphique.") : "");
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
        if (report.Count > 0) ShowFigures(ScanSummary.Of(report)); // before any analysis, the last one's stay
        ReportPlaceholder.Text = report.Count == 0 ? L.T("Le rapport apparaîtra ici.") : reportGaps ?? L.T("Tous vos pilotes sont à jour.");
        ReportPlaceholder.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateReportButton();
    }

    void ShowFigures(ScanSummary figures)
    {
        ReportTotal.Text = figures.Devices.ToString();
        ReportOk.Text = figures.UpToDate.ToString();
        ReportUpdates.Text = figures.Updates.ToString();
        ReportIssues.Text = figures.Issues.ToString();
    }

    /// <summary>At start-up: the last analysis, with its date, until the user runs a new one.</summary>
    void ShowLastScan()
    {
        if (Settings.LastScan is not { } last) return;
        ShowFigures(last);
        ScanStatus.Text = L.F("Dernière analyse le {0:dd/MM à HH:mm} : {1} pilote(s) à mettre à jour. ANALYSER vérifie à nouveau.", last.When, last.Updates)
                          + (last.Complete ? "" : L.T(" Elle était incomplète."));
    }

    void SaveLastScan(DateTime when) => Settings.LastScan = ScanSummary.Of(report) with { When = when, Complete = reportGaps is null };

    /// <summary>At start-up, what the dashboard's tiles can read at once from the PC itself.</summary>
    Task CheckLocalTiles() => Task.WhenAll(CheckStartupTile(), CheckDisksTile(), CheckSecurityTile(askWindowsUpdate: false));

    void OnShowUpToDate(object sender, RoutedEventArgs e) => ShowReport();

    void OnReportChoiceChanged(object sender, RoutedEventArgs e) => UpdateReportButton();

    void UpdateReportButton()
    {
        int chosen = report.Where(r => r.Selected && r.CanInstall).Select(r => r.Offer!.UpdateId).Distinct().Count();
        UpdateAllButton.Content = chosen == 0 ? L.T("Mettre à jour la sélection") : L.F("Mettre à jour ({0})", chosen);
        UpdateButtons();
    }

    /// <summary>Everything ticked, from both sources, under a single administrator consent.</summary>
    async void OnUpdateAll(object sender, RoutedEventArgs e)
    {
        var offers = report.Where(r => r.Selected && r.CanInstall).Select(r => r.Offer!).DistinctBy(o => o.UpdateId).ToList();
        if (offers.Count == 0) return;
        if (Ask(L.F("Installer {0} pilote(s) ?\n\nUn point de restauration système sera créé avant. ", offers.Count) +
                L.T("Windows garde un pilote actuel s'il est plus adapté à votre PC.")) != MessageBoxResult.Yes)
            return;

        await RunBusy(L.T("En attente de l'autorisation administrateur…"), async () =>
        {
            var items = offers.Select(o => (o.Source == DriverOffer.WindowsUpdate ? AdminCommand.FromWindowsUpdate : AdminCommand.FromCatalog) + o.UpdateId);
            var result = await RunAdminAsync<CatalogInstallReport>(AdminCommand.InstallAll, items.ToArray());
            if (result.Cancelled) return L.T("Installation annulée.");

            var names = report.Where(r => r.Offer is not null)
                .GroupBy(r => r.Offer!.UpdateId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => string.Join(", ", g.Select(r => r.Device.Name).Distinct()), StringComparer.OrdinalIgnoreCase);
            var lines = result.Results.Select(r => L.F("{0} {1} : {2}", r.Succeeded ? "✔" : "✘", names.GetValueOrDefault(r.UpdateId, r.UpdateId), r.Outcome));
            foreach (var r in result.Results.Where(r => r.Succeeded))
                Journal.Add(Journal.Drivers, L.F("{0} : {1}", names.GetValueOrDefault(r.UpdateId, r.UpdateId), r.Outcome));
            bool reboot = result.Results.Any(r => r.RebootRequired);
            MessageBox.Show(this, result.RestorePoint + "\n\n" + string.Join("\n", lines) +
                (reboot ? L.T("\n\nRedémarrez l'ordinateur pour terminer.") : ""), L.T("Résultat des mises à jour"),
                MessageBoxButton.OK, MessageBoxImage.Information);

            var done = result.Results.Where(r => r.Succeeded).Select(r => r.UpdateId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            await ForgetInstalled(done);
            await LoadHistory();
            return L.F("{0} pilote(s) sur {1} mis à jour.", done.Count, offers.Count) + (reboot ? L.T(" Redémarrage requis.") : "");
        });
    }

    /// <summary>
    /// Installed drivers leave every list that offered them, wherever they were installed from:
    /// the report, the Windows Update page and the catalog's. The rest stays as found.
    /// </summary>
    async Task ForgetInstalled(IReadOnlySet<string> done)
    {
        windowsUpdateOffers = windowsUpdateOffers.Where(w => !done.Contains(w.Offer.UpdateId)).ToList();
        catalogOffers = catalogOffers.Where(c => !done.Contains(c.Value.UpdateId)).ToDictionary(StringComparer.OrdinalIgnoreCase);
        await ShowUpdates(drivers.Where(d => !done.Contains(d.UpdateId)).ToList());
        ShowCatalog(catalogDrivers.Where(d => !done.Contains(d.Entry.UpdateId)).ToList());
        if (report.Count == 0) return; // no analysis yet
        report = DriverReport.Build(await LoadInventory(), windowsUpdateOffers, catalogOffers, vendorOffers);
        ShowReport();
        SaveLastScan(Settings.LastScan?.When ?? DateTime.Now); // same analysis, fewer updates left
    }

    Task CheckSoftwareTile() => Tile(TileSoftwareValue, TileSoftwareText, async () =>
    {
        if (!WinGet.IsAvailable) return ("—", "winget absent");
        ShowSoftware(await SoftwareUpgrades());
        return (software.Count.ToString(), software.Count == 0 ? L.T("logiciels à jour") : L.T("logiciel(s) à mettre à jour"));
    });

    Task CheckStartupTile() => Tile(TileStartupValue, TileStartupText, async () =>
    {
        await LoadStartup();
        return (startupEntries.Count(s => s.Enabled).ToString(), L.T("programmes lancés avec Windows"));
    });

    Task CheckDisksTile() => Tile(TileDisksValue, TileDisksText, async () =>
    {
        var list = await Task.Run(SystemInfo.Disks);
        DiskGrid.ItemsSource = list;
        var bad = list.Where(d => d.Health != L.T("Bon")).ToList();
        return bad.Count == 0 ? ("OK", L.F("{0} disque(s) en bon état", list.Count)) : (bad.Count.ToString(), L.T("disque(s) à surveiller"));
    });

    /// <param name="askWindowsUpdate">
    /// An analysis asks Windows Update for pending updates too (slow); at start-up, only the
    /// antivirus and the firewall are read.
    /// </param>
    Task CheckSecurityTile(bool askWindowsUpdate = true) => Tile(TileSecurityValue, TileSecurityText, async () =>
    {
        // A new analysis asks again; the "Sécurité" page shows the same answer.
        var reading = LoadSecurity(askWindowsUpdate);
        if (askWindowsUpdate) fullSecurity = reading;
        if (await reading is not { } state) return ("!", L.T("lecture impossible"));
        return state.Problems.Count > 0 ? (state.Problems.Count.ToString(), L.T("point(s) à régler"))
            : askWindowsUpdate ? ("OK", L.T("antivirus, pare-feu et Windows à jour"))
            : ("OK", L.T("antivirus et pare-feu actifs"));
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
