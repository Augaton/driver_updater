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
