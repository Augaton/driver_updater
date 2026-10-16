using DriverUpdater.Tuning;
using Xunit;

// What the gaming checks flag: wrong advice here would send people into their BIOS for nothing.
public sealed class HardwareRulesTests
{
    [Theory]
    [InlineData("F5-6000J3038F16G", 6000)]       // G.Skill
    [InlineData("F4-3600C16D-16GTZNC", 3600)]
    [InlineData("CMK32GX5M2B6000C36", 6000)]     // Corsair
    [InlineData("CMW32GX4M2E3200C16", 3200)]
    [InlineData("KF560C36BBEK2-32", 6000)]       // Kingston Fury
    [InlineData("KF432C16BB/8", 3200)]
    [InlineData("BL2K16G36C16U4B", 3600)]        // Crucial Ballistix
    [InlineData("TF10D48G3200HC16CBK", 3200)]    // the speed next to its latency
    [InlineData("M378A1K43EB2-CWE", null)]       // Samsung, OEM: no speed in the name
    [InlineData("KVR32N22S8/8", null)]           // Kingston ValueRAM: standard speed only
    [InlineData("", null)]
    public void Reads_the_speed_a_kit_is_sold_for(string partNumber, int? speed) =>
        Assert.Equal(speed, HardwareRules.PartNumberSpeed(partNumber));

    static MemoryModule Ddr5(int speed, int configured, string part = "", int formFactor = 8) => new(speed, configured, 34, formFactor, part);

    [Fact]
    public void Spots_xmp_off_from_the_part_number_when_the_firmware_hides_it()
    {
        // XMP off: the firmware reports the standard speed both ways.
        var checks = HardwareRules.Memory([Ddr5(4800, 4800, "CMK32GX5M2B6000C36"), Ddr5(4800, 4800, "CMK32GX5M2B6000C36")]).ToList();

        Assert.Contains(checks, c => c.NeedsAttention && c.Finding.Contains("6000"));
    }

    [Fact]
    public void Leaves_a_kit_at_its_own_speed_alone()
    {
        var checks = HardwareRules.Memory([Ddr5(4800, 6000, "F5-6000J3038F16G"), Ddr5(4800, 6000, "F5-6000J3038F16G")]).ToList();

        Assert.DoesNotContain(checks, c => c.NeedsAttention);
    }

    [Fact]
    public void Does_not_blame_a_standard_kit_it_cannot_identify()
    {
        // A PC maker's plain 4800 modules: only a hint, not a problem.
        var checks = HardwareRules.Memory([Ddr5(4800, 4800, "M425R1GB4BB0-CQK"), Ddr5(4800, 4800, "M425R1GB4BB0-CQK")]).ToList();

        Assert.DoesNotContain(checks, c => c.NeedsAttention);
    }

    [Fact]
    public void Flags_a_single_stick_but_not_soldered_memory()
    {
        Assert.Contains(HardwareRules.Memory([Ddr5(6000, 6000)]), c => c.NeedsAttention && c.Title == "Barrettes de mémoire");
        Assert.DoesNotContain(HardwareRules.Memory([Ddr5(6000, 6000, formFactor: 0)]), c => c.Title == "Barrettes de mémoire");
    }

    [Theory]
    [InlineData(59.94, 60, false)]
    [InlineData(143.98, 144, false)]
    [InlineData(60, 144, true)]
    [InlineData(120, 165, true)]
    public void Flags_a_screen_only_clearly_below_its_best_rate(double current, int max, bool flagged) =>
        Assert.Equal(flagged, HardwareRules.Refresh("Test", current, max).NeedsAttention);

    [Fact]
    public void A_screen_on_the_motherboard_matters_on_a_desktop_only()
    {
        Assert.True(HardwareRules.OnIntegratedGraphics("Test", hasDiscreteGpu: true, drivenByIntegrated: true, laptop: false)!.NeedsAttention);
        Assert.False(HardwareRules.OnIntegratedGraphics("Test", hasDiscreteGpu: true, drivenByIntegrated: true, laptop: true)!.NeedsAttention);
        Assert.Null(HardwareRules.OnIntegratedGraphics("Test", hasDiscreteGpu: false, drivenByIntegrated: true, laptop: false));
        Assert.Null(HardwareRules.OnIntegratedGraphics("Test", hasDiscreteGpu: true, drivenByIntegrated: false, laptop: false));
    }

    // A laptop's memory speed is the maker's choice: no BIOS to send people to.
    [Fact]
    public void Never_sends_a_laptop_to_its_bios_for_xmp()
    {
        var checks = HardwareRules.Memory([new MemoryModule(3200, 2933, 26, 12, "KF432S20IB/8")], laptop: true).ToList();

        Assert.DoesNotContain(checks, c => c.NeedsAttention && c.Title == "Mémoire");
    }

    [Fact]
    public void Offers_the_one_click_fix_when_it_knows_the_screen()
    {
        var check = HardwareRules.Refresh("Test", 60, 144, @"\\.\DISPLAY1");

        Assert.Equal(("Passer à 144 Hz", @"refresh:144:\\.\DISPLAY1"), (check.ActionText, check.Action));
        Assert.False(HardwareRules.Refresh("Test", 0, 144).NeedsAttention); // rate not known: no claim
    }
}
