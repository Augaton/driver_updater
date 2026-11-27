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
        string kind = sticks.Any(m => m.Type == Ddr5) ? "DDR5" : sticks.Any(m => m.Type == Ddr4) ? "DDR4" : L.T("mémoire");
        string Bios = L.T("Activez le profil XMP (Intel) ou EXPO (AMD) dans le BIOS : un seul réglage, et la mémoire tourne à la vitesse pour laquelle elle est vendue.");

        if (laptop && (sold > running + 200 || rated > running + 100))
            yield return new(L.T("Mémoire"), $"{running} MT/s ({kind})", false,
                L.T("Sur un portable, la vitesse de la mémoire est fixée par le processeur et le fabricant ; le BIOS ne propose en général pas de profil XMP."));
        else if (sold > running + 200)
            yield return new(L.T("Mémoire"), L.F("{0} MT/s, alors que ces barrettes sont vendues pour {1} MT/s", running, sold), true,
                L.T("Profil XMP/EXPO probablement désactivé. ") + Bios);
        else if (rated > running + 100)
            yield return new(L.T("Mémoire"), L.F("{0} MT/s, alors que les barrettes annoncent {1} MT/s", running, rated), true, Bios);
        else
            yield return new(L.T("Mémoire"), $"{running} MT/s ({kind})", false,
                sold is null
                    ? L.F("Si vos barrettes sont vendues pour plus (c'est écrit sur la boîte ou l'étiquette : {0}…), activez XMP ou EXPO dans le BIOS.", kind == "DDR5" ? "DDR5-6000" : "DDR4-3600")
                    : L.T("Elles tournent à la vitesse pour laquelle elles sont vendues."));

        // One module in a slot can only use one memory channel. Soldered memory is left out:
        // its layout cannot be read from Windows.
        var socketed = sticks.Where(m => m.FormFactor is Dimm or Sodimm).ToList();
        if (socketed.Count == 1 && socketed.Count == sticks.Count)
            yield return new(L.T("Barrettes de mémoire"), L.T("Une seule barrette : la mémoire fonctionne sur un seul canal"), true,
                L.T("Avec deux barrettes identiques, dans les emplacements indiqués par le manuel de la carte mère (souvent A2 et B2), la mémoire double son débit : les jeux qui sollicitent le processeur gagnent nettement en images par seconde."));
    }

    /// <summary>A screen below its best refresh rate at its current resolution; 59.94 Hz and 60 Hz count as the same.</summary>
    /// <param name="device">The screen's GDI name: given, the fix is one click here ("refresh:144:\\.\DISPLAY1").</param>
    public static PcCheck Refresh(string screen, double currentHz, int maxHz, string? device = null)
    {
        if (currentHz <= 0) return new(L.F("Écran {0}", screen), L.T("Fréquence non communiquée par Windows"), false, L.T("Rien à vérifier d'ici."));
        string current = currentHz.ToString(currentHz % 1 == 0 ? "0" : "0.##");
        return maxHz - currentHz > 1.5
            ? new(L.F("Écran {0}", screen), L.F("Réglé à {0} Hz, il peut afficher {1} Hz", current, maxHz), true,
                L.F("C'est le réglage le plus rentable : à {0} Hz, une image dure {1:0.#} ms au lieu de {2:0.#} ms. Si l'image ne revient pas, l'ancienne fréquence est remise au bout de 15 secondes.", maxHz, 1000.0 / maxHz, 1000.0 / currentHz),
                device is null ? L.T("Paramètres d'affichage") : L.F("Passer à {0} Hz", maxHz),
                device is null ? "ms-settings:display-advanced" : $"refresh:{maxHz}:{device}")
            : new(L.F("Écran {0}", screen), L.F("{0} Hz, le maximum à cette définition", current), false, L.T("Rien à faire."));
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
            ? new(L.F("Écran {0}", screen), L.T("Relié aux graphiques du processeur"), false,
                L.T("Sur un portable, ce port est relié au processeur graphique intégré par construction. Si un jeu tourne mal, réglez-le sur « Hautes performances » dans Paramètres → Affichage → Graphiques."),
                L.T("Paramètres graphiques"), "ms-settings:display-advancedgraphics")
            : new(L.F("Écran {0}", screen), L.T("Branché sur la carte mère, pas sur la carte graphique"), true,
                L.T("Les jeux tournent alors sur les graphiques du processeur, ou perdent du temps à recopier chaque image. Branchez le câble de l'écran sur une sortie de la carte graphique (les prises du bas, à l'horizontale, au dos du PC)."));
    }

    /// <summary>
    /// What the anti-cheats of competitive games ask of the PC. Battlefield 6 (Javelin) needs Secure
    /// Boot; Call of Duty Black Ops 7 (Ricochet) and Valorant under Windows 11 (Vanguard) need Secure
    /// Boot and a TPM 2.0. Both are switched on in the motherboard's firmware setup, never from Windows.
    /// </summary>
    /// <param name="uefi">Windows started from UEFI firmware; on a legacy BIOS (CSM) there is no Secure Boot.</param>
    /// <param name="tpmVersion">2 for a TPM 2.0, 1 for a 1.2, 0 for none or one switched off.</param>
    public static PcCheck AntiCheat(bool uefi, bool secureBoot, int tpmVersion)
    {
        var missing = new List<string>();
        if (!uefi) missing.Add(L.T("le PC démarre en mode BIOS ancien (CSM), sans Secure Boot possible"));
        else if (!secureBoot) missing.Add(L.T("Secure Boot est désactivé"));
        if (tpmVersion < 2) missing.Add(tpmVersion == 1 ? L.T("la puce TPM est en version 1.2") : L.T("aucune puce TPM active"));
        if (missing.Count == 0)
            return new(L.T("Anti-triche des jeux en ligne"), L.T("Secure Boot et TPM 2.0 actifs"), false,
                L.T("Ce que demandent Valorant, Call of Duty et Battlefield 6 est en place."));
        return new(L.T("Anti-triche des jeux en ligne"), string.Join(", ", missing), true,
            L.T("Battlefield 6 refuse de se lancer sans Secure Boot ; Call of Duty Black Ops 7 et Valorant (sous Windows 11) demandent aussi une puce TPM 2.0. Les deux s'activent dans le BIOS de la carte mère : Secure Boot, et fTPM (AMD) ou PTT (Intel).")
            + (uefi ? "" : L.T(" Passer du mode BIOS ancien à l'UEFI demande d'abord de convertir le disque système (mbr2gpt) : à faire avec une sauvegarde.")),
            L.T("Carte mère"), "tab:BoardTab");
    }

    /// <summary>
    /// The revision in Windows' "Update Revision" value: Intel's in the upper four of eight bytes
    /// (the lower four zero), AMD's in the lower four.
    /// </summary>
    public static uint MicrocodeRevision(byte[] value)
    {
        ulong bits = value.Length >= 8 ? BitConverter.ToUInt64(value, 0) : BitConverter.ToUInt32(value, 0);
        return (uint)((bits & 0xFFFFFFFF) == 0 ? bits >> 32 : bits & 0xFFFFFFFF);
    }

    /// <summary>The microcode from which Intel's 13th and 14th generation desktop processors stop degrading (May 2025).</summary>
    public const uint VminFixed = 0x12F;

    /// <summary>
    /// Intel's 13th and 14th generation desktop processors (the Raptor Lake die, family 6 model 183)
    /// degrade for good under voltage requests set too high ("Vmin shift instability"). Intel's
    /// microcode fixes it from <see cref="VminFixed"/>; it comes with a BIOS update. The laptop "HX"
    /// chips share the die but not the trouble; other processors are not concerned (null).
    /// </summary>
    /// <param name="identifier">Windows' processor identifier: "Intel64 Family 6 Model 183 Stepping 1".</param>
    public static PcCheck? IntelVmin(string name, string identifier, uint revision)
    {
        if (!identifier.Contains("Family 6 Model 183 ", StringComparison.OrdinalIgnoreCase)
            || !name.Contains("Intel", StringComparison.OrdinalIgnoreCase) || name.Contains("HX", StringComparison.Ordinal))
            return null;
        return revision >= VminFixed
            ? new(name, L.F("Microcode 0x{0:X} : correctif d'Intel en place", revision), false,
                L.T("Ces processeurs pouvaient se dégrader à cause de tensions trop élevées ; ce microcode l'empêche. Gardez le profil « Intel Default Settings » du BIOS."))
            : new(name, L.F("Microcode 0x{0:X}, antérieur au correctif d'Intel (0x12F)", revision), true,
                L.T("Les Core de 13e et 14e génération de bureau peuvent se dégrader définitivement (plantages, puis instabilité qui s'aggrave) à cause de tensions trop élevées. Intel l'a corrigé par un microcode livré avec les BIOS récents : mettez le BIOS de la carte mère à jour, puis gardez le profil « Intel Default Settings »."),
                L.T("Carte mère"), "tab:BoardTab");
    }
}
