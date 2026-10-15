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
