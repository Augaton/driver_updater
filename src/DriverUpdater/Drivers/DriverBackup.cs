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

    /// <summary>Exports every third-party driver into a new dated folder inside <paramref name="parent"/>.</summary>
    public static async Task<(string Folder, int Count)> ExportAsync(string parent)
    {
        var folder = Path.Combine(Path.GetFullPath(parent), L.F("Pilotes {0:yyyy-MM-dd HH'h'mm'm'ss}", DateTime.Now));
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
                int code = await PnpUtilAsync("/export-driver", "*", folder);
                if (code != Success) throw new InvalidOperationException(L.F("La sauvegarde a échoué (pnputil, code {0}).", code));
            }
            // Done: the backup takes the rights of the folder it is in, the user's own.
            var inherit = new DirectorySecurity();
            inherit.SetAccessRuleProtection(isProtected: false, preserveInheritance: false);
            new DirectoryInfo(folder).SetAccessControl(inherit);
        }
        // pnputil writes one subfolder per driver package.
        return (folder, Directory.GetDirectories(folder).Length);
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
