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
        Assert.All(entries, e => Assert.True(Catalog.Fits(e, 26100), e.Title));
    }

    [Theory]
    [InlineData("Windows 11 Client, version 24H2 and later, Servicing Drivers, Windows 11 Client, version 24H2 and later, Upgrade & Servicing Drivers", 26100)]
    [InlineData("Windows 11 Client S, version 22H2 and later, Servicing Drivers", 22621)]
    [InlineData("Windows 10 version 1803 and Later Servicing Drivers, Windows 10 Version 1803 and Later Upgrade & Servicing Drivers", 17134)]
    [InlineData("Windows 10, version 1809 and later, Servicing Drivers, Windows 11 Client, version 21H2 and later, Servicing Drivers", 17763)]
    [InlineData("Windows 10 S and Later Servicing Drivers", 10240)]
    [InlineData("Windows 11 Client, version 27H2 and later, Servicing Drivers", 26201)] // newer than the list: only for newer than it
    public void Reads_the_oldest_windows_build_an_entry_is_for(string products, int build) =>
        Assert.Equal(build, Catalog.MinBuild(products));

    [Fact]
    public void Leaves_out_packages_made_for_a_newer_windows()
    {
        // The fixture's Realtek driver, twice: for 24H2 and later, then for 22H2 and later.
        var entries = Catalog.ParseSearch(Fixture("catalog-search.html"));

        Assert.Equal([false, true, true], entries.Select(e => Catalog.Fits(e, 22631)));  // Windows 11 23H2
        Assert.Equal([false, false, true], entries.Select(e => Catalog.Fits(e, 19045))); // Windows 10 22H2
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

    static readonly DeviceDriver Nic = new()
    {
        DeviceId = @"PCI\VEN_10EC&DEV_8168\1", Name = "Realtek", Class = "Net", Provider = "Realtek", Version = "10.70.0.0",
        Date = new DateTime(2024, 1, 1), Signed = true, Problem = 0, InfName = "oem1.inf", HardwareIds = [@"PCI\VEN_10EC&DEV_8168&SUBSYS_1"],
    };

    static CatalogEntry Entry(string id, string version, string products = "Windows 10 version 1803 and Later Servicing Drivers") =>
        new(id, "Realtek " + version, products, "Drivers (Networking)", new DateTime(2025, 1, 1), new Version(version), 1);

    static readonly HashSet<string> None = [];

    [Fact]
    public void Offers_each_newer_version_once_and_the_package_for_the_nearest_windows()
    {
        var for22H2 = Entry("a", "10.79.0.0", "Windows 11 Client, version 22H2 and later, Servicing Drivers");
        var for24H2 = Entry("b", "10.79.0.0", "Windows 11 Client, version 24H2 and later, Servicing Drivers");

        var (versions, _) = Catalog.Newer(Nic, [for22H2, for24H2, Entry("c", "10.75.0.0"), Entry("d", "10.60.0.0")], [], None, None);

        Assert.Equal(["b", "c"], versions.Select(v => v.UpdateId));
    }

    [Fact]
    public void Hidden_versions_go_and_windows_choice_also_rules_out_older_ones()
    {
        var found = new[] { Entry("new", "10.80.0.0"), Entry("kept", "10.79.0.0"), Entry("older", "10.75.0.0") };

        Assert.Equal(["kept", "older"], Catalog.Newer(Nic, found, [], hidden: new HashSet<string> { "new" }, None).Versions.Select(v => v.UpdateId));
        // Windows kept its own driver over 10.79: 10.75 would not win either, a newer one may.
        Assert.Equal(["new"], Catalog.Newer(Nic, found, [], None, kept: new HashSet<string> { "kept" }).Versions.Select(v => v.UpdateId));
    }

    [Fact]
    public void Leaves_out_arm_and_other_systems()
    {
        var arm = new CatalogEntry("x", "t", "Windows 11 Client ARM64, version 24H2", "Drivers (Other)", DateTime.Today, new Version(1, 0), 1);
        var old = arm with { Products = "Windows 7" };

        Assert.False(Catalog.Fits(arm, 26100));
        Assert.False(Catalog.Fits(old, 26100));
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
