using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace DriverUpdater.Maintenance;

/// <summary>
/// Finds and deletes old temporary files. The app runs as administrator, so this class
/// assumes another program could plant links or swap folders under it while it works:
/// reparse points (junctions, symbolic links) are never followed, and on Windows each file
/// is deleted through a handle only after checking where that handle really points.
/// </summary>
public static class TempCleaner
{
    /// <summary>Younger temp files may still belong to a running installer or app.</summary>
    public static readonly TimeSpan MinAge = TimeSpan.FromDays(2);

    public sealed record Target(string Folder, bool Recursive);

    /// <param name="Root">The target folder's real path; deletion never leaves it.</param>
    public sealed record Candidate(FileInfo File, string Root);

    public sealed record ScanResult(IReadOnlyList<Candidate> Files, long TotalBytes, IReadOnlyList<string> RefusedFolders);
    public sealed record CleanResult(int Deleted, long FreedBytes, int Skipped);

    public static ScanResult Find(IEnumerable<Target> targets, DateTime cutoffUtc)
    {
        var files = new List<Candidate>();
        var refused = new List<string>();
        foreach (var target in targets)
        {
            if (!Directory.Exists(target.Folder)) continue;
            string? root = OperatingSystem.IsWindows()
                ? WindowsFiles.GenuineFolder(target.Folder)
                : Path.TrimEndingDirectorySeparator(Path.GetFullPath(target.Folder));
            if (root is null)
            {
                refused.Add(target.Folder);
                continue;
            }
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = target.Recursive,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System,
            };
            foreach (var file in new DirectoryInfo(target.Folder).EnumerateFiles("*", options))
                if (file.LastWriteTimeUtc < cutoffUtc) files.Add(new Candidate(file, root));
        }
        return new ScanResult(files, files.Sum(c => c.File.Length), refused);
    }

    /// <summary>
    /// Deletes the files found by <see cref="Find"/>. Files in use, read-only, modified since
    /// the scan, or now resolving outside their folder are left alone and counted as skipped.
    /// </summary>
    public static CleanResult Delete(IEnumerable<Candidate> candidates, DateTime cutoffUtc)
    {
        int deleted = 0, skipped = 0;
        long freed = 0;
        foreach (var candidate in candidates)
        {
            long? size;
            try
            {
                size = OperatingSystem.IsWindows()
                    ? WindowsFiles.DeleteWithin(candidate.File.FullName, candidate.Root, cutoffUtc)
                    : DeleteByPath(candidate.File, cutoffUtc);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                size = null;
            }
            if (size is long bytes)
            {
                deleted++;
                freed += bytes;
            }
            else
            {
                skipped++;
            }
        }
        return new CleanResult(deleted, freed, skipped);
    }

    // Outside Windows (the tests' platform) there is no admin-rights race to defend against.
    static long? DeleteByPath(FileInfo file, DateTime cutoffUtc)
    {
        file.Refresh();
        if (!file.Exists || file.LastWriteTimeUtc >= cutoffUtc || file.Attributes.HasFlag(FileAttributes.ReparsePoint))
            return null;
        long size = file.Length;
        file.Delete();
        return size;
    }

    [SupportedOSPlatform("windows")]
    static class WindowsFiles
    {
        const uint AccessDelete = 0x00010000, AccessReadAttributes = 0x80;
        const uint FlagOpenReparsePoint = 0x00200000, FlagBackupSemantics = 0x02000000;
        const int FileDispositionInfo = 4;

        /// <summary>
        /// The folder's real path, or null if a junction, link or short name anywhere on the
        /// way changes where it leads. Callers pass paths built from known folders.
        /// </summary>
        public static string? GenuineFolder(string folder)
        {
            using var handle = Open(folder, AccessReadAttributes, FlagBackupSemantics);
            if (handle.IsInvalid) return null;
            string? real = FinalPath(handle);
            string expected = @"\\?\" + Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
            return string.Equals(real, expected, StringComparison.OrdinalIgnoreCase) ? real : null;
        }

        /// <summary>Deletes the file if it is still old, plain, and inside <paramref name="root"/>; returns its size.</summary>
        public static long? DeleteWithin(string path, string root, DateTime cutoffUtc)
        {
            // Every check below reads the object this handle points to, and the delete goes
            // through the same handle: swapping folders after the checks cannot redirect it.
            using var handle = Open(path, AccessDelete | AccessReadAttributes, FlagOpenReparsePoint);
            if (handle.IsInvalid) return null;
            if ((File.GetAttributes(handle) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0) return null;
            if (File.GetLastWriteTimeUtc(handle) >= cutoffUtc) return null;
            string? real = FinalPath(handle);
            if (real is null || !real.StartsWith(root + @"\", StringComparison.OrdinalIgnoreCase)) return null;

            long size = RandomAccess.GetLength(handle);
            byte deleteFile = 1;
            return SetFileInformationByHandle(handle, FileDispositionInfo, ref deleteFile, 1) ? size : null;
        }

        static SafeFileHandle Open(string path, uint access, uint flags) =>
            CreateFileW(path, access, FileShare.ReadWrite | FileShare.Delete, IntPtr.Zero, FileMode.Open, flags, IntPtr.Zero);

        static string? FinalPath(SafeFileHandle handle)
        {
            var buffer = new char[512];
            while (true)
            {
                uint length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Length, 0);
                if (length == 0) return null;
                if (length < buffer.Length) return new string(buffer, 0, (int)length);
                buffer = new char[length]; // too small: length is the size needed
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        static extern SafeFileHandle CreateFileW(string fileName, uint access, FileShare share, IntPtr security,
            FileMode mode, uint flags, IntPtr template);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        static extern uint GetFinalPathNameByHandleW(SafeFileHandle file, [Out] char[] path, uint length, uint flags);

        [DllImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool SetFileInformationByHandle(SafeFileHandle file, int infoClass, ref byte info, uint size);
    }
}
