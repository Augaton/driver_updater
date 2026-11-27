namespace DriverUpdater.Maintenance;

/// <summary>A scheduled task that starts a program when someone signs in.</summary>
/// <param name="AllUsers">Its sign-in trigger fires for anyone, not only for this user.</param>
public sealed record LogonTask(string Path, string Name, string Program, string Arguments, bool Enabled, bool AllUsers);

/// <summary>
/// Programs started at sign-in by a scheduled task rather than by Run or the Startup folder: many
/// updaters and launchers start this way, and Task Manager's list leaves them out. Windows' own
/// tasks (under \Microsoft\) are never listed nor touched, nor another user's. Switched with the
/// task's own on and off, as the Task Scheduler does: nothing is deleted. The rules here; reading
/// and switching the tasks in StartupTasks.Scheduler.cs.
/// </summary>
public static partial class StartupTasks
{
    const int TriggerLogon = 9;

    /// <summary>A task worth listing: outside Windows' own folder, with a sign-in trigger on and a program to run.</summary>
    public static bool Listed(string path, IEnumerable<(int Type, bool Enabled)> triggers, bool runsProgram) =>
        runsProgram && !IsWindowsOwn(path) && triggers.Any(t => t.Type == TriggerLogon && t.Enabled);

    public static bool IsWindowsOwn(string path) =>
        path.StartsWith(@"\Microsoft\", StringComparison.OrdinalIgnoreCase) || path.Equals(@"\Microsoft", StringComparison.OrdinalIgnoreCase);
}
