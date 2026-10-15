namespace DriverUpdater.Drivers;

/// <summary>A device present in the PC and the driver it uses (if any).</summary>
public sealed class DeviceDriver
{
    public required string DeviceId { get; init; }
    public required string Name { get; init; }
    /// <summary>Windows' device class: Display, Net, Media, HIDClass…</summary>
    public required string Class { get; init; }
    public required string Provider { get; init; }
    public required string Version { get; init; }
    public required DateTime? Date { get; init; }
    public required bool? Signed { get; init; }
    /// <summary>Device Manager's error code; 0 means working.</summary>
    public required int Problem { get; init; }
    public required string InfName { get; init; }
    public required IReadOnlyList<string> HardwareIds { get; init; }

    /// <summary>"PCI", "USB", "HDAUDIO"…: the part of the device ID before the first backslash.</summary>
    public string Bus => DeviceId.Split('\\')[0].ToUpperInvariant();
    public bool HasDriver => Version.Length > 0 && Problem != 28;
    /// <summary>Not working, for another reason than being switched off on purpose.</summary>
    public bool NeedsAttention => Problem is not (0 or 22);
    public bool IsMicrosoft => Provider.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase);
    public System.Version? ParsedVersion => System.Version.TryParse(Version, out var v) ? v : null;

    public string Status => Problem switch
    {
        0 => "OK",
        22 => "Désactivé",
        28 => "Aucun pilote",
        _ => $"Erreur {Problem}",
    };

    public string DateText => Date is { } d ? d.ToString("dd/MM/yyyy") : "—";
    public string SignedText => Signed switch { true => "Oui", false => "Non", null => "—" };

    public bool IsOlderThan(int years, DateTime today) => Date is { } d && d < today.AddYears(-years);
}

public static class InstalledDrivers
{
    /// <summary>
    /// Each installed driver under every hardware ID of its device: the ID Windows Update reports
    /// for the device an update targets. Compatible IDs are left out on purpose: they are generic
    /// (e.g. "USB\Class_03"), shared by many devices, and would pair updates with the wrong driver.
    /// </summary>
    public static Dictionary<string, InstalledDriver> ByHardwareId(IEnumerable<DeviceDriver> devices)
    {
        var map = new Dictionary<string, InstalledDriver>(StringComparer.OrdinalIgnoreCase);
        foreach (var device in devices.Where(d => d.HasDriver))
            foreach (var id in device.HardwareIds)
                map.TryAdd(id, new InstalledDriver(device.Version, device.Date));
        return map;
    }
}

/// <summary>Which devices are worth a search in the Microsoft Update Catalog.</summary>
public static class CatalogScope
{
    /// <summary>Buses whose devices get drivers from their makers.</summary>
    static readonly HashSet<string> Buses = new(StringComparer.OrdinalIgnoreCase)
        { "PCI", "HDAUDIO", "INTELAUDIO", "USB", "ACPI", "HID" };

    /// <summary>
    /// Classes where a maker's driver beats Windows' generic one even when the generic one
    /// works: graphics, network, sound, Bluetooth, cameras, fingerprint readers.
    /// </summary>
    static readonly HashSet<string> WorthReplacingGeneric = new(StringComparer.OrdinalIgnoreCase)
        { "Display", "Net", "Media", "Bluetooth", "Camera", "Image", "Biometric" };

    /// <summary>
    /// Storage controllers and processors are left alone: a wrong driver there stops Windows
    /// from starting at all, which no restore point can fix from inside Windows.
    /// </summary>
    public static readonly HashSet<string> BootCritical = new(StringComparer.OrdinalIgnoreCase)
        { "SCSIAdapter", "HDC", "DiskDrive", "Volume", "VolumeSnapshot", "Processor", "Computer" };

    public static bool ShouldSearch(DeviceDriver device) =>
        device.HardwareIds.Count > 0
        && Buses.Contains(device.Bus)
        && !BootCritical.Contains(device.Class)
        && (!device.HasDriver || !device.IsMicrosoft || WorthReplacingGeneric.Contains(device.Class));

    /// <summary>The devices to search, one per hardware ID (identical twins need one search).</summary>
    public static List<DeviceDriver> Select(IEnumerable<DeviceDriver> devices) =>
        devices.Where(ShouldSearch)
            .GroupBy(d => d.HardwareIds[0], StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
}

/// <summary>A driver package kept in Windows' driver store (C:\Windows\System32\DriverStore).</summary>
public sealed record StorePackage(string PublishedName, string OriginalName, string Provider, string Class,
    Version? Version, DateTime? Date, bool BootCritical, long Size)
{
    public string VersionText => Version?.ToString() ?? "—";
    public string DateText => Date is { } d ? d.ToString("dd/MM/yyyy") : "—";
    public string SizeText => Bytes.Format(Size);
}

/// <summary>
/// Old driver versions Windows keeps "just in case": every graphics driver update leaves
/// the previous one behind, close to 1 GB each. One may go when a newer version of the same
/// driver is in the store, no device uses it, and Windows does not need it to start.
/// </summary>
public static class DriverStoreRules
{
    public static List<StorePackage> Removable(IEnumerable<StorePackage> packages, ISet<string> inUse) =>
        packages
            .GroupBy(p => (p.Class.ToLowerInvariant(), p.Provider.ToLowerInvariant(), p.OriginalName.ToLowerInvariant()))
            .SelectMany(family =>
            {
                var newest = family.OrderByDescending(p => p.Version).ThenByDescending(p => p.Date).First();
                return family.Where(p => p != newest && !p.BootCritical && !inUse.Contains(p.PublishedName));
            })
            .OrderByDescending(p => p.Size)
            .ToList();
}
