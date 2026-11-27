using System.IO;
using System.Runtime.InteropServices;

namespace DriverUpdater.Maintenance;

// The Task Scheduler's side of StartupTasks: reading the tasks and switching one.
public static partial class StartupTasks
{
    const int ActionExec = 0, EnumHidden = 1;

    public static List<LogonTask> List()
    {
        var list = new List<LogonTask>();
        Walk(Connect().GetFolder(@"\"), list);
        return list;
    }

    /// <summary>Turns one listed task on or off. Any other path (Windows' own, a task that starts nothing at sign-in) is refused.</summary>
    public static void SetEnabled(string path, bool enabled)
    {
        dynamic? task = null;
        try
        {
            task = Connect().GetFolder(Folder(path)).GetTask(path[(path.LastIndexOf('\\') + 1)..]);
        }
        catch (Exception e) when (e is COMException or FileNotFoundException or DirectoryNotFoundException)
        {
        }
        if (task is null || Read(task) is null) throw new InvalidOperationException(L.F("Tâche de démarrage introuvable : {0}", path));
        task.Enabled = enabled;
    }

    static string Folder(string path) => path.LastIndexOf('\\') is > 0 and var cut ? path[..cut] : @"\";

    static dynamic Connect()
    {
        dynamic service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")
                                                   ?? throw new InvalidOperationException(L.T("Planificateur de tâches indisponible.")))!;
        service.Connect();
        return service;
    }

    // The collections of the Task Scheduler are numbered from 1.
    static void Walk(dynamic folder, List<LogonTask> list)
    {
        if (IsWindowsOwn((string)folder.Path)) return;
        dynamic tasks = folder.GetTasks(EnumHidden);
        int count = tasks.Count;
        for (int i = 1; i <= count; i++)
            try
            {
                if (Read(tasks[i]) is LogonTask task) list.Add(task);
            }
            catch (COMException)
            {
                // A task this user may not read: left out, as the Task Scheduler leaves it out.
            }
        dynamic folders = folder.GetFolders(0);
        int subfolders = folders.Count;
        for (int i = 1; i <= subfolders; i++) Walk(folders[i], list);
    }

    static LogonTask? Read(dynamic task)
    {
        string path = task.Path;
        dynamic definition = task.Definition;
        var triggers = new List<(int, bool)>();
        bool allUsers = false, forSomeoneElse = false;
        dynamic triggerList = definition.Triggers;
        int count = triggerList.Count;
        for (int i = 1; i <= count; i++)
        {
            dynamic trigger = triggerList[i];
            int type = trigger.Type;
            bool on = trigger.Enabled;
            triggers.Add((type, on));
            if (type != TriggerLogon || !on) continue;
            string? user = trigger.UserId;
            if (string.IsNullOrEmpty(user)) allUsers = true;
            else if (!IsCurrentUser(user)) forSomeoneElse = true;
        }
        string? program = null, arguments = "";
        dynamic actions = definition.Actions;
        int actionCount = actions.Count;
        for (int i = 1; i <= actionCount && program is null; i++)
        {
            dynamic action = actions[i];
            if ((int)action.Type != ActionExec) continue;
            program = Environment.ExpandEnvironmentVariables(((string)action.Path).Trim().Trim('"'));
            arguments = (string?)action.Arguments ?? "";
        }
        if (!Listed(path, triggers, program is not null) || forSomeoneElse && !allUsers) return null;
        return new LogonTask(path, (string)task.Name, program!, arguments, (bool)task.Enabled, allUsers);
    }

    // "PC\alice" or "alice": the trigger names the user it fires for.
    static bool IsCurrentUser(string user) =>
        user.Equals(Environment.UserName, StringComparison.OrdinalIgnoreCase)
        || user.Equals($@"{Environment.UserDomainName}\{Environment.UserName}", StringComparison.OrdinalIgnoreCase);
}
