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

    /// <summary>
    /// What winget can update. A winget stuck on its sources is stopped after a few minutes,
    /// or as soon as <paramref name="cancel"/> asks: listing changes nothing.
    /// </summary>
    public static async Task<UpgradeList> ListUpgradesAsync(CancellationToken cancel = default)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        limit.CancelAfter(TimeSpan.FromMinutes(5));
        string output;
        try
        {
            (_, output) = await RunAsync(limit.Token, "upgrade", "--accept-source-agreements", "--disable-interactivity");
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            throw new TimeoutException(L.T("winget n'a pas répondu en 5 minutes."));
        }
        return WingetOutput.ParseUpgrades(output);
    }

    /// <summary>
    /// Upgrades one package and returns winget's exit code (0 = done). If the package's
    /// installer needs administrator rights, Windows asks the user for consent.
    /// </summary>
    public static async Task<int> UpgradeAsync(string id)
    {
        var (code, _) = await RunAsync(CancellationToken.None, "upgrade", "--id", id, "--exact", "--silent",
            "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity");
        return code;
    }

    /// <summary>Installs one package; same rules and exit code as <see cref="UpgradeAsync"/>.</summary>
    public static async Task<int> InstallAsync(string id)
    {
        var (code, _) = await RunAsync(CancellationToken.None, "install", "--id", id, "--exact", "--silent",
            "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity");
        return code;
    }

    // Installs are never cut short (a half-run installer can break the program): they get no token.
    static async Task<(int ExitCode, string Output)> RunAsync(CancellationToken cancel, params string[] arguments)
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
        try
        {
            var output = await process.StandardOutput.ReadToEndAsync(cancel);
            await process.WaitForExitAsync(cancel);
            return (process.ExitCode, output);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Already gone, or a part of it is not ours to stop.
            }
            throw;
        }
    }
}
