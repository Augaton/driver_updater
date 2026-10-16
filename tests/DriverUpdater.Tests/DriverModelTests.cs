using Xunit;

public sealed class CatalogScopeTests
{
    static DeviceDriver Device(string id, string cls, string provider = "NVIDIA", int problem = 0, string version = "1.0") => new()
    {
        DeviceId = id + @"\4&1234&0&0008", Name = "x", Class = cls, Provider = provider, Version = version, Date = null,
        Signed = true, Problem = problem, InfName = "oem1.inf", HardwareIds = [id],
    };

    [Fact]
    public void Searches_makers_drivers() => Assert.True(CatalogScope.ShouldSearch(Device(@"PCI\VEN_10DE&DEV_2504", "Display")));

    [Fact]
    public void Searches_devices_without_driver_whatever_their_class() =>
        Assert.True(CatalogScope.ShouldSearch(Device(@"ACPI\ELAN0001", "Unknown", provider: "", problem: 28, version: "")));

    [Fact]
    public void Searches_generic_drivers_only_where_a_maker_s_one_helps()
    {
        Assert.True(CatalogScope.ShouldSearch(Device(@"PCI\VEN_10EC&DEV_8168", "Net", provider: "Microsoft")));
        Assert.False(CatalogScope.ShouldSearch(Device(@"HID\VID_046D&PID_C52B", "HIDClass", provider: "Microsoft")));
    }

    [Theory]
    [InlineData("SCSIAdapter")]
    [InlineData("HDC")]
    [InlineData("Processor")]
    public void Never_touches_what_windows_needs_to_start(string cls) =>
        Assert.False(CatalogScope.ShouldSearch(Device(@"PCI\VEN_8086&DEV_A0D3", cls, provider: "Intel")));

    [Fact]
    public void Ignores_virtual_and_software_devices() =>
        Assert.False(CatalogScope.ShouldSearch(Device(@"SWD\MMDEVAPI", "AudioEndpoint", provider: "Realtek")));

    [Fact]
    public void Searches_identical_devices_once()
    {
        var twins = new[] { Device(@"USB\VID_8087&PID_0026", "Bluetooth"), Device(@"USB\VID_8087&PID_0026", "Bluetooth") };

        Assert.Single(CatalogScope.Select(twins));
    }
}

public sealed class DriverStoreRulesTests
{
    static StorePackage Package(string published, string version, bool bootCritical = false, string original = "nv_dispi.inf") =>
        new(published, original, "NVIDIA", "Display", new Version(version), null, bootCritical, 900L << 20);

    [Fact]
    public void Keeps_the_newest_and_whatever_is_in_use()
    {
        var packages = new[] { Package("oem10.inf", "31.0.15.1"), Package("oem20.inf", "32.0.15.6094"), Package("oem30.inf", "32.0.16.1060") };

        var removable = DriverStoreRules.Removable(packages, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "oem20.inf" });

        Assert.Equal(["oem10.inf"], removable.Select(p => p.PublishedName));
    }

    [Fact]
    public void Never_removes_what_windows_needs_to_start()
    {
        var packages = new[] { Package("oem1.inf", "1.0", bootCritical: true), Package("oem2.inf", "2.0", bootCritical: true) };

        Assert.Empty(DriverStoreRules.Removable(packages, new HashSet<string>()));
    }

    [Fact]
    public void Compares_versions_only_within_the_same_driver()
    {
        var packages = new[] { Package("oem1.inf", "1.0", original: "audio.inf"), Package("oem2.inf", "9.0", original: "video.inf") };

        Assert.Empty(DriverStoreRules.Removable(packages, new HashSet<string>()));
    }
}

public sealed class InstalledDriversTests
{
    [Fact]
    public void Indexes_installed_drivers_by_every_hardware_id_but_skips_devices_without_one()
    {
        DeviceDriver Device(string id, string version, int problem = 0) => new()
        {
            DeviceId = id + @"\1", Name = id, Class = "Net", Provider = "Realtek", Version = version, Date = null, Signed = true,
            Problem = problem, InfName = "oem1.inf", HardwareIds = [id + "&SUBSYS_1", id],
        };

        var map = InstalledDrivers.ByHardwareId([Device(@"PCI\VEN_10EC&DEV_8168", "10.79.0.0"), Device(@"PCI\VEN_8086&DEV_1", "", problem: 28)]);

        Assert.Equal("10.79.0.0", map[@"pci\ven_10ec&dev_8168"].Version); // either spelling
        Assert.Equal("10.79.0.0", map[@"PCI\VEN_10EC&DEV_8168&SUBSYS_1"].Version);
        Assert.False(map.ContainsKey(@"PCI\VEN_8086&DEV_1"));
    }
}

public sealed class DriverStoreKeepsRollbackTests
{
    static StorePackage Package(string published, string version) =>
        new(published, "nv_dispi.inf", "NVIDIA", "Display", new Version(version), null, false, 900L << 20);

    [Fact]
    public void Keeps_the_version_before_the_newest_for_rolling_back()
    {
        var packages = new[] { Package("oem1.inf", "31.0.0.1"), Package("oem2.inf", "32.0.0.1"), Package("oem3.inf", "33.0.0.1"), Package("oem4.inf", "34.0.0.1") };

        var removable = DriverStoreRules.Removable(packages, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "oem4.inf" });

        Assert.Equal(["oem2.inf", "oem1.inf"], removable.Select(p => p.PublishedName).OrderByDescending(n => n));
    }
}
