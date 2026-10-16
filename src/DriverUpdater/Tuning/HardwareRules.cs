using System.Text.RegularExpressions;

namespace DriverUpdater.Tuning;

/// <param name="Action">An ms-settings: or windowsdefender: link, an https link, or "tab:Name" for a page of this app.</param>
public sealed record PcCheck(string Title, string Finding, bool NeedsAttention, string Advice, string? ActionText = null, string? Action = null);

/// <summary>One memory module, as the firmware describes it (SMBIOS, through Win32_PhysicalMemory).</summary>
/// <param name="Speed">The module's rated speed in MT/s, usually its standard (JEDEC) one.</param>
/// <param name="Configured">The speed it runs at, in MT/s.</param>
/// <param name="Type">SMBIOS memory type: 26 is DDR4, 34 is DDR5.</param>
/// <param name="FormFactor">8 is a desktop DIMM, 12 a laptop SODIMM; anything else is soldered or unknown.</param>
public sealed record MemoryModule(int Speed, int Configured, int Type, int FormFactor, string? PartNumber);

/// <summary>
/// The hardware checks' verdicts, apart from the Windows calls that feed them: what costs
/// frames, how sure we are, and what to do.
/// </summary>
public static partial class HardwareRules
{
    const int Ddr4 = 26, Ddr5 = 34, Dimm = 8, Sodimm = 12;

    /// <summary>
    /// The speed a memory kit is sold for, read from its part number when the maker encodes it:
    /// G.Skill "F5-6000J3038F16G", Corsair "CMK32GX5M2B6000C36", Kingston Fury "KF560C36BBEK2"
    /// (560 → 6000), Crucial Ballistix "BL2K16G36C16U4B" (36 → 3600), or "…3600HC18…".
    /// </summary>
    public static int? PartNumberSpeed(string? partNumber)
    {
        if (string.IsNullOrWhiteSpace(partNumber)) return null;
        var part = partNumber.Trim().ToUpperInvariant();
        int? speed =
            GSkill().Match(part) is { Success: true } g ? int.Parse(g.Groups[1].Value)
            : Corsair().Match(part) is { Success: true } c ? int.Parse(c.Groups[1].Value)
            : Kingston().Match(part) is { Success: true } k ? int.Parse(k.Groups[1].Value) * 100
            : Ballistix().Match(part) is { Success: true } b ? int.Parse(b.Groups[1].Value) * 100
            : Generic().Match(part) is { Success: true } x ? int.Parse(x.Groups[1].Value)
            : null;
        return speed is >= 2133 and <= 10000 ? speed : null;
    }

    [GeneratedRegex(@"^F[345]-(\d{4})")]
    private static partial Regex GSkill();
    [GeneratedRegex(@"X[345]M\d[A-Z](\d{4})C\d{1,2}")]
    private static partial Regex Corsair();
    [GeneratedRegex(@"^KF[345](\d{2})C\d{2}")]
    private static partial Regex Kingston();
    [GeneratedRegex(@"^BL\d*K?\d+G(\d{2})C\d{2}")]
    private static partial Regex Ballistix();
    [GeneratedRegex(@"(?<!\d)(\d{4})(?:HC|CL|C)\d{2}(?!\d)")]
    private static partial Regex Generic();

    /// <summary>Memory speed (XMP/EXPO) and channels: the two settings that cost the most frames in CPU-bound games.</summary>
    /// <param name="laptop">A laptop's memory speed is set by its processor and maker: shown, never flagged.</param>
    public static IEnumerable<PcCheck> Memory(IReadOnlyList<MemoryModule> modules, bool laptop = false)
    {
        var sticks = modules.Where(m => m.Configured > 0).ToList();
        if (sticks.Count == 0) yield break;

        int running = sticks.Min(m => m.Configured), rated = sticks.Max(m => m.Speed);
        int? sold = sticks.Select(m => PartNumberSpeed(m.PartNumber)).Where(s => s is not null).Min();
        string kind = sticks.Any(m => m.Type == Ddr5) ? "DDR5" : sticks.Any(m => m.Type == Ddr4) ? "DDR4" : "mémoire";
        const string Bios = "Activez le profil XMP (Intel) ou EXPO (AMD) dans le BIOS : un seul réglage, et la mémoire tourne à la vitesse pour laquelle elle est vendue.";

        if (laptop && (sold > running + 200 || rated > running + 100))
            yield return new("Mémoire", $"{running} MT/s ({kind})", false,
                "Sur un portable, la vitesse de la mémoire est fixée par le processeur et le fabricant ; le BIOS ne propose en général pas de profil XMP.");
        else if (sold > running + 200)
            yield return new("Mémoire", $"{running} MT/s, alors que ces barrettes sont vendues pour {sold} MT/s", true,
                "Profil XMP/EXPO probablement désactivé. " + Bios);
        else if (rated > running + 100)
            yield return new("Mémoire", $"{running} MT/s, alors que les barrettes annoncent {rated} MT/s", true, Bios);
        else
            yield return new("Mémoire", $"{running} MT/s ({kind})", false,
                sold is null
                    ? $"Si vos barrettes sont vendues pour plus (c'est écrit sur la boîte ou l'étiquette : {(kind == "DDR5" ? "DDR5-6000" : "DDR4-3600")}…), activez XMP ou EXPO dans le BIOS."
                    : "Elles tournent à la vitesse pour laquelle elles sont vendues.");

        // One module in a slot can only use one memory channel. Soldered memory is left out:
        // its layout cannot be read from Windows.
        var socketed = sticks.Where(m => m.FormFactor is Dimm or Sodimm).ToList();
        if (socketed.Count == 1 && socketed.Count == sticks.Count)
            yield return new("Barrettes de mémoire", "Une seule barrette : la mémoire fonctionne sur un seul canal", true,
                "Avec deux barrettes identiques, dans les emplacements indiqués par le manuel de la carte mère (souvent A2 et B2), la mémoire double son débit : les jeux qui sollicitent le processeur gagnent nettement en images par seconde.");
    }

    /// <summary>A screen below its best refresh rate at its current resolution; 59.94 Hz and 60 Hz count as the same.</summary>
    public static PcCheck Refresh(string screen, double currentHz, int maxHz)
    {
        string current = currentHz.ToString(currentHz % 1 == 0 ? "0" : "0.##");
        return maxHz - currentHz > 1.5
            ? new($"Écran {screen}", $"Réglé à {current} Hz, il peut afficher {maxHz} Hz", true,
                "C'est le réglage le plus rentable : Paramètres → Affichage → Affichage avancé → choisir la fréquence la plus haute. À 144 Hz, une image dure 6,9 ms au lieu de 16,7 ms à 60 Hz.",
                "Paramètres d'affichage", "ms-settings:display-advanced")
            : new($"Écran {screen}", $"{current} Hz, le maximum à cette définition", false, "Rien à faire.");
    }

    /// <summary>
    /// A screen plugged into the motherboard while a graphics card is there: games then run on
    /// the processor's graphics, or pay to copy every frame across. On a laptop, the ports are
    /// wired as the maker chose: said, not flagged.
    /// </summary>
    public static PcCheck? OnIntegratedGraphics(string screen, bool hasDiscreteGpu, bool drivenByIntegrated, bool laptop)
    {
        if (!hasDiscreteGpu || !drivenByIntegrated) return null;
        return laptop
            ? new($"Écran {screen}", "Relié aux graphiques du processeur", false,
                "Sur un portable, ce port est relié au processeur graphique intégré par construction. Si un jeu tourne mal, réglez-le sur « Hautes performances » dans Paramètres → Affichage → Graphiques.",
                "Paramètres graphiques", "ms-settings:display-advancedgraphics")
            : new($"Écran {screen}", "Branché sur la carte mère, pas sur la carte graphique", true,
                "Les jeux tournent alors sur les graphiques du processeur, ou perdent du temps à recopier chaque image. Branchez le câble de l'écran sur une sortie de la carte graphique (les prises du bas, à l'horizontale, au dos du PC).");
    }
}
