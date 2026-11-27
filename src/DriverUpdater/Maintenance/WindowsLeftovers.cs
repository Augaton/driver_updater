using System.IO;
using System.Runtime.InteropServices;

namespace DriverUpdater.Maintenance;

public sealed record LeftoversReport(int Dumps, long DumpBytes, bool CacheCleared, long CacheBytes);

/// <summary>
/// What Windows lets grow on its own (administrator side): crash dumps, kept for diagnosis but
/// left there for good, and the Delivery Optimization cache, updates already installed kept to
/// share them. Dumps younger than <see cref="DumpAge"/> stay: they explain a recent crash.
/// </summary>
public static class WindowsLeftovers
{
    public static readonly TimeSpan DumpAge = TimeSpan.FromDays(30);

    public static async Task<LeftoversReport> CleanAsync(Action<string> progress)
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var cutoff = DateTime.UtcNow - DumpAge;

        progress(L.T("Suppression des anciens vidages de plantage…"));
        // Folders that hold nothing but dumps; the cleaner never follows a link out of them.
        var scan = TempCleaner.Find([new TempCleaner.Target(Path.Combine(windows, "Minidump"), Recursive: false),
                                     new TempCleaner.Target(Path.Combine(windows, "LiveKernelReports"), Recursive: true)], cutoff);
        var dumps = TempCleaner.Delete(scan.Files, cutoff);
        var (fullDumps, fullBytes) = DeleteFullDump(Path.Combine(windows, "MEMORY.DMP"), cutoff);

        progress(L.T("Vidage du cache de l'Optimisation de distribution…"));
        // Where the cache usually is, to tell what was freed; Windows' own command does the work.
        var cache = Path.Combine(windows, "ServiceProfiles", "NetworkService", "AppData", "Local", "Microsoft", "Windows", "DeliveryOptimization", "Cache");
        long before = FolderSize(cache);
        bool cleared = (await SystemPowerShell.RunAsync("DeliveryOptimization\\Delete-DeliveryOptimizationCache -Force -ErrorAction Stop", "DeliveryOptimization")).ExitCode == 0;
        long freed = Math.Max(0, before - FolderSize(cache));
        return new LeftoversReport(dumps.Deleted + fullDumps, dumps.FreedBytes + fullBytes, cleared, freed);
    }

    // The full dump sits alone in the Windows folder: that one file, never the folder around it.
    static (int Count, long Bytes) DeleteFullDump(string path, DateTime cutoffUtc)
    {
        var file = new FileInfo(path);
        if (!file.Exists || file.Attributes.HasFlag(FileAttributes.ReparsePoint) || file.LastWriteTimeUtc >= cutoffUtc) return (0, 0);
        long size = file.Length;
        try
        {
            file.Delete();
            return (1, size);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return (0, 0);
        }
    }

    static long FolderSize(string folder)
    {
        try
        {
            return Directory.Exists(folder)
                ? new DirectoryInfo(folder).EnumerateFiles("*", new EnumerationOptions
                    {
                        RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint,
                    }).Sum(f => f.Length)
                : 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

}

/// <summary>The recycle bin of every drive, as Explorer shows it (no administrator rights needed).</summary>
public static class RecycleBin
{
    public static (long Items, long Bytes) Query()
    {
        var info = new QueryInfo { Size = Marshal.SizeOf<QueryInfo>() };
        return SHQueryRecycleBinW(null, ref info) == 0 ? (info.Items, info.Bytes) : (0, 0);
    }

    /// <summary>Empties it for good, without Explorer's own question: the window asked first.</summary>
    public static void Empty() => SHEmptyRecycleBinW(IntPtr.Zero, null, NoConfirmation | NoProgressUi | NoSound);

    const uint NoConfirmation = 1, NoProgressUi = 2, NoSound = 4;

    // SHQUERYRBINFO (shellapi.h), natural alignment on 64-bit Windows.
    [StructLayout(LayoutKind.Sequential)]
    struct QueryInfo
    {
        public int Size;
        public long Bytes, Items;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern int SHQueryRecycleBinW(string? rootPath, ref QueryInfo info);

    // Already empty: it says E_UNEXPECTED, which changes nothing.
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern int SHEmptyRecycleBinW(IntPtr window, string? rootPath, uint flags);
}
