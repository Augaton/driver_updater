using System.Management;

namespace DriverUpdater;

public sealed record ProblemDevice(string Name, string Meaning);
public sealed record Disk(string Name, string Type, string Size, string Health, string Wear, string Temperature);
public sealed record Battery(string Name, string Designed, string FullCharge, string Health, string Cycles);
public sealed record RestorePointResult(bool Ok, string Message);

public sealed record Gpu(string Name, string Driver, string? VendorTool, string? VendorUrl)
{
    public bool HasVendorTool => VendorUrl is not null;
}

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
        using var searcher = new ManagementObjectSearcher(@"root\Microsoft\Windows\Storage", "SELECT * FROM MSFT_PhysicalDisk");
        using var results = searcher.Get();
        var list = new List<Disk>();
        foreach (ManagementObject disk in results)
            using (disk)
            {
                var (wear, temperature) = ReliabilityCounters(disk);
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
                    },
                    wear is { } w ? $"{w} %" : "—",
                    temperature is > 0 and var t ? $"{t} °C" : "—"));
            }
        return list;
    }

    /// <summary>
    /// SSD wear (percentage of the rated endurance used) and temperature. Many disks, and
    /// most virtual ones, do not report them: those come back as null.
    /// </summary>
    static (int? Wear, int? Temperature) ReliabilityCounters(ManagementObject disk)
    {
        try
        {
            using var related = disk.GetRelated("MSFT_StorageReliabilityCounter");
            foreach (ManagementBaseObject counter in related)
                using (counter)
                    return (ToInt(counter["Wear"]), ToInt(counter["Temperature"]));
        }
        catch (ManagementException)
        {
        }
        return (null, null);
    }

    /// <summary>Laptop batteries: capacity left compared with new. Empty on a desktop.</summary>
    public static List<Battery> Batteries()
    {
        var scope = new ManagementScope(@"\\.\root\wmi");
        try
        {
            var full = ByInstance(scope, "SELECT InstanceName, FullChargedCapacity FROM BatteryFullChargedCapacity", "FullChargedCapacity");
            var cycles = ByInstance(scope, "SELECT InstanceName, CycleCount FROM BatteryCycleCount", "CycleCount");
            using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT InstanceName, DeviceName, DesignedCapacity FROM BatteryStaticData"));
            using var results = searcher.Get();
            var list = new List<Battery>();
            foreach (ManagementBaseObject battery in results)
                using (battery)
                {
                    var instance = (string)battery["InstanceName"];
                    int designed = ToInt(battery["DesignedCapacity"]) ?? 0;
                    int? charge = full.GetValueOrDefault(instance);
                    list.Add(new Battery(
                        (battery["DeviceName"] as string)?.Trim() is { Length: > 0 } name ? name : "Batterie",
                        designed > 0 ? $"{designed / 1000.0:0.#} Wh" : "—",
                        charge > 0 ? $"{charge / 1000.0:0.#} Wh" : "—",
                        designed > 0 && charge > 0 ? $"{100 * charge / designed} %" : "—",
                        cycles.GetValueOrDefault(instance) is > 0 and var c ? c.ToString() : "—"));
                }
            return list;
        }
        catch (ManagementException)
        {
            return []; // no battery, or a firmware without these classes
        }
    }

    static Dictionary<string, int?> ByInstance(ManagementScope scope, string query, string property)
    {
        var values = new Dictionary<string, int?>();
        try
        {
            using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery(query));
            using var results = searcher.Get();
            foreach (ManagementBaseObject item in results)
                using (item)
                    values[(string)item["InstanceName"]] = ToInt(item[property]);
        }
        catch (ManagementException)
        {
        }
        return values;
    }

    public static List<Gpu> Gpus()
    {
        using var searcher = new ManagementObjectSearcher("SELECT Name, PNPDeviceID, DriverVersion, DriverDate FROM Win32_VideoController");
        using var results = searcher.Get();
        var list = new List<Gpu>();
        foreach (ManagementBaseObject gpu in results)
            using (gpu)
            {
                var device = gpu["PNPDeviceID"] as string ?? "";
                if (device.StartsWith(@"ROOT\", StringComparison.OrdinalIgnoreCase)) continue; // virtual display adapters
                var driver = gpu["DriverVersion"] is string version
                    ? "Pilote " + new InstalledDriver(version, ParseDate(gpu["DriverDate"] as string))
                    : "Pilote inconnu";
                var (tool, url) = VendorTool(device);
                list.Add(new Gpu(gpu["Name"] as string ?? "Carte graphique", driver, tool, url));
            }
        return list;
    }

    // Vendor IDs from the PCI ID: 10DE NVIDIA, 1002 AMD, 8086 Intel.
    static (string? Tool, string? Url) VendorTool(string pnpDeviceId) =>
        pnpDeviceId.ToUpperInvariant() switch
        {
            var id when id.Contains("VEN_10DE") => ("NVIDIA App", "https://www.nvidia.com/fr-fr/software/nvidia-app/"),
            var id when id.Contains("VEN_1002") => ("AMD Software", "https://www.amd.com/fr/support/download/drivers.html"),
            var id when id.Contains("VEN_8086") => ("l'Assistant Intel", "https://www.intel.com/content/www/fr/fr/support/intel-driver-support-assistant.html"),
            _ => (null, null),
        };

    static int? ToInt(object? value) => value is null ? null : Convert.ToInt32(value);

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
