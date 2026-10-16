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
        Add(checks, () => LaptopGraphics(laptop));
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
            yield return HardwareRules.Refresh(screen.Name, screen.CurrentHz, screen.MaxHz, screen.Device);
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
        string Where = L.T("Paramètres d'alimentation");
        if (plan == Machine.PowerSaver)
            return new(L.T("Alimentation"), L.T("Mode « Économie d'énergie »"), !laptop,
                L.T("Le processeur et la carte graphique sont bridés pour consommer moins. Pour jouer, choisissez le mode « Équilibré »."),
                Where, "ms-settings:powersleep");
        if (plan != Machine.Balanced && Machine.IsDualDieX3D())
            return new(L.T("Alimentation"), L.T("Plan autre que « Équilibré » sur un Ryzen X3D à deux CCD"), true,
                L.T("Ces processeurs ont besoin du plan « Équilibré » : c'est lui qui laisse le pilote d'AMD garder le jeu sur la partie munie du cache 3D. Sinon, le jeu s'étale sur les deux et perd des images."),
                Where, "ms-settings:powersleep");
        return plan == Machine.Balanced
            ? new(L.T("Alimentation"), L.T("Mode « Équilibré »"), false, L.T("Le bon choix : le processeur monte en fréquence dès qu'un jeu le demande."))
            : new(L.T("Alimentation"), L.T("Plan à hautes performances"), false,
                L.T("Aucun gain mesuré sur les processeurs récents : il consomme seulement plus. Le mode « Équilibré » suffit."));
    }

    static PcCheck? X3D()
    {
        if (!Machine.IsDualDieX3D()) return null;
        var missing = new List<string>();
        switch (Machine.VCacheOptimizerWorks())
        {
            case null: missing.Add(L.T("le pilote « 3D V-Cache Performance Optimizer » est absent")); break;
            case false: missing.Add(L.T("le pilote « 3D V-Cache Performance Optimizer » est en erreur")); break;
        }
        if (!Machine.GameBarInstalled()) missing.Add(L.T("la Xbox Game Bar est désinstallée"));
        if (!Machine.GameModeOn()) missing.Add(L.T("le Mode jeu est désactivé"));
        var cpu = Machine.ProcessorName();
        return missing.Count == 0
            ? new(cpu, L.T("Pilote du cache 3D, Game Bar et Mode jeu en place"), false, L.T("Les jeux restent sur la partie munie du cache 3D."))
            : new(cpu, string.Join(", ", missing), true,
                L.T("Sans eux, les jeux s'étalent sur les deux parties du processeur et perdent des images. Installez le dernier pilote chipset AMD (il contient le pilote du cache 3D), gardez la Xbox Game Bar et le Mode jeu."),
                L.T("Pilotes AMD"), L.T("https://www.amd.com/fr/support/download/drivers.html"));
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
        return new(L.T("Isolation du noyau (intégrité de la mémoire)"), on ? L.T("Activée") : L.T("Désactivée"), false,
            on ? L.T("Elle bloque les pilotes malveillants. Elle coûte environ 5 % d'images en 1080p (jusqu'à 10 % dans quelques jeux, presque rien en 4K). Microsoft conseille de la laisser active ; la couper est un choix de sécurité, à faire en connaissance de cause, et certains anti-triche l'exigent.")
               : L.T("Désactivée : quelques images de plus, mais moins de protection contre les pilotes malveillants."),
            L.T("Sécurité Windows"), "windowsdefender://coreisolation");
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
                true => new PcCheck(L.T("Jeux Steam"), L.F("{0} jeu(x) sur un disque dur ({1})", games, library), true,
                    L.T("Chargements bien plus longs et saccades dans les mondes ouverts. Steam → Paramètres → Stockage permet de déplacer un jeu vers un SSD sans le retélécharger.")),
                false => new PcCheck(L.T("Jeux Steam"), L.F("{0} jeu(x) sur un SSD ({1})", games, library), false, L.T("Rien à faire.")),
                null => null,
            };
        }
    }

    // A laptop with two graphics chips: are the Steam games set to use the dedicated one?
    static PcCheck? LaptopGraphics(bool laptop)
    {
        if (!laptop) return null;
        var adapters = Displays.Adapters();
        if (!adapters.Any(a => a.Integrated) || !adapters.Any(a => !a.Integrated)) return null;
        var registry = new WindowsRegistry();
        var games = SteamGames.AllGameFolders().Select(SteamGames.GamePrograms).Where(programs => programs.Count > 0).ToList();
        if (games.Count == 0) return null;
        int left = games.Count(programs => !programs.All(p => GpuPreference.IsForced(p, registry)));
        return left == 0
            ? new(L.T("Jeux Steam"), L.T("Tous réglés sur la carte graphique dédiée"), false, L.T("Windows ne les fera pas tourner sur la puce graphique du processeur."),
                GpuPreference.CanUndo(registry) ? L.T("Annuler") : null, GpuPreference.CanUndo(registry) ? "gpu-preference-undo" : null)
            : new(L.T("Jeux Steam"), L.F("{0} jeu(x) sur {1} laissé(s) au choix de Windows", left, games.Count), true,
                L.T("Sur un portable à deux puces graphiques, Windows peut faire tourner un jeu sur celle du processeur, bien moins puissante. Ce réglage de Windows (Paramètres → Affichage → Graphiques) les force sur la carte dédiée ; il s'annule d'un clic."),
                L.T("Forcer la carte dédiée"), "gpu-preference");
    }

    static PcCheck? SystemDisk()
    {
        var system = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!);
        double free = system.AvailableFreeSpace, total = system.TotalSize;
        bool low = free < 20L * 1024 * 1024 * 1024 || free / total < 0.10;
        return new(L.T("Disque système"), L.F("{0} libres sur {1}", Bytes.Format((long)free), Bytes.Format((long)total)), low,
            low ? L.T("Moins de 10 % libres : Windows et les jeux ralentissent et les mises à jour échouent. Faites de la place.")
                : L.T("Assez de place."),
            low ? L.T("Libérer de l'espace") : null, low ? "tab:CleanupTab" : null);
    }
}
