using System.ComponentModel;
using System.Runtime.InteropServices;

namespace DriverUpdater.Drivers;

/// <summary>
/// Device Manager's "Roll back driver", for one device (administrator side): Windows puts back
/// the exact driver it used before the last update, which it keeps for that purpose.
/// </summary>
public static class DriverRollback
{
    const uint NoUi = 0x1;
    const int NoPreviousDriver = 259; // ERROR_NO_MORE_ITEMS: what DiRollbackDriver reports without a backup driver

    /// <returns>Whether Windows must restart to finish.</returns>
    public static bool RollBack(string deviceInstanceId)
    {
        var set = SetupDiCreateDeviceInfoList(IntPtr.Zero, IntPtr.Zero);
        if (set == new IntPtr(-1)) throw new Win32Exception();
        try
        {
            var device = new DeviceInfoData { Size = (uint)Marshal.SizeOf<DeviceInfoData>() };
            if (!SetupDiOpenDeviceInfoW(set, deviceInstanceId, IntPtr.Zero, 0, ref device))
                throw new InvalidOperationException("Périphérique introuvable : il a peut-être été débranché.");
            if (!DiRollbackDriver(set, ref device, IntPtr.Zero, NoUi, out bool reboot))
            {
                int error = Marshal.GetLastWin32Error();
                throw new InvalidOperationException(error == NoPreviousDriver
                    ? "Windows n'a pas gardé de version précédente pour ce pilote."
                    : $"Retour en arrière refusé par Windows ({new Win32Exception(error).Message}).");
            }
            return reboot;
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct DeviceInfoData
    {
        public uint Size;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [DllImport("setupapi.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern IntPtr SetupDiCreateDeviceInfoList(IntPtr classGuid, IntPtr parent);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetupDiOpenDeviceInfoW(IntPtr set, string instanceId, IntPtr parent, uint flags, ref DeviceInfoData data);

    [DllImport("setupapi.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

    [DllImport("newdev.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool DiRollbackDriver(IntPtr set, ref DeviceInfoData data, IntPtr parent, uint flags,
        [MarshalAs(UnmanagedType.Bool)] out bool needReboot);
}
