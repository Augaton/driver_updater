using System.Windows;
using System.Windows.Threading;
using System.Xml.Linq;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace DriverUpdater.Drivers;

/// <summary>
/// The weekly background check: a scheduled task starts the app with <see cref="Argument"/>;
/// it searches quietly and shows a notification only when a driver update is new. Searching
/// needs no administrator rights, so the task runs with the user's own: it cannot be used to
/// gain rights, whatever folder the app sits in.
/// </summary>
public static class WeeklyCheck
{
    public const string Argument = "--check";
    public const string TaskName = "Driver Updater weekly check"; // also in installer\DriverUpdater.iss

    const string StateKey = @"Software\Driver Updater"; // under HKEY_CURRENT_USER
    const string NotifiedValue = "NotifiedUpdates";

    const int TaskCreateOrUpdate = 6, TaskLogonInteractiveToken = 3;
    // Matched on HResult, not on the exception type: late-bound COM turns this code into a
    // FileNotFoundException, not a COMException.
    const int FileNotFound = unchecked((int)0x80070002);

    public static bool IsEnabled(string taskName = TaskName)
    {
        try
        {
            TaskFolder().GetTask(taskName);
            return true;
        }
        catch (Exception e) when (e.HResult == FileNotFound)
        {
            return false;
        }
    }

    public static void Enable(string exePath, string taskName = TaskName) =>
        TaskFolder().RegisterTask(taskName, TaskXml(exePath), TaskCreateOrUpdate, null, null, TaskLogonInteractiveToken, null);

    /// <summary>
    /// If the check is on but points at another copy of the app, moves it here when that copy
    /// is gone, or when this one is the installed copy (in Program Files). Running the portable
    /// zip once must not take the check away from the installed app. Best effort: a task an
    /// older version created with administrator rights cannot be changed from here.
    /// </summary>
    public static void FollowThisCopy(string exePath, string taskName = TaskName)
    {
        try
        {
            dynamic task = TaskFolder().GetTask(taskName);
            string current = task.Definition.Actions[1].Path; // Task Scheduler collections start at 1
            if (string.Equals(current, exePath, StringComparison.OrdinalIgnoreCase)) return;
            bool installedHere = exePath.StartsWith(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) + @"\", StringComparison.OrdinalIgnoreCase);
            if (!System.IO.File.Exists(current) || installedHere) Enable(exePath, taskName);
        }
        catch (Exception)
        {
            // No task, or not ours to change: nothing to do.
        }
    }

    public static void Disable(string taskName = TaskName)
    {
        try
        {
            TaskFolder().DeleteTask(taskName, 0);
        }
        catch (Exception e) when (e.HResult == FileNotFound)
        {
        }
    }

    static dynamic TaskFolder()
    {
        dynamic service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")
            ?? throw new InvalidOperationException("Planificateur de tâches introuvable."))!;
        service.Connect();
        return service.GetFolder(@"\");
    }

    /// <summary>
    /// Wednesday at noon, or at the next start-up if the PC was off; only for the signed-in
    /// user, so the notification can show on their desktop.
    /// </summary>
    public static string TaskXml(string exePath)
    {
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        return new XElement(ns + "Task", new XAttribute("version", "1.2"),
            new XElement(ns + "RegistrationInfo",
                new XElement(ns + "Description", "Cherche chaque semaine les mises à jour de pilotes et prévient s'il y en a de nouvelles.")),
            new XElement(ns + "Triggers",
                new XElement(ns + "CalendarTrigger",
                    new XElement(ns + "StartBoundary", "2026-01-07T12:00:00"),
                    new XElement(ns + "ScheduleByWeek",
                        new XElement(ns + "DaysOfWeek", new XElement(ns + "Wednesday")),
                        new XElement(ns + "WeeksInterval", 1)))),
            new XElement(ns + "Principals",
                new XElement(ns + "Principal", new XAttribute("id", "Author"),
                    new XElement(ns + "LogonType", "InteractiveToken"),
                    new XElement(ns + "RunLevel", "LeastPrivilege"))),
            new XElement(ns + "Settings",
                new XElement(ns + "MultipleInstancesPolicy", "IgnoreNew"),
                new XElement(ns + "DisallowStartIfOnBatteries", false),
                new XElement(ns + "StopIfGoingOnBatteries", false),
                new XElement(ns + "StartWhenAvailable", true),
                new XElement(ns + "RunOnlyIfNetworkAvailable", true),
                new XElement(ns + "ExecutionTimeLimit", "PT6H")),
            new XElement(ns + "Actions", new XAttribute("Context", "Author"),
                new XElement(ns + "Exec",
                    new XElement(ns + "Command", exePath),
                    new XElement(ns + "Arguments", Argument)))).ToString();
    }

    public static async Task RunAsync(App app)
    {
        List<DriverUpdate> found;
        try
        {
            found = await Task.Run(WindowsUpdateDrivers.Search);
        }
        catch (Exception)
        {
            // Offline, Windows Update busy…: nothing to report, next week will tell.
            app.Shutdown();
            return;
        }

        var current = found.Select(u => u.UpdateId).ToHashSet();
        bool hasNew = current.Except(Notified()).Any();
        SaveNotified(current);
        if (hasNew) Notify(app, found);
        else app.Shutdown();
    }

    static HashSet<string> Notified()
    {
        using var key = Registry.CurrentUser.OpenSubKey(StateKey);
        return (key?.GetValue(NotifiedValue) as string[] ?? []).ToHashSet();
    }

    static void SaveNotified(IEnumerable<string> ids)
    {
        using var key = Registry.CurrentUser.CreateSubKey(StateKey);
        key.SetValue(NotifiedValue, ids.ToArray(), RegistryValueKind.MultiString);
    }

    static void Notify(App app, List<DriverUpdate> found)
    {
        var icon = new Forms.NotifyIcon
        {
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? System.Drawing.SystemIcons.Information,
            Text = "Driver Updater",
            Visible = true,
        };
        bool opened = false;
        void Open()
        {
            if (opened) return;
            opened = true;
            icon.Dispose();
            app.ShutdownMode = ShutdownMode.OnLastWindowClose;
            var window = new MainWindow();
            window.Show();
            _ = window.ShowUpdates(found, "Trouvé par la vérification hebdomadaire.");
        }
        icon.BalloonTipClicked += (_, _) => Open();
        icon.MouseClick += (_, _) => Open();
        icon.ShowBalloonTip(0, "Mises à jour de pilotes",
            $"{found.Count} mise(s) à jour disponible(s). Cliquez pour les voir.", Forms.ToolTipIcon.Info);

        // Nobody clicked: leave quietly rather than sit in the tray until next week.
        var timeout = new DispatcherTimer { Interval = TimeSpan.FromHours(4) };
        timeout.Tick += (_, _) =>
        {
            timeout.Stop();
            if (opened) return;
            icon.Dispose();
            app.Shutdown();
        };
        timeout.Start();
    }
}
