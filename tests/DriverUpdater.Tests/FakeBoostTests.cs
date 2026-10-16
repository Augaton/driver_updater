using DriverUpdater.Tuning;
using Xunit;

// "Défaire les faux boosts" must flag only what is really wrong, and undo exactly.
public sealed class FakeBoostTests
{
    const string MemoryManagement = @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management";
    static FakeBoost Boost(string id) => FakeBoostCatalog.Find(id)!;
    static readonly PcContext Desktop = new(Laptop: false, Client: true, Managed: false, DualDieX3D: false, XboxApp: false, OtherAntivirus: false);

    [Fact]
    public void Flags_only_the_bits_that_turn_cpu_protections_off()
    {
        var registry = new FakeRegistry();
        Assert.False(Boost("mitigations-off").IsFound(registry)); // nothing set: protections on

        registry.Set(Hive.LocalMachine, MemoryManagement, "FeatureSettingsOverride", 3);
        registry.Set(Hive.LocalMachine, MemoryManagement, "FeatureSettingsOverrideMask", 3);
        Assert.True(Boost("mitigations-off").IsFound(registry));

        // 72 ADDS protections (Microsoft's guidance for some CPUs): never flagged.
        registry.Set(Hive.LocalMachine, MemoryManagement, "FeatureSettingsOverride", 72);
        Assert.False(Boost("mitigations-off").IsFound(registry));
    }

    [Fact]
    public void An_absent_uac_value_means_uac_is_on()
    {
        var registry = new FakeRegistry();
        Assert.False(Boost("uac-off").IsFound(registry));
        registry.Set(Hive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "EnableLUA", 0);
        Assert.True(Boost("uac-off").IsFound(registry));
    }

    [Fact]
    public void Restoring_removes_the_value_and_undoing_puts_it_back()
    {
        const string Ipv6 = @"SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters";
        var boost = Boost("ipv6-off");
        var registry = new FakeRegistry();
        registry.Set(Hive.LocalMachine, Ipv6, "DisabledComponents", -1); // 0xFFFFFFFF

        TweakEngine.Apply(boost.Tweak, registry);
        Assert.Null(registry.Get(Hive.LocalMachine, Ipv6, "DisabledComponents"));
        Assert.False(boost.IsFound(registry));

        Assert.True(TweakEngine.Undo(boost.Tweak, registry));
        Assert.Equal(-1, registry.Get(Hive.LocalMachine, Ipv6, "DisabledComponents"));
    }

    [Fact]
    public void Each_pc_gets_its_own_verdict()
    {
        // Battery settings: a fix on a laptop, a remark on a desktop.
        Assert.Equal(FakeBoostVerdict.Info, Boost("power-throttling-off").Verdict(Desktop));
        Assert.Equal(FakeBoostVerdict.Fix, Boost("power-throttling-off").Verdict(Desktop with { Laptop = true }));
        // A company's own settings are its IT's business.
        Assert.Equal(FakeBoostVerdict.Info, Boost("windows-update-policy").Verdict(Desktop with { Managed = true }));
        // Not concerned at all: hidden.
        Assert.Equal(FakeBoostVerdict.Hidden, Boost("core-parking").Verdict(Desktop));
        Assert.Equal(FakeBoostVerdict.Hidden, Boost("defender-policy").Verdict(Desktop with { OtherAntivirus = true }));
        Assert.Equal(FakeBoostVerdict.Hidden, Boost("service-sysmain").Verdict(Desktop with { Client = false }));
        Assert.Equal(FakeBoostVerdict.Info, Boost("system-responsiveness").Verdict(Desktop));
    }

    [Fact]
    public void Ids_are_unique_across_tweaks_and_fixes_and_each_says_why()
    {
        var ids = TweakCatalog.All.Select(t => t.Id).Concat(FakeBoostCatalog.All.Select(b => b.Tweak.Id)).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.All(FakeBoostCatalog.All, b => Assert.False(string.IsNullOrWhiteSpace(b.Tweak.Why)));
        Assert.All(FakeBoostCatalog.All.Where(b => b.InfoOnly), b => Assert.False(b.Tweak.InOneClick && b.Tweak.Values.Count > 0));
    }
}
