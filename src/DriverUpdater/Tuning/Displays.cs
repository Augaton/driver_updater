using System.Runtime.InteropServices;

namespace DriverUpdater.Tuning;

/// <param name="CurrentHz">The exact refresh rate (143.98, 59.94…).</param>
/// <param name="MaxHz">The best rate the screen offers at its current resolution.</param>
/// <param name="External">Plugged into a port (not a laptop's own panel, a wireless or virtual screen).</param>
/// <param name="Adapter">The graphics adapter driving it (its LUID).</param>
/// <param name="Device">Its GDI name ("\\.\DISPLAY1"), to change its mode.</param>
public sealed record Screen(string Name, double CurrentHz, int MaxHz, bool External, long Adapter, string Device);

/// <param name="Integrated">The processor's own graphics.</param>
public sealed record GraphicsAdapter(long Luid, bool Integrated);

/// <summary>
/// The screens in use and the graphics adapters behind them, read the way Windows' display
/// settings do (QueryDisplayConfig), and DXCore to tell integrated graphics from a card.
/// </summary>
public static class Displays
{
    const uint OnlyActivePaths = 2;
    const int GetSourceName = 1, GetTargetName = 2;

    public static List<Screen> Screens()
    {
        if (GetDisplayConfigBufferSizes(OnlyActivePaths, out uint pathCount, out uint modeCount) != 0) return [];
        var paths = new PathInfo[pathCount];
        var modes = new byte[Math.Max(1, modeCount) * 64]; // DISPLAYCONFIG_MODE_INFO, not read here
        if (QueryDisplayConfig(OnlyActivePaths, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0) return [];

        var screens = new List<Screen>();
        foreach (var path in paths.Take((int)pathCount))
        {
            var source = new SourceName { Header = new() { Type = GetSourceName, Size = Marshal.SizeOf<SourceName>(), Adapter = path.SourceAdapter, Id = path.SourceId } };
            if (DisplayConfigGetDeviceInfo(ref source) != 0) continue;
            var target = new TargetName { Header = new() { Type = GetTargetName, Size = Marshal.SizeOf<TargetName>(), Adapter = path.TargetAdapter, Id = path.TargetId } };
            string name = DisplayConfigGetDeviceInfo(ref target) == 0 && target.FriendlyName.Length > 0 ? target.FriendlyName : L.T("sans nom");

            double current = path.RefreshDenominator == 0 ? 0 : (double)path.RefreshNumerator / path.RefreshDenominator;
            var (width, height, max) = BestMode(source.GdiName);
            screens.Add(new Screen($"{name} ({width} × {height})", Math.Round(current, 2), Math.Max(max, (int)Math.Round(current)),
                IsExternal(path.OutputTechnology), path.SourceAdapter.Value, source.GdiName));
        }
        return screens;
    }

    // Internal panels, wireless and virtual screens: their wiring is not the user's to change.
    static bool IsExternal(uint technology) => technology switch
    {
        6 or 11 or 13 => false,        // LVDS, embedded DisplayPort, embedded UDI: a laptop's own screen
        15 or 16 or 17 => false,       // Miracast, indirect wired (DisplayLink), indirect virtual
        0x80000000 => false,           // internal
        _ => true,
    };

    // The current resolution, and the best refresh rate offered at it (interlaced modes aside).
    static (int Width, int Height, int MaxHz) BestMode(string device)
    {
        const int CurrentSettings = -1, Interlaced = 0x2;
        var current = new DevMode { Size = (short)Marshal.SizeOf<DevMode>() };
        if (!EnumDisplaySettingsW(device, CurrentSettings, ref current)) return (0, 0, 0);
        int max = 0;
        var mode = new DevMode { Size = (short)Marshal.SizeOf<DevMode>() };
        for (int i = 0; EnumDisplaySettingsW(device, i, ref mode); i++)
            if (mode.PelsWidth == current.PelsWidth && mode.PelsHeight == current.PelsHeight
                && (mode.DisplayFlags & Interlaced) == 0 && mode.DisplayFrequency > max)
                max = mode.DisplayFrequency;
        return (current.PelsWidth, current.PelsHeight, max);
    }

    /// <summary>
    /// Sets the screen's refresh rate at its current resolution. Not saved: Windows goes back
    /// to the saved mode at the next sign-in, or at once with <see cref="RevertMode"/>, until
    /// <see cref="SaveRefresh"/> keeps it. False if Windows refuses the mode.
    /// </summary>
    public static bool TryRefresh(string device, int hz) => ChangeRefresh(device, hz, save: false);

    public static bool SaveRefresh(string device, int hz) => ChangeRefresh(device, hz, save: true);

    /// <summary>Back to the mode Windows has saved for the screen.</summary>
    public static void RevertMode(string device) => ChangeDisplaySettingsExW(device, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);

    static bool ChangeRefresh(string device, int hz, bool save)
    {
        const int CurrentSettings = -1, Frequency = 0x400000; // DM_DISPLAYFREQUENCY
        const uint Test = 0x2, UpdateRegistry = 0x1;
        var mode = new DevMode { Size = (short)Marshal.SizeOf<DevMode>() };
        if (!EnumDisplaySettingsW(device, CurrentSettings, ref mode)) return false;
        mode.DisplayFrequency = hz;
        mode.Fields = Frequency;
        return ChangeDisplaySettingsExW(device, ref mode, IntPtr.Zero, Test, IntPtr.Zero) == 0
               && ChangeDisplaySettingsExW(device, ref mode, IntPtr.Zero, save ? UpdateRegistry : 0, IntPtr.Zero) == 0;
    }

    /// <summary>The hardware graphics adapters (no software renderer), or empty when DXCore cannot tell.</summary>
    public static List<GraphicsAdapter> Adapters()
    {
        const uint IsHardware = 11, IsIntegrated = 12, InstanceLuid = 0;
        var list = new List<GraphicsAdapter>();
        IDXCoreAdapterFactory factory;
        try
        {
            if (DXCoreCreateAdapterFactory(typeof(IDXCoreAdapterFactory).GUID, out factory) != 0) return list;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return list; // DXCore came with Windows 10 1903
        }
        try
        {
            var graphics = new Guid("0c9ece4d-2f6e-4f01-8c96-e89e331b47b1"); // DXCORE_ADAPTER_ATTRIBUTE_D3D12_GRAPHICS
            if (factory.CreateAdapterList(1, ref graphics, typeof(IDXCoreAdapterList).GUID, out var adapters) != 0) return list;
            try
            {
                for (uint i = 0; i < adapters.GetAdapterCount(); i++)
                {
                    if (adapters.GetAdapter(i, typeof(IDXCoreAdapter).GUID, out var adapter) != 0) continue;
                    try
                    {
                        if (!adapter.IsPropertySupported(IsIntegrated) || !adapter.IsPropertySupported(IsHardware)) continue;
                        // bool properties are one byte; the LUID is eight.
                        if (Property(adapter, IsHardware, 1) != 0)
                            list.Add(new GraphicsAdapter(Property(adapter, InstanceLuid, 8), Property(adapter, IsIntegrated, 1) != 0));
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(adapter);
                    }
                }
            }
            finally
            {
                Marshal.ReleaseComObject(adapters);
            }
        }
        finally
        {
            Marshal.ReleaseComObject(factory);
        }
        return list;
    }

    // ---- QueryDisplayConfig (wingdi.h) ------------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    struct Luid
    {
        public uint Low;
        public int High;
        public readonly long Value => ((long)High << 32) | Low;
    }

    // DISPLAYCONFIG_PATH_INFO, 72 bytes: source info, target info, flags.
    [StructLayout(LayoutKind.Sequential)]
    struct PathInfo
    {
        public Luid SourceAdapter;
        public uint SourceId, SourceModeIndex, SourceStatus;
        public Luid TargetAdapter;
        public uint TargetId, TargetModeIndex, OutputTechnology, Rotation, Scaling;
        public uint RefreshNumerator, RefreshDenominator;
        public uint ScanLineOrdering;
        public int TargetAvailable;
        public uint TargetStatus;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct DeviceInfoHeader
    {
        public int Type, Size;
        public Luid Adapter;
        public uint Id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SourceName
    {
        public DeviceInfoHeader Header;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string GdiName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct TargetName
    {
        public DeviceInfoHeader Header;
        public uint Flags, OutputTechnology;
        public ushort ManufactureId, ProductCodeId;
        public uint ConnectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string FriendlyName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DevicePath;
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

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern int QueryDisplayConfig(uint flags, ref uint pathCount, [Out] PathInfo[] paths, ref uint modeCount, [Out] byte[] modes, IntPtr topology);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern int DisplayConfigGetDeviceInfo(ref SourceName request);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern int DisplayConfigGetDeviceInfo(ref TargetName request);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool EnumDisplaySettingsW(string device, int mode, ref DevMode devMode);

    // DISP_CHANGE_SUCCESSFUL is 0.
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern int ChangeDisplaySettingsExW(string device, ref DevMode devMode, IntPtr window, uint flags, IntPtr parameter);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern int ChangeDisplaySettingsExW(string device, IntPtr devMode, IntPtr window, uint flags, IntPtr parameter);

    // ---- DXCore (dxcore_interface.h, Microsoft DirectX-Headers): vtable order matters ------------

    [DllImport("dxcore.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern int DXCoreCreateAdapterFactory(in Guid riid, [MarshalAs(UnmanagedType.Interface)] out IDXCoreAdapterFactory factory);

    [ComImport, Guid("78ee5945-c36e-4b13-a669-005dd11c0f06"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDXCoreAdapterFactory
    {
        [PreserveSig]
        int CreateAdapterList(uint attributeCount, ref Guid attributes, in Guid riid, [MarshalAs(UnmanagedType.Interface)] out IDXCoreAdapterList list);
    }

    [ComImport, Guid("526c7776-40e9-459b-b711-f32ad76dfc28"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDXCoreAdapterList
    {
        [PreserveSig]
        int GetAdapter(uint index, in Guid riid, [MarshalAs(UnmanagedType.Interface)] out IDXCoreAdapter adapter);
        [PreserveSig]
        uint GetAdapterCount();
    }

    [ComImport, Guid("f0db4c7f-fe5a-42a2-bd62-f2a6cf6fc83e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDXCoreAdapter
    {
        [PreserveSig]
        [return: MarshalAs(UnmanagedType.U1)]
        bool IsValid();
        [PreserveSig]
        [return: MarshalAs(UnmanagedType.U1)]
        bool IsAttributeSupported(in Guid attribute);
        [PreserveSig]
        [return: MarshalAs(UnmanagedType.U1)]
        bool IsPropertySupported(uint property);
        // One declaration per vtable slot: an overload here would shift every method after it.
        [PreserveSig]
        int GetProperty(uint property, nuint size, IntPtr value);
    }

    // A property of known size, read into a small native buffer.
    static long Property(IDXCoreAdapter adapter, uint property, int size)
    {
        var buffer = Marshal.AllocHGlobal(8);
        try
        {
            Marshal.WriteInt64(buffer, 0);
            if (adapter.GetProperty(property, (nuint)size, buffer) != 0) return 0;
            return size == 1 ? Marshal.ReadByte(buffer) : Marshal.ReadInt64(buffer);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
