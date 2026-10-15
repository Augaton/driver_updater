using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace DriverUpdater.Common;

public sealed record AppRelease(Version Version, string Url);

/// <summary>
/// Looks for a newer release of this app on GitHub. It only tells the user: installing an
/// unsigned download automatically, with administrator rights, would be too risky.
/// </summary>
public static class UpdateCheck
{
    const string Repository = "Augaton/driver_updater";
    public const string ReleasesUrl = $"https://github.com/{Repository}/releases";

    sealed record GitHubRelease(
        [property: JsonPropertyName("tag_name")] string Tag,
        [property: JsonPropertyName("html_url")] string Url);

    /// <summary>The latest release if it is newer than <paramref name="current"/>, else null.</summary>
    public static async Task<AppRelease?> NewerReleaseAsync(Version current)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        // GitHub's API refuses requests without a User-Agent.
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"DriverUpdater/{current.ToString(3)}");
        var release = await http.GetFromJsonAsync<GitHubRelease>($"https://api.github.com/repos/{Repository}/releases/latest");
        return release is null ? null : Newer(release.Tag, release.Url, current);
    }

    public static AppRelease? Newer(string tag, string url, Version current)
    {
        if (!Version.TryParse(tag.TrimStart('v'), out var version)) return null;
        // The page we will offer to open must belong to this repository.
        if (!url.StartsWith(ReleasesUrl + "/", StringComparison.Ordinal)) url = ReleasesUrl;
        return Normalize(version) > Normalize(current) ? new AppRelease(version, url) : null;
    }

    // 0.3.0 and 0.3.0.0 are the same version.
    static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));
}
