namespace DriverUpdater.Tuning;

/// <summary>
/// The settings of a "PC de guerre". Each one is in for a reason that can be checked: a
/// measured effect, or Microsoft's own documentation; each has an exact undo. Left out on
/// purpose, though "boost" guides love them, because nothing measured them or they hurt:
/// TCP/Nagle and network throttling (games use UDP; "SystemResponsiveness=0" is clamped to 20
/// by Windows itself), HPET and timer-resolution changes, core-parking tweaks, disabling
/// SysMain, Search or services, disabling fullscreen optimizations or MPO, the "Ultimate
/// performance" power plan (no measured gain on current PCs, and it breaks the core parking
/// Ryzen X3D chips rely on), and anything that only works on Windows Enterprise.
/// </summary>
public static class TweakCatalog
{
    public const string Game = "Jeu", Comfort = "Confort de jeu", Privacy = "Windows plus discret", Space = "Espace disque";

    // Not registry tweaks: the Windows side handles them with powercfg.
    public const string PowerPlan = "power-plan", Hibernation = "hibernation-off";

    const string Gpu = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers";
    const string Mouse = @"Control Panel\Mouse";

    const string PolicyNote = " Paramètres affichera « géré par votre organisation » pour ce réglage : c'est le signe qu'il est appliqué.";

    public static readonly IReadOnlyList<Tweak> All =
    [
        // ---- Performance in games ------------------------------------------------------------
        new("game-mode", Game, "Mode jeu de Windows",
            "Pendant une partie, Windows reporte ses mises à jour et l'installation de pilotes, et donne la priorité au jeu. Activé par défaut ; indispensable aux Ryzen X3D à deux CCD, dont le pilote s'en sert pour garder le jeu sur le cache 3D.",
            NeedsAdmin: false, InOneClick: true,
            [new(Hive.CurrentUser, @"Software\Microsoft\GameBar", "AutoGameModeEnabled", 1, WhenAbsent: 1)]),
        new("background-recording-off", Game, "Pas d'enregistrement en arrière-plan",
            "La Xbox Game Bar peut filmer en continu les dernières minutes de jeu : jusqu'à 7 % d'images en moins sur une carte graphique modeste. Désactivé par défaut ; les captures à la demande (Win + Alt + R) restent possibles.",
            NeedsAdmin: false, InOneClick: true,
            [new(Hive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "HistoricalCaptureEnabled", 0, WhenAbsent: 0)]),
        new("windowed-games", Game, "Optimisations des jeux fenêtrés",
            "Les jeux DirectX 10 et 11 en fenêtre ou fenêtre sans bordure passent au mode d'affichage moderne : moins de latence, et le VRR (G-Sync, FreeSync) en fenêtre. C'est le réglage que recommande Microsoft ; il est désactivé par défaut.",
            NeedsAdmin: false, InOneClick: true,
            [new(Hive.CurrentUser, @"Software\Microsoft\DirectX\UserGpuPreferences", "DirectXUserGlobalSettings", "SwapEffectUpgradeEnable=1", Flag: true)],
            MinBuild: 22000),
        new("gpu-scheduling", Game, "Planification GPU à accélération matérielle",
            "La carte graphique gère elle-même sa file d'attente : jusqu'à 4 % de mieux selon les jeux, un peu moins de latence, et nécessaire à la génération d'images DLSS. Cartes depuis les GeForce GTX 10 et Radeon RX 5000 ; sans effet sur les autres.",
            NeedsAdmin: true, InOneClick: true,
            [new(Hive.LocalMachine, Gpu, "HwSchMode", 2)], NeedsRestart: true),

        // ---- Comfort: no frames gained, said so --------------------------------------------
        new("mouse-precision-off", Comfort, "Précision du pointeur désactivée",
            "Sans accélération, un même geste donne toujours le même déplacement : utile pour viser dans les jeux qui suivent le curseur de Windows. Les jeux de tir modernes lisent la souris directement et ne sont pas concernés. Change le ressenti au bureau.",
            NeedsAdmin: false, InOneClick: false,
            [new(Hive.CurrentUser, Mouse, "MouseSpeed", "0"),
             new(Hive.CurrentUser, Mouse, "MouseThreshold1", "0"),
             new(Hive.CurrentUser, Mouse, "MouseThreshold2", "0")]),
        new("end-task", Comfort, "« Fin de tâche » au clic droit dans la barre des tâches",
            "Fermer un jeu figé en deux clics, sans ouvrir le Gestionnaire des tâches.",
            NeedsAdmin: false, InOneClick: true,
            [new(Hive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarDeveloperSettings", "TaskbarEndTask", 1)],
            MinBuild: 22631),

        // ---- Privacy ---------------------------------------------------------------------------
        new("advertising-id-off", Privacy, "Identifiant publicitaire désactivé",
            "Les applications ne peuvent plus vous suivre d'une appli à l'autre pour la publicité.",
            NeedsAdmin: false, InOneClick: true,
            [new(Hive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 0)]),
        new("tailored-off", Privacy, "Pas d'expériences « personnalisées »",
            "Windows n'utilise plus vos données de diagnostic pour vous proposer conseils et publicités.",
            NeedsAdmin: false, InOneClick: true,
            [new(Hive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", 0)]),
        new("suggested-apps-off", Privacy, "Pas d'applications installées « en suggestion »",
            "Windows n'installe plus de lui-même jeux et applis sponsorisés, et le menu Démarrer cesse de les suggérer.",
            NeedsAdmin: false, InOneClick: true,
            [new(Hive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SilentInstalledAppsEnabled", 0),
             new(Hive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SystemPaneSuggestionsEnabled", 0)]),
        new("bing-search-off", Privacy, "Recherche Windows sans Bing",
            "Taper dans le menu Démarrer cherche sur le PC, sans envoyer chaque frappe à Bing.",
            NeedsAdmin: false, InOneClick: true,
            [new(Hive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Search", "BingSearchEnabled", 0)]),
        new("activity-history-off", Privacy, "Historique des activités désactivé",
            "Windows ne garde plus la liste de vos applis et fichiers ouverts, et ne l'envoie plus à Microsoft." + PolicyNote,
            NeedsAdmin: true, InOneClick: true,
            [new(Hive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\System", "EnableActivityFeed", 0),
             new(Hive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\System", "PublishUserActivities", 0),
             new(Hive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\System", "UploadUserActivities", 0)]),
        new("telemetry-min", Privacy, "Télémétrie au minimum",
            "Seules les données de diagnostic obligatoires partent encore : Windows Famille et Pro ne descendent pas plus bas." + PolicyNote,
            NeedsAdmin: true, InOneClick: true,
            [new(Hive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0)]),

        // ---- Disk space --------------------------------------------------------------------------
        new(Hibernation, Space, "Mise en veille prolongée désactivée",
            "Libère sur le disque système un fichier de la taille de la moitié de la mémoire (8 Go sur 16 Go). Le démarrage rapide de Windows s'en sert aussi : il est désactivé avec elle. Sur un portable, mieux vaut la garder.",
            NeedsAdmin: true, InOneClick: false, []),

        // ---- Retired: shown only where this app applied them, to undo them ---------------------
        new(PowerPlan, Game, "Plan « Performances optimales » (retiré)",
            "Retiré : aucun gain mesuré sur les PC actuels, et il empêche les Ryzen X3D à deux CCD de garder le jeu sur le cache 3D. « Annuler » remet votre plan d'alimentation d'avant.",
            NeedsAdmin: false, InOneClick: false, [], Retired: true),
        new("game-dvr-off", Game, "Captures de la Game Bar coupées (retiré)",
            "Retiré : couper les captures ne fait rien gagner quand rien n'enregistre ; seul l'enregistrement en arrière-plan coûte des images (réglage ci-dessus). « Annuler » remet les captures comme avant.",
            NeedsAdmin: false, InOneClick: false,
            [new(Hive.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled", 0),
             new(Hive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0)], Retired: true),
    ];

    public static Tweak? Find(string id) => All.FirstOrDefault(t => t.Id == id);
}
