namespace DriverUpdater.Drivers;

public enum Verdict { Update, NoDriver, Problem, Disabled, UpToDate }

/// <summary>
/// A driver update on offer: from Windows Update or the Microsoft Update Catalog (installed
/// here, together), or from NVIDIA's site (its own installer, from the graphics card page).
/// </summary>
public sealed record DriverOffer(string UpdateId, string Source, Version? Version, DateTime? Date, string Label)
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

    public string Installed => Device.HasDriver ? new InstalledDriver(Device.Version, Device.Date).ToString() : "aucun";
    public string Available => Offer?.Label ?? "—";
    public string Source => Offer?.Source ?? "—";
    public bool CanInstall => Offer is { InstallsHere: true };
    public string VerdictText => Verdict switch
    {
        Verdict.Update => "Mise à jour",
        Verdict.NoDriver => Offer is null ? "Aucun pilote" : "Aucun pilote, un trouvé",
        Verdict.Problem => Device.Status,
        Verdict.Disabled => "Désactivé",
        _ => "À jour",
    };
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
    public static List<ReportRow> Build(IEnumerable<DeviceDriver> devices,
        IReadOnlyList<(string HardwareId, DriverOffer Offer)> windowsUpdate,
        IReadOnlyDictionary<string, DriverOffer> catalog,
        IReadOnlyDictionary<string, DriverOffer>? vendor = null)
    {
        var rows = new List<ReportRow>();
        foreach (var device in devices)
        {
            var offers = windowsUpdate
                .Where(w => device.HardwareIds.Contains(w.HardwareId, StringComparer.OrdinalIgnoreCase))
                .Select(w => w.Offer)
                .ToList();
            if (device.HardwareIds.Count > 0 && catalog.TryGetValue(device.HardwareIds[0], out var fromCatalog)) offers.Add(fromCatalog);
            if (vendor is not null && vendor.TryGetValue(device.DeviceId, out var fromVendor)) offers.Add(fromVendor);

            // Highest version wins; on a tie, Windows Update, which Microsoft targeted at this PC,
            // then the catalog: both install from here.
            var best = offers
                .Where(o => !device.HasDriver || device.ParsedVersion is not { } installed || o.Version is null || o.Version > installed)
                .OrderByDescending(o => o.Version)
                .ThenBy(o => o.Source switch { DriverOffer.WindowsUpdate => 0, DriverOffer.Catalog => 1, _ => 2 })
                .FirstOrDefault();

            var verdict = !device.HasDriver ? Verdict.NoDriver
                : best is not null ? Verdict.Update
                : device.Problem == 22 ? Verdict.Disabled
                : device.NeedsAttention ? Verdict.Problem
                : Verdict.UpToDate;
            rows.Add(new ReportRow { Device = device, Verdict = verdict, Offer = best, Selected = best is { InstallsHere: true } });
        }
        return rows
            .OrderBy(r => r.Verdict)
            .ThenBy(r => r.Device.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
