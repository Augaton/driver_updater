using System.Globalization;
using System.Text.Json;

namespace DriverUpdater.Drivers;

/// <param name="Version">NVIDIA's own numbering: "617.42".</param>
/// <param name="Studio">A Studio driver (for creative apps) rather than Game Ready.</param>
public sealed record NvidiaRelease(string Version, DateTime? Date, string Name, bool Studio, string DownloadUrl, string DetailsUrl);

/// <param name="Cancelled">No restore point, and the user chose not to go on.</param>
/// <param name="ExitCode">What NVIDIA's installer returned when it closed.</param>
public sealed record NvidiaInstallReport(string RestorePoint, bool Cancelled, string Version, int ExitCode);

/// <summary>
/// The newest GeForce driver for a card, as NVIDIA's own app asks for it: by the card's PCI
/// device ID, no product table needed. The Microsoft Update Catalog lags weeks behind NVIDIA
/// and skips releases, so this is the source for the one driver that changes frame rates.
/// Undocumented by NVIDIA: any answer that does not look right is ignored, never trusted.
/// </summary>
public static class NvidiaModel
{
    public const string Vendor = "VEN_10DE";

    /// <summary>Where downloads may come from: NVIDIA's own servers, in HTTPS.</summary>
    static readonly HashSet<string> DownloadHosts = new(StringComparer.OrdinalIgnoreCase)
        { "us.download.nvidia.com", "international.download.nvidia.com" };

    /// <summary>"PCI\VEN_10DE&amp;DEV_2684&amp;SUBSYS…" gives "2684", or null for another maker's device.</summary>
    public static string? DeviceId(string hardwareId)
    {
        var id = hardwareId.ToUpperInvariant();
        int dev = id.IndexOf("&DEV_", StringComparison.Ordinal);
        if (!id.StartsWith(@"PCI\" + Vendor, StringComparison.Ordinal) || dev < 0 || id.Length < dev + 9) return null;
        var hex = id.Substring(dev + 5, 4);
        return hex.All(Uri.IsHexDigit) ? hex : null;
    }

    public static string LookupUrl(string deviceId, int windowsBuild, bool laptop)
    {
        var query = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["dIDa"] = new[] { deviceId + "_10DE" },
            ["osC"] = "10.0", ["osB"] = windowsBuild.ToString(CultureInfo.InvariantCulture), ["is6"] = "1", ["lg"] = "1033",
            ["iLp"] = laptop ? "1" : "0", ["prvMd"] = "0", ["gcV"] = "3.28.0.417", ["gIsB"] = "0", ["dch"] = "1",
            ["upCRD"] = "0", ["isCRD"] = "0",
        });
        return "https://gfwsl.geforce.com/nvidia_web_services/controller.gfeclientcontent.NG.php/" +
               "com.nvidia.services.GFEClientContent_NG.getDispDrvrByDevid/" + Uri.EscapeDataString(query);
    }

    /// <summary>The release in NVIDIA's answer, or null if the card is not supported or anything is off.</summary>
    public static NvidiaRelease? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!root.TryGetProperty("criteria", out var criteria) || !criteria.TryGetProperty("IsSupported", out var supported)
                || Text(supported, "state") != "true" || !root.TryGetProperty("DriverAttributes", out var driver))
                return null;
            var version = Text(driver, "Version");
            var download = Text(driver, "DownloadURLAdmin");
            var details = Text(driver, "DetailsURL");
            if (version is null || !System.Version.TryParse(version, out _) || download is null || !IsTrustedDownload(download)) return null;
            if (Number(driver, "IsWHQL") != 1 || Number(driver, "IsBeta") != 0) return null; // signed by Microsoft, not a beta
            DateTime? date = Text(driver, "ReleaseDateTime") is { Length: >= 10 } when
                && DateTime.TryParseExact(when[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
            return new NvidiaRelease(version, date, Text(driver, "Name") ?? "Pilote NVIDIA", Number(driver, "IsCRD") == 1, download,
                details is not null && Uri.TryCreate(details, UriKind.Absolute, out var page) && page.Scheme == Uri.UriSchemeHttps
                    && (page.Host.Equals("nvidia.com", StringComparison.OrdinalIgnoreCase) || page.Host.EndsWith(".nvidia.com", StringComparison.OrdinalIgnoreCase))
                    ? details : "https://www.nvidia.com/fr-fr/drivers/");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static bool IsTrustedDownload(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && DownloadHosts.Contains(uri.Host)
        && uri.AbsolutePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Windows numbers NVIDIA's drivers "32.0.16.1742" for NVIDIA's 617.42: the last five
    /// digits. Null for a version that does not follow that rule.
    /// </summary>
    public static string? NvidiaVersion(string windowsVersion)
    {
        // NVIDIA's third field is always "1" followed by one digit ("15", "16"): Microsoft's own
        // display driver ("10.0.26100.1150") must not read as NVIDIA 11.50.
        var parts = windowsVersion.Split('.');
        if (parts.Length != 4 || !parts.All(p => p.Length > 0 && p.All(char.IsAsciiDigit))
            || parts[2].Length != 2 || parts[2][0] != '1' || parts[2][1] == '0' || parts[3].Length > 4) return null;
        var digits = parts[2][^1] + parts[3].PadLeft(4, '0');
        return $"{int.Parse(digits[..3], CultureInfo.InvariantCulture)}.{digits[3..]}";
    }

    /// <summary>NVIDIA's "617.42" in Windows' numbering, next to the installed one, so they compare.</summary>
    public static Version? WindowsVersion(string nvidiaVersion, Version installed)
    {
        if (!System.Version.TryParse(nvidiaVersion, out var v) || v.Major is < 100 or > 999 || v.Minor is < 0 or > 99) return null;
        int digits = v.Major * 100 + v.Minor; // 61742
        return new Version(installed.Major, installed.Minor, 10 + digits / 10000, digits % 10000);
    }

    /// <summary>The offer for the driver report: the label says where to install it from.</summary>
    public static DriverOffer Offer(NvidiaRelease release, Version installed) =>
        new("nvidia:" + release.Version, DriverOffer.Nvidia, WindowsVersion(release.Version, installed), release.Date,
            $"{release.Version}{(release.Date is { } d ? $" — {d:dd/MM/yyyy}" : "")} (page Carte graphique)");

    static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    static int? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int n) ? n : null;
}
