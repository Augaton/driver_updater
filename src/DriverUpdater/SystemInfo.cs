using System.Management;

namespace DriverUpdater;

public sealed record ProblemDevice(string Name, string Meaning);
public sealed record Disk(string Name, string Type, string Size, string Health);
public sealed record RestorePointResult(bool Ok, string Message);

public sealed record InstalledDriver(string Version, DateTime? Date)
{
    public override string ToString() => Date is { } date ? $"{Version} ({date:dd/MM/yyyy})" : Version;
}

/// <summary>System checks and restore points, through WMI.</summary>
public static class SystemInfo
{
    /// <summary>
    /// Installed drivers keyed by every hardware ID of their device, the ID Windows Update
    /// reports for the device an update targets.
    /// </summary>
    public static Dictionary<string, InstalledDriver> InstalledDrivers()
    {
        var byDevice = new Dictionary<string, InstalledDriver>(StringComparer.OrdinalIgnoreCase);
        using (var searcher = new ManagementObjectSearcher("SELECT DeviceID, DriverVersion, DriverDate FROM Win32_PnPSignedDriver"))
        using (var results = searcher.Get())
            foreach (ManagementBaseObject driver in results)
                using (driver)
                    if (driver["DeviceID"] is string device && driver["DriverVersion"] is string version)
                        byDevice[device] = new InstalledDriver(version, ParseDate(driver["DriverDate"] as string));

        // Compatible IDs are left out on purpose: they are generic (e.g. "USB\Class_03"),
        // shared by many devices, so they would pair updates with the wrong driver.
        var byHardwareId = new Dictionary<string, InstalledDriver>(StringComparer.OrdinalIgnoreCase);
        using (var searcher = new ManagementObjectSearcher("SELECT DeviceID, HardwareID FROM Win32_PnPEntity"))
        using (var results = searcher.Get())
            foreach (ManagementBaseObject device in results)
                using (device)
                    if (device["DeviceID"] is string id && byDevice.TryGetValue(id, out var driver))
                        foreach (var hardwareId in device["HardwareID"] as string[] ?? [])
                            byHardwareId.TryAdd(hardwareId, driver);
        return byHardwareId;
    }

    static DateTime? ParseDate(string? wmiDate)
    {
        try
        {
            return wmiDate is null ? null : ManagementDateTimeConverter.ToDateTime(wmiDate);
        }
        catch (Exception e) when (e is FormatException or ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    public static List<ProblemDevice> ProblemDevices()
    {
        // Code 45 only means "not plugged in right now", which is not a problem.
        using var searcher = new ManagementObjectSearcher(
            "SELECT Name, ConfigManagerErrorCode FROM Win32_PnPEntity " +
            "WHERE ConfigManagerErrorCode <> 0 AND ConfigManagerErrorCode <> 45");
        using var results = searcher.Get();
        var list = new List<ProblemDevice>();
        foreach (ManagementBaseObject device in results)
            using (device)
                list.Add(new ProblemDevice(
                    device["Name"] as string ?? "Périphérique inconnu",
                    ErrorMeaning(Convert.ToInt32(device["ConfigManagerErrorCode"]))));
        return list;
    }

    public static List<Disk> Disks()
    {
        using var searcher = new ManagementObjectSearcher(
            @"root\Microsoft\Windows\Storage",
            "SELECT FriendlyName, MediaType, HealthStatus, Size FROM MSFT_PhysicalDisk");
        using var results = searcher.Get();
        var list = new List<Disk>();
        foreach (ManagementBaseObject disk in results)
            using (disk)
                list.Add(new Disk(
                    disk["FriendlyName"] as string ?? "Disque",
                    Convert.ToInt32(disk["MediaType"]) switch { 3 => "HDD", 4 => "SSD", 5 => "SCM", _ => "Inconnu" },
                    Bytes.Format(Convert.ToInt64(disk["Size"])),
                    Convert.ToInt32(disk["HealthStatus"]) switch
                    {
                        0 => "Bon",
                        1 => "Avertissement",
                        2 => "Défaillant",
                        _ => "Inconnu",
                    }));
        return list;
    }

    /// <summary>Creates a restore point, or reports a usable one from the last 24 hours.</summary>
    public static RestorePointResult CreateRestorePoint()
    {
        try
        {
            var scope = new ManagementScope(@"\\.\root\default");
            using var restore = new ManagementClass(scope, new ManagementPath("SystemRestore"), null);
            using var args = restore.GetMethodParameters("CreateRestorePoint");
            args["Description"] = "Driver Updater : avant installation de pilotes";
            args["RestorePointType"] = 10u; // DEVICE_DRIVER_INSTALL
            args["EventType"] = 100u;       // BEGIN_SYSTEM_CHANGE
            using var result = restore.InvokeMethod("CreateRestorePoint", args, null);
            uint code = Convert.ToUInt32(result["ReturnValue"]);

            // Windows reports success but silently skips the point when another one is
            // less than 24 hours old, so look at what actually exists.
            DateTime? latest = LatestRestorePoint(scope);
            if (latest > DateTime.Now.AddMinutes(-10))
                return new(true, "Point de restauration créé.");
            if (latest > DateTime.Now.AddHours(-24))
                return new(true, $"Windows n'autorise qu'un point de restauration par 24 h ; le plus récent date du {latest:g}.");
            return new(false, code == 0
                ? "Aucun point de restauration n'a été créé."
                : $"Impossible de créer un point de restauration (code {code}). La protection du système est peut-être désactivée.");
        }
        catch (ManagementException e)
        {
            return new(false, "Points de restauration indisponibles : " + e.Message);
        }
    }

    static DateTime? LatestRestorePoint(ManagementScope scope)
    {
        using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT CreationTime FROM SystemRestore"));
        using var results = searcher.Get();
        DateTime? latest = null;
        foreach (ManagementBaseObject point in results)
            using (point)
            {
                var created = ManagementDateTimeConverter.ToDateTime((string)point["CreationTime"]);
                if (latest is null || created > latest) latest = created;
            }
        return latest;
    }

    // Device Manager error codes: https://learn.microsoft.com/windows-hardware/drivers/install/device-manager-error-messages
    static string ErrorMeaning(int code) => code switch
    {
        1 => "mal configuré",
        3 => "pilote corrompu ou mémoire insuffisante",
        10 => "ne peut pas démarrer",
        12 => "ressources insuffisantes",
        14 => "redémarrage nécessaire",
        18 => "pilotes à réinstaller",
        22 => "désactivé",
        24 => "absent ou pilotes incomplets",
        28 => "aucun pilote installé",
        31 => "le pilote ne se charge pas",
        39 => "pilote manquant ou corrompu",
        43 => "arrêté après avoir signalé un problème",
        _ => $"erreur {code}",
    };
}
