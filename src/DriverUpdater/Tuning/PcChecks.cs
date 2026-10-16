using System.ComponentModel;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;

namespace DriverUpdater.Tuning;

/// <summary>
/// What no setting fixes from here but costs the most in games: a screen left at 60 Hz or
/// plugged into the motherboard, memory at its slow default speed or on one channel, a power
/// plan that holds the processor back, a Ryzen X3D without its driver, games on a hard disk, a
/// full system disk. Read-only: each finding says where to act. The verdicts themselves are in
/// <see cref="HardwareRules"/>, tested apart from Windows.
/// </summary>
public static class PcChecks
{
    public static List<PcCheck> Run()
    {
        var checks = new List<PcCheck>();
        bool laptop = Machine.HasBattery;
        Add(checks, () => Screens(laptop));
        Add(checks, () => Memory(laptop));
        Add(checks, () => Power(laptop));
        Add(checks, X3D);
        Add(checks, MemoryIntegrity);
        Add(checks, SteamLibraries);
        Add(checks, SystemDisk);
        return checks;
    }

    // One unreadable check must not hide the others.
    static void Add(List<PcCheck> checks, Func<IEnumerable<PcCheck?>> check)
    {
        try
        {
            checks.AddRange(check().OfType<PcCheck>());
        }
        catch (Exception e) when (e is ManagementException or COMException or IOException or UnauthorizedAccessException
                                      or Win32Exception or InvalidCastException or EntryPointNotFoundException or DllNotFoundException)
        {
        }
    }

    static void Add(List<PcCheck> checks, Func<PcCheck?> check) => Add(checks, () => [check()]);

    static IEnumerable<PcCheck?> Screens(bool laptop)
    {
        var adapters = Displays.Adapters();
        bool discrete = adapters.Any(a => !a.Integrated);
        var integrated = adapters.Where(a => a.Integrated).Select(a => a.Luid).ToHashSet();
        foreach (var screen in Displays.Screens())
        {
            yield return HardwareRules.Refresh(screen.Name, screen.CurrentHz, screen.MaxHz);
            if (screen.External)
                yield return HardwareRules.OnIntegratedGraphics(screen.Name, discrete, integrated.Contains(screen.Adapter), laptop);
        }
    }

    static IEnumerable<PcCheck?> Memory(bool laptop)
    {
        using var searcher = new ManagementObjectSearcher("SELECT Speed, ConfiguredClockSpeed, SMBIOSMemoryType, FormFactor, PartNumber FROM Win32_PhysicalMemory");
        using var results = searcher.Get();
        var modules = new List<MemoryModule>();
        foreach (ManagementBaseObject m in results)
            using (m)
                modules.Add(new MemoryModule(ToInt(m["Speed"]), ToInt(m["ConfiguredClockSpeed"]), ToInt(m["SMBIOSMemoryType"]),
                    ToInt(m["FormFactor"]), m["PartNumber"] as string));
        return HardwareRules.Memory(modules, laptop);
    }

    static int ToInt(object? value) => value is null ? 0 : Convert.ToInt32(value);

    static PcCheck? Power(bool laptop)
    {
        if (Machine.ActivePowerPlan() is not { } plan) return null;
        const string Where = "Paramètres d'alimentation";
        if (plan == Machine.PowerSaver)
            return new("Alimentation", "Mode « Économie d'énergie »", !laptop,
                "Le processeur et la carte graphique sont bridés pour consommer moins. Pour jouer, choisissez le mode « Équilibré ».",
                Where, "ms-settings:powersleep");
        if (plan != Machine.Balanced && Machine.IsDualDieX3D())
            return new("Alimentation", "Plan autre que « Équilibré » sur un Ryzen X3D à deux CCD", true,
                "Ces processeurs ont besoin du plan « Équilibré » : c'est lui qui laisse le pilote d'AMD garder le jeu sur la partie munie du cache 3D. Sinon, le jeu s'étale sur les deux et perd des images.",
                Where, "ms-settings:powersleep");
        return plan == Machine.Balanced
            ? new("Alimentation", "Mode « Équilibré »", false, "Le bon choix : le processeur monte en fréquence dès qu'un jeu le demande.")
            : new("Alimentation", "Plan à hautes performances", false,
                "Aucun gain mesuré sur les processeurs récents : il consomme seulement plus. Le mode « Équilibré » suffit.");
    }

    static PcCheck? X3D()
    {
        if (!Machine.IsDualDieX3D()) return null;
        var missing = new List<string>();
        switch (Machine.VCacheOptimizerWorks())
        {
            case null: missing.Add("le pilote « 3D V-Cache Performance Optimizer » est absent"); break;
            case false: missing.Add("le pilote « 3D V-Cache Performance Optimizer » est en erreur"); break;
        }
        if (!Machine.GameBarInstalled()) missing.Add("la Xbox Game Bar est désinstallée");
        if (!Machine.GameModeOn()) missing.Add("le Mode jeu est désactivé");
        var cpu = Machine.ProcessorName();
        return missing.Count == 0
            ? new(cpu, "Pilote du cache 3D, Game Bar et Mode jeu en place", false, "Les jeux restent sur la partie munie du cache 3D.")
            : new(cpu, string.Join(", ", missing), true,
                "Sans eux, les jeux s'étalent sur les deux parties du processeur et perdent des images. Installez le dernier pilote chipset AMD (il contient le pilote du cache 3D), gardez la Xbox Game Bar et le Mode jeu.",
                "Pilotes AMD", "https://www.amd.com/fr/support/download/drivers.html");
    }

    // Memory integrity blocks malicious drivers; it costs a few frames in some games. Shown,
    // never switched off from here: that choice belongs to the user, in Windows Security.
    static PcCheck? MemoryIntegrity()
    {
        using var searcher = new ManagementObjectSearcher(@"root\Microsoft\Windows\DeviceGuard", "SELECT SecurityServicesRunning FROM Win32_DeviceGuard");
        using var results = searcher.Get();
        var guard = results.Cast<ManagementBaseObject>().FirstOrDefault();
        if (guard is null) return null;
        bool on;
        using (guard)
            on = (guard["SecurityServicesRunning"] as uint[] ?? []).Contains(2u); // 2 = hypervisor-enforced code integrity
        return new("Isolation du noyau (intégrité de la mémoire)", on ? "Activée" : "Désactivée", false,
            on ? "Elle bloque les pilotes malveillants. Elle coûte environ 5 % d'images en 1080p (jusqu'à 10 % dans quelques jeux, presque rien en 4K). Microsoft conseille de la laisser active ; la couper est un choix de sécurité, à faire en connaissance de cause, et certains anti-triche l'exigent."
               : "Désactivée : quelques images de plus, mais moins de protection contre les pilotes malveillants.",
            "Sécurité Windows", "windowsdefender://coreisolation");
    }

    // Open worlds stream from the disk while playing: on a hard disk, that is stutter.
    static IEnumerable<PcCheck?> SteamLibraries()
    {
        foreach (var library in SteamGames.Libraries())
        {
            int games = SteamGames.GameCount(library);
            if (games == 0) continue;
            yield return Machine.OnHardDisk(library) switch
            {
                true => new PcCheck("Jeux Steam", $"{games} jeu(x) sur un disque dur ({library})", true,
                    "Chargements bien plus longs et saccades dans les mondes ouverts. Steam → Paramètres → Stockage permet de déplacer un jeu vers un SSD sans le retélécharger."),
                false => new PcCheck("Jeux Steam", $"{games} jeu(x) sur un SSD ({library})", false, "Rien à faire."),
                null => null,
            };
        }
    }

    static PcCheck? SystemDisk()
    {
        var system = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);
        double free = system.AvailableFreeSpace, total = system.TotalSize;
        bool low = free < 20L * 1024 * 1024 * 1024 || free / total < 0.10;
        return new("Disque système", $"{Bytes.Format((long)free)} libres sur {Bytes.Format((long)total)}", low,
            low ? "Moins de 10 % libres : Windows et les jeux ralentissent et les mises à jour échouent. Faites de la place."
                : "Assez de place.",
            low ? "Libérer de l'espace" : null, low ? "tab:CleanupTab" : null);
    }
}
