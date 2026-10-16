using Xunit;

// Fixtures are excerpts of real catalog pages (Realtek network card, NVIDIA RTX 3060).
public sealed class CatalogTests
{
    static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));

    [Fact]
    public void Reads_search_results()
    {
        var entries = Catalog.ParseSearch(Fixture("catalog-search.html"));

        Assert.Equal(3, entries.Count);
        var realtek = entries[0];
        Assert.Equal("55e4c288-1590-4fed-b4c1-248ebd64da45", realtek.UpdateId);
        Assert.Equal(new Version(10, 79, 50, 1003), realtek.Version);
        Assert.Equal(new DateTime(2025, 10, 2), realtek.Date);
        Assert.Contains("Networking", realtek.Classification);
        Assert.True(realtek.Size > 0);
        Assert.All(entries, e => Assert.True(Catalog.Fits(e), e.Title));
    }

    [Fact]
    public void Takes_the_version_from_the_title_when_the_column_says_na()
    {
        var nvidia = Catalog.ParseSearch(Fixture("catalog-search.html"))[2];

        Assert.Equal(new Version(32, 0, 15, 6094), nvidia.Version);
    }

    [Fact]
    public void Reads_the_download_link_and_digest()
    {
        var download = Catalog.ParseDownload(Fixture("catalog-download.html"));

        Assert.NotNull(download);
        Assert.True(Catalog.IsTrustedDownload(download.Value.Url), download.Value.Url);
        Assert.Equal(20, Convert.FromBase64String(download.Value.Digest).Length); // SHA-1
    }

    [Theory]
    [InlineData("http://catalog.s.download.windowsupdate.com/d/x.cab")]       // not HTTPS
    [InlineData("https://catalog.s.download.windowsupdate.com.evil.example/x.cab")]
    [InlineData("https://evil.example/catalog.s.download.windowsupdate.com/x.cab")]
    public void Trusts_only_microsoft_download_servers(string url) => Assert.False(Catalog.IsTrustedDownload(url));

    [Fact]
    public void Returns_nothing_for_an_unknown_page()
    {
        Assert.Empty(Catalog.ParseSearch("<html>Nous n'avons trouvé aucun résultat</html>"));
        Assert.Null(Catalog.ParseDownload("<html></html>"));
    }

    [Theory]
    [InlineData("PCI\\VEN_10DE&DEV_2504&SUBSYS_397D1462&REV_A1", "PCI\\VEN_10DE&DEV_2504&SUBSYS_397D1462", "PCI\\VEN_10DE&DEV_2504")]
    [InlineData("HDAUDIO\\FUNC_01&VEN_10EC&DEV_0257&SUBSYS_17AA224B&REV_1000", "HDAUDIO\\FUNC_01&VEN_10EC&DEV_0257&SUBSYS_17AA224B", "HDAUDIO\\FUNC_01&VEN_10EC&DEV_0257")]
    public void Searches_the_specific_then_the_generic_id(string hardwareId, string specific, string generic) =>
        Assert.Equal([specific, generic], Catalog.Queries([hardwareId]));

    [Fact]
    public void Searches_once_when_both_ids_are_the_same() =>
        Assert.Equal(["USB\\VID_046D&PID_C52B"], Catalog.Queries(["USB\\VID_046D&PID_C52B&REV_2410"]));

    [Fact]
    public void Leaves_out_arm_and_other_systems()
    {
        var arm = new CatalogEntry("x", "t", "Windows 11 Client ARM64, version 24H2", "Drivers (Other)", DateTime.Today, new Version(1, 0), 1);
        var old = arm with { Products = "Windows 7" };

        Assert.False(Catalog.Fits(arm));
        Assert.False(Catalog.Fits(old));
    }
}

public sealed class InfClassTests
{
    [Fact]
    public void Reads_the_class_from_the_version_section_only()
    {
        const string inf = """
            ; Copyright
            [Version]
            Signature   = "$WINDOWS NT$"
            ClassGuid   = {4d36e97b-e325-11ce-bfc1-08002be10318}
            Class       = SCSIAdapter ; storage controllers
            Provider    = %Intel%

            [Strings]
            Class = "Display"
            """;

        Assert.Equal("SCSIAdapter", Catalog.InfClass(inf));
    }

    [Fact]
    public void Spots_a_storage_driver_by_its_class_guid_alone()
    {
        Assert.True(Catalog.IsBootCritical("[Version]\r\nClassGuid={4D36E97B-E325-11CE-BFC1-08002BE10318}\r\n"));
        Assert.True(Catalog.IsBootCritical("[Version]\r\nClass=HDC\r\n"));
        Assert.False(Catalog.IsBootCritical("[Version]\r\nClass=Display\r\nClassGuid={4d36e968-e325-11ce-bfc1-08002be10318}\r\n"));
    }

    [Fact]
    public void Gives_nothing_when_the_class_is_missing() =>
        Assert.Null(Catalog.InfClass("[Version]\r\nSignature=\"$WINDOWS NT$\"\r\n[Manufacturer]\r\nClass=Display\r\n"));
}
