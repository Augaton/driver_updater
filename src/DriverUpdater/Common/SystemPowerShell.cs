using System.Diagnostics;
using System.IO;
using System.Text;

namespace DriverUpdater.Common;

/// <summary>
/// Windows PowerShell for the administrator helper, with Windows' own modules only. Left to itself,
/// PowerShell loads a command's module from the first folder of PSModulePath that has one by that
/// name, and the user's Documents\WindowsPowerShell\Modules comes first: any program of the user
/// could put a module there and have it run as administrator. So module auto-loading is turned off,
/// each module the script needs is imported by its full path in System32, and the script names its
/// commands with their module (Dism\Get-WindowsDriver).
/// </summary>
public static class SystemPowerShell
{
    static string Home => Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0");

    /// <param name="modules">Windows' modules the script uses, by folder name under System32\WindowsPowerShell\v1.0\Modules.</param>
    /// <returns>PowerShell's exit code (0 when the last command succeeded) and its standard output.</returns>
    public static async Task<(int ExitCode, string Output)> RunAsync(string script, params string[] modules)
    {
        var imports = string.Concat(modules.Select(module =>
            $"Import-Module '{Path.Combine(Home, "Modules", module)}' -DisableNameChecking -ErrorAction Stop; "));
        // Full path: a bare "powershell.exe" could be picked up from the current folder.
        var start = new ProcessStartInfo(Path.Combine(Home, "powershell.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-Command", "$PSModuleAutoLoadingPreference = 'None'; " + imports + script })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync(); // read too, or a chatty error stream would block it
        await Task.WhenAll(output, errors, process.WaitForExitAsync());
        return (process.ExitCode, await output);
    }
}
