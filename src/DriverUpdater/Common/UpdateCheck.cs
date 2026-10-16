using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace DriverUpdater.Common;

/// <param name="SetupUrl">The release's installer, when it has one from this repository.</param>
/// <param name="ChecksumsUrl">The release's SHA256SUMS.txt, needed to trust that installer.</param>
public sealed record AppRelease(Version Version, string Url, string? SetupUrl = null, string? ChecksumsUrl = null)
{
    public bool CanInstall => SetupUrl is not null && ChecksumsUrl is not null;
}

/// <summary>
/// Looks for a newer release of this app on GitHub and, if asked, installs it: the installer
/// is only taken from this repository's releases, and only run if it matches the SHA-256
/// published with it.
/// </summary>
public static class UpdateCheck
{
    const string Repository = "Augaton/driver_updater";
    public const string ReleasesUrl = $"https://github.com/{Repository}/releases";
    const string DownloadsUrl = ReleasesUrl + "/download/";

    sealed record GitHubAsset(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("browser_download_url")] string Url);

    sealed record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string Tag,
        [property: JsonPropertyName("html_url")] string Url,
        [property: JsonPropertyName("assets")] List<GitHubAsset>? Assets);

    static HttpClient Client(Version current, TimeSpan timeout)
    {
        var http = new HttpClient { Timeout = timeout };
        // GitHub's API refuses requests without a User-Agent.
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"DriverUpdater/{current.ToString(3)}");
        return http;
    }

    /// <summary>The latest release if it is newer than <paramref name="current"/>, else null.</summary>
    public static async Task<AppRelease?> NewerReleaseAsync(Version current)
    {
        using var http = Client(current, TimeSpan.FromSeconds(10));
        var release = await http.GetFromJsonAsync<GitHubRelease>($"https://api.github.com/repos/{Repository}/releases/latest");
        if (release is null) return null;
        var assets = (release.Assets ?? []).Select(a => (a.Name, a.Url)).ToList();
        return Newer(release.Tag, release.Url, current, assets);
    }

    public static AppRelease? Newer(string tag, string url, Version current, IReadOnlyList<(string Name, string Url)>? assets = null)
    {
        if (!Version.TryParse(tag.TrimStart('v'), out var version)) return null;
        // The page we will offer to open must belong to this repository.
        if (!url.StartsWith(ReleasesUrl + "/", StringComparison.Ordinal)) url = ReleasesUrl;
        if (Normalize(version) <= Normalize(current)) return null;

        // Files are only taken from this repository's own release downloads.
        string? Asset(Func<string, bool> pick) => assets?
            .Where(a => pick(a.Name) && a.Url.StartsWith(DownloadsUrl, StringComparison.Ordinal))
            .Select(a => a.Url).FirstOrDefault();
        return new AppRelease(version, url,
            Asset(n => n.StartsWith("DriverUpdater-Setup-", StringComparison.Ordinal) && n.EndsWith(".exe", StringComparison.Ordinal)),
            Asset(n => n == "SHA256SUMS.txt"));
    }

    /// <summary>The hash SHA256SUMS.txt gives for a file ("&lt;hex&gt;  &lt;name&gt;" lines), or null.</summary>
    public static string? ExpectedHash(string checksums, string fileName) =>
        checksums.Split('\n')
            .Select(line => line.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries))
            .Where(parts => parts.Length == 2 && parts[1].Trim().TrimStart('*') == fileName && parts[0].Length == 64)
            .Select(parts => parts[0].ToLowerInvariant())
            .FirstOrDefault();

    /// <summary>
    /// Removes this app's download folders from %TEMP% older than a day: their installer has
    /// run long ago (NVIDIA's weighs close to 1 GB). One still in use is simply left.
    /// </summary>
    public static void CleanOldDownloads()
    {
        foreach (var folder in new DirectoryInfo(Path.GetTempPath()).EnumerateDirectories("DriverUpdater-*"))
        {
            var suffix = folder.Name["DriverUpdater-".Length..];
            if (suffix.Length != 32 || !suffix.All(Uri.IsHexDigit) || folder.CreationTimeUtc > DateTime.UtcNow.AddDays(-1)) continue;
            try
            {
                folder.Delete(recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>
    /// Downloads the installer into a new folder and checks it against SHA256SUMS.txt. Throws if
    /// anything does not match: nothing unchecked is ever started.
    /// </summary>
    public static async Task<VerifiedInstaller> DownloadInstallerAsync(AppRelease release, Version current, IProgress<string> progress)
    {
        if (!release.CanInstall) throw new InvalidOperationException(L.T("Cette version n'a pas d'installateur téléchargeable."));
        using var http = Client(current, TimeSpan.FromMinutes(15));
        var fileName = Path.GetFileName(new Uri(release.SetupUrl!).LocalPath);
        progress.Report(L.T("Téléchargement des empreintes…"));
        var expected = ExpectedHash(await http.GetStringAsync(release.ChecksumsUrl), fileName)
            ?? throw new InvalidOperationException(L.T("L'empreinte de l'installateur manque dans SHA256SUMS.txt."));

        CleanOldDownloads();
        var folder = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "DriverUpdater-" + Guid.NewGuid().ToString("N")));
        var path = Path.Combine(folder.FullName, fileName);
        progress.Report(L.F("Téléchargement de {0}…", fileName));
        using (var response = await http.GetAsync(release.SetupUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            await Download.ToFileAsync(response, path, progress: null, TimeSpan.FromSeconds(60));
        }

        // Hashed through the handle that then keeps it locked: what was checked is what runs.
        var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            if (Convert.ToHexStringLower(await SHA256.HashDataAsync(file)) == expected) return new VerifiedInstaller(path, file);
        }
        catch
        {
            await file.DisposeAsync();
            throw;
        }
        await file.DisposeAsync();
        folder.Delete(recursive: true);
        throw new InvalidOperationException(L.T("L'installateur téléchargé ne correspond pas à son empreinte publiée : il n'est pas lancé."));
    }

    // 0.3.0 and 0.3.0.0 are the same version.
    static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));
}

/// <summary>
/// A checked installer, held open for reading only: until it has started, no other program can
/// rewrite, rename or delete it to slip something else into the administrator prompt.
/// </summary>
public sealed class VerifiedInstaller(string path, FileStream locked) : IDisposable
{
    public string Path { get; } = path;

    /// <summary>Starts it; it asks Windows for administrator rights itself.</summary>
    public void Start() => Process.Start(new ProcessStartInfo(Path) { UseShellExecute = true });

    public void Dispose() => locked.Dispose();
}
