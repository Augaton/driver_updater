using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace DriverUpdater.Drivers;

/// <summary>
/// Backs up and restores third-party drivers (those Windows does not ship itself) with
/// pnputil, the driver tool built into Windows.
/// </summary>
public static class DriverBackup
{
    const int Success = 0, NothingToInstall = 259, RebootRequired = 3010;

    /// <summary>
    /// Exports the third-party drivers worth putting back after reinstalling Windows
    /// (<see cref="DriverStoreRules.WorthBackingUp"/>) into a new dated folder inside
    /// <paramref name="parent"/>, one subfolder each. Returns how many went, and the ones
    /// pnputil refused.
    /// </summary>
    public static async Task<(string Folder, int Count, List<string> Failed)> ExportAsync(string parent, Action<string> progress)
    {
        var folder = Path.Combine(Path.GetFullPath(parent), L.F("Pilotes {0:yyyy-MM-dd HH'h'mm'm'ss}", DateTime.Now));
        var packages = await DriverStore.WorthBackingUpAsync(progress);
        if (packages.Count == 0) throw new InvalidOperationException(L.T("Aucun pilote de fabricant à sauvegarder."));
        var failed = new List<string>();
        // The user chose the parent, so any program of theirs can write there: the folder must
        // be truly new (not a link planted under the expected name), and the folders above it
        // stay pinned while pnputil, running as administrator, writes into it.
        // While pnputil fills it, only administrators may write into it (no program of the user
        // can plant a link inside), and it stays pinned (none can rename it away).
        using (NativeFiles.PinParents(Path.GetPathRoot(folder)!, folder))
        {
            NativeFiles.CreateNewFolder(folder, AdministratorsOnly());
            using (NativeFiles.Pin(folder))
            {
                if (NativeFiles.GenuineFolder(folder) is null) throw new InvalidOperationException(L.F("Dossier de sauvegarde refusé : {0}", folder));
                try
                {
                    for (int i = 0; i < packages.Count; i++)
                    {
                        var package = packages[i];
                        progress(L.F("Sauvegarde du pilote {0} sur {1} : {2}…", i + 1, packages.Count, package.OriginalName));
                        // Made inside the folder only administrators can write to: nothing can be planted there.
                        var target = Directory.CreateDirectory(Path.Combine(folder, Path.GetFileName(package.PublishedName))).FullName;
                        int code = await PnpUtilAsync("/export-driver", package.PublishedName, target);
                        if (code != Success) failed.Add(L.F("{0} ({1}, code {2})", package.OriginalName, package.Provider, code));
                    }
                }
                finally
                {
                    // Done or not, the backup takes the rights of the folder it is in, the user's own:
                    // one cut short (a full USB stick) can still be opened or deleted. Set while the
                    // folder is still pinned, so these rights cannot land on another one.
                    if (KeepsRights(folder))
                    {
                        var inherit = new DirectorySecurity();
                        inherit.SetAccessRuleProtection(isProtected: false, preserveInheritance: false);
                        new DirectoryInfo(folder).SetAccessControl(inherit);
                    }
                }
            }
        }
        int count = packages.Count - failed.Count;
        if (count == 0) throw new InvalidOperationException(L.F("La sauvegarde a échoué : {0}.", string.Join(", ", failed)));
        return (folder, count, failed);
    }

    // FAT and exFAT (most USB sticks) keep no rights at all: nothing to give back there, and
    // no links either, which the rights guarded against.
    static bool KeepsRights(string folder)
    {
        var format = new DriveInfo(Path.GetPathRoot(folder)!).DriveFormat;
        return format.Equals("NTFS", StringComparison.OrdinalIgnoreCase) || format.Equals("ReFS", StringComparison.OrdinalIgnoreCase);
    }

    static DirectorySecurity AdministratorsOnly()
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var sid in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null), FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        return security;
    }

    /// <summary>Installs every driver found in a backup folder; returns true if Windows must restart.</summary>
    public static async Task<bool> ImportAsync(string folder)
    {
        int code = await PnpUtilAsync("/add-driver", Path.Combine(folder, "*.inf"), "/subdirs", "/install");
        return code switch
        {
            Success or NothingToInstall => false,
            RebootRequired => true,
            _ => throw new InvalidOperationException(L.F("La restauration a échoué (pnputil, code {0}).", code)),
        };
    }

    internal static async Task<int> PnpUtilAsync(params string[] arguments)
    {
        // Full path: a bare "pnputil.exe" could be picked up from the current folder.
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "pnputil.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        await process.StandardOutput.ReadToEndAsync(); // drained so pnputil never blocks on a full pipe
        await process.WaitForExitAsync();
        return process.ExitCode;
    }
}
