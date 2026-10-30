using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace DriverUpdater.Drivers;

public sealed record CatalogEntry(string UpdateId, string Title, string Products, string Classification, DateTime Date, Version? Version, long Size)
{
    /// <summary>How a version reads in the version picker.</summary>
    public string Label => $"{Version} — {Date:dd/MM/yyyy}";
}

/// <summary>
/// Reads the Microsoft Update Catalog (catalog.update.microsoft.com), where every hardware
/// maker publishes its Windows drivers. The site has no API: these parsers follow its HTML,
/// and return nothing rather than guess if that HTML changes.
/// </summary>
public static partial class Catalog
{
    public const string SearchUrl = "https://www.catalog.update.microsoft.com/Search.aspx?q=";
    public const string DownloadDialogUrl = "https://www.catalog.update.microsoft.com/DownloadDialog.aspx";

    /// <summary>
    /// Whether an INF installs a driver Windows needs to start (storage controller, disk,
    /// volume, processor), by its class name or, when it only gives that, its class GUID.
    /// </summary>
    public static bool IsBootCritical(string inf) =>
        InfClass(inf) is { } name && CatalogScope.BootCritical.Contains(name)
        || InfVersionValue(inf, "ClassGuid") is { } guid && Guid.TryParse(guid, out var id) && CatalogScope.BootCriticalGuids.Contains(id);

    /// <summary>The device class an INF file installs ("Display", "SCSIAdapter"…), from its [Version] section.</summary>
    public static string? InfClass(string inf) => InfVersionValue(inf, "Class");

    static string? InfVersionValue(string inf, string key)
    {
        bool inVersion = false;
        foreach (var raw in inf.Split('\n'))
        {
            var line = raw.Split(';')[0].Trim(); // ";" starts a comment
            if (line.StartsWith('['))
            {
                inVersion = line.Equals("[Version]", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            int equals = line.IndexOf('=');
            if (inVersion && equals > 0 && line[..equals].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                return line[(equals + 1)..].Trim().Trim('"');
        }
        return null;
    }

    /// <summary>Only files from Microsoft's own download servers are ever fetched.</summary>
    public static bool IsTrustedDownload(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
        && (uri.Host.Equals("catalog.s.download.windowsupdate.com", StringComparison.OrdinalIgnoreCase)
            || uri.Host.Equals("catalog.sf.dl.delivery.mp.microsoft.com", StringComparison.OrdinalIgnoreCase));

    /// <summary>Search results, newest first as the catalog lists them.</summary>
    public static List<CatalogEntry> ParseSearch(string html)
    {
        var entries = new List<CatalogEntry>();
        // One pass over the page for the rows, then small passes inside each row.
        foreach (Match row in Row().Matches(html))
        {
            var body = row.Groups[2].Value;
            var cells = new Dictionary<int, string>();
            foreach (Match cell in Cell().Matches(body))
                cells.TryAdd(int.Parse(cell.Groups[1].Value, CultureInfo.InvariantCulture), Text(cell.Groups[2].Value));
            string Column(int n) => cells.GetValueOrDefault(n, "");

            if (!DateTime.TryParseExact(Column(4), "M/d/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) continue;
            var title = Column(1);
            var size = OriginalSize().Match(body);
            entries.Add(new CatalogEntry(row.Groups[1].Value, title, Column(2), Column(3), date, VersionOf(Column(5), title),
                size.Success ? long.Parse(size.Groups[1].Value, CultureInfo.InvariantCulture) : 0));
        }
        return entries;
    }

    /// <summary>The version column, or when it says "n/a" the version at the end of the title.</summary>
    public static Version? VersionOf(string column, string title)
    {
        if (Version.TryParse(column, out var version)) return version;
        var inTitle = VersionInText().Matches(title).LastOrDefault();
        return inTitle is not null && Version.TryParse(inTitle.Value, out version) ? version : null;
    }

    /// <summary>The download link and its SHA-1 digest (base64) from DownloadDialog.aspx.</summary>
    public static (string Url, string Digest)? ParseDownload(string page)
    {
        var url = DownloadField().Match(page);
        var digest = DigestField().Match(page);
        return url.Success && digest.Success ? (url.Groups[1].Value, digest.Groups[1].Value) : null;
    }

    /// <summary>The request body DownloadDialog.aspx expects for one update.</summary>
    public static string DownloadRequest(string updateId) =>
        $$"""[{"size":0,"languages":"","uidInfo":"{{updateId}}","updateID":"{{updateId}}"}]""";

    /// <summary>
    /// The catalog versions worth offering for a device, newest first, one entry per version;
    /// and which of them only the generic search found.
    /// </summary>
    /// <param name="hidden">Updates the user hid: never offered again.</param>
    /// <param name="kept">
    /// Updates Windows added, then kept the device's own driver over: neither they nor older
    /// versions would win, so offering them again only costs a download and a restore point.
    /// </param>
    public static (List<CatalogEntry> Versions, IReadOnlySet<string> GenericOnly) Newer(DeviceDriver device,
        IReadOnlyList<CatalogEntry> specific, IReadOnlyList<CatalogEntry> generic, IReadOnlySet<string> hidden, IReadOnlySet<string> kept)
    {
        var genericOnly = generic.Select(e => e.UpdateId).Except(specific.Select(e => e.UpdateId)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var installed = device.HasDriver ? device.ParsedVersion : null;
        var found = specific.Concat(generic).ToList();
        var keptUpTo = found.Where(e => kept.Contains(e.UpdateId)).Max(e => e.Version);

        var versions = found
            .Where(e => !hidden.Contains(e.UpdateId) && (keptUpTo is null || e.Version > keptUpTo))
            // Over Windows' generic driver, only one made for this PC: one for a cousin of the
            // chip would not even be picked by Windows.
            .Where(e => genericOnly.Contains(e.UpdateId) ? installed is null || e.Version > installed : device.WouldUpdate(e.Version, e.Date))
            // One entry per version: the one made for this PC if there is one, then the one
            // published for the nearest Windows version (24H2 over 22H2 on a 24H2 PC), then the newest.
            .GroupBy(e => e.Version)
            .Select(g => g.OrderBy(e => genericOnly.Contains(e.UpdateId)).ThenByDescending(e => MinBuild(e.Products))
                .ThenByDescending(e => e.Date).First())
            .OrderByDescending(e => e.Version)
            .ToList();
        return (versions, genericOnly);
    }

    /// <summary>
    /// What to search for a device: its most specific hardware ID (minus the revision, which
    /// drivers almost never target), then the bare vendor/device ID, which finds the maker's
    /// generic drivers. Windows ranks the two kinds at install time, not this app.
    /// </summary>
    public static IReadOnlyList<string> Queries(IReadOnlyList<string> hardwareIds)
    {
        if (hardwareIds.Count == 0) return [];
        var specific = Without(hardwareIds[0], "REV_");
        var generic = Without(hardwareIds[0], "REV_", "SUBSYS_", "CC_");
        return specific.Equals(generic, StringComparison.OrdinalIgnoreCase) ? [specific] : [specific, generic];
    }

    /// <summary>
    /// Drivers for 64-bit Windows 10 or 11, not ARM ones or other systems, published for this
    /// Windows build or an older one: a package for 24H2 is not for a PC on 23H2.
    /// </summary>
    public static bool Fits(CatalogEntry entry, int windowsBuild) =>
        entry.Classification.Contains("Driver", StringComparison.OrdinalIgnoreCase)
        && (entry.Products.Contains("Windows 10", StringComparison.OrdinalIgnoreCase) || entry.Products.Contains("Windows 11", StringComparison.OrdinalIgnoreCase))
        && !entry.Products.Contains("ARM64", StringComparison.OrdinalIgnoreCase)
        && entry.Version is not null
        && MinBuild(entry.Products) <= windowsBuild;

    // Windows' own numbering of each version: what "version 22H2 and later" means in builds.
    static readonly Dictionary<string, int> Windows10Builds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["1507"] = 10240, ["1511"] = 10586, ["1607"] = 14393, ["1703"] = 15063, ["1709"] = 16299, ["1803"] = 17134,
        ["1809"] = 17763, ["1903"] = 18362, ["1909"] = 18363, ["2004"] = 19041, ["20H2"] = 19042, ["21H1"] = 19043,
        ["21H2"] = 19044, ["22H2"] = 19045,
    };
    static readonly Dictionary<string, int> Windows11Builds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["21H2"] = 22000, ["22H2"] = 22621, ["23H2"] = 22631, ["24H2"] = 26100, ["25H2"] = 26200,
    };

    /// <summary>
    /// The oldest Windows build an entry is published for: catalog drivers are "for this
    /// version and later", and an entry may list several. "Windows 11 Client, version 24H2 and
    /// later" is 26100; a Windows 11 version newer than this list, a build newer than all of it.
    /// </summary>
    public static int MinBuild(string products)
    {
        int? lowest = null;
        foreach (Match product in Product().Matches(products))
        {
            var version = product.Groups[2].Value;
            int build = product.Groups[1].Value == "10"
                ? Windows10Builds.GetValueOrDefault(version, 10240)
                : version.Length == 0 ? 22000 : Windows11Builds.GetValueOrDefault(version, Windows11Builds.Values.Max() + 1);
            lowest = Math.Min(lowest ?? build, build);
        }
        return lowest ?? 0;
    }

    static string Without(string id, params string[] prefixes) =>
        string.Join('&', id.Split('&').Where((part, i) => i == 0 || !prefixes.Any(p => part.StartsWith(p, StringComparison.OrdinalIgnoreCase))));

    static string Text(string html) => WebUtility.HtmlDecode(Spaces().Replace(Tags().Replace(html, " "), " ")).Trim();

    [GeneratedRegex(@"Windows\s+(10|11)\b(?:\s+Client)?(?:\s+S)?(?:,?\s+version\s+(\d{4}|\d{2}H\d))?", RegexOptions.IgnoreCase)]
    private static partial Regex Product();

    [GeneratedRegex(@"<tr id=""([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})_R\d+""(.*?)</tr>", RegexOptions.Singleline)]
    private static partial Regex Row();

    [GeneratedRegex(@"_C(\d+)_R\d+""[^>]*>(.*?)</td>", RegexOptions.Singleline)]
    private static partial Regex Cell();

    [GeneratedRegex(@"_originalSize"">(\d+)<")]
    private static partial Regex OriginalSize();

    [GeneratedRegex(@"downloadInformation\[0\]\.files\[0\]\.url = '([^']+)'")]
    private static partial Regex DownloadField();

    [GeneratedRegex(@"downloadInformation\[0\]\.files\[0\]\.digest = '([^']+)'")]
    private static partial Regex DigestField();

    [GeneratedRegex(@"\d+(\.\d+){1,3}")]
    private static partial Regex VersionInText();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
