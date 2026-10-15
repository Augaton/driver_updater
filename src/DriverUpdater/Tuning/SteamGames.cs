using System.IO;
using Microsoft.Win32;

namespace DriverUpdater.Tuning;

public sealed class InstalledGame
{
    public required GameTip Tip { get; init; }
    public required string Folder { get; init; }

    public string? CfgFile => Tip.CfgPath is null ? null : Path.Combine(Folder, Tip.CfgPath);
    public bool HasCfg => Tip.Cfg is not null;
    public bool CfgApplied => CfgFile is { } file && File.Exists(file) && GameTips.HasBlock(File.ReadAllText(file));
    public bool HasLink => Tip.Link is not null;
}

/// <summary>
/// The games of <see cref="GameTips"/> installed through Steam, in any of its libraries. Game
/// folders belong to the user: their cfg files are written without administrator rights.
/// </summary>
public static class SteamGames
{
    public static List<InstalledGame> Find()
    {
        using var steam = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        if (steam?.GetValue("SteamPath") is not string steamPath) return [];
        steamPath = Path.GetFullPath(steamPath.Replace('/', '\\'));

        var libraries = new List<string> { steamPath };
        var vdf = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdf)) libraries.AddRange(GameTips.LibraryFolders(File.ReadAllText(vdf)));

        var games = new List<InstalledGame>();
        foreach (var tip in GameTips.Known)
            foreach (var library in libraries.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var manifest = Path.Combine(library, "steamapps", $"appmanifest_{tip.SteamAppId}.acf");
                if (!File.Exists(manifest) || GameTips.InstallDir(File.ReadAllText(manifest)) is not { } dir) continue;
                var folder = Path.Combine(library, "steamapps", "common", dir);
                if (Directory.Exists(folder))
                {
                    games.Add(new InstalledGame { Tip = tip, Folder = folder });
                    break;
                }
            }
        return games;
    }

    /// <summary>Adds the tip's lines to the game's autoexec.cfg, keeping the user's own, after a backup copy.</summary>
    public static void ApplyCfg(InstalledGame game)
    {
        var file = game.CfgFile ?? throw new InvalidOperationException("Pas de cfg pour ce jeu.");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        string? current = File.Exists(file) ? File.ReadAllText(file) : null;
        if (current is not null && !File.Exists(file + ".avant-driver-updater")) File.Copy(file, file + ".avant-driver-updater");
        File.WriteAllText(file, GameTips.WithBlock(current, game.Tip.Cfg!));
    }

    public static void RemoveCfg(InstalledGame game)
    {
        if (game.CfgFile is { } file && File.Exists(file)) File.WriteAllText(file, GameTips.WithoutBlock(File.ReadAllText(file)));
    }
}
