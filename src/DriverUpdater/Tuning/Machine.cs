using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace DriverUpdater.Tuning;

/// <summary>Facts about this PC that the gaming checks need, read without administrator rights.</summary>
public static class Machine
{
    public static readonly Guid Balanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    public static readonly Guid HighPerformance = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    public static readonly Guid PowerSaver = new("a1841308-3541-4fab-bc81-f71556f20b4a");

    public static bool HasBattery => !System.Windows.Forms.SystemInformation.PowerStatus.BatteryChargeStatus
        .HasFlag(System.Windows.Forms.BatteryChargeStatus.NoSystemBattery);

    public static string ProcessorName()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
        return (key?.GetValue("ProcessorNameString") as string)?.Trim() ?? "";
    }

    /// <summary>
    /// A Ryzen X3D with the 3D cache on one of its two dies (7900X3D, 7950X3D, 9900X3D,
    /// 9950X3D): its level-3 caches differ in size. Games must stay on the cache die, which
    /// AMD's driver arranges through Game Mode and the Balanced plan.
    /// </summary>
    public static bool IsDualDieX3D()
    {
        // Ryzen AI 300 also has two level-3 cache sizes (Zen 5 and Zen 5c cores): the name decides.
        var name = ProcessorName();
        if (!name.Contains("AMD", StringComparison.OrdinalIgnoreCase) || !name.Contains("X3D", StringComparison.OrdinalIgnoreCase)) return false;
        const int RelationCache = 2;
        uint length = 0;
        GetLogicalProcessorInformationEx(RelationCache, IntPtr.Zero, ref length);
        if (length == 0) return false;
        var buffer = Marshal.AllocHGlobal((int)length);
        try
        {
            if (!GetLogicalProcessorInformationEx(RelationCache, buffer, ref length)) return false;
            var l3Sizes = new HashSet<uint>();
            for (int offset = 0; offset < length;)
            {
                // SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX: Relationship, Size, then CACHE_RELATIONSHIP
                // (Level at +8, CacheSize at +12).
                int size = Marshal.ReadInt32(buffer, offset + 4);
                if (size <= 0) break;
                if (Marshal.ReadByte(buffer, offset + 8) == 3) l3Sizes.Add((uint)Marshal.ReadInt32(buffer, offset + 12));
                offset += size;
            }
            return l3Sizes.Count > 1;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    /// <summary>The active power plan, or null if Windows does not say.</summary>
    public static Guid? ActivePowerPlan()
    {
        if (PowerGetActiveScheme(IntPtr.Zero, out var pointer) != 0) return null;
        try
        {
            return Marshal.PtrToStructure<Guid>(pointer);
        }
        finally
        {
            LocalFree(pointer);
        }
    }

    /// <summary>The Xbox Game Bar, installed for this user (AMD's X3D driver relies on it to spot games).</summary>
    public static bool GameBarInstalled()
    {
        uint count = 0, bufferLength = 0;
        int result = GetPackagesByPackageFamily("Microsoft.XboxGamingOverlay_8wekyb3d8bbwe", ref count, IntPtr.Zero, ref bufferLength, IntPtr.Zero);
        return result is 0 or 122 && count > 0; // 122: buffer too small, meaning there is at least one
    }

    public static bool GameModeOn()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\GameBar");
        return key?.GetValue("AutoGameModeEnabled") is not 0; // absent means on
    }

    /// <summary>AMD's "3D V-Cache Performance Optimizer" (ACPI\AMDI0101): null if absent, else whether it works.</summary>
    public static bool? VCacheOptimizerWorks()
    {
        using var searcher = new ManagementObjectSearcher("SELECT ConfigManagerErrorCode FROM Win32_PnPEntity WHERE DeviceID LIKE 'ACPI\\\\AMDI0101%'");
        using var results = searcher.Get();
        var device = results.Cast<ManagementBaseObject>().FirstOrDefault();
        if (device is null) return null;
        using (device)
            return Convert.ToUInt32(device["ConfigManagerErrorCode"] ?? 1u) == 0;
    }

    /// <summary>
    /// Whether the folder's disk is a spinning one (it "incurs a seek penalty"), or null if the
    /// disk does not say (some USB and RAID controllers).
    /// </summary>
    public static bool? OnHardDisk(string folder)
    {
        var volumePath = new char[512];
        if (!GetVolumePathNameW(folder, volumePath, (uint)volumePath.Length)) return null;
        var volumeName = new char[64];
        if (!GetVolumeNameForVolumeMountPointW(new string(volumePath).TrimEnd('\0'), volumeName, (uint)volumeName.Length)) return null;
        var volume = new string(volumeName).TrimEnd('\0').TrimEnd('\\'); // \\?\Volume{…}

        // No access rights asked: this query needs none.
        using var handle = CreateFileW(volume, 0, FileShare.ReadWrite, IntPtr.Zero, FileMode.Open, 0, IntPtr.Zero);
        if (handle.IsInvalid) return null;
        const uint QueryProperty = 0x2D1400; // IOCTL_STORAGE_QUERY_PROPERTY
        var query = new PropertyQuery { PropertyId = 7 }; // StorageDeviceSeekPenaltyProperty, standard query
        if (!DeviceIoControl(handle, QueryProperty, ref query, (uint)Marshal.SizeOf<PropertyQuery>(),
                out SeekPenalty answer, (uint)Marshal.SizeOf<SeekPenalty>(), out _, IntPtr.Zero))
            return null;
        return answer.IncursSeekPenalty != 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PropertyQuery
    {
        public int PropertyId, QueryType;
        public byte AdditionalParameters;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct SeekPenalty
    {
        public uint Version, Size;
        public byte IncursSeekPenalty;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetLogicalProcessorInformationEx(int relationship, IntPtr buffer, ref uint length);

    [DllImport("powrprof.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern uint PowerGetActiveScheme(IntPtr rootPowerKey, out IntPtr scheme);

    [DllImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern IntPtr LocalFree(IntPtr memory);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern int GetPackagesByPackageFamily(string family, ref uint count, IntPtr names, ref uint bufferLength, IntPtr buffer);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetVolumePathNameW(string path, [Out] char[] volumePath, uint length);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetVolumeNameForVolumeMountPointW(string mountPoint, [Out] char[] volumeName, uint length);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern SafeFileHandle CreateFileW(string fileName, uint access, FileShare share, IntPtr security,
        FileMode mode, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool DeviceIoControl(SafeFileHandle device, uint code, ref PropertyQuery input, uint inputSize,
        out SeekPenalty output, uint outputSize, out uint returned, IntPtr overlapped);
}
