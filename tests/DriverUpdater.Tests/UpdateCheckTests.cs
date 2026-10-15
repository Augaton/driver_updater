using Xunit;

public sealed class UpdateCheckTests
{
    const string Page = UpdateCheck.ReleasesUrl + "/tag/v0.4.0";

    [Theory]
    [InlineData("v0.4.0", true)]
    [InlineData("v0.3.1", true)]
    [InlineData("v0.3.0", false)] // same as the running 0.3.0.0
    [InlineData("v0.2.9", false)]
    [InlineData("nightly", false)]
    public void Offers_only_a_newer_version(string tag, bool offered) =>
        Assert.Equal(offered, UpdateCheck.Newer(tag, Page, new Version(0, 3, 0, 0)) is not null);

    [Fact]
    public void Never_offers_a_page_outside_this_repository()
    {
        var release = UpdateCheck.Newer("v9.0.0", "https://evil.example/driver-updater.exe", new Version(0, 3, 0));

        Assert.Equal(UpdateCheck.ReleasesUrl, release!.Url);
    }
}

public sealed class AppReleaseTests
{
    const string Downloads = UpdateCheck.ReleasesUrl + "/download/v0.7.0/";

    [Fact]
    public void Takes_the_installer_and_checksums_only_from_this_repository()
    {
        var release = UpdateCheck.Newer("v0.7.0", UpdateCheck.ReleasesUrl + "/tag/v0.7.0", new Version(0, 6, 0),
        [
            ("DriverUpdater-Setup-0.7.0.exe", "https://evil.example/DriverUpdater-Setup-0.7.0.exe"),
            ("DriverUpdater-Setup-0.7.0.exe", Downloads + "DriverUpdater-Setup-0.7.0.exe"),
            ("SHA256SUMS.txt", Downloads + "SHA256SUMS.txt"),
        ]);

        Assert.Equal(Downloads + "DriverUpdater-Setup-0.7.0.exe", release!.SetupUrl);
        Assert.True(release.CanInstall);
    }

    [Fact]
    public void Cannot_install_without_published_checksums()
    {
        var release = UpdateCheck.Newer("v0.7.0", UpdateCheck.ReleasesUrl, new Version(0, 6, 0),
            [("DriverUpdater-Setup-0.7.0.exe", Downloads + "DriverUpdater-Setup-0.7.0.exe")]);

        Assert.False(release!.CanInstall);
    }

    [Fact]
    public void Finds_a_file_s_hash_in_sha256sums()
    {
        var hash = new string('a', 64);
        var sums = $"{new string('b', 64)}  DriverUpdater-0.7.0-win-x64.zip\n{hash}  DriverUpdater-Setup-0.7.0.exe\n";

        Assert.Equal(hash, UpdateCheck.ExpectedHash(sums, "DriverUpdater-Setup-0.7.0.exe"));
        Assert.Null(UpdateCheck.ExpectedHash(sums, "autre.exe"));
        Assert.Null(UpdateCheck.ExpectedHash("abc  DriverUpdater-Setup-0.7.0.exe", "DriverUpdater-Setup-0.7.0.exe")); // not a SHA-256
    }
}
