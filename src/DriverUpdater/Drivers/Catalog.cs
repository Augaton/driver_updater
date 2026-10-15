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

    /// <summary>Drivers for 64-bit Windows 10 or 11, not ARM ones or other systems.</summary>
    public static bool Fits(CatalogEntry entry) =>
        entry.Classification.Contains("Driver", StringComparison.OrdinalIgnoreCase)
        && (entry.Products.Contains("Windows 10", StringComparison.OrdinalIgnoreCase) || entry.Products.Contains("Windows 11", StringComparison.OrdinalIgnoreCase))
        && !entry.Products.Contains("ARM64", StringComparison.OrdinalIgnoreCase)
        && entry.Version is not null;

    static string Without(string id, params string[] prefixes) =>
        string.Join('&', id.Split('&').Where((part, i) => i == 0 || !prefixes.Any(p => part.StartsWith(p, StringComparison.OrdinalIgnoreCase))));

    static string Text(string html) => WebUtility.HtmlDecode(Spaces().Replace(Tags().Replace(html, " "), " ")).Trim();

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
