using System.IO;
using System.Text.RegularExpressions;

namespace DriverUpdater.Software;

/// <summary>The folders programs install into; leftovers are only ever looked for inside them.</summary>
public sealed record LeftoverRoots(string ProgramFiles, string ProgramFilesX86, string ProgramData, string AppData, string LocalAppData)
{
    public static LeftoverRoots Current => new(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    /// <summary>Only administrators can write there.</summary>
    public IEnumerable<string> Machine => new[] { ProgramFiles, ProgramFilesX86, ProgramData }.Where(r => r.Length > 0);
    public IEnumerable<string> All => Machine.Concat(new[] { AppData, LocalAppData }.Where(r => r.Length > 0));
}

public sealed class Leftover
{
    public required string Path { get; init; }
    public required string Reason { get; init; }
    public required bool NeedsAdmin { get; init; }
    public bool Selected { get; set; }
}

/// <summary>
/// Folders an uninstalled program left behind. Wrongly deleting a folder loses someone's
/// data, so the rules only ever widen by evidence: the recorded install folder is offered
/// ticked; folders that merely share the program's name are offered unticked; folders shared
/// by many programs are never offered; and everything goes to the Recycle Bin, not away.
/// </summary>
public static partial class Leftovers
{
    // Folders that hold many programs' files, never one program's leftovers.
    static readonly HashSet<string> Shared = new(StringComparer.OrdinalIgnoreCase)
    {
        "Microsoft", "Windows", "Common Files", "Microsoft Shared", "Windows Defender", "Windows NT", "WindowsApps",
        "Packages", "Programs", "Temp", "Package Cache", "Installer", "Microsoft.NET", "dotnet", "Intel", "NVIDIA",
        "NVIDIA Corporation", "AMD", "Realtek", "Google", "Mozilla", "Adobe", "Apple", "Oracle", "Java", "Steam",
        "Epic Games", "Ubisoft", "Electronic Arts", "Origin", "Riot Games", "JetBrains", "Python",
    };

    /// <param name="inUse">
    /// Install folders of the programs still installed. Office, for one, records the same folder
    /// for each of its languages: uninstalling one must not offer the folder the others use.
    /// </param>
    public static List<Leftover> Find(string displayName, string? publisher, string? installLocation, LeftoverRoots roots,
        IEnumerable<string?>? inUse = null)
    {
        var used = (inUse ?? []).Where(p => !string.IsNullOrWhiteSpace(p) && Path.IsPathFullyQualified(p.Trim().Trim('"')))
            .Select(p => Normalize(p!.Trim().Trim('"'))).ToList();
        var found = new List<Leftover>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Offer(string path, string reason, bool selected)
        {
            if (!IsSafeToRecycle(path, roots, out bool admin) || !seen.Add(Normalize(path))) return;
            var full = Normalize(path);
            // The same folder as one still in use, inside it, or holding it: never offered.
            if (used.Any(u => string.Equals(u, full, StringComparison.OrdinalIgnoreCase) || IsInside(full, u) || IsInside(u, full))) return;
            found.Add(new Leftover { Path = full, Reason = reason, NeedsAdmin = admin, Selected = selected });
        }

        if (!string.IsNullOrWhiteSpace(installLocation)) Offer(installLocation.Trim().Trim('"'), L.T("Dossier d'installation"), selected: true);
        var publishers = PublisherNames(publisher).ToList();
        foreach (var name in Names(displayName))
            foreach (var root in roots.All)
            {
                Offer(Path.Combine(root, name), L.T("Même nom que le programme"), selected: false);
                foreach (var company in publishers)
                    Offer(Path.Combine(root, company, name), L.T("Même nom que le programme"), selected: false);
            }
        return found;
    }

    /// <summary>
    /// A folder strictly inside one of the roots, with no shared folder on the way, not a link,
    /// and not this app's own folder. Checked again by the administrator helper, whatever the
    /// window asked for.
    /// </summary>
    public static bool IsSafeToRecycle(string path, LeftoverRoots roots, out bool needsAdmin)
    {
        needsAdmin = false;
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal))
            return false;
        var full = Normalize(path);
        var root = roots.All.FirstOrDefault(r => IsInside(full, Normalize(r)));
        if (root is null) return false;
        if (Path.GetRelativePath(Normalize(root), full).Split(Path.DirectorySeparatorChar).Any(Shared.Contains)) return false;
        if (!Directory.Exists(full) || new DirectoryInfo(full).Attributes.HasFlag(FileAttributes.ReparsePoint)) return false;
        var self = Normalize(AppContext.BaseDirectory);
        if (string.Equals(self, full, StringComparison.OrdinalIgnoreCase) || IsInside(self, full)) return false;
        needsAdmin = roots.Machine.Any(r => IsInside(full, Normalize(r)));
        return true;
    }

    /// <summary>The all-users root (Program Files, ProgramData…) a folder sits in, or null.</summary>
    public static string? MachineRootOf(string path, LeftoverRoots roots)
    {
        var full = Normalize(path);
        return roots.Machine.Select(Normalize).FirstOrDefault(r => IsInside(full, r));
    }

    static bool IsInside(string path, string root) =>
        path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    /// <summary>"Discord 1.0.9165 (x64)" gives "Discord 1.0.9165 (x64)" and "Discord".</summary>
    static IEnumerable<string> Names(string displayName)
    {
        var exact = displayName.Trim();
        var bare = VersionOrArchitecture().Replace(exact, "").Trim();
        return new[] { exact, bare }.Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(n => n.Length >= 4 && !Shared.Contains(n) && n.IndexOfAny(Path.GetInvalidFileNameChars()) < 0);
    }

    /// <summary>"Valve Corporation" gives "Valve Corporation" and "Valve".</summary>
    static IEnumerable<string> PublisherNames(string? publisher)
    {
        if (string.IsNullOrWhiteSpace(publisher)) return [];
        var exact = publisher.Trim();
        var bare = CompanySuffix().Replace(exact, "").Trim().TrimEnd(',');
        return new[] { exact, bare }.Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(n => n.Length >= 3 && !Shared.Contains(n) && n.IndexOfAny(Path.GetInvalidFileNameChars()) < 0);
    }

    [GeneratedRegex(@"(\s+v?\d+(\.\d+)+|\s*\((x64|x86|64-bit|32-bit|64 bits|32 bits)\))+\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex VersionOrArchitecture();

    [GeneratedRegex(@",?\s+(Inc\.?|Corporation|Corp\.?|Ltd\.?|LLC|GmbH|SAS|S\.A\.|SA|AG|Limited|Co\.,? Ltd\.?)$", RegexOptions.IgnoreCase)]
    private static partial Regex CompanySuffix();
}
