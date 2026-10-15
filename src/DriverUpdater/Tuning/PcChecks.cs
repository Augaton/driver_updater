using System.IO;
using System.Management;
using System.Runtime.InteropServices;

namespace DriverUpdater.Tuning;

/// <param name="Action">An ms-settings: or windowsdefender: link, or "tab:Name" for a section of this app.</param>
public sealed record PcCheck(string Title, string Finding, bool NeedsAttention, string Advice, string? ActionText = null, string? Action = null);

/// <summary>
/// What a setting cannot fix but costs the most in games: a screen left at 60 Hz, memory at
/// its slow default speed, a full system disk. Read-only: each finding says where to act.
/// </summary>
public static class PcChecks
{
    public static List<PcCheck> Run()
    {
        var checks = new List<PcCheck>();
        foreach (var screen in Displays.Screens())
            checks.Add(screen.MaxHz > screen.CurrentHz
                ? new($"Écran {screen.Name}", $"Réglé à {screen.CurrentHz} Hz, il peut afficher {screen.MaxHz} Hz", true,
                    "C'est le réglage le plus rentable : choisissez la fréquence la plus haute dans les paramètres d'affichage avancés.",
                    "Paramètres d'affichage", "ms-settings:display-advanced")
                : new($"Écran {screen.Name}", $"{screen.CurrentHz} Hz, le maximum", false, "Rien à faire."));
        AddIfAny(checks, Memory);
        AddIfAny(checks, MemoryIntegrity);
        AddIfAny(checks, SystemDisk);
        return checks;
    }

    static void AddIfAny(List<PcCheck> checks, Func<PcCheck?> check)
    {
        try
        {
            if (check() is { } found) checks.Add(found);
        }
        catch (Exception e) when (e is ManagementException or COMException or IOException or UnauthorizedAccessException)
        {
            // One unreadable check must not hide the others.
        }
    }

    // Memory sold for 3200 or 6000 MT/s runs at its slow default (2133, 4800…) until XMP or
    // EXPO is switched on in the BIOS; games lose up to a fifth of their frames.
    static PcCheck? Memory()
    {
        using var searcher = new ManagementObjectSearcher("SELECT Speed, ConfiguredClockSpeed, SMBIOSMemoryType FROM Win32_PhysicalMemory");
        using var results = searcher.Get();
        var sticks = results.Cast<ManagementBaseObject>()
            .Select(m => (Rated: Convert.ToInt32(m["Speed"] ?? 0), Running: Convert.ToInt32(m["ConfiguredClockSpeed"] ?? 0), Type: Convert.ToInt32(m["SMBIOSMemoryType"] ?? 0)))
            .Where(s => s.Running > 0).ToList();
        if (sticks.Count == 0) return null;
        int running = sticks.Min(s => s.Running), rated = sticks.Max(s => s.Rated);
        bool ddr5 = sticks.Any(s => s.Type == 34);
        int jedecDefault = ddr5 ? 4800 : 2666;
        if (rated > running)
            return new("Mémoire", $"{running} MT/s, alors que les barrettes annoncent {rated} MT/s", true,
                "Activez le profil XMP (Intel) ou EXPO (AMD) dans le BIOS : un réglage, et la mémoire tourne à sa vraie vitesse.");
        return running <= jedecDefault
            ? new("Mémoire", $"{running} MT/s, la vitesse par défaut de la {(ddr5 ? "DDR5" : "DDR4")}", true,
                $"Si vos barrettes sont vendues pour plus (inscrit sur la boîte : {(ddr5 ? "DDR5-6000" : "DDR4-3600")}…), activez XMP ou EXPO dans le BIOS.")
            : new("Mémoire", $"{running} MT/s", false, "Profil XMP/EXPO actif.");
    }

    // Memory integrity blocks malicious drivers; it costs a few frames in some games. Shown,
    // never switched off from here: that choice belongs to the user, in Windows Security.
    static PcCheck? MemoryIntegrity()
    {
        using var searcher = new ManagementObjectSearcher(@"root\Microsoft\Windows\DeviceGuard", "SELECT SecurityServicesRunning FROM Win32_DeviceGuard");
        using var results = searcher.Get();
        var guard = results.Cast<ManagementBaseObject>().FirstOrDefault();
        if (guard is null) return null;
        bool on = (guard["SecurityServicesRunning"] as uint[] ?? []).Contains(2u); // 2 = hypervisor-enforced code integrity
        return new("Isolation du noyau (intégrité de la mémoire)", on ? "Activée" : "Désactivée", false,
            on ? "Elle bloque les pilotes malveillants et coûte quelques pour cent d'images par seconde dans certains jeux. Microsoft conseille de la laisser active ; la désactiver est un choix de sécurité, à faire en connaissance de cause."
               : "Désactivée : un peu plus d'images par seconde, mais moins de protection contre les pilotes malveillants.",
            "Sécurité Windows", "windowsdefender://coreisolation");
    }

    static PcCheck? SystemDisk()
    {
        var system = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);
        double free = system.AvailableFreeSpace, total = system.TotalSize;
        bool low = free < 20L * 1024 * 1024 * 1024 || free / total < 0.10;
        return new("Disque système", $"{Bytes.Format((long)free)} libres sur {Bytes.Format((long)total)}", low,
            low ? "Moins de 10 % libres : Windows et les jeux ralentissent et les mises à jour échouent. Faites de la place."
                : "Assez de place.",
            low ? "Libérer de l'espace" : null, low ? "tab:HealthTab" : null);
    }
}
