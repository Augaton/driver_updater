using System.IO;
using System.Text.RegularExpressions;

namespace DriverUpdater.Admin;

/// <summary>
/// A command for the administrator helper, read from its command line. Anyone can start the
/// helper with any arguments (Windows then asks for consent), so everything is checked here
/// before the helper touches the system: known command, GUIDs for update IDs, full paths.
/// </summary>
public sealed partial record AdminCommand(string Name, IReadOnlyList<string> Args)
{
    public const string Version = "version";
    public const string Install = "install";
    public const string Hide = "hide";
    public const string Unhide = "unhide";
    public const string ExportDrivers = "export-drivers";
    public const string ImportDrivers = "import-drivers";
    public const string CleanWindowsTemp = "clean-windows-temp";
    public const string Health = "health";
    public const string Sfc = "sfc";
    public const string DismRestore = "dism-restore";
    public const string DismCleanup = "dism-cleanup";
    public const string StartupSet = "startup-set";
    public const string Recycle = "recycle";
    public const string BootReport = "boot-report";
    public const string InstallCatalog = "install-catalog";
    public const string DriverStore = "driver-store";
    public const string DeleteDrivers = "delete-drivers";
    /// <summary>Windows Update and catalog drivers in one go, under one consent: "wu:&lt;id&gt;" or "catalog:&lt;id&gt;".</summary>
    public const string InstallAll = "install-all";
    public const string FromWindowsUpdate = "wu:", FromCatalog = "catalog:";
    public const string Rollback = "rollback";
    public const string TweakApply = "tweak-apply";
    public const string TweakUndo = "tweak-undo";
    public const string RestorePoints = "restore-points";
    public const string CreateRestorePoint = "create-restore-point";
    public const string InstallNvidia = "install-nvidia";

    /// <summary>Pipe names the app creates: anything else (such as \\server\pipe) is refused.</summary>
    public static bool IsPipeName(string name) => PipeNamePattern().IsMatch(name);

    /// <summary>
    /// A full path on one of this PC's drives ("C:\…"), not a share whatever its spelling
    /// ("\\server", "//server", "\??\UNC\server"). Elsewhere than Windows (the tests), any full path.
    /// </summary>
    static bool IsLocalPath(string path) =>
        Path.IsPathFullyQualified(path) && (!OperatingSystem.IsWindows() || char.IsAsciiLetter(path[0]) && path[1] == ':');

    public static string NewPipeName() => "DriverUpdater-" + Guid.NewGuid().ToString("N");

    public static AdminCommand Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0) throw new ArgumentException(L.T("Commande manquante."));
        var name = args[0];
        var rest = args.Skip(1).ToList();
        switch (name)
        {
            case Install or Hide or InstallCatalog:
                if (rest.Count == 0) throw new ArgumentException(L.T("Aucune mise à jour indiquée."));
                // Update IDs end up inside a Windows Update search string: only plain GUIDs pass.
                // Two devices can share one package: each is installed once.
                return new(name, rest.Select(id => Guid.TryParseExact(id, "D", out var guid)
                    ? guid.ToString("D")
                    : throw new ArgumentException(L.F("Identifiant de mise à jour invalide : {0}", id))).Distinct().ToList());
            case ExportDrivers or ImportDrivers:
                if (rest.Count != 1 || !IsLocalPath(rest[0]))
                    throw new ArgumentException(L.T("Un dossier local, avec son chemin complet, est attendu."));
                return new(name, rest);
            case Recycle:
                // Each folder is checked again against the leftover rules before anything moves.
                if (rest.Count == 0 || rest.Any(f => !IsLocalPath(f)))
                    throw new ArgumentException(L.T("Des dossiers locaux, avec leur chemin complet, sont attendus."));
                return new(name, rest);
            case InstallAll:
                if (rest.Count == 0) throw new ArgumentException(L.T("Aucune mise à jour indiquée."));
                return new(name, rest.Select(item =>
                {
                    var prefix = item.StartsWith(FromWindowsUpdate, StringComparison.Ordinal) ? FromWindowsUpdate
                        : item.StartsWith(FromCatalog, StringComparison.Ordinal) ? FromCatalog
                        : throw new ArgumentException(L.F("Source de mise à jour inconnue : {0}", item));
                    return Guid.TryParseExact(item[prefix.Length..], "D", out var guid)
                        ? prefix + guid.ToString("D")
                        : throw new ArgumentException(L.F("Identifiant de mise à jour invalide : {0}", item));
                }).Distinct().ToList());
            case Rollback:
                // A device instance ID ("PCI\VEN_10DE&DEV_2504&…\4&2B6DDD8&0&0008"): only looked up, never a path.
                if (rest.Count != 1 || rest[0].Length is 0 or > 260 || rest[0].Any(char.IsControl))
                    throw new ArgumentException(L.T("Identifiant de périphérique invalide."));
                return new(name, rest);
            case InstallNvidia:
                // A PCI device ID, four hex digits: the helper asks NVIDIA itself what to install.
                if (rest.Count != 1 || rest[0].Length != 4 || !rest[0].All(Uri.IsHexDigit))
                    throw new ArgumentException(L.T("Identifiant de carte graphique invalide."));
                return new(name, [rest[0].ToUpperInvariant()]);
            case TweakApply or TweakUndo:
                // Only tweaks of the catalog that need these rights: never a registry key from outside.
                // A retired one may still be undone, never applied.
                if (rest.Count == 0 || rest.Any(id => DriverUpdater.Tuning.TweakCatalog.Find(id) is not { NeedsAdmin: true } tweak
                                                      || (name == TweakApply && tweak.Retired)))
                    throw new ArgumentException(L.T("Réglage inconnu, retiré, ou ne demandant pas l'administrateur."));
                return new(name, rest);
            case DeleteDrivers:
                // Published driver package names only ("oem12.inf"), checked again against the store.
                if (rest.Count == 0 || !rest.All(n => DriverPackageName().IsMatch(n)))
                    throw new ArgumentException(L.T("Noms de paquets de pilotes invalides."));
                return new(name, rest.Select(n => n.ToLowerInvariant()).ToList());
            case StartupSet:
                // <run|run32|folder> <name> <on|off>, for an entry of all users.
                if (rest.Count != 3 || rest[0] is not ("run" or "run32" or "folder") || rest[2] is not ("on" or "off")
                    || rest[1].Length is 0 or > 260 || rest[1].Any(char.IsControl))
                    throw new ArgumentException(L.T("Entrée de démarrage invalide."));
                return new(name, rest);
            case Version or Unhide or CleanWindowsTemp or Health or Sfc or DismRestore or DismCleanup or BootReport or DriverStore or RestorePoints or CreateRestorePoint:
                if (rest.Count != 0) throw new ArgumentException(L.F("« {0} » ne prend pas d'argument.", name));
                return new(name, rest);
            default:
                throw new ArgumentException(L.F("Commande inconnue : {0}", name));
        }
    }

    [GeneratedRegex(@"^oem\d{1,5}\.inf\z", RegexOptions.IgnoreCase)]
    private static partial Regex DriverPackageName();

    // \z, not $: in .NET, $ also matches before a final "\n".
    [GeneratedRegex(@"^DriverUpdater-[0-9a-f]{32}\z")]
    private static partial Regex PipeNamePattern();
}
