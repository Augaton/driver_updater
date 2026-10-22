using System.Management;

namespace DriverUpdater.Drivers;

/// <summary>Every device present in the PC with its driver, in one pass over WMI.</summary>
public static class DriverInventory
{
    public static List<DeviceDriver> Scan()
    {
        var drivers = new Dictionary<string, ManagementValues>(StringComparer.OrdinalIgnoreCase);
        foreach (var driver in Query("SELECT DeviceID, DriverProviderName, DriverVersion, DriverDate, IsSigned, InfName FROM Win32_PnPSignedDriver"))
            if (driver.Get("DeviceID") is string id)
                drivers[id] = driver;

        var devices = new List<DeviceDriver>();
        foreach (var entity in Query("SELECT DeviceID, Name, PNPClass, ConfigManagerErrorCode, HardwareID, CompatibleID FROM Win32_PnPEntity"))
        {
            if (entity.Get("DeviceID") is not string id) continue;
            drivers.TryGetValue(id, out var driver);
            devices.Add(new DeviceDriver
            {
                DeviceId = id,
                Name = (entity.Get("Name") as string)?.Trim() is { Length: > 0 } name ? name : id,
                Class = entity.Get("PNPClass") as string ?? "",
                Provider = (driver?.Get("DriverProviderName") as string)?.Trim() ?? "",
                Version = driver?.Get("DriverVersion") as string ?? "",
                Date = SystemInfo.ParseWmiDate(driver?.Get("DriverDate") as string),
                Signed = driver?.Get("IsSigned") as bool?,
                Problem = entity.Get("ConfigManagerErrorCode") is { } code ? Convert.ToInt32(code) : 0,
                InfName = driver?.Get("InfName") as string ?? "",
                HardwareIds = entity.Get("HardwareID") as string[] ?? [],
                CompatibleIds = DeviceDriver.ChipIds(entity.Get("CompatibleID") as string[] ?? []),
            });
        }
        return devices.OrderBy(d => d.Class, StringComparer.OrdinalIgnoreCase).ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>A WMI object's values, copied out so the objects themselves can be released at once.</summary>
    sealed class ManagementValues(Dictionary<string, object?> values)
    {
        public object? Get(string name) => values.GetValueOrDefault(name);
    }

    static List<ManagementValues> Query(string query)
    {
        using var searcher = new ManagementObjectSearcher(query);
        using var results = searcher.Get();
        var list = new List<ManagementValues>();
        foreach (ManagementBaseObject item in results)
            using (item)
                list.Add(new ManagementValues(item.Properties.Cast<PropertyData>().ToDictionary(p => p.Name, p => (object?)p.Value)));
        return list;
    }
}
