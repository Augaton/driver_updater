using System.Management;
using System.Reflection;
using System.Runtime.InteropServices;

namespace DriverUpdater.Maintenance;

/// <summary>
/// The basics Windows Security already watches, read from Windows itself: antivirus on and up
/// to date (Security Center), firewall on, Windows updates waiting. No "security score":
/// only facts, each with where to fix it.
/// </summary>
public static class SecurityStatus
{
    public static SecurityState Read(bool askWindowsUpdate)
    {
        var firewall = Firewall();
        // A suite's firewall only matters when Windows' own is not on.
        return new SecurityState(Antiviruses(), firewall, askWindowsUpdate ? PendingWindowsUpdates() : null, firewall == true ? [] : OtherFirewalls());
    }

    /// <summary>An active antivirus other than Microsoft Defender: Defender then stays passive on purpose.</summary>
    public static bool OtherAntivirus() => SecurityCenter.OtherActiveAntivirus(Antiviruses());

    static List<Antivirus> Antiviruses() =>
        Products("AntiVirusProduct", "Antivirus")
            .Select(a => new Antivirus(a.Name, SecurityCenter.IsEnabled(a.State), SecurityCenter.IsUpToDate(a.State)))
            .ToList();

    // A security suite registers its firewall with Security Center, as it does its antivirus.
    static List<string> OtherFirewalls() =>
        Products("FirewallProduct", L.T("Pare-feu")).Where(f => SecurityCenter.IsEnabled(f.State)).Select(f => f.Name).ToList();

    // Security Center does not exist on Windows Server: nothing listed then.
    static List<(string Name, uint State)> Products(string wmiClass, string unnamed)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\SecurityCenter2", $"SELECT displayName, productState FROM {wmiClass}");
            using var results = searcher.Get();
            return results.Cast<ManagementBaseObject>()
                .Select(p => (p["displayName"] as string ?? unnamed, Convert.ToUInt32(p["productState"] ?? 0u)))
                .ToList();
        }
        catch (ManagementException)
        {
            return [];
        }
    }

    // Each of the domain, private and public profiles; "on" when the profile in use is on.
    static bool? Firewall()
    {
        object? policy = null;
        try
        {
            var firewall = policy = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2", throwOnError: true)!)!;
            object? Get(string property, params object[] args) =>
                firewall.GetType().InvokeMember(property, BindingFlags.GetProperty, null, firewall, args, System.Globalization.CultureInfo.InvariantCulture);
            int current = Convert.ToInt32(Get("CurrentProfileTypes"), System.Globalization.CultureInfo.InvariantCulture);
            foreach (int profile in new[] { 1, 2, 4 })
                if ((current & profile) != 0 && Get("FirewallEnabled", profile) is false) return false;
            return true;
        }
        catch (Exception e) when (e is COMException or InvalidCastException or TargetInvocationException or TypeLoadException or MissingMethodException)
        {
            return null;
        }
        finally
        {
            if (policy is not null && Marshal.IsComObject(policy)) Marshal.ReleaseComObject(policy);
        }
    }

    static int? PendingWindowsUpdates()
    {
        try
        {
            return WindowsUpdateDrivers.CountPendingSoftware();
        }
        catch (Exception e) when (e is COMException or InvalidOperationException)
        {
            return null;
        }
    }
}
