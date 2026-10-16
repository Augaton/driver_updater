using System.IO;

namespace DriverUpdater.Tuning;

/// <summary>
/// Windows' per-program graphics choice (Paramètres → Affichage → Graphiques), one value per
/// program in HKCU\Software\Microsoft\DirectX\UserGpuPreferences; "GpuPreference=2" is the
/// dedicated card. On a laptop with two graphics chips, Windows may otherwise run a game on the
/// processor's. Each previous value is kept, and undone exactly.
/// </summary>
public static class GpuPreference
{
    public const string Key = @"Software\Microsoft\DirectX\UserGpuPreferences";
    const string Backup = TweakEngine.BackupKey + @"\gpu-preference";
    const string HighPerformance = "GpuPreference=2";

    public static bool IsForced(string program, IRegistry registry) =>
        (registry.Get(Hive.CurrentUser, Key, program) as string)?.Split(';').Any(item => item.Trim() == HighPerformance) == true;

    public static void Force(IEnumerable<string> programs, IRegistry registry)
    {
        foreach (var program in programs)
        {
            var current = registry.Get(Hive.CurrentUser, Key, program) as string;
            // Never overwrite a backup: forcing twice must still undo to the original.
            if (registry.Get(Hive.CurrentUser, Backup, program) is null)
                registry.Set(Hive.CurrentUser, Backup, program, current is null ? "absent" : "string:" + current);
            // The other choices the user made for that program (HDR, VRR…) stay.
            registry.Set(Hive.CurrentUser, Key, program, TweakEngine.WithFlag(current, HighPerformance));
        }
    }

    public static bool CanUndo(IRegistry registry) => registry.ValueNames(Hive.CurrentUser, Backup).Count > 0;

    /// <summary>Puts every program back as it was; returns how many.</summary>
    public static int Undo(IRegistry registry)
    {
        var programs = registry.ValueNames(Hive.CurrentUser, Backup);
        foreach (var program in programs)
        {
            if (registry.Get(Hive.CurrentUser, Backup, program) is string saved && saved.StartsWith("string:", StringComparison.Ordinal))
                registry.Set(Hive.CurrentUser, Key, program, saved[7..]);
            else
                registry.Delete(Hive.CurrentUser, Key, program);
            registry.Delete(Hive.CurrentUser, Backup, program);
        }
        return programs.Count;
    }

    static readonly HashSet<string> SkippedFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "_CommonRedist", "CommonRedist", "Redist", "Redistributables", "DirectX", "vcredist", "Support",
        "Installer", "Installers", "__Installer", "Prerequisites", "dotnet",
    };

    static readonly string[] SkippedWords = ["unins", "crash", "report", "setup", "redist", "install", "update", "vc_", "dxsetup", "helper", "uploader"];

    /// <summary>
    /// Whether a program in a game's folder (path relative to it) is the game or its launcher,
    /// not an installer, a crash reporter or a redistributable.
    /// </summary>
    public static bool IsGameProgram(string relativePath)
    {
        var parts = relativePath.Split('\\', '/');
        if (!parts[^1].EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || parts[..^1].Any(SkippedFolders.Contains)) return false;
        var name = Path.GetFileNameWithoutExtension(parts[^1]).ToLowerInvariant();
        return !SkippedWords.Any(name.Contains);
    }
}
