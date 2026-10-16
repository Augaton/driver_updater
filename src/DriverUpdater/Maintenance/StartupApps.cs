using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace DriverUpdater.Maintenance;

public sealed class StartupEntry
{
    /// <summary>The Run value name, or the shortcut's file name in a Startup folder.</summary>
    public required string Name { get; init; }
    public required string Kind { get; init; } // StartupApps.Run, Run32 or Folder
    public required bool Machine { get; init; } // for every user: changing it needs administrator rights
    public required string Command { get; init; }
    public required string Program { get; init; }
    public required string Publisher { get; init; }
    public bool Enabled { get; set; }
    public string Impact { get; set; } = "—";

    public string Title => Kind == StartupApps.Folder ? Path.GetFileNameWithoutExtension(Name) : Name;
    public string Scope => Machine ? "Tous les utilisateurs" : "Vous";
}

/// <summary>
/// Programs Windows starts at sign-in, from the same places Task Manager reads, switched
/// on and off the same way (see <see cref="StartupApproval"/>). Nothing is ever deleted.
/// </summary>
public static class StartupApps
{
    public const string Run = "run", Run32 = "run32", Folder = "folder";
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string Run32Key = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

    public static List<StartupEntry> List()
    {
        var list = new List<StartupEntry>();
        AddRunValues(list, Run, machine: false);
        AddRunValues(list, Run, machine: true);
        AddRunValues(list, Run32, machine: true);
        AddShortcuts(list, machine: false);
        AddShortcuts(list, machine: true);
        return list.OrderBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Switches an entry on or off. It must exist: nothing is created for a name that isn't there.</summary>
    public static void SetEnabled(string kind, string name, bool machine, bool enabled)
    {
        if (!Exists(kind, name, machine)) throw new InvalidOperationException($"Programme de démarrage introuvable : {name}");
        using var key = Hive(machine).CreateSubKey(ApprovedKey + ApprovedSubkey(kind));
        key.SetValue(name, StartupApproval.Value(enabled, DateTime.UtcNow), RegistryValueKind.Binary);
    }

    static void AddRunValues(List<StartupEntry> list, string kind, bool machine)
    {
        using var key = Hive(machine).OpenSubKey(kind == Run32 ? Run32Key : RunKey);
        if (key is null) return;
        foreach (var name in key.GetValueNames().Where(n => n.Length > 0))
        {
            if (key.GetValue(name) is not string command || command.Length == 0) continue;
            var program = CommandLine.Split(command).Program;
            list.Add(new StartupEntry
            {
                Name = name, Kind = kind, Machine = machine, Command = command, Program = program,
                Publisher = PublisherOf(program), Enabled = IsEnabled(kind, name, machine),
            });
        }
    }

    static void AddShortcuts(List<StartupEntry> list, bool machine)
    {
        var folder = StartupFolder(machine);
        if (!Directory.Exists(folder)) return;
        dynamic? shell = null; // created once, only if there are shortcuts to read
        try
        {
            foreach (var file in Directory.EnumerateFiles(folder))
            {
                var name = Path.GetFileName(file);
                if (name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                var program = file;
                if (name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                    program = ShortcutTarget(shell ??= Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!, file);
                list.Add(new StartupEntry
                {
                    Name = name, Kind = Folder, Machine = machine, Command = program, Program = program,
                    Publisher = PublisherOf(program), Enabled = IsEnabled(Folder, name, machine),
                });
            }
        }
        finally
        {
            if (shell is not null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
        }
    }

    static bool IsEnabled(string kind, string name, bool machine)
    {
        using var key = Hive(machine).OpenSubKey(ApprovedKey + ApprovedSubkey(kind));
        return StartupApproval.IsEnabled(key?.GetValue(name) as byte[]);
    }

    static bool Exists(string kind, string name, bool machine)
    {
        if (kind == Folder)
            return name.IndexOfAny(['\\', '/']) < 0 && File.Exists(Path.Combine(StartupFolder(machine), name));
        if (kind == Run32 && !machine) return false;
        using var key = Hive(machine).OpenSubKey(kind == Run32 ? Run32Key : RunKey);
        return key?.GetValueNames().Contains(name) == true;
    }

    static string ApprovedSubkey(string kind) => kind switch
    {
        Run => "Run",
        Run32 => "Run32",
        Folder => "StartupFolder",
        _ => throw new ArgumentException($"Type d'entrée inconnu : {kind}"),
    };

    static RegistryKey Hive(bool machine) => machine ? Registry.LocalMachine : Registry.CurrentUser;

    static string StartupFolder(bool machine) =>
        Environment.GetFolderPath(machine ? Environment.SpecialFolder.CommonStartup : Environment.SpecialFolder.Startup);

    static string ShortcutTarget(dynamic shell, string shortcut)
    {
        try
        {
            return (string)shell.CreateShortcut(shortcut).TargetPath is { Length: > 0 } target ? target : shortcut;
        }
        catch (Exception)
        {
            return shortcut; // unreadable shortcut: show the shortcut itself
        }
    }

    internal static string PublisherOf(string program)
    {
        try
        {
            return File.Exists(program) ? FileVersionInfo.GetVersionInfo(program).CompanyName?.Trim() ?? "" : "";
        }
        catch (Exception)
        {
            return "";
        }
    }
}
