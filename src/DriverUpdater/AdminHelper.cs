using System.IO;

namespace DriverUpdater;

public sealed record InstallResult(string UpdateId, string Title, bool Succeeded, string Detail, bool RebootRequired);
public sealed record InstallReport(string RestorePoint, bool Cancelled, List<InstallResult> Results);
public sealed record BackupReport(string Folder, int Count);
public sealed record ImportReport(string RestorePoint, bool Cancelled, bool RebootRequired);
public sealed record HealthReport(List<Disk> Disks, List<Battery> Batteries);

/// <summary>
/// The administrator side of <see cref="Elevation"/>: started with one command, it does
/// that, reports through the pipe and exits.
/// </summary>
public static class AdminHelper
{
    public static async Task RunAsync(App app, string[] args)
    {
        int exitCode = 0;
        HelperChannel? channel = null;
        try
        {
            // --admin <pipe> <command> [arguments…]
            if (args.Length < 3) throw new ArgumentException("Arguments manquants.");
            channel = await HelperChannel.ConnectAsync(args[1]);
            var command = AdminCommand.Parse(args[2..]);
            channel.Result(await ExecuteAsync(command, channel));
        }
        catch (Exception e)
        {
            exitCode = 1;
            try
            {
                channel?.Error(Errors.Describe(e));
            }
            catch (IOException)
            {
                // The window is gone: nobody left to tell.
            }
        }
        finally
        {
            channel?.Dispose();
            app.Shutdown(exitCode);
        }
    }

    static async Task<object?> ExecuteAsync(AdminCommand command, HelperChannel channel)
    {
        switch (command.Name)
        {
            case AdminCommand.Version:
                return typeof(AdminHelper).Assembly.GetName().Version!.ToString();

            case AdminCommand.Install:
                return await InstallAsync(command.Args, channel);

            case AdminCommand.Hide:
                channel.Progress("Recherche des pilotes à masquer…");
                var toHide = await Task.Run(() => WindowsUpdateDrivers.FindByIds(command.Args));
                await Task.Run(() => toHide.ForEach(WindowsUpdateDrivers.Hide));
                return toHide.Count;

            case AdminCommand.Unhide:
                return await Task.Run(WindowsUpdateDrivers.UnhideAll);

            case AdminCommand.ExportDrivers:
                var (folder, count) = await DriverBackup.ExportAsync(command.Args[0]);
                return new BackupReport(folder, count);

            case AdminCommand.ImportDrivers:
                var point = await RestorePointAsync(channel);
                if (point is null) return new ImportReport("", true, false);
                channel.Progress("Installation des pilotes sauvegardés…");
                return new ImportReport(point, false, await DriverBackup.ImportAsync(command.Args[0]));

            case AdminCommand.CleanWindowsTemp:
                var cutoff = DateTime.UtcNow - TempCleaner.MinAge;
                var windowsTemp = new TempCleaner.Target(
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"), Recursive: true);
                var scan = await Task.Run(() => TempCleaner.Find([windowsTemp], cutoff));
                return await Task.Run(() => TempCleaner.Delete(scan.Files, cutoff));

            case AdminCommand.Health:
                return new HealthReport(await Task.Run(SystemInfo.Disks), await Task.Run(SystemInfo.Batteries));

            case AdminCommand.Sfc or AdminCommand.DismRestore or AdminCommand.DismCleanup:
                return await Repair.RunAsync(command.Name, channel.Progress);

            default:
                throw new ArgumentException($"Commande non prise en charge : {command.Name}");
        }
    }

    static async Task<InstallReport> InstallAsync(IReadOnlyList<string> ids, HelperChannel channel)
    {
        var point = await RestorePointAsync(channel);
        if (point is null) return new InstallReport("", true, []);

        channel.Progress("Recherche des pilotes sur Windows Update…");
        var drivers = await Task.Run(() => WindowsUpdateDrivers.FindByIds(ids));
        var results = new List<InstallResult>();
        foreach (var driver in drivers)
        {
            channel.Progress($"Pilote {results.Count + 1} sur {drivers.Count} : {driver.Title}…");
            var outcome = await Task.Run(() => WindowsUpdateDrivers.Install(driver));
            results.Add(new(driver.UpdateId, driver.Title, outcome.Succeeded, outcome.Detail, outcome.RebootRequired));
        }
        // Installed in the meantime, or withdrawn by Microsoft.
        foreach (var missing in ids.Except(drivers.Select(d => d.UpdateId), StringComparer.OrdinalIgnoreCase))
            results.Add(new(missing, missing, false, "n'est plus proposé par Windows Update", false));
        return new InstallReport(point, false, results);
    }

    /// <summary>The restore point's message, or null if it failed and the user would rather stop.</summary>
    static async Task<string?> RestorePointAsync(HelperChannel channel)
    {
        channel.Progress("Création d'un point de restauration, cela peut prendre une minute…");
        var point = await Task.Run(SystemInfo.CreateRestorePoint);
        if (point.Ok) return point.Message;
        return await channel.AskAsync(point.Message + "\n\nContinuer sans point de restauration ?") ? point.Message : null;
    }
}
