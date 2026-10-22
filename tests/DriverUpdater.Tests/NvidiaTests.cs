using Xunit;

// NVIDIA's answers are undocumented: anything that does not look right must be ignored.
public sealed class NvidiaTests
{
    static string Fixture() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "nvidia-driver.json"));

    [Fact]
    public void Reads_the_game_ready_release_from_nvidia_s_answer()
    {
        var release = NvidiaModel.Parse(Fixture())!;

        Assert.Equal("617.42", release.Version);
        Assert.Equal(new DateTime(2026, 10, 6), release.Date);
        Assert.False(release.Studio);
        Assert.StartsWith("https://us.download.nvidia.com/", release.DownloadUrl); // the standard package, not the -g one
        Assert.StartsWith("https://www.nvidia.com/", release.DetailsUrl);
    }

    [Fact]
    public void Ignores_an_answer_for_an_unsupported_card_or_a_foreign_download()
    {
        Assert.Null(NvidiaModel.Parse(Fixture().Replace("\"state\": \"true\"", "\"state\": \"false\"")));
        Assert.Null(NvidiaModel.Parse(Fixture().Replace("https://us.download.nvidia.com/", "https://evil.example/")));
        Assert.Null(NvidiaModel.Parse(Fixture().Replace("\"IsBeta\": 0", "\"IsBeta\": 1")));
        Assert.Null(NvidiaModel.Parse("<html>maintenance</html>"));
    }

    [Theory]
    [InlineData("https://us.download.nvidia.com/Windows/617.42/617.42-desktop.exe", true)]
    [InlineData("https://international.download.nvidia.com/Windows/617.42/x.exe", true)]
    [InlineData("http://us.download.nvidia.com/Windows/617.42/x.exe", false)]      // not HTTPS
    [InlineData("https://us.download.nvidia.com.evil.example/x.exe", false)]
    [InlineData("https://us.download.nvidia.com/Windows/617.42/x.zip", false)]
    public void Downloads_only_nvidia_s_installers(string url, bool trusted) =>
        Assert.Equal(trusted, NvidiaModel.IsTrustedDownload(url));

    [Theory]
    [InlineData("32.0.16.1742", "617.42")]
    [InlineData("32.0.15.6094", "560.94")]
    [InlineData("31.0.15.3623", "536.23")]
    [InlineData("10.0.26100.1150", null)] // Microsoft's basic display driver
    [InlineData("garbage", null)]
    public void Translates_windows_numbering_to_nvidia_s(string windows, string? nvidia) =>
        Assert.Equal(nvidia, NvidiaModel.NvidiaVersion(windows));

    [Fact]
    public void Compares_nvidia_s_numbering_with_the_installed_driver()
    {
        Assert.Equal(new Version(32, 0, 16, 1742), NvidiaModel.WindowsVersion("617.42", new Version(32, 0, 15, 6094)));
        Assert.Null(NvidiaModel.WindowsVersion("not-a-version", new Version(32, 0, 0, 0)));
        // Microsoft's basic display driver, an NVIDIA one from before the 560s: never below them.
        Assert.Equal(new Version(32, 0, 16, 1742), NvidiaModel.WindowsVersion("617.42", new Version(10, 0, 26100, 1150)));
        Assert.Equal(new Version(32, 0, 16, 1742), NvidiaModel.WindowsVersion("617.42", new Version(31, 0, 15, 5244)));
    }

    [Theory]
    [InlineData(@"PCI\VEN_10DE&DEV_2684&SUBSYS_16F110DE&REV_A1", "2684")]
    [InlineData(@"PCI\VEN_1002&DEV_744C&SUBSYS_0E3B1458", null)] // AMD
    [InlineData(@"PCI\VEN_10DE", null)]
    public void Reads_the_card_s_device_id(string hardwareId, string? id) =>
        Assert.Equal(id, NvidiaModel.DeviceId(hardwareId));

    [Fact]
    public void A_newer_driver_on_nvidia_s_site_shows_in_the_report_but_installs_from_its_page()
    {
        var gpu = new DeviceDriver
        {
            DeviceId = @"PCI\VEN_10DE&DEV_2684\1", Name = "RTX 4090", Class = "Display", Provider = "NVIDIA", Version = "32.0.15.6094",
            Date = null, Signed = true, Problem = 0, InfName = "oem1.inf", HardwareIds = [@"PCI\VEN_10DE&DEV_2684"],
        };
        var catalog = new DriverOffer(Guid.NewGuid().ToString(), DriverOffer.Catalog, new Version(32, 0, 16, 1093), null, "1093");
        var nvidia = NvidiaModel.Offer(NvidiaModel.Parse(Fixture())!, gpu.ParsedVersion!);

        var row = Assert.Single(DriverReport.Build([gpu], [],
            new Dictionary<string, DriverOffer> { [gpu.HardwareIds[0]] = catalog },
            new Dictionary<string, DriverOffer> { [gpu.DeviceId] = nvidia }));

        Assert.Equal((Verdict.Update, DriverOffer.Nvidia, false, false), (row.Verdict, row.Offer!.Source, row.CanInstall, row.Selected));
    }

    // NVIDIA marks its Studio drivers "IsWHQL 0": each kind is held to its own rule.
    [Fact]
    public void Takes_a_studio_driver_only_when_asked_for_one()
    {
        var studio = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "nvidia-studio.json"));

        var release = NvidiaModel.Parse(studio, studio: true)!;
        Assert.True(release.Studio);
        Assert.Contains("-nsd-", release.DownloadUrl);
        Assert.Null(NvidiaModel.Parse(studio));                    // asked for Game Ready
        Assert.Null(NvidiaModel.Parse(Fixture(), studio: true));   // asked for Studio, got Game Ready
    }
}
