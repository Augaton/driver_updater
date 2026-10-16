using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace DriverUpdater.Common;

/// <summary>
/// File operations for code that runs as administrator on paths another program of the user
/// can reach: it must assume folders may be swapped for links (junctions, symbolic links)
/// while it works. Every check reads what a handle points to, never a path twice.
/// </summary>
[SupportedOSPlatform("windows")]
public static class NativeFiles
{
    const uint AccessReadAttributes = 0x80;
    // Windows only applies sharing rules to handles with data access: a folder pinned with
    // "read attributes" alone could still be renamed. Listing its contents is data access.
    const uint AccessListDirectory = 0x1;
    internal const uint FlagOpenReparsePoint = 0x00200000, FlagBackupSemantics = 0x02000000;

    internal static SafeFileHandle Open(string path, uint access, uint flags, FileShare share = FileShare.ReadWrite | FileShare.Delete) =>
        CreateFileW(path, access, share, IntPtr.Zero, FileMode.Open, flags, IntPtr.Zero);

    internal static string? FinalPath(SafeFileHandle handle)
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

    /// <summary>
    /// Whether the object is a junction or a symbolic link: a reparse point that sends
    /// elsewhere ("name surrogate"). OneDrive's cloud folders are reparse points too, but stay
    /// where they are. An unreadable tag counts as a link.
    /// </summary>
    internal static bool Redirects(SafeFileHandle handle)
    {
        const int AttributeTagInfo = 9;
        const uint NameSurrogate = 0x20000000;
        if (!File.GetAttributes(handle).HasFlag(FileAttributes.ReparsePoint)) return false;
        return !GetFileInformationByHandleEx(handle, AttributeTagInfo, out var info, (uint)Marshal.SizeOf<TagInfo>())
               || (info.ReparseTag & NameSurrogate) != 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct TagInfo
    {
        public uint FileAttributes, ReparseTag;
    }

    /// <summary>
    /// The folder's real path, or null if it is a link, or if a junction, link or short name
    /// anywhere on the way changes where it leads. Callers pass paths built from known folders.
    /// </summary>
    public static string? GenuineFolder(string folder)
    {
        using var handle = Open(folder, AccessReadAttributes, FlagBackupSemantics | FlagOpenReparsePoint);
        if (handle.IsInvalid) return null;
        if (!File.GetAttributes(handle).HasFlag(FileAttributes.Directory) || Redirects(handle)) return null;
        string? real = FinalPath(handle);
        string expected = @"\\?\" + Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        return string.Equals(real, expected, StringComparison.OrdinalIgnoreCase) ? real : null;
    }

    /// <summary>
    /// Holds every folder between <paramref name="root"/> (excluded) and <paramref name="path"/>
    /// (excluded) open without delete sharing: until disposed, none of them can be renamed,
    /// deleted or replaced by a link. Throws if one already is a link, or is not where it seems.
    /// </summary>
    public static IDisposable PinParents(string root, string path)
    {
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var parent = Path.GetDirectoryName(Path.GetFullPath(path))
            ?? throw new ArgumentException(L.F("Chemin sans dossier parent : {0}", path));
        var relative = Path.GetRelativePath(root, parent);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
            throw new ArgumentException(L.F("{0} n'est pas dans {1}.", path, root));

        var handles = new List<SafeFileHandle>();
        try
        {
            var current = root;
            foreach (var part in relative == "." ? [] : relative.Split(Path.DirectorySeparatorChar))
            {
                current = Path.Combine(current, part);
                var handle = Open(current, AccessListDirectory | AccessReadAttributes, FlagBackupSemantics | FlagOpenReparsePoint, FileShare.ReadWrite);
                handles.Add(handle);
                if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastPInvokeError(), L.F("Dossier inaccessible : {0}", current));
                if (!File.GetAttributes(handle).HasFlag(FileAttributes.Directory) || Redirects(handle)
                    || !string.Equals(FinalPath(handle), @"\\?\" + current, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(L.F("Dossier redirigé par un lien, refusé : {0}", current));
            }
            return new Pinned(handles);
        }
        catch
        {
            foreach (var handle in handles) handle.Dispose();
            throw;
        }
    }

    sealed class Pinned(List<SafeFileHandle> handles) : IDisposable
    {
        public void Dispose()
        {
            foreach (var handle in handles) handle.Dispose();
        }
    }

    /// <summary>
    /// Creates a folder that did not exist, with these rights if given; fails if anything, even
    /// a link, is already there.
    /// </summary>
    public static void CreateNewFolder(string path, System.Security.AccessControl.DirectorySecurity? security = null)
    {
        var descriptor = security?.GetSecurityDescriptorBinaryForm();
        var pinned = descriptor is null ? default : GCHandle.Alloc(descriptor, GCHandleType.Pinned);
        try
        {
            var attributes = new SecurityAttributes
            {
                Length = Marshal.SizeOf<SecurityAttributes>(),
                Descriptor = descriptor is null ? IntPtr.Zero : pinned.AddrOfPinnedObject(),
            };
            if (!CreateDirectoryW(path, ref attributes))
                throw new Win32Exception(Marshal.GetLastPInvokeError(), L.F("Création du dossier impossible : {0}", path));
        }
        finally
        {
            if (descriptor is not null) pinned.Free();
        }
    }

    /// <summary>Holds a folder open without delete sharing: until disposed, it cannot be renamed or deleted.</summary>
    public static IDisposable Pin(string folder)
    {
        var handle = Open(folder, AccessListDirectory | AccessReadAttributes, FlagBackupSemantics | FlagOpenReparsePoint, FileShare.ReadWrite);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastPInvokeError(), L.F("Dossier inaccessible : {0}", folder));
        return handle;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct SecurityAttributes
    {
        public int Length;
        public IntPtr Descriptor;
        [MarshalAs(UnmanagedType.Bool)] public bool InheritHandle;
    }

    /// <summary>
    /// Moves a folder to the Recycle Bin. If it cannot be recycled (too big for the bin, or the
    /// bin is off on that drive), Windows asks before deleting it for good, instead of doing it
    /// silently. Returns false if the user said no.
    /// </summary>
    public static bool Recycle(string path, IntPtr owner)
    {
        const uint Delete = 3;
        const ushort Silent = 0x4, NoConfirmation = 0x10, AllowUndo = 0x40, NoErrorUi = 0x400, WantNukeWarning = 0x4000;
        var operation = new FileOperation
        {
            Window = owner,
            Function = Delete,
            From = Path.GetFullPath(path) + "\0", // the list ends with a double null
            Flags = (ushort)(AllowUndo | NoConfirmation | WantNukeWarning | Silent | NoErrorUi),
        };
        int result = SHFileOperationW(ref operation);
        const int Cancelled = 1223;
        if (operation.AnyOperationsAborted || result == Cancelled) return false;
        if (result != 0) throw new IOException(L.F("Mise à la corbeille impossible ({0}) : {1}", result, path));
        return true;
    }

    // SHFILEOPSTRUCTW, natural alignment on 64-bit Windows.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct FileOperation
    {
        public IntPtr Window;
        public uint Function;
        public string From;
        public string? To;
        public ushort Flags;
        [MarshalAs(UnmanagedType.Bool)] public bool AnyOperationsAborted;
        public IntPtr NameMappings;
        public string? ProgressTitle;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern SafeFileHandle CreateFileW(string fileName, uint access, FileShare share, IntPtr security,
        FileMode mode, uint flags, IntPtr template);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern uint GetFinalPathNameByHandleW(SafeFileHandle file, [Out] char[] path, uint length, uint flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool CreateDirectoryW(string path, ref SecurityAttributes security);

    [DllImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetFileInformationByHandleEx(SafeFileHandle file, int infoClass, out TagInfo info, uint size);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    static extern int SHFileOperationW(ref FileOperation operation);
}
