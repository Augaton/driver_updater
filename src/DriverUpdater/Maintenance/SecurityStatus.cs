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
        return new SecurityState(Antiviruses(), Firewall(), askWindowsUpdate ? PendingWindowsUpdates() : null);
    }

    // Security Center does not exist on Windows Server: no antivirus listed then.
    static List<Antivirus> Antiviruses()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\SecurityCenter2", "SELECT displayName, productState FROM AntiVirusProduct");
            using var results = searcher.Get();
            return results.Cast<ManagementBaseObject>()
                .Select(a => (Name: a["displayName"] as string ?? "Antivirus", State: Convert.ToUInt32(a["productState"] ?? 0u)))
                .Select(a => new Antivirus(a.Name, SecurityCenter.IsEnabled(a.State), SecurityCenter.IsUpToDate(a.State)))
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
