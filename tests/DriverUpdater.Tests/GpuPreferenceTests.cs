using DriverUpdater.Tuning;
using Xunit;

// Forcing games onto the dedicated card must touch only games, keep the user's other choices, and undo exactly.
public sealed class GpuPreferenceTests
{
    [Theory]
    [InlineData(@"Cyberpunk2077.exe", true)]
    [InlineData(@"bin\x64\Cyberpunk2077.exe", true)]
    [InlineData(@"start_protected_game.exe", true)]          // the anti-cheat launcher starts the game
    [InlineData(@"_CommonRedist\vcredist\2019\VC_redist.x64.exe", false)]
    [InlineData(@"UnityCrashHandler64.exe", false)]
    [InlineData(@"unins000.exe", false)]
    [InlineData(@"Engine\Extras\Redist\en-us\UEPrereqSetup_x64.exe", false)]
    [InlineData(@"readme.txt", false)]
    public void Picks_the_game_s_programs_only(string path, bool game) =>
        Assert.Equal(game, GpuPreference.IsGameProgram(path));

    [Fact]
    public void Forces_the_dedicated_card_keeping_other_choices_and_undoes_exactly()
    {
        const string Game = @"D:\SteamLibrary\steamapps\common\Game\game.exe", Other = @"D:\SteamLibrary\steamapps\common\Game\launcher.exe";
        var registry = new FakeRegistry();
        registry.Set(Hive.CurrentUser, GpuPreference.Key, Game, "AutoHDREnable=1;GpuPreference=1;");

        GpuPreference.Force([Game, Other], registry);
        GpuPreference.Force([Game], registry); // twice: the original is still what comes back
        Assert.True(GpuPreference.IsForced(Game, registry) && GpuPreference.IsForced(Other, registry));
        Assert.Contains("AutoHDREnable=1", (string)registry.Get(Hive.CurrentUser, GpuPreference.Key, Game)!);

        Assert.Equal(2, GpuPreference.Undo(registry));
        Assert.Equal("AutoHDREnable=1;GpuPreference=1;", registry.Get(Hive.CurrentUser, GpuPreference.Key, Game));
        Assert.Null(registry.Get(Hive.CurrentUser, GpuPreference.Key, Other));
        Assert.False(GpuPreference.CanUndo(registry));
    }

    [Fact]
    public void Undo_keeps_a_choice_made_since()
    {
        const string Game = @"D:\SteamLibrary\steamapps\common\Game\game.exe";
        var registry = new FakeRegistry();

        GpuPreference.Force([Game], registry);
        registry.Set(Hive.CurrentUser, GpuPreference.Key, Game, "GpuPreference=2;SwapEffectUpgradeEnable=0;"); // set in Settings since

        GpuPreference.Undo(registry);
        Assert.Equal("SwapEffectUpgradeEnable=0;", registry.Get(Hive.CurrentUser, GpuPreference.Key, Game));
    }
}
