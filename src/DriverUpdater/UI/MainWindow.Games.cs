using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace DriverUpdater.UI;

public sealed class GpuRow
{
    public required DeviceDriver Device { get; init; }
    public required string Name { get; init; }
    public required string Installed { get; init; }
    public string Latest { get; init; } = L.T("cliquez sur « Chercher dans le catalogue »");
    public CatalogDriver? Newer { get; init; }
    public string? Tool { get; init; }
    public string? Url { get; init; }
    public bool HasTool => Url is not null;
    /// <summary>A newer driver on NVIDIA's site, installed with NVIDIA's own installer.</summary>
    public NvidiaRelease? Official { get; init; }
    public bool HasOfficial => Official is not null;
    public string OfficialText => Official is { } r ? L.F("Installer {0}", r.Version) : "";
    public string? NotesUrl => Official?.DetailsUrl;
}

public sealed class ComponentRow
{
    public required GameComponent Component { get; init; }
    public required bool Installed { get; init; }
    public required bool Outdated { get; init; }
    public bool Selected { get; set; }

    public bool CanInstall => !Installed || Outdated;
    public string Status => !Installed ? L.T("Absent") : Outdated ? L.T("Mise à jour disponible") : L.T("Installé");
}

// "Jeux" tab: graphics driver, game runtimes, Windows' gaming settings.
public partial class MainWindow
{
    List<GpuRow> gpuRows = [];
    List<ComponentRow> componentRows = [];
    Dictionary<string, NvidiaRelease> nvidiaReleases = []; // by device instance ID

    async Task LoadGames() => await Task.WhenAll(LoadGpus(), CheckComponents(), LoadGameTips());

    async Task LoadGpus()
    {
        try
        {
            var devices = inventory.Count > 0 ? inventory : await LoadInventory();
            var tools = await Task.Run(SystemInfo.Gpus);
            var displays = devices.Where(d => d.Class.Equals("Display", StringComparison.OrdinalIgnoreCase)).ToList();
            ShowGpus(displays, tools, newer: null);
            // NVIDIA answers in a second: no need to wait for a click.
            if (displays.Any(IsNvidia))
            {
                nvidiaReleases = await NvidiaDrivers.LatestAsync(displays.Where(IsNvidia), Machine.HasBattery);
                ShowGpus(displays, tools, newer: null);
            }
        }
        catch (Exception e)
        {
            SetStatus(L.T("Lecture de la carte graphique impossible : ") + Errors.Describe(e));
        }
    }

    // Graphics cards only: an NVIDIA card's HDMI sound and USB-C controller are NVIDIA devices too.
    static bool IsNvidia(DeviceDriver device) =>
        device.Class.Equals("Display", StringComparison.OrdinalIgnoreCase) && NvidiaDrivers.CardId(device) is not null;

    /// <summary>The newer NVIDIA release for this card, if NVIDIA has one.</summary>
    NvidiaRelease? NewerFromNvidia(DeviceDriver device) =>
        nvidiaReleases.TryGetValue(device.DeviceId, out var release) && NvidiaModel.IsNewer(release, device) ? release : null;

    void ShowGpus(IEnumerable<DeviceDriver> displays, IReadOnlyList<Hardware> tools, IReadOnlyList<CatalogDriver>? newer)
    {
        gpuRows = displays.Select(device =>
        {
            var tool = tools.FirstOrDefault(t => t.Name.Equals(device.Name, StringComparison.OrdinalIgnoreCase));
            var found = newer?.FirstOrDefault(n => n.DeviceId == device.DeviceId
                || device.HardwareIds.Contains(n.HardwareId, StringComparer.OrdinalIgnoreCase));
            var official = NewerFromNvidia(device);
            bool nvidiaKnown = nvidiaReleases.TryGetValue(device.DeviceId, out var latest);
            string installed = device.HasDriver ? new InstalledDriver(device.Version, device.Date).ToString() : L.T("aucun pilote");
            if (NvidiaModel.NvidiaVersion(device.Version) is { } nvidiaNumber && device.HasDriver) installed = $"{nvidiaNumber} ({installed})";
            return new GpuRow
            {
                Device = device,
                Name = device.Name,
                Installed = installed,
                Latest = official is { } r ? L.F("{0} {1}{2}, sur le site de NVIDIA", r.Name, r.Version, r.Date is { } d ? L.F(" du {0:dd/MM/yyyy}", d) : "")
                    : nvidiaKnown ? L.F("à jour : {0} est le dernier {1}", latest!.Version, latest.Studio ? L.T("pilote Studio") : "Game Ready")
                    : newer is null ? L.T("cliquez sur « Chercher dans le catalogue »")
                    : found is null ? L.T("vous avez déjà le plus récent du catalogue")
                    : found.Entry.Label,
                Official = official,
                Newer = found,
                Tool = tool?.Tool,
                Url = tool?.Url,
            };
        }).ToList();
        GpuDriverList.ItemsSource = gpuRows;
        UpdateButtons();
    }

    async void OnGpuCheck(object sender, RoutedEventArgs e) =>
        await RunBusy(L.T("Recherche du dernier pilote graphique dans le catalogue Microsoft…"), async () =>
        {
            var displays = gpuRows.Select(r => r.Device).ToList();
            var newer = await DriverCatalog.FindAsync(displays, new Progress<string>(SetStatus));
            ShowGpus(displays, await Task.Run(SystemInfo.Gpus), newer);
            return newer.Count == 0 ? L.T("Votre pilote graphique est le plus récent du catalogue.") : L.T("Pilote graphique plus récent trouvé.");
        });

    async void OnGpuInstall(object sender, RoutedEventArgs e)
    {
        var newer = gpuRows.Where(r => r.Newer is not null).Select(r => r.Newer!).ToList();
        if (newer.Count == 0) return;
        var size = Bytes.Format(newer.Sum(n => n.Entry.Size));
        if (Ask(L.F("Installer le pilote graphique {0} ({1}) ?\n\n", string.Join(", ", newer.Select(n => n.Entry.Version)), size) +
                L.T("Un point de restauration sera créé avant. L'écran peut clignoter pendant l'installation.")) != MessageBoxResult.Yes)
            return;

        await RunBusy(L.T("En attente de l'autorisation administrateur…"), async () =>
        {
            var result = await RunAdminAsync<CatalogInstallReport>(AdminCommand.InstallCatalog, newer.Select(n => n.Entry.UpdateId).ToArray());
            if (result.Cancelled) return L.T("Installation annulée.");
            bool reboot = result.Results.Any(r => r.RebootRequired);
            await LoadInventory();
            ShowGpus(inventory.Where(d => d.Class.Equals("Display", StringComparison.OrdinalIgnoreCase)), await Task.Run(SystemInfo.Gpus), newer: null);
            return string.Join(" ; ", result.Results.Select(r => r.Outcome)) + (reboot ? L.T(". Redémarrez l'ordinateur.") : ".");
        });
    }

    async void OnNvidiaInstall(object sender, RoutedEventArgs e)
    {
        if (busy || sender is not Button { Tag: GpuRow { Official: { } release } row } || NvidiaDrivers.CardId(row.Device) is not { } card) return;
        if (Ask(L.F("Installer le pilote NVIDIA {0} ({1}) ?\n\n", release.Version, release.Name) +
                L.T("Après un point de restauration, il est téléchargé depuis les serveurs de NVIDIA (près de 1 Go) dans un dossier protégé, ") +
                L.T("sa signature est vérifiée, puis l'installateur officiel de NVIDIA s'ouvre : « Installation express » garde vos réglages. ") +
                L.T("L'écran peut clignoter pendant l'installation.")) != MessageBoxResult.Yes)
            return;

        await RunBusy(L.T("En attente de l'autorisation administrateur…"), async () =>
        {
            var report = await RunAdminAsync<NvidiaInstallReport>(AdminCommand.InstallNvidia, card);
            if (report.Cancelled) return L.T("Installation annulée.");
            await LoadInventory();
            await LoadGpus();
            return report.ExitCode == 0
                ? L.F("Installateur NVIDIA {0} terminé. Redémarrez si l'installateur l'a demandé.", report.Version)
                : L.F("L'installateur NVIDIA {0} s'est fermé (code {1}) : il a peut-être été annulé.", report.Version, report.ExitCode);
        });
    }

    async Task CheckComponents()
    {
        try
        {
            var names = (await Task.Run(InstalledPrograms.List)).Select(p => p.Name).ToList();
            var outdated = WinGet.IsAvailable
                ? (await SoftwareUpgrades()).Select(u => u.Id).ToHashSet(StringComparer.OrdinalIgnoreCase)
                : [];
            // DirectX 9's extras leave no entry in "Apps installées": look for one of their files.
            bool directX = File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.SystemX86), "d3dx9_43.dll"));
            bool nvidia = inventory.Any(d => d.Class.Equals("Display", StringComparison.OrdinalIgnoreCase)
                                             && (d.Name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) || d.Provider.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)));

            componentRows = GameComponent.All
                .Where(c => c.WingetId != "Nvidia.PhysX" || nvidia) // PhysX only matters on NVIDIA cards
                .Select(c =>
                {
                    bool installed = c.WingetId == "Microsoft.DirectX" ? directX : c.IsIn(names);
                    var row = new ComponentRow { Component = c, Installed = installed, Outdated = installed && outdated.Contains(c.WingetId) };
                    row.Selected = row.CanInstall;
                    return row;
                })
                .OrderBy(r => r.Installed && !r.Outdated)
                .ToList();
            ComponentGrid.ItemsSource = componentRows;
            ComponentGrid.Visibility = Visibility.Visible;
            int missing = componentRows.Count(r => r.CanInstall);
            SetStatus(missing == 0 ? L.T("Composants de jeu : tout est en place.") : L.F("Composants de jeu : {0} à installer ou mettre à jour.", missing));
        }
        catch (Exception e)
        {
            SetStatus(L.T("Vérification des composants impossible : ") + Errors.Describe(e));
        }
        UpdateButtons();
    }

    async void OnComponentsCheck(object sender, RoutedEventArgs e) =>
        await RunBusy(L.T("Vérification des composants de jeu…"), async () =>
        {
            await CheckComponents();
            return StatusText.Text;
        });

    async void OnComponentsInstall(object sender, RoutedEventArgs e)
    {
        var chosen = componentRows.Where(r => r.Selected && r.CanInstall).ToList();
        if (chosen.Count == 0) return;
        if (!WinGet.IsAvailable)
        {
            SetStatus(L.T("winget est introuvable : installez « Programme d'installation d'application » depuis le Microsoft Store."));
            return;
        }
        await RunBusy(L.T("Installation des composants de jeu…"), async () =>
        {
            BusyBar.IsIndeterminate = false;
            BusyBar.Maximum = chosen.Count;
            var failed = new List<string>();
            for (int i = 0; i < chosen.Count; i++)
            {
                var row = chosen[i];
                SetStatus(L.F("Composant {0} sur {1} : {2} (Windows peut demander une autorisation)…", i + 1, chosen.Count, row.Component.Name));
                int code = row.Installed ? await WinGet.UpgradeAsync(row.Component.WingetId) : await WinGet.InstallAsync(row.Component.WingetId);
                if (code != 0) failed.Add($"{row.Component.Name} (0x{code:X8})");
                BusyBar.Value = i + 1;
            }
            upgrades = null; // just updated: ask winget again
            await CheckComponents();
            return failed.Count == 0
                ? L.F("{0} composant(s) installé(s).", chosen.Count)
                : L.F("{0} sur {1} installé(s). Échec : {2}.", chosen.Count - failed.Count, chosen.Count, string.Join(", ", failed));
        });
    }
}
