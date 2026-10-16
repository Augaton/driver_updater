using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace DriverUpdater.Tuning;

/// <summary>
/// The Windows side of "Défaire les faux boosts": the facts that decide which repairs concern
/// this PC, and the repairs that are not registry values (boot options, page file, the power
/// plan's core parking). The fixing ones run in the administrator helper, except core parking.
/// </summary>
public static class FakeBoostFixes
{
    static readonly Guid ProcessorSettings = new("54533251-82be-4824-96c1-47b60b740d00"); // SUB_PROCESSOR
    static readonly Guid MinimumCores = new("0cc5b647-c1df-4637-891a-dec35c318583");      // CPMINCORES
    static readonly string[] TimerOverrides = ["useplatformclock", "useplatformtick", "disabledynamictick", "tscsyncpolicy"];

    public static PcContext Context() => new(
        Laptop: Machine.HasBattery,
        Client: Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "InstallationType", null) as string != "Server",
        Managed: Managed(),
        DualDieX3D: Machine.IsDualDieX3D(),
        XboxApp: Machine.PackageInstalled("Microsoft.GamingApp_8wekyb3d8bbwe") || Machine.PackageInstalled("Microsoft.GamingServices_8wekyb3d8bbwe"),
        OtherAntivirus: SecurityStatus.OtherAntivirus());

    /// <summary>
    /// Managed by a company: joined to a domain or to Entra ID, enrolled in a device manager, or
    /// told to use a company update server. Its settings may be what its IT wants.
    /// </summary>
    static bool Managed()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT PartOfDomain FROM Win32_ComputerSystem");
            using var results = searcher.Get();
            if (results.Cast<ManagementBaseObject>().Any(c => c["PartOfDomain"] is true)) return true;
        }
        catch (ManagementException)
        {
        }
        if (NetGetAadJoinInformation(null, out var join) == 0 && join != IntPtr.Zero)
        {
            int type = Marshal.ReadInt32(join); // DSREG_JOIN_INFO.joinType: 1 is a device joined to Entra ID
            NetFreeAadJoinInformation(join);
            if (type == 1) return true;
        }
        if (Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "UseWUServer", null) is 1) return true;
        using var enrollments = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Enrollments");
        foreach (var name in enrollments?.GetSubKeyNames() ?? [])
        {
            using var enrollment = enrollments!.OpenSubKey(name);
            if (enrollment?.GetValue("ProviderID") is string { Length: > 0 } && enrollment.GetValue("EnrollmentState") is 1) return true;
        }
        return false;
    }

    /// <summary>
    /// The performance counter runs at the motherboard timer's frequency (HPET 14.318 MHz, ACPI
    /// 3.579 MHz) only when the boot options force it: Windows otherwise uses the processor's
    /// own, reported at 10 MHz. Readable without rights; the boot options themselves are not.
    /// </summary>
    public static bool PlatformClockForced() => QueryPerformanceFrequency(out long frequency) && frequency is 14_318_180 or 3_579_545;

    /// <summary>Administrator helper: removes the timer options from the current boot entry.</summary>
    public static void RemoveTimerOverrides()
    {
        var bcdedit = Path.Combine(Environment.SystemDirectory, "bcdedit.exe");
        var current = Run(bcdedit, "/enum", "{current}").Output;
        var present = TimerOverrides.Where(option => current.Split('\n').Any(line => line.TrimStart().StartsWith(option + " ", StringComparison.OrdinalIgnoreCase))).ToList();
        if (present.Count == 0) return;
        // BitLocker checks the boot options: suspended for one restart, as Microsoft asks,
        // so the change does not lock the PC behind its recovery key.
        SuspendBitLockerOnce();
        foreach (var option in present)
            if (Run(bcdedit, "/deletevalue", "{current}", option).ExitCode != 0)
                throw new InvalidOperationException(L.F("bcdedit refuse de retirer « {0} ».", option));
    }

    static void SuspendBitLockerOnce()
    {
        var drive = Path.GetPathRoot(Environment.SystemDirectory)!.TrimEnd('\\');
        try
        {
            var scope = new ManagementScope(@"\\.\root\cimv2\Security\MicrosoftVolumeEncryption");
            using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery($"SELECT * FROM Win32_EncryptableVolume WHERE DriveLetter = '{drive}'"));
            using var volumes = searcher.Get();
            foreach (ManagementObject volume in volumes)
                using (volume)
                {
                    using var status = volume.InvokeMethod("GetProtectionStatus", null, null);
                    if (status?["ProtectionStatus"] is not 1u) continue;
                    using var arguments = volume.GetMethodParameters("DisableKeyProtectors");
                    arguments["DisableCount"] = 1u;
                    volume.InvokeMethod("DisableKeyProtectors", arguments, null);
                }
        }
        catch (ManagementException)
        {
            // No BitLocker on this edition of Windows: nothing to suspend.
        }
    }

    /// <summary>No page file at all, and Windows not told to manage one.</summary>
    public static bool NoPageFile()
    {
        using var system = new ManagementObjectSearcher("SELECT AutomaticManagedPagefile FROM Win32_ComputerSystem");
        using var systems = system.Get();
        if (systems.Cast<ManagementBaseObject>().Any(c => c["AutomaticManagedPagefile"] is true)) return false;
        using var settings = new ManagementObjectSearcher("SELECT Name FROM Win32_PageFileSetting");
        using var files = settings.Get();
        return files.Count == 0;
    }

    /// <summary>Administrator helper: the page file back under Windows' management (from the next start).</summary>
    public static void SystemManagedPageFile()
    {
        var scope = new ManagementScope(@"\\.\root\cimv2", new ConnectionOptions { EnablePrivileges = true });
        using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM Win32_ComputerSystem"));
        using var systems = searcher.Get();
        foreach (ManagementObject computer in systems)
            using (computer)
            {
                computer["AutomaticManagedPagefile"] = true;
                computer.Put();
            }
    }

    /// <summary>On a dual-CCD Ryzen X3D in the Balanced plan, core parking held off (its minimum away from the plan's own).</summary>
    public static bool ParkingOff()
    {
        if (Machine.ActivePowerPlan() != Machine.Balanced || !Machine.IsDualDieX3D()) return false;
        var plan = Machine.Balanced;
        return PowerReadACValueIndex(IntPtr.Zero, plan, ProcessorSettings, MinimumCores, out uint value) == 0
               && PowerReadACDefaultIndex(IntPtr.Zero, plan, ProcessorSettings, MinimumCores, out uint standard) == 0
               && value != standard;
    }

    /// <summary>The Balanced plan's own minimum back (a user's plan: no rights needed).</summary>
    public static void DefaultParking()
    {
        var plan = Machine.Balanced;
        if (PowerReadACDefaultIndex(IntPtr.Zero, plan, ProcessorSettings, MinimumCores, out uint standard) != 0
            || PowerWriteACValueIndex(IntPtr.Zero, plan, ProcessorSettings, MinimumCores, standard) != 0
            || PowerSetActiveScheme(IntPtr.Zero, plan) != 0)
            throw new InvalidOperationException(L.T("Windows refuse de modifier le plan d'alimentation."));
    }

    static (int ExitCode, string Output) Run(string program, params string[] arguments)
    {
        var start = new ProcessStartInfo(program) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }

    [DllImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool QueryPerformanceFrequency(out long frequency);

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern int NetGetAadJoinInformation(string? tenantId, out IntPtr joinInfo);

    [DllImport("netapi32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern void NetFreeAadJoinInformation(IntPtr joinInfo);

    [DllImport("powrprof.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern uint PowerReadACValueIndex(IntPtr root, in Guid scheme, in Guid subgroup, in Guid setting, out uint value);

    [DllImport("powrprof.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern uint PowerReadACDefaultIndex(IntPtr root, in Guid personality, in Guid subgroup, in Guid setting, out uint value);

    [DllImport("powrprof.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern uint PowerWriteACValueIndex(IntPtr root, in Guid scheme, in Guid subgroup, in Guid setting, uint value);

    [DllImport("powrprof.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern uint PowerSetActiveScheme(IntPtr root, in Guid scheme);
}
