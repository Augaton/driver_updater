using System.Diagnostics;
using System.IO;
using System.Text;

namespace DriverUpdater.Software;

/// <summary>Software updates through winget, the package manager built into Windows.</summary>
public static class WinGet
{
    // An "app execution alias" in the user's own WindowsApps folder. The window runs without
    // administrator rights, so starting it from there grants it nothing the user lacks.
    static string ExePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "winget.exe");

    public static bool IsAvailable => File.Exists(ExePath);

    public static async Task<List<SoftwareUpdate>> ListUpgradesAsync()
    {
        var (_, output) = await RunAsync("upgrade", "--accept-source-agreements", "--disable-interactivity");
        return WingetOutput.ParseUpgrades(output);
    }

    /// <summary>
    /// Upgrades one package and returns winget's exit code (0 = done). If the package's
    /// installer needs administrator rights, Windows asks the user for consent.
    /// </summary>
    public static async Task<int> UpgradeAsync(string id)
    {
        var (code, _) = await RunAsync("upgrade", "--id", id, "--exact", "--silent",
            "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity");
        return code;
    }

    /// <summary>Installs one package; same rules and exit code as <see cref="UpgradeAsync"/>.</summary>
    public static async Task<int> InstallAsync(string id)
    {
        var (code, _) = await RunAsync("install", "--id", id, "--exact", "--silent",
            "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity");
        return code;
    }

    static async Task<(int ExitCode, string Output)> RunAsync(params string[] arguments)
    {
        var start = new ProcessStartInfo(ExePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, output);
    }
}
