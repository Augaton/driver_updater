using System.IO;
using System.Security;
using System.Text.Json;
using Microsoft.Win32;

namespace DriverUpdater.Tuning;

/// <summary>A game installed by a launcher other than Steam.</summary>
public sealed record LauncherGame(string Launcher, string Name, string Folder);

/// <summary>
/// Games installed through Epic Games, GOG Galaxy and Ubisoft Connect, read where each launcher
/// keeps its list (the places Playnite reads too). Xbox app games (MSIX packages) stay out of
/// reach. Read-only: the checks only look at where the games are and what programs they run.
/// </summary>
public static class LauncherGames
{
    public static List<LauncherGame> All()
    {
        var games = new List<LauncherGame>();
        Add(games, Epic);
        Add(games, Gog);
        Add(games, Ubisoft);
        return games.Where(g => Directory.Exists(g.Folder)).DistinctBy(g => g.Folder, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Every game folder, Steam's and the other launchers': for the checks that look at all games.</summary>
    public static List<string> AllFolders() =>
        SteamGames.AllGameFolders().Concat(All().Select(g => g.Folder)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    // One launcher's list unreadable (moved, half uninstalled) leaves the others.
    static void Add(List<LauncherGame> games, Func<IEnumerable<LauncherGame>> source)
    {
        try
        {
            games.AddRange(source());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or SecurityException)
        {
        }
    }

    // One JSON manifest per installed item, add-ons included (GameTips.EpicManifest leaves those out).
    static IEnumerable<LauncherGame> Epic()
    {
        var manifests = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");
        if (!Directory.Exists(manifests)) return [];
        return Directory.EnumerateFiles(manifests, "*.item")
            .Select(file => GameTips.EpicManifest(File.ReadAllText(file)))
            .OfType<(string Name, string Folder)>()
            .Select(game => new LauncherGame("Epic Games", game.Name, game.Folder))
            .ToList();
    }

    // A key per game; one that "depends on" another is that game's add-on.
    static IEnumerable<LauncherGame> Gog()
    {
        using var games = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\GOG.com\Games");
        var list = new List<LauncherGame>();
        foreach (var id in games?.GetSubKeyNames() ?? [])
        {
            using var game = games!.OpenSubKey(id);
            if (game is null || game.GetValue("dependsOn") is string { Length: > 0 }) continue;
            if ((game.GetValue("path") ?? game.GetValue("workingDir")) is string { Length: > 0 } folder)
                list.Add(new LauncherGame("GOG", game.GetValue("gameName") as string ?? id, folder));
        }
        return list;
    }

    // Only the folder is kept (written with forward slashes): its name stands for the game's.
    static IEnumerable<LauncherGame> Ubisoft()
    {
        using var installs = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs");
        var list = new List<LauncherGame>();
        foreach (var id in installs?.GetSubKeyNames() ?? [])
        {
            using var game = installs!.OpenSubKey(id);
            if (game?.GetValue("InstallDir") is not string { Length: > 0 } dir) continue;
            var folder = Path.GetFullPath(dir.Replace('/', '\\'));
            list.Add(new LauncherGame("Ubisoft Connect", Path.GetFileName(folder.TrimEnd('\\')), folder));
        }
        return list;
    }
}
