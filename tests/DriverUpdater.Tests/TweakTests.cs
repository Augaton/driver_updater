using DriverUpdater.Tuning;
using Xunit;

// Undo must give back exactly what was there: that is what makes the tweaks safe to try.
public sealed class TweakTests
{
    static readonly Tweak GameMode = TweakCatalog.Find("game-mode")!;
    static readonly Tweak Windowed = TweakCatalog.Find("windowed-games")!;

    [Fact]
    public void Applies_and_undoes_to_exactly_what_was_there()
    {
        var suggestions = TweakCatalog.Find("suggested-apps-off")!;
        var registry = new FakeRegistry();
        const string Key = @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
        registry.Set(Hive.CurrentUser, Key, "SilentInstalledAppsEnabled", 1); // the other value is absent
        var before = new Dictionary<(Hive, string, string), object>(registry.Values);

        TweakEngine.Apply(suggestions, registry);
        Assert.True(TweakEngine.IsApplied(suggestions, registry));

        Assert.True(TweakEngine.Undo(suggestions, registry));
        Assert.Equal(before, registry.Values); // the absent value is absent again, the backups are gone
    }

    // Game Mode is on when its value does not exist: nothing to do on a fresh Windows.
    [Fact]
    public void A_missing_value_counts_as_what_windows_does_by_default()
    {
        var registry = new FakeRegistry();
        Assert.True(TweakEngine.IsApplied(GameMode, registry));

        registry.Set(Hive.CurrentUser, @"Software\Microsoft\GameBar", "AutoGameModeEnabled", 0);
        Assert.False(TweakEngine.IsApplied(GameMode, registry));
    }

    [Fact]
    public void Retired_tweaks_are_never_applied_in_one_click() =>
        Assert.All(TweakCatalog.All.Where(t => t.Retired), t => Assert.False(t.InOneClick));

    [Fact]
    public void Applying_twice_still_undoes_to_the_original()
    {
        var registry = new FakeRegistry();
        registry.Set(Hive.CurrentUser, @"Software\Microsoft\GameBar", "AutoGameModeEnabled", 0);

        TweakEngine.Apply(GameMode, registry);
        TweakEngine.Apply(GameMode, registry);
        TweakEngine.Undo(GameMode, registry);

        Assert.Equal(0, registry.Get(Hive.CurrentUser, @"Software\Microsoft\GameBar", "AutoGameModeEnabled"));
    }

    [Fact]
    public void Undo_leaves_alone_a_tweak_never_applied_here()
    {
        var registry = new FakeRegistry();
        registry.Set(Hive.CurrentUser, @"Software\Microsoft\GameBar", "AllowAutoGameMode", 1);

        Assert.False(TweakEngine.Undo(GameMode, registry));
        Assert.Equal(1, registry.Get(Hive.CurrentUser, @"Software\Microsoft\GameBar", "AllowAutoGameMode"));
    }

    [Fact]
    public void Sets_one_item_of_a_list_and_keeps_the_others()
    {
        var registry = new FakeRegistry();
        var key = @"Software\Microsoft\DirectX\UserGpuPreferences";
        registry.Set(Hive.CurrentUser, key, "DirectXUserGlobalSettings", "VRROptimizeEnable=0;SwapEffectUpgradeEnable=0;");

        TweakEngine.Apply(Windowed, registry);

        Assert.Equal("VRROptimizeEnable=0;SwapEffectUpgradeEnable=1;", registry.Get(Hive.CurrentUser, key, "DirectXUserGlobalSettings"));
        Assert.True(TweakEngine.IsApplied(Windowed, registry));
        TweakEngine.Undo(Windowed, registry);
        Assert.Equal("VRROptimizeEnable=0;SwapEffectUpgradeEnable=0;", registry.Get(Hive.CurrentUser, key, "DirectXUserGlobalSettings"));
    }

    [Fact]
    public void Undoing_one_item_keeps_those_windows_added_since()
    {
        var registry = new FakeRegistry();
        var key = @"Software\Microsoft\DirectX\UserGpuPreferences";

        TweakEngine.Apply(Windowed, registry); // the list did not exist
        registry.Set(Hive.CurrentUser, key, "DirectXUserGlobalSettings", "SwapEffectUpgradeEnable=1;VRROptimizeEnable=0;"); // VRR turned off in Settings
        TweakEngine.Undo(Windowed, registry);
        Assert.Equal("VRROptimizeEnable=0;", registry.Get(Hive.CurrentUser, key, "DirectXUserGlobalSettings"));

        registry.Delete(Hive.CurrentUser, key, "DirectXUserGlobalSettings");
        TweakEngine.Apply(Windowed, registry);
        TweakEngine.Undo(Windowed, registry);
        Assert.Null(registry.Get(Hive.CurrentUser, key, "DirectXUserGlobalSettings")); // nothing left: gone, as it was
    }

    [Fact]
    public void A_string_one_is_not_the_number_one()
    {
        var registry = new FakeRegistry();
        registry.Set(Hive.CurrentUser, @"Control Panel\Mouse", "MouseSpeed", 0); // wrong type: not applied

        Assert.False(TweakEngine.IsApplied(TweakCatalog.Find("mouse-precision-off")!, registry));
    }

    [Fact]
    public void Every_tweak_has_a_unique_id_and_says_why()
    {
        Assert.Equal(TweakCatalog.All.Count, TweakCatalog.All.Select(t => t.Id).Distinct().Count());
        Assert.All(TweakCatalog.All, t => Assert.False(string.IsNullOrWhiteSpace(t.Why)));
    }
}

public sealed class GameTipsTests
{
    [Fact]
    public void Reads_steam_library_folders_and_install_dirs()
    {
        const string vdf = "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"C:\\\\Program Files (x86)\\\\Steam\"\n\t}\n\t\"1\"\n\t{\n\t\t\"path\"\t\t\"D:\\\\SteamLibrary\"\n\t}\n}";
        const string acf = "\"AppState\"\n{\n\t\"appid\"\t\t\"4000\"\n\t\"installdir\"\t\t\"GarrysMod\"\n}";

        Assert.Equal([@"C:\Program Files (x86)\Steam", @"D:\SteamLibrary"], GameTips.LibraryFolders(vdf));
        Assert.Equal("GarrysMod", GameTips.InstallDir(acf));
    }

    [Fact]
    public void Adds_the_block_once_and_removes_it_leaving_the_user_s_lines()
    {
        const string mine = "bind f \"say hello\"\r\nfps_max 300\r\n";

        var withBlock = GameTips.WithBlock(mine, ["gmod_mcore_test 1"]);
        var again = GameTips.WithBlock(withBlock, ["gmod_mcore_test 1", "mat_queue_mode -1"]);

        Assert.True(GameTips.HasBlock(again));
        Assert.Single(again.Split(GameTips.BlockStart)[1..]); // one block, not two
        Assert.Contains("mat_queue_mode -1", again);
        Assert.Equal(mine, GameTips.WithoutBlock(again));
    }

    [Fact]
    public void Removing_from_a_file_that_only_held_the_block_leaves_it_empty() =>
        Assert.Equal("", GameTips.WithoutBlock(GameTips.WithBlock(null, ["gmod_mcore_test 1"])));
}
