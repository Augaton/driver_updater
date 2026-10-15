namespace DriverUpdater.Tuning;

/// <summary>
/// The settings of a "PC de guerre", each with a measurable effect and an exact undo. Left
/// out on purpose, though "boost" guides love them: TCP/Nagle tweaks (games use UDP), HPET and
/// timer changes (placebo or worse), disabling services or removing Edge/OneDrive (things
/// break), and any tweak that only works on Windows Enterprise.
/// </summary>
public static class TweakCatalog
{
    public const string Game = "Jeu", Privacy = "Windows plus discret", Space = "Espace disque";

    // Not registry tweaks: the Windows side handles them with powercfg.
    public const string PowerPlan = "power-plan", Hibernation = "hibernation-off";

    const string Gpu = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers";
    const string Mouse = @"Control Panel\Mouse";

    public static readonly IReadOnlyList<Tweak> All =
    [
        new(PowerPlan, Game, "Plan « Performances optimales »",
            "Le processeur reste à pleine fréquence au lieu d'économiser : moins de saccades. Sur un portable, à garder pour le secteur.",
            NeedsAdmin: false, InOneClick: true, []),
        new("game-mode", Game, "Mode jeu de Windows",
            "Pendant une partie, Windows met en pause ses mises à jour et réserve le processeur au jeu.",
            NeedsAdmin: false, InOneClick: true,
            [new(Hive.CurrentUser, @"Software\Microsoft\GameBar", "AllowAutoGameMode", 1),
             new(Hive.CurrentUser, @"Software\Microsoft\GameBar", "AutoGameModeEnabled", 1)]),
        new("game-dvr-off", Game, "Pas d'enregistrement vidéo en arrière-plan",
            "La Xbox Game Bar peut filmer en continu les dernières minutes de jeu : ça coûte des images par seconde.",
            NeedsAdmin: false, InOneClick: true,
            [new(Hive.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled", 0),
             new(Hive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0)]),
        new("windowed-games", Game, "Optimisations des jeux fenêtrés",
            "Les jeux DirectX 10 et 11 en fenêtre ou fenêtre sans bordure gagnent en latence et peuvent utiliser le VRR (G-Sync, FreeSync). Windows 11.",
            NeedsAdmin: false, InOneClick: true,
            [new(Hive.CurrentUser, @"Software\Microsoft\DirectX\UserGpuPreferences", "DirectXUserGlobalSettings", "SwapEffectUpgradeEnable=1", Flag: true)]),
        new("gpu-scheduling", Game, "Planification GPU à accélération matérielle",
            "La carte graphique gère elle-même sa file d'attente : un peu moins de latence, et nécessaire à la génération d'images DLSS 3.",
            NeedsAdmin: true, InOneClick: true,
            [new(Hive.LocalMachine, Gpu, "HwSchMode", 2)], NeedsRestart: true),
        new("mouse-precision-off", Game, "Précision du pointeur désactivée",
            "Supprime l'accélération de la souris : un même geste donne toujours le même déplacement. Indispensable pour viser, mais change le ressenti au bureau.",
            NeedsAdmin: false, InOneClick: false,
            [new(Hive.CurrentUser, Mouse, "MouseSpeed", "0"),
             new(Hive.CurrentUser, Mouse, "MouseThreshold1", "0"),
             new(Hive.CurrentUser, Mouse, "MouseThreshold2", "0")]),
        new("end-task", Game, "« Fin de tâche » au clic droit dans la barre des tâches",
            "Fermer un jeu figé en deux clics, sans ouvrir le Gestionnaire des tâches. Windows 11.",
            NeedsAdmin: false, InOneClick: true,
            [new(Hive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\TaskbarDeveloperSettings", "TaskbarEndTask", 1)]),

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
            "Windows ne garde plus la liste de vos applis et fichiers ouverts, et ne l'envoie plus à Microsoft.",
            NeedsAdmin: true, InOneClick: true,
            [new(Hive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\System", "EnableActivityFeed", 0),
             new(Hive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\System", "PublishUserActivities", 0),
             new(Hive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\System", "UploadUserActivities", 0)]),
        new("telemetry-min", Privacy, "Télémétrie au minimum",
            "Seules les données de diagnostic obligatoires partent encore : Windows Famille et Pro ne descendent pas plus bas.",
            NeedsAdmin: true, InOneClick: true,
            [new(Hive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0)]),

        new(Hibernation, Space, "Mise en veille prolongée désactivée",
            "Libère sur le disque système un fichier de la taille de la moitié de la mémoire (8 Go sur 16 Go). Sur un portable, mieux vaut la garder.",
            NeedsAdmin: true, InOneClick: false, []),
    ];

    public static Tweak? Find(string id) => All.FirstOrDefault(t => t.Id == id);
}
