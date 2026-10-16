using System.Text.RegularExpressions;

namespace DriverUpdater.Tuning;

/// <param name="Cfg">Lines worth adding to the game's autoexec.cfg, if any.</param>
/// <param name="CfgPath">Where that file lives, relative to the game's install folder.</param>
public sealed record GameTip(int SteamAppId, string Game, IReadOnlyList<string> Tips,
    string? CfgPath = null, IReadOnlyList<string>? Cfg = null, string? LinkText = null, string? Link = null);

/// <summary>
/// Settings that make a real difference in a few popular Steam games, as their communities
/// recommend them. Old "boost" launch options (-high, -threads…) are left out: modern games
/// ignore them or run worse.
/// </summary>
public static partial class GameTips
{
    public const string BlockStart = "// --- Driver Updater : début (supprimer ce bloc pour revenir en arrière) ---";
    public const string BlockEnd = "// --- Driver Updater : fin ---";

    public static readonly IReadOnlyList<GameTip> Known =
    [
        new(4000, "Garry's Mod",
            [
                L.T("Passez sur la version 64 bits : dans Steam, clic droit sur Garry's Mod → Propriétés → Bêtas → « x86-64 ». Le gain est souvent le plus gros possible, et les plantages par manque de mémoire disparaissent."),
                L.T("Le cfg multicœur fait travailler tous les cœurs du processeur au lieu d'un seul. S'il provoque des clignotements ou des plantages sur votre PC, retirez-le d'un clic."),
            ],
            CfgPath: @"garrysmod\cfg\autoexec.cfg",
            Cfg:
            [
                "gmod_mcore_test 1", "mat_queue_mode -1", "cl_threaded_bone_setup 1", "cl_threaded_client_leaf_system 1",
                "r_threaded_client_shadow_manager 1", "r_threaded_particles 1", "r_threaded_renderables 1",
                "r_queued_ropes 1", "studio_queue_mode 1",
            ]),
        new(730, "Counter-Strike 2",
            [
                L.T("Dans les options vidéo, NVIDIA Reflex sur « Activé + Boost » réduit nettement la latence."),
                L.T("Synchronisation verticale désactivée dans le jeu. Avec un écran G-Sync ou FreeSync, limitez plutôt les images par seconde dans le pilote graphique, juste sous la fréquence de l'écran."),
                L.T("Les vieilles options de lancement (-high, -threads, +fps_max 0) n'apportent plus rien sous CS2 : laissez-les vides."),
            ]),
        new(440, "Team Fortress 2",
            [
                L.T("mastercomfig, la référence de la communauté, règle des centaines de variables selon votre PC (de « faible » à « ultra »)."),
                L.T("Le jeu est en 64 bits depuis 2024 : supprimez une éventuelle option de lancement « -dxlevel » héritée d'un vieux guide."),
            ],
            LinkText: "mastercomfig", Link: "https://mastercomfig.com/"),
    ];

    /// <summary>Library folders listed in Steam's libraryfolders.vdf ("path" "D:\\SteamLibrary").</summary>
    public static IReadOnlyList<string> LibraryFolders(string vdf) =>
        PathEntry().Matches(vdf).Select(m => m.Groups[1].Value.Replace(@"\\", @"\")).ToList();

    /// <summary>A game's folder name under steamapps\common, from its appmanifest_*.acf.</summary>
    public static string? InstallDir(string acf) => InstallDirEntry().Match(acf) is { Success: true } m ? m.Groups[1].Value : null;

    public static bool HasBlock(string? cfg) => cfg is not null && cfg.Contains(BlockStart, StringComparison.Ordinal);

    /// <summary>The cfg with our block added at the end (replacing an earlier one); the user's own lines stay.</summary>
    public static string WithBlock(string? cfg, IEnumerable<string> lines)
    {
        var kept = WithoutBlock(cfg ?? "").TrimEnd('\r', '\n');
        var block = string.Join("\r\n", new[] { BlockStart }.Concat(lines).Append(BlockEnd));
        return (kept.Length > 0 ? kept + "\r\n\r\n" : "") + block + "\r\n";
    }

    /// <summary>The cfg without our block: exactly the user's own lines.</summary>
    public static string WithoutBlock(string cfg)
    {
        int start = cfg.IndexOf(BlockStart, StringComparison.Ordinal);
        if (start < 0) return cfg;
        int end = cfg.IndexOf(BlockEnd, start, StringComparison.Ordinal);
        end = end < 0 ? cfg.Length : end + BlockEnd.Length;
        var before = cfg[..start].TrimEnd('\r', '\n');
        var after = cfg[end..].TrimStart('\r', '\n');
        return before.Length > 0 && after.Length > 0 ? before + "\r\n" + after
            : before.Length > 0 ? before + "\r\n"
            : after;
    }

    [GeneratedRegex(@"""path""\s+""([^""]+)""")]
    private static partial Regex PathEntry();

    [GeneratedRegex(@"""installdir""\s+""([^""]+)""")]
    private static partial Regex InstallDirEntry();
}
