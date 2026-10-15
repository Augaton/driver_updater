using System.IO;
using System.Text.RegularExpressions;

namespace DriverUpdater;

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

    /// <summary>Pipe names the app creates: anything else (such as \\server\pipe) is refused.</summary>
    public static bool IsPipeName(string name) => PipeNamePattern().IsMatch(name);

    public static string NewPipeName() => "DriverUpdater-" + Guid.NewGuid().ToString("N");

    public static AdminCommand Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0) throw new ArgumentException("Commande manquante.");
        var name = args[0];
        var rest = args.Skip(1).ToList();
        switch (name)
        {
            case Install or Hide:
                if (rest.Count == 0) throw new ArgumentException("Aucune mise à jour indiquée.");
                // Update IDs end up inside a Windows Update search string: only plain GUIDs pass.
                return new(name, rest.Select(id => Guid.TryParseExact(id, "D", out var guid)
                    ? guid.ToString("D")
                    : throw new ArgumentException($"Identifiant de mise à jour invalide : {id}")).ToList());
            case ExportDrivers or ImportDrivers:
                if (rest.Count != 1 || !Path.IsPathFullyQualified(rest[0]) || rest[0].StartsWith(@"\\", StringComparison.Ordinal))
                    throw new ArgumentException("Un dossier local, avec son chemin complet, est attendu.");
                return new(name, rest);
            case Version or Unhide or CleanWindowsTemp or Health or Sfc or DismRestore or DismCleanup:
                if (rest.Count != 0) throw new ArgumentException($"« {name} » ne prend pas d'argument.");
                return new(name, rest);
            default:
                throw new ArgumentException($"Commande inconnue : {name}");
        }
    }

    // \z, not $: in .NET, $ also matches before a final "\n".
    [GeneratedRegex(@"^DriverUpdater-[0-9a-f]{32}\z")]
    private static partial Regex PipeNamePattern();
}
