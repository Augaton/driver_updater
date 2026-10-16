using System.IO;

namespace DriverUpdater.Admin;

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
            // "version" changes nothing, which lets the tests check the whole path.
            if (command.Name != AdminCommand.Version && !channel.FromThisApp)
                throw new UnauthorizedAccessException("Refusé : seule la fenêtre de Driver Updater peut commander ce module.");
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

            case AdminCommand.StartupSet:
                StartupApps.SetEnabled(command.Args[0], command.Args[1], machine: true, enabled: command.Args[2] == "on");
                return true;

            case AdminCommand.Recycle:
                return Recycle(command.Args);

            case AdminCommand.BootReport:
                return await Task.Run(BootLog.Read);

            case AdminCommand.InstallCatalog:
                return await InstallCatalogAsync(command.Args, channel);

            case AdminCommand.InstallAll:
                return await InstallAllAsync(command.Args, channel);

            case AdminCommand.Rollback:
                channel.Progress("Retour au pilote précédent…");
                return await Task.Run(() => DriverRollback.RollBack(command.Args[0]));

            case AdminCommand.TweakApply or AdminCommand.TweakUndo:
                foreach (var tweak in command.Args.Select(id => DriverUpdater.Tuning.TweakCatalog.Find(id)!))
                {
                    channel.Progress(tweak.Title + "…");
                    if (command.Name == AdminCommand.TweakApply) await Task.Run(() => DriverUpdater.Tuning.Tuner.Apply(tweak));
                    else await Task.Run(() => DriverUpdater.Tuning.Tuner.Undo(tweak));
                }
                return command.Args.Count;

            case AdminCommand.InstallNvidia:
            {
                if (await RestorePointAsync(channel) is not { } restorePoint) return new NvidiaInstallReport("", true, "", 0);
                var (version, exitCode) = await NvidiaDrivers.InstallAsync(command.Args[0], channel.Progress);
                return new NvidiaInstallReport(restorePoint, false, version, exitCode);
            }

            case AdminCommand.RestorePoints:
                return await Task.Run(SystemInfo.RestorePoints);

            case AdminCommand.CreateRestorePoint:
                channel.Progress("Création du point de restauration, cela peut prendre une minute…");
                return await Task.Run(() => SystemInfo.CreateRestorePoint(onDemand: true));

            case AdminCommand.DriverStore:
                return await DriverStore.RemovableAsync(channel.Progress);

            case AdminCommand.DeleteDrivers:
                return await DriverStore.DeleteAsync(command.Args, channel.Progress);

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

    /// <summary>
    /// Moves leftover folders of the all-users locations to the Recycle Bin (the user's own
    /// folders need no rights: the window recycles those). Each is checked again against the
    /// leftover rules with every folder on its way held open, so none can be swapped for a link
    /// between the check and the move. Returns how many went.
    /// </summary>
    static int Recycle(IReadOnlyList<string> folders)
    {
        var roots = LeftoverRoots.Current;
        foreach (var folder in folders)
            if (!Leftovers.IsSafeToRecycle(folder, roots, out bool machine) || !machine)
                throw new ArgumentException($"Dossier refusé : {folder}");
        int recycled = 0;
        foreach (var folder in folders)
        {
            var root = Leftovers.MachineRootOf(folder, roots) ?? throw new ArgumentException($"Dossier refusé : {folder}");
            using (NativeFiles.PinParents(root, folder))
            {
                if (NativeFiles.GenuineFolder(folder) is null || !Leftovers.IsSafeToRecycle(folder, roots, out _))
                    throw new ArgumentException($"Dossier refusé : {folder}");
                if (NativeFiles.Recycle(folder, IntPtr.Zero)) recycled++;
            }
        }
        return recycled;
    }

    static async Task<CatalogInstallReport> InstallCatalogAsync(IReadOnlyList<string> ids, HelperChannel channel)
    {
        var point = await RestorePointAsync(channel);
        if (point is null) return new CatalogInstallReport("", true, []);
        var results = new List<CatalogInstallResult>();
        await InstallFromCatalogAsync(ids, channel, results);
        return new CatalogInstallReport(point, false, results);
    }

    /// <summary>The full report's "update everything": one restore point, then both sources.</summary>
    static async Task<CatalogInstallReport> InstallAllAsync(IReadOnlyList<string> items, HelperChannel channel)
    {
        var point = await RestorePointAsync(channel);
        if (point is null) return new CatalogInstallReport("", true, []);
        var results = new List<CatalogInstallResult>();

        var fromWindowsUpdate = items.Where(i => i.StartsWith(AdminCommand.FromWindowsUpdate, StringComparison.Ordinal))
            .Select(i => i[AdminCommand.FromWindowsUpdate.Length..]).ToList();
        if (fromWindowsUpdate.Count > 0)
        {
            channel.Progress("Recherche des pilotes sur Windows Update…");
            var drivers = await Task.Run(() => WindowsUpdateDrivers.FindByIds(fromWindowsUpdate));
            foreach (var driver in drivers)
            {
                channel.Progress($"Windows Update, pilote {results.Count + 1} sur {items.Count} : {driver.Title}…");
                var outcome = await Task.Run(() => WindowsUpdateDrivers.Install(driver));
                results.Add(new(driver.UpdateId, outcome.Succeeded, outcome.RebootRequired, outcome.Succeeded ? "installé" : outcome.Detail));
            }
            foreach (var missing in fromWindowsUpdate.Except(drivers.Select(d => d.UpdateId), StringComparer.OrdinalIgnoreCase))
                results.Add(new(missing, false, false, "n'est plus proposé par Windows Update"));
        }

        var fromCatalog = items.Where(i => i.StartsWith(AdminCommand.FromCatalog, StringComparison.Ordinal))
            .Select(i => i[AdminCommand.FromCatalog.Length..]).ToList();
        await InstallFromCatalogAsync(fromCatalog, channel, results);
        return new CatalogInstallReport(point, false, results);
    }

    static async Task InstallFromCatalogAsync(IReadOnlyList<string> ids, HelperChannel channel, List<CatalogInstallResult> results)
    {
        int done = 0;
        foreach (var id in ids)
        {
            channel.Progress($"Pilote du fabricant {++done} sur {ids.Count}…");
            try
            {
                results.Add(await DriverCatalog.InstallAsync(id, channel.Progress));
            }
            catch (Exception e) when (e is System.Net.Http.HttpRequestException or IOException or TaskCanceledException)
            {
                results.Add(new CatalogInstallResult(id, false, false, Errors.Describe(e)));
            }
        }
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
