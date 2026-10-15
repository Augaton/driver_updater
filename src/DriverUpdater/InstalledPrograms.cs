using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace DriverUpdater;

public sealed class InstalledProgram
{
    public required string Name { get; init; }
    public required string Publisher { get; init; }
    public required string Version { get; init; }
    public required string? InstallLocation { get; init; }
    public required string UninstallCommand { get; init; }
    public required long Size { get; init; }
    public required DateTime? InstalledOn { get; init; }
    public required RegistryKey Hive { get; init; }
    public required string KeyPath { get; init; }

    public string SizeText => Size > 0 ? Bytes.Format(Size) : "—";
}

/// <summary>Programs listed in "Apps installées", from the same registry keys Windows reads.</summary>
public static partial class InstalledPrograms
{
    const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";
    const string Uninstall32Key = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";

    public static List<InstalledProgram> List()
    {
        var list = new List<InstalledProgram>();
        Add(list, Registry.LocalMachine, UninstallKey);
        Add(list, Registry.LocalMachine, Uninstall32Key);
        Add(list, Registry.CurrentUser, UninstallKey);
        return list
            .GroupBy(p => (p.Name.ToLowerInvariant(), p.Version))
            .Select(g => g.First())
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    static void Add(List<InstalledProgram> list, RegistryKey hive, string path)
    {
        using var root = hive.OpenSubKey(path);
        if (root is null) return;
        foreach (var subkey in root.GetSubKeyNames())
        {
            using var key = root.OpenSubKey(subkey);
            if (key?.GetValue("DisplayName") is not string name || name.Length == 0) continue;
            if (key.GetValue("UninstallString") is not string uninstall || uninstall.Length == 0) continue;
            // Parts of Windows, and updates to another program, are not separate programs.
            if (key.GetValue("SystemComponent") is 1 || key.GetValue("ParentKeyName") is not null) continue;
            if (key.GetValue("ReleaseType") is string type && (type.Contains("Update") || type.Contains("Hotfix"))) continue;
            list.Add(new InstalledProgram
            {
                Name = name.Trim(),
                Publisher = (key.GetValue("Publisher") as string ?? "").Trim(),
                Version = (key.GetValue("DisplayVersion") as string ?? "").Trim(),
                InstallLocation = key.GetValue("InstallLocation") as string,
                UninstallCommand = uninstall,
                Size = key.GetValue("EstimatedSize") is int kilobytes ? kilobytes * 1024L : 0,
                InstalledOn = DateTime.TryParseExact(key.GetValue("InstallDate") as string, "yyyyMMdd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null,
                Hive = hive,
                KeyPath = path + @"\" + subkey,
            });
        }
    }

    /// <summary>Whether Windows still lists it, i.e. the uninstaller did not finish or was cancelled.</summary>
    public static bool StillInstalled(InstalledProgram program)
    {
        using var key = program.Hive.OpenSubKey(program.KeyPath);
        return key is not null;
    }

    /// <summary>
    /// Starts the program's own uninstaller. The window has no administrator rights: an
    /// uninstaller that needs them asks Windows itself, as when started from Settings.
    /// </summary>
    public static Process? Uninstall(InstalledProgram program)
    {
        var (file, arguments) = CommandLine.Split(program.UninstallCommand);
        if (Path.GetFileName(file).Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase))
        {
            file = Path.Combine(Environment.SystemDirectory, "msiexec.exe");
            // Many MSI entries say /I{…} (change or repair): /X{…} uninstalls.
            arguments = MsiInstallSwitch().Replace(arguments, "/X");
        }
        return Process.Start(new ProcessStartInfo(file, arguments) { UseShellExecute = true });
    }

    [GeneratedRegex(@"/I(?=\{)", RegexOptions.IgnoreCase)]
    private static partial Regex MsiInstallSwitch();
}
