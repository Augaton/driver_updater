using System.Diagnostics;
using System.IO;

namespace DriverUpdater;

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
        var folder = Path.Combine(parent, $"Pilotes {DateTime.Now:yyyy-MM-dd HH'h'mm}");
        Directory.CreateDirectory(folder);
        int code = await PnpUtilAsync("/export-driver", "*", folder);
        if (code != Success) throw new InvalidOperationException($"La sauvegarde a échoué (pnputil, code {code}).");
        // pnputil writes one subfolder per driver package.
        return (folder, Directory.GetDirectories(folder).Length);
    }

    /// <summary>Installs every driver found in a backup folder; returns true if Windows must restart.</summary>
    public static async Task<bool> ImportAsync(string folder)
    {
        int code = await PnpUtilAsync("/add-driver", Path.Combine(folder, "*.inf"), "/subdirs", "/install");
        return code switch
        {
            Success or NothingToInstall => false,
            RebootRequired => true,
            _ => throw new InvalidOperationException($"La restauration a échoué (pnputil, code {code})."),
        };
    }

    static async Task<int> PnpUtilAsync(params string[] arguments)
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
