namespace DriverUpdater.Drivers;

/// <summary>Unchecked: no source answered for this device, so nothing says it is up to date.</summary>
public enum Verdict { Update, NoDriver, Problem, Disabled, Unchecked, UpToDate }

/// <summary>
/// A driver update on offer: from Windows Update or the Microsoft Update Catalog (installed
/// here, together), or from NVIDIA's site (its own installer, from the graphics card page).
/// </summary>
/// <param name="Optional">
/// One of Windows Update's "optional" drivers: Microsoft only advises it for a known problem,
/// so it is offered, never ticked.
/// </param>
public sealed record DriverOffer(string UpdateId, string Source, Version? Version, DateTime? Date, string Label, bool Optional = false)
{
    public const string WindowsUpdate = "Windows Update", Catalog = "Fabricant", Nvidia = "Site NVIDIA";

    public bool InstallsHere => Source is WindowsUpdate or Catalog;
}

/// <summary>One line of the full driver report: one device, its driver, and what to do.</summary>
public sealed class ReportRow
{
    public required DeviceDriver Device { get; init; }
    public required Verdict Verdict { get; init; }
    public DriverOffer? Offer { get; init; }
    public bool Selected { get; set; }

    public string Installed => Device.HasDriver ? new InstalledDriver(Device.Version, Device.Date).ToString() : L.T("aucun");
    public string Available => Offer?.Label ?? "—";
    public string Source => Offer?.Source switch
    {
        null => "—",
        DriverOffer.Catalog => L.T("Fabricant"),
        DriverOffer.Nvidia => L.T("Site NVIDIA"),
        var source => source,
    };
    public bool CanInstall => Offer is { InstallsHere: true };
    public string VerdictText => Verdict switch
    {
        Verdict.Update => Offer is { Optional: true } ? L.T("Mise à jour facultative") : L.T("Mise à jour"),
        Verdict.NoDriver => Offer is null ? L.T("Aucun pilote") : L.T("Aucun pilote, un trouvé"),
        Verdict.Problem => Device.Status,
        Verdict.Disabled => L.T("Désactivé"),
        Verdict.Unchecked => L.T("Non vérifié"),
        _ => L.T("À jour"),
    };
}

/// <summary>
/// The report's figures, as the dashboard shows them. Kept from one start to the next, so the
/// last analysis is shown, with its date, until a new one.
/// </summary>
public sealed record ScanSummary(int Devices, int UpToDate, int Updates, int Issues)
{
    public DateTime When { get; init; }
    /// <summary>Every source answered (Windows Update, the catalog).</summary>
    public bool Complete { get; init; } = true;

    public static ScanSummary Of(IReadOnlyCollection<ReportRow> report) => new(
        report.Count,
        report.Count(r => r.Verdict is Verdict.UpToDate or Verdict.Disabled),
        report.Count(r => r.Offer is not null),
        report.Count(r => !r.CanInstall && r.Verdict is Verdict.NoDriver or Verdict.Problem));
}

/// <summary>
/// Puts every device and every offer together: for each device, the newest offer from either
/// source, and a verdict. Updates come first and arrive ticked, as asked: "check everything
/// outdated at the end".
/// </summary>
public static class DriverReport
{
    /// <param name="windowsUpdate">Offers keyed by the hardware ID Windows Update matched.</param>
    /// <param name="catalog">
    /// Offers keyed by the device's first hardware ID: identical devices (two ports of the same
    /// card) are searched once, and both must show the offer.
    /// </param>
    /// <param name="vendor">Offers from a maker's own site (NVIDIA), keyed by device instance ID.</param>
    /// <param name="unchecked">
    /// Devices no source answered for (by instance ID): without a newer offer, they are
    /// "not checked", never "up to date".
    /// </param>
    public static List<ReportRow> Build(IEnumerable<DeviceDriver> devices,
        IReadOnlyList<(string HardwareId, DriverOffer Offer)> windowsUpdate,
        IReadOnlyDictionary<string, DriverOffer> catalog,
        IReadOnlyDictionary<string, DriverOffer>? vendor = null,
        IReadOnlySet<string>? @unchecked = null)
    {
        var rows = new List<ReportRow>();
        foreach (var device in devices)
        {
            var offers = windowsUpdate
                .Where(w => device.Matches(w.HardwareId))
                .Select(w => w.Offer)
                .ToList();
            if (device.HardwareIds.Count > 0 && catalog.TryGetValue(device.HardwareIds[0], out var fromCatalog)) offers.Add(fromCatalog);
            if (vendor is not null && vendor.TryGetValue(device.DeviceId, out var fromVendor)) offers.Add(fromVendor);

            // Highest version wins; on a tie, Windows Update, which Microsoft targeted at this PC,
            // then the catalog: both install from here.
            var best = offers
                .Where(o => device.WouldUpdate(o.Version, o.Date))
                .OrderByDescending(o => o.Version)
                .ThenBy(o => o.Source switch { DriverOffer.WindowsUpdate => 0, DriverOffer.Catalog => 1, _ => 2 })
                .FirstOrDefault();

            var verdict = !device.HasDriver ? Verdict.NoDriver
                : best is not null ? Verdict.Update
                : device.Problem == 22 ? Verdict.Disabled
                : device.NeedsAttention ? Verdict.Problem
                : @unchecked?.Contains(device.DeviceId) == true ? Verdict.Unchecked
                : Verdict.UpToDate;
            rows.Add(new ReportRow { Device = device, Verdict = verdict, Offer = best, Selected = best is { InstallsHere: true, Optional: false } });
        }
        return rows
            .OrderBy(r => r.Verdict)
            .ThenBy(r => r.Device.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// The devices no source looked at, by instance ID. Windows Update looks at every device;
    /// without it, only those the catalog searched (its scope, minus the searches that failed)
    /// or NVIDIA answered for were checked.
    /// </summary>
    /// <param name="catalogUnreached">First hardware IDs whose catalog search failed; null when the catalog did not answer at all.</param>
    public static HashSet<string> Unchecked(IEnumerable<DeviceDriver> devices, bool windowsUpdateAnswered,
        IReadOnlySet<string>? catalogUnreached, IEnumerable<string> vendorAnswered)
    {
        if (windowsUpdateAnswered) return [];
        var vendor = vendorAnswered.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return devices
            .Where(d => !vendor.Contains(d.DeviceId)
                        && !(catalogUnreached is not null && CatalogScope.ShouldSearch(d) && !catalogUnreached.Contains(d.HardwareIds[0])))
            .Select(d => d.DeviceId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}
