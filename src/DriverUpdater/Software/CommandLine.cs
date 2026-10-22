namespace DriverUpdater.Software;

/// <summary>Splits the command lines Windows stores for startup entries and uninstallers.</summary>
public static class CommandLine
{
    /// <summary>
    /// The program and its arguments, from <c>"C:\Program Files\App\app.exe" --x</c> or the
    /// unquoted <c>C:\Program Files\App\app.exe --x</c> that many installers write.
    /// </summary>
    public static (string Program, string Arguments) Split(string command)
    {
        command = Environment.ExpandEnvironmentVariables(command.Trim());
        if (command.StartsWith('"'))
        {
            int end = command.IndexOf('"', 1);
            return end > 1 ? (command[1..end], command[(end + 1)..].Trim()) : (command.Trim('"'), "");
        }
        // Unquoted paths may contain spaces: the program ends at ".exe", unless that ".exe" is in a
        // quoted argument (InstallShield: RunDll32 C:\…\Ctor.dll,LaunchSetup "C:\…\setup.exe" -l0x9).
        int exe = command.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        int quote = command.IndexOf('"');
        if (exe > 0 && (quote < 0 || exe < quote)) return (command[..(exe + 4)], command[(exe + 4)..].Trim());
        int space = command.IndexOf(' ');
        return space > 0 ? (command[..space], command[(space + 1)..].Trim()) : (command, "");
    }
}
