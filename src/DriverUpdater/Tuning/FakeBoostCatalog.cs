namespace DriverUpdater.Tuning;

/// <summary>When a repair concerns this PC.</summary>
[Flags]
public enum FakeBoostWhen
{
    Always = 0,
    /// <summary>A fix on a laptop, where it costs battery; only said on a desktop.</summary>
    Laptop = 1,
    /// <summary>Windows for PCs, not Windows Server (whose defaults differ).</summary>
    Client = 2,
    /// <summary>A PC not managed by a company: on a managed one, only said (its IT may want it so).</summary>
    Unmanaged = 4,
    DualDieX3D = 8,
    /// <summary>Only where the Xbox app or Gaming Services is installed.</summary>
    XboxApp = 16,
    /// <summary>Only where Microsoft Defender is the antivirus (another one leaves it passive on purpose).</summary>
    DefenderOnly = 32,
}

/// <summary>Facts about this PC that decide which repairs apply.</summary>
public sealed record PcContext(bool Laptop, bool Client, bool Managed, bool DualDieX3D, bool XboxApp, bool OtherAntivirus);

public enum FakeBoostVerdict { Hidden, Info, Fix }

/// <param name="Found">How to tell it is there, when not simply "the values differ from Windows' own".</param>
/// <param name="InfoOnly">Said, never changed: no measured harm, or a choice someone may have made on purpose.</param>
public sealed record FakeBoost(Tweak Tweak, FakeBoostWhen When = FakeBoostWhen.Always, bool InfoOnly = false, Func<IRegistry, bool>? Found = null)
{
    public bool IsFound(IRegistry registry) => Found?.Invoke(registry) ?? !TweakEngine.IsApplied(Tweak, registry);

    public FakeBoostVerdict Verdict(PcContext pc)
    {
        if (When.HasFlag(FakeBoostWhen.Client) && !pc.Client
            || When.HasFlag(FakeBoostWhen.DualDieX3D) && !pc.DualDieX3D
            || When.HasFlag(FakeBoostWhen.XboxApp) && !pc.XboxApp
            || When.HasFlag(FakeBoostWhen.DefenderOnly) && pc.OtherAntivirus)
            return FakeBoostVerdict.Hidden;
        if (InfoOnly || When.HasFlag(FakeBoostWhen.Laptop) && !pc.Laptop || When.HasFlag(FakeBoostWhen.Unmanaged) && pc.Managed)
            return FakeBoostVerdict.Info;
        return FakeBoostVerdict.Fix;
    }
}

/// <summary>
/// "Défaire les faux boosts": what "optimizer" tools and guides leave behind that slows the PC
/// down or opens it up, each backed by Microsoft's documentation or published measurements.
/// A fix puts Windows' own value back, keeping the old one to undo. Left out because nothing
/// shows harm: Nagle and TCP acknowledgements, network throttling, IRP stack size, MSI mode, a
/// disabled HPET device, telemetry services (a privacy choice).
/// </summary>
public static class FakeBoostCatalog
{
    public const string Group = "Faux boosts";

    // Not registry values: Tuner handles them (bcdedit, the page file, the power plan).
    public const string BootTimers = "boot-timers", PageFile = "page-file", CoreParking = "core-parking";

    const string Services = @"SYSTEM\CurrentControlSet\Services\";
    const string MemoryManagement = @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management";
    const string UpdatePolicy = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate";
    const string RealTime = @"SOFTWARE\Policies\Microsoft\Windows Defender\Real-Time Protection";
    static readonly object Removed = RegistryValue.Removed;

    static Tweak Fix(string id, string title, string why, bool admin, IReadOnlyList<RegistryValue> values, bool restart = true, bool oneClick = true) =>
        new(id, Group, title, why, admin, oneClick, values, restart);

    static RegistryValue Machine(string key, string name, object value, object? whenAbsent = null) =>
        new(Hive.LocalMachine, key, name, value, WhenAbsent: whenAbsent);

    static RegistryValue Start(string service, int start) => Machine(Services + service, "Start", start);

    static bool Is(IRegistry r, string key, string name, int value) => r.Get(Hive.LocalMachine, key, name) is int v && v == value;

    static bool Disabled(IRegistry r, string service) => Is(r, Services + service, "Start", 4);

    public static readonly IReadOnlyList<FakeBoost> All =
    [
        new(Fix(BootTimers, L.T("Minuterie forcée au démarrage (HPET)"),
            L.T("Un guide a forcé la minuterie de la carte mère (bcdedit « useplatformclock ») : Microsoft la réserve au débogage, et les tests ont mesuré de fortes pertes d'images sur les processeurs Intel. Windows choisit seul la meilleure minuterie."),
            admin: true, [])),
        new(Fix(PageFile, L.T("Pas de fichier d'échange"),
            L.T("Sans lui, Windows ne peut engager que la taille de la mémoire : les jeux et applications gourmands plantent faute de mémoire, et aucun rapport de plantage n'est écrit. Microsoft conseille de le laisser gérer par Windows."),
            admin: true, [])),
        new(Fix(CoreParking, L.T("Mise en veille des cœurs désactivée"),
            L.T("Les Ryzen X3D à deux CCD mettent en veille les cœurs sans cache 3D pendant un jeu : c'est ce réglage qui le permet. Désactivé, le jeu s'étale sur les deux parties et perd des images."),
            admin: false, [], restart: false), FakeBoostWhen.DualDieX3D),

        new(Fix("mitigations-off", L.T("Protections du processeur coupées (Spectre, Meltdown)"),
            L.T("Un outil a coupé les protections contre les failles Spectre et Meltdown : un programme ou une page web peut alors lire la mémoire des autres. Le gain est de quelques pour cent au mieux sur les processeurs depuis 2016 (Microsoft)."),
            admin: true, [Machine(MemoryManagement, "FeatureSettingsOverride", Removed), Machine(MemoryManagement, "FeatureSettingsOverrideMask", Removed)]),
            FakeBoostWhen.Unmanaged,
            // Only the "turn off" bits: other values ADD protections and stay.
            Found: r => r.Get(Hive.LocalMachine, MemoryManagement, "FeatureSettingsOverride") is int o && (o & 3) != 0
                        && Is(r, MemoryManagement, "FeatureSettingsOverrideMask", 3)),
        new(Fix("uac-off", L.T("Contrôle de compte d'utilisateur (UAC) coupé"),
            L.T("Tout programme tourne alors avec tous les droits, sans rien demander : la protection de base de Windows est retirée, et des applications du Store refusent de se lancer."),
            admin: true, [Machine(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "EnableLUA", 1, whenAbsent: 1)]),
            FakeBoostWhen.Unmanaged),
        new(Fix("windows-update-policy", L.T("Mises à jour de Windows bloquées par une stratégie"),
            L.T("Windows ne cherche plus ses correctifs de sécurité : chaque faille publiée reste ouverte. Sur un PC personnel, rien ne le justifie ; « Suspendre » dans Windows Update existe pour quelques semaines."),
            admin: true, [Machine(UpdatePolicy + @"\AU", "NoAutoUpdate", Removed), Machine(UpdatePolicy + @"\AU", "AUOptions", Removed),
                          Machine(UpdatePolicy, "DoNotConnectToWindowsUpdateInternetLocations", Removed)]),
            FakeBoostWhen.Unmanaged,
            Found: r => Is(r, UpdatePolicy + @"\AU", "NoAutoUpdate", 1) || Is(r, UpdatePolicy + @"\AU", "AUOptions", 1)
                        || Is(r, UpdatePolicy, "DoNotConnectToWindowsUpdateInternetLocations", 1) && r.Get(Hive.LocalMachine, UpdatePolicy, "WUServer") is null),
        new(Fix("service-wuauserv", L.T("Service Windows Update désactivé"),
            L.T("Plus aucune mise à jour de Windows, ni de pilote par Windows Update."), admin: true, [Start("wuauserv", 3)]),
            FakeBoostWhen.Unmanaged, Found: r => Disabled(r, "wuauserv")),
        new(Fix("service-bits", L.T("Service de transfert en arrière-plan (BITS) désactivé"),
            L.T("Windows Update et le Microsoft Store ne peuvent plus rien télécharger."), admin: true, [Start("BITS", 3)]),
            FakeBoostWhen.Unmanaged, Found: r => Disabled(r, "BITS")),
        new(Fix("defender-policy", L.T("Protection en temps réel de Defender coupée par une stratégie"),
            L.T("Sans autre antivirus, plus rien ne bloque un programme malveillant à son arrivée. Si Defender ne se remet pas, la protection contre les falsifications a pris la main : Sécurité Windows le dira."),
            admin: true, [Machine(RealTime, "DisableRealtimeMonitoring", Removed), Machine(RealTime, "DisableBehaviorMonitoring", Removed),
                          Machine(RealTime, "DisableOnAccessProtection", Removed), Machine(RealTime, "DisableScanOnRealtimeEnable", Removed),
                          Machine(RealTime, "DisableIOAVProtection", Removed)]),
            FakeBoostWhen.Unmanaged | FakeBoostWhen.DefenderOnly,
            Found: r => new[] { "DisableRealtimeMonitoring", "DisableBehaviorMonitoring", "DisableOnAccessProtection", "DisableScanOnRealtimeEnable", "DisableIOAVProtection" }
                .Any(name => Is(r, RealTime, name, 1))),
        new(Fix("service-sysmain", L.T("SysMain (préchargement) désactivé"),
            L.T("Windows ne prépare plus les programmes que vous ouvrez souvent : leur lancement est plus lent, sur disque dur surtout. Aucun gain en jeu n'a été mesuré à le couper."),
            admin: true, [Start("SysMain", 2)]),
            FakeBoostWhen.Client, Found: r => Disabled(r, "SysMain")),
        new(Fix("xbox-services", L.T("Services Xbox désactivés"),
            L.T("La connexion au compte Xbox, les sauvegardes dans le cloud, le jeu en ligne et les manettes du Game Pass en dépendent."),
            admin: true, [Start("XblAuthManager", 3), Start("XblGameSave", 3), Start("XboxNetApiSvc", 3), Start("XboxGipSvc", 3)]),
            FakeBoostWhen.XboxApp, Found: r => new[] { "XblAuthManager", "XblGameSave", "XboxNetApiSvc", "XboxGipSvc" }.Any(s => Disabled(r, s))),
        new(Fix("task-offload-off", L.T("Déchargement réseau désactivé"),
            L.T("La carte réseau ne calcule plus elle-même ce qu'elle sait calculer : le processeur le fait à sa place. Microsoft indique que cela dégrade les performances."),
            admin: true, [Machine(Services + @"Tcpip\Parameters", "DisableTaskOffload", Removed)]),
            Found: r => Is(r, Services + @"Tcpip\Parameters", "DisableTaskOffload", 1)),
        new(Fix("ipv6-off", L.T("IPv6 entièrement désactivé"),
            L.T("Avec cette valeur, chaque démarrage prend 5 secondes de plus (Microsoft), et le chat de groupe Xbox ne passe plus. Microsoft déconseille de couper IPv6."),
            admin: true, [Machine(Services + @"Tcpip6\Parameters", "DisabledComponents", Removed)]),
            Found: r => Is(r, Services + @"Tcpip6\Parameters", "DisabledComponents", -1)), // 0xFFFFFFFF
        new(Fix("power-throttling-off", L.T("Économie d'énergie des tâches de fond coupée"),
            L.T("Windows ne peut plus ralentir les tâches de fond pour la batterie (jusqu'à 90 % d'énergie en moins pour elles selon Microsoft), ni les envoyer sur les cœurs économes des processeurs récents."),
            admin: true, [Machine(@"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", "PowerThrottlingOff", Removed)]),
            FakeBoostWhen.Laptop, Found: r => Is(r, @"SYSTEM\CurrentControlSet\Control\Power\PowerThrottling", "PowerThrottlingOff", 1)),
        new(Fix("timer-global", L.T("Minuterie la plus fine imposée à tout le PC"),
            L.T("Remet le comportement d'avant Windows 10 2004 : tout le PC suit la minuterie la plus fine qu'un programme demande, ce qui empêche le processeur de se mettre en économie (Microsoft). Aucun gain d'images mesuré."),
            admin: true, [Machine(@"SYSTEM\CurrentControlSet\Control\Session Manager\kernel", "GlobalTimerResolutionRequests", Removed)]),
            FakeBoostWhen.Laptop, Found: r => Is(r, @"SYSTEM\CurrentControlSet\Control\Session Manager\kernel", "GlobalTimerResolutionRequests", 1)),
        new(Fix("driver-search-off", L.T("Windows ne cherche plus de pilotes"),
            L.T("Windows Update n'installe plus de pilotes, et un nouveau périphérique reste sans le sien. Certains le règlent exprès, pour éviter les logiciels de souris ou de casque installés d'office : à rétablir seulement si ce n'est pas votre choix."),
            admin: true, [Machine(@"SOFTWARE\Microsoft\Windows\CurrentVersion\DriverSearching", "SearchOrderConfig", 1),
                          Machine(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Device Installer", "DisableCoInstallers", Removed),
                          Machine(UpdatePolicy, "ExcludeWUDriversInQualityUpdate", Removed)], restart: false, oneClick: false),
            FakeBoostWhen.Unmanaged,
            Found: r => Is(r, @"SOFTWARE\Microsoft\Windows\CurrentVersion\DriverSearching", "SearchOrderConfig", 0)
                        || Is(r, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Device Installer", "DisableCoInstallers", 1)
                        || Is(r, UpdatePolicy, "ExcludeWUDriversInQualityUpdate", 1)),

        // ---- Said only -------------------------------------------------------------------------
        new(Fix("system-responsiveness", L.T("« SystemResponsiveness » à 0"),
            L.T("Sans effet : Windows ramène toute valeur sous 10 à 20 (documentation de Microsoft). Inoffensif, mais ce n'est pas un gain."),
            admin: true, []), InfoOnly: true,
            Found: r => r.Get(Hive.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "SystemResponsiveness") is int v && v < 10),
        new(Fix("fullscreen-optimizations-off", L.T("Optimisations plein écran coupées partout"),
            L.T("Microsoft mesure qu'elles font aussi bien ou mieux que le plein écran exclusif ; les couper partout prive aussi du mode fenêtré moderne et du HDR automatique. Mieux vaut les couper jeu par jeu, si l'un pose problème."),
            admin: false, []), InfoOnly: true,
            Found: r => r.Get(Hive.CurrentUser, @"System\GameConfigStore", "GameDVR_HonorUserFSEBehaviorMode") is 1
                        && r.Get(Hive.CurrentUser, @"System\GameConfigStore", "GameDVR_FSEBehavior") is 2),
        new(Fix("clear-pagefile", L.T("Fichier d'échange effacé à chaque arrêt"),
            L.T("L'arrêt peut prendre plusieurs minutes (Microsoft). C'est aussi une stratégie de sécurité légitime : à garder si c'est voulu."),
            admin: true, []), InfoOnly: true,
            Found: r => Is(r, MemoryManagement, "ClearPageFileAtShutdown", 1)),
        new(Fix("search-off", L.T("Recherche Windows désactivée"),
            L.T("La recherche du menu Démarrer, de l'Explorateur et d'Outlook ne trouve plus rien. Son gain en jeu n'a pas été mesuré : depuis 2020, elle se met d'elle-même en retrait pendant une partie."),
            admin: true, []), InfoOnly: true,
            Found: r => Disabled(r, "WSearch")),
    ];

    public static FakeBoost? Find(string id) => All.FirstOrDefault(r => r.Tweak.Id == id);
}
