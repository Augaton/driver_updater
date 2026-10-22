using Xunit;

public sealed class DriverReportTests
{
    static DeviceDriver Device(string id, string version, int problem = 0) => new()
    {
        DeviceId = id + @"\1", Name = id, Class = "Display", Provider = "NVIDIA", Version = version, Date = null, Signed = true,
        Problem = problem, InfName = "oem1.inf", HardwareIds = [id + "&SUBSYS_1", id],
    };

    static DriverOffer Offer(string source, string version) => new(Guid.NewGuid().ToString(), source, new Version(version), null, version);

    [Fact]
    public void Picks_the_newest_offer_and_ticks_it()
    {
        var gpu = Device(@"PCI\VEN_10DE&DEV_2504", "31.0.15.1");
        var rows = DriverReport.Build([gpu],
            [(@"PCI\VEN_10DE&DEV_2504", Offer(DriverOffer.WindowsUpdate, "32.0.15.6094"))],
            new Dictionary<string, DriverOffer> { [gpu.HardwareIds[0]] = Offer(DriverOffer.Catalog, "32.0.16.1060") });

        var row = Assert.Single(rows);
        Assert.Equal((Verdict.Update, DriverOffer.Catalog, true), (row.Verdict, row.Offer!.Source, row.Selected));
    }

    [Fact]
    public void Prefers_windows_update_on_the_same_version()
    {
        var gpu = Device(@"PCI\VEN_10DE&DEV_2504", "31.0.0.1");
        var rows = DriverReport.Build([gpu],
            [(@"PCI\VEN_10DE&DEV_2504&SUBSYS_1", Offer(DriverOffer.WindowsUpdate, "32.0.0.1"))],
            new Dictionary<string, DriverOffer> { [gpu.HardwareIds[0]] = Offer(DriverOffer.Catalog, "32.0.0.1") });

        Assert.Equal(DriverOffer.WindowsUpdate, rows[0].Offer!.Source);
    }

    [Fact]
    public void Ignores_offers_not_newer_than_what_is_installed()
    {
        var nic = Device(@"PCI\VEN_10EC&DEV_8168", "10.79.0.0");
        var rows = DriverReport.Build([nic], [],
            new Dictionary<string, DriverOffer> { [nic.HardwareIds[0]] = Offer(DriverOffer.Catalog, "10.70.0.0") });

        Assert.Equal((Verdict.UpToDate, false), (rows[0].Verdict, rows[0].Selected));
    }

    [Fact]
    public void Matches_a_windows_update_offer_by_the_chip_s_compatible_id()
    {
        // As Windows lists a Realtek network card: the bare VEN&DEV its driver targets is only a compatible ID.
        var nic = new DeviceDriver
        {
            DeviceId = @"PCI\VEN_10EC&DEV_8168&SUBSYS_E0001458&REV_15\1", Name = "Realtek", Class = "Net", Provider = "Realtek",
            Version = "10.70.0.0", Date = null, Signed = true, Problem = 0, InfName = "oem1.inf",
            HardwareIds = [@"PCI\VEN_10EC&DEV_8168&SUBSYS_E0001458&REV_15", @"PCI\VEN_10EC&DEV_8168&SUBSYS_E0001458"],
            CompatibleIds = DeviceDriver.ChipIds([@"PCI\VEN_10EC&DEV_8168&REV_15", @"PCI\VEN_10EC&DEV_8168", @"PCI\VEN_10EC&CC_0200", @"PCI\CC_0200"]),
        };

        var row = Assert.Single(DriverReport.Build([nic], [(@"PCI\VEN_10EC&DEV_8168", Offer(DriverOffer.WindowsUpdate, "10.79.0.0"))],
            new Dictionary<string, DriverOffer>()));

        Assert.Equal((Verdict.Update, true), (row.Verdict, row.Selected));
        Assert.Equal("10.70.0.0", InstalledDrivers.ByHardwareId([nic])[@"PCI\VEN_10EC&DEV_8168"].Version);
    }

    [Fact]
    public void Keeps_only_the_compatible_ids_that_name_the_chip()
    {
        string[] ids =
        [
            @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0897&REV_1005", @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0897",
            @"HDAUDIO\FUNC_01&CTLR_VEN_1022&CTLR_DEV_1487", @"HDAUDIO\FUNC_01&VEN_10EC", @"HDAUDIO\FUNC_01",
            @"USB\Class_03&SubClass_01", @"PCI\CC_0300",
        ];

        Assert.Equal(ids[..2], DeviceDriver.ChipIds(ids));
    }

    [Fact]
    public void Offers_the_maker_s_driver_over_windows_generic_one_by_date()
    {
        // Microsoft's HD Audio (10.0.26100, dated 2006) against Realtek's 6.0.9xxx.
        var audio = new DeviceDriver
        {
            DeviceId = @"HDAUDIO\FUNC_01&VEN_10EC&DEV_0897\1", Name = "Audio", Class = "Media", Provider = "Microsoft",
            Version = "10.0.26100.1", Date = new DateTime(2006, 6, 21), Signed = true, Problem = 0, InfName = "hdaudio.inf",
            HardwareIds = [@"HDAUDIO\FUNC_01&VEN_10EC&DEV_0897&SUBSYS_1458A194"],
        };
        DriverOffer Realtek(DateTime date) => new(Guid.NewGuid().ToString(), DriverOffer.Catalog, new Version(6, 0, 9733, 1), date, "6.0.9733.1");

        var newer = DriverReport.Build([audio], [], new Dictionary<string, DriverOffer> { [audio.HardwareIds[0]] = Realtek(new DateTime(2024, 5, 2)) });
        var older = DriverReport.Build([audio], [], new Dictionary<string, DriverOffer> { [audio.HardwareIds[0]] = Realtek(new DateTime(2005, 1, 1)) });

        Assert.Equal((Verdict.Update, true), (newer[0].Verdict, newer[0].Selected));
        Assert.Equal(Verdict.UpToDate, older[0].Verdict);
    }

    [Fact]
    public void Shows_one_catalog_offer_on_every_identical_device()
    {
        var first = Device(@"PCI\VEN_10EC&DEV_8168", "10.0.0.0");
        var twin = new DeviceDriver
        {
            DeviceId = first.DeviceId + "_2", Name = "twin", Class = first.Class, Provider = first.Provider, Version = first.Version,
            Date = null, Signed = true, Problem = 0, InfName = first.InfName, HardwareIds = first.HardwareIds,
        };
        var rows = DriverReport.Build([first, twin], [],
            new Dictionary<string, DriverOffer> { [first.HardwareIds[0]] = Offer(DriverOffer.Catalog, "11.0.0.0") });

        Assert.All(rows, r => Assert.Equal(Verdict.Update, r.Verdict));
    }

    [Fact]
    public void Lists_updates_first_then_missing_drivers_then_problems_then_the_rest()
    {
        var rows = DriverReport.Build(
            [Device("A", "1.0"), Device("B", "", problem: 28), Device("C", "1.0", problem: 43), Device("D", "1.0")],
            [("D", Offer(DriverOffer.WindowsUpdate, "2.0"))], new Dictionary<string, DriverOffer>());

        Assert.Equal(["D", "B", "C", "A"], rows.Select(r => r.Device.Name));
        Assert.Equal([Verdict.Update, Verdict.NoDriver, Verdict.Problem, Verdict.UpToDate], rows.Select(r => r.Verdict));
    }
}

public sealed class GameComponentTests
{
    static GameComponent Component(string id) => GameComponent.All.Single(c => c.WingetId == id);

    [Theory]
    [InlineData("Microsoft.VCRedist.2015+.x64", "Microsoft Visual C++ 2015-2022 Redistributable (x64) - 14.44.35208")]
    [InlineData("Microsoft.VCRedist.2015+.x86", "Microsoft Visual C++ 2017 Redistributable (x86) - 14.16.27033")]
    [InlineData("Microsoft.VCRedist.2015+.x64", "Microsoft Visual C++ v14 Redistributable (x64) - 14.51.36247")]
    [InlineData("Microsoft.VCRedist.2015+.x86", "Microsoft Visual C++ v14 Redistributable (x86) - 14.51.36247")]
    [InlineData("Microsoft.VCRedist.2010.x64", "Microsoft Visual C++ 2010  x64 Redistributable - 10.0.40219")]
    [InlineData("Microsoft.VCRedist.2008.x86", "Microsoft Visual C++ 2008 Redistributable - x86 9.0.30729.6161")]
    [InlineData("Microsoft.VCRedist.2005.x86", "Microsoft Visual C++ 2005 Redistributable")]
    [InlineData("Microsoft.XNARedist", "Microsoft XNA Framework Redistributable 4.0 Refresh")]
    [InlineData("Nvidia.PhysX", "NVIDIA PhysX System Software 9.23.1019")]
    public void Recognises_installed_components(string id, string installedName) =>
        Assert.True(Component(id).IsIn([installedName]));

    [Fact]
    public void Does_not_mix_up_architectures_or_years()
    {
        Assert.False(Component("Microsoft.VCRedist.2015+.x86").IsIn(["Microsoft Visual C++ 2015-2022 Redistributable (x64) - 14.44"]));
        Assert.False(Component("Microsoft.VCRedist.2005.x86").IsIn(["Microsoft Visual C++ 2005 Redistributable (x64)"]));
        Assert.False(Component("Microsoft.VCRedist.2012.x64").IsIn(["Microsoft Visual C++ 2013 Redistributable (x64)"]));
    }
}
