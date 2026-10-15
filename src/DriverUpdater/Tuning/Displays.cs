using System.Runtime.InteropServices;

namespace DriverUpdater.Tuning;

public sealed record Screen(string Name, int CurrentHz, int MaxHz);

/// <summary>
/// Each screen's refresh rate, and the highest it offers at its current resolution: a 144 Hz
/// screen left at 60 Hz is the most common, and most costly, gaming mistake.
/// </summary>
public static class Displays
{
    const int CurrentSettings = -1;
    const int AttachedToDesktop = 0x1;

    public static List<Screen> Screens()
    {
        var screens = new List<Screen>();
        var adapter = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
        for (uint i = 0; EnumDisplayDevicesW(null, i, ref adapter, 0); i++, adapter.Size = Marshal.SizeOf<DisplayDevice>())
        {
            if ((adapter.StateFlags & AttachedToDesktop) == 0) continue;
            var current = new DevMode { Size = (short)Marshal.SizeOf<DevMode>() };
            if (!EnumDisplaySettingsW(adapter.DeviceName, CurrentSettings, ref current)) continue;

            int max = current.DisplayFrequency;
            var mode = new DevMode { Size = (short)Marshal.SizeOf<DevMode>() };
            for (int m = 0; EnumDisplaySettingsW(adapter.DeviceName, m, ref mode); m++)
                if (mode.PelsWidth == current.PelsWidth && mode.PelsHeight == current.PelsHeight && mode.DisplayFrequency > max)
                    max = mode.DisplayFrequency;

            // The monitor's own name sits one level below its adapter.
            var monitor = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
            var name = EnumDisplayDevicesW(adapter.DeviceName, 0, ref monitor, 0) && monitor.DeviceString.Length > 0
                ? monitor.DeviceString : adapter.DeviceString;
            screens.Add(new Screen($"{name} ({current.PelsWidth} × {current.PelsHeight})", current.DisplayFrequency, max));
        }
        return screens;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DisplayDevice
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    // DEVMODEW, display variant of its unions.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        public short SpecVersion, DriverVersion, Size, DriverExtra;
        public int Fields;
        public int PositionX, PositionY, DisplayOrientation, DisplayFixedOutput;
        public short Color, Duplex, YResolution, TTOption, Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string FormName;
        public short LogPixels;
        public int BitsPerPel, PelsWidth, PelsHeight, DisplayFlags, DisplayFrequency;
        public int IcmMethod, IcmIntent, MediaType, DitherType, Reserved1, Reserved2, PanningWidth, PanningHeight;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool EnumDisplayDevicesW(string? device, uint index, ref DisplayDevice displayDevice, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool EnumDisplaySettingsW(string device, int mode, ref DevMode devMode);
}
