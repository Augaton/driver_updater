using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using System.Xml.Linq;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace DriverUpdater;

/// <summary>
/// The weekly background check: a scheduled task starts the app with <see cref="Argument"/>;
/// it searches quietly and shows a notification only when a driver update is new.
/// </summary>
public static class WeeklyCheck
{
    public const string Argument = "--check";
    public const string TaskName = "Driver Updater weekly check"; // also in installer\DriverUpdater.iss

    // Under HKLM, which only administrators can write. A file in the user's profile could be
    // swapped for a link that makes this administrator process overwrite a system file.
    const string StateKey = @"SOFTWARE\Driver Updater";
    const string NotifiedValue = "NotifiedUpdates";

    const int TaskCreateOrUpdate = 6, TaskLogonInteractiveToken = 3;
    const int FileNotFound = unchecked((int)0x80070002);

    /// <summary>
    /// The task starts the app with full rights and no prompt, so the exe must sit where only
    /// administrators can replace it: Program Files, where the installer puts it.
    /// </summary>
    public static bool InstalledInProgramFiles =>
        Environment.ProcessPath is { } exe
        && exe.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) + @"\", StringComparison.OrdinalIgnoreCase);

    public static bool IsEnabled(string taskName = TaskName)
    {
        try
        {
            TaskFolder().GetTask(taskName);
            return true;
        }
        catch (COMException e) when (e.HResult == FileNotFound)
        {
            return false;
        }
    }

    public static void Enable(string exePath, string taskName = TaskName) =>
        TaskFolder().RegisterTask(taskName, TaskXml(exePath), TaskCreateOrUpdate, null, null, TaskLogonInteractiveToken, null);

    public static void Disable(string taskName = TaskName)
    {
        try
        {
            TaskFolder().DeleteTask(taskName, 0);
        }
        catch (COMException e) when (e.HResult == FileNotFound)
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
                    new XElement(ns + "RunLevel", "HighestAvailable"))),
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
        using var key = Registry.LocalMachine.OpenSubKey(StateKey);
        return (key?.GetValue(NotifiedValue) as string[] ?? []).ToHashSet();
    }

    static void SaveNotified(IEnumerable<string> ids)
    {
        using var key = Registry.LocalMachine.CreateSubKey(StateKey);
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
