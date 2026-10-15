using Xunit;

// The helper runs as administrator with whatever arguments it is started with: these are
// the only gate between a command line and the system.
public sealed class AdminCommandTests
{
    const string Guid1 = "8f1c2b3a-1111-4ac1-bd9c-c0b08c365d74";

    [Fact]
    public void Accepts_update_ids_and_normalizes_them()
    {
        var command = AdminCommand.Parse([AdminCommand.Install, Guid1.ToUpperInvariant()]);

        Assert.Equal(AdminCommand.Install, command.Name);
        Assert.Equal([Guid1], command.Args);
    }

    [Theory]
    [InlineData("x' or UpdateID='y")]          // would rewrite the Windows Update search
    [InlineData("8f1c2b3a11114ac1bd9cc0b08c365d74")] // GUID without dashes: not the format we send
    [InlineData("")]
    public void Refuses_anything_but_a_guid_as_update_id(string id) =>
        Assert.Throws<ArgumentException>(() => AdminCommand.Parse([AdminCommand.Hide, id]));

    [Fact]
    public void Refuses_install_without_updates() =>
        Assert.Throws<ArgumentException>(() => AdminCommand.Parse([AdminCommand.Install]));

    [Theory]
    [InlineData("relative/folder")]
    [InlineData(@"\\server\share\drivers")]
    public void Refuses_a_folder_that_is_not_a_full_local_path(string folder) =>
        Assert.Throws<ArgumentException>(() => AdminCommand.Parse([AdminCommand.ImportDrivers, folder]));

    [Fact]
    public void Accepts_a_full_local_folder()
    {
        var folder = OperatingSystem.IsWindows() ? @"D:\Sauvegarde pilotes" : "/home/user/sauvegarde";

        Assert.Equal([folder], AdminCommand.Parse([AdminCommand.ExportDrivers, folder]).Args);
    }

    [Theory]
    [InlineData("format-disk")]
    [InlineData("")]
    public void Refuses_unknown_commands(string name) =>
        Assert.Throws<ArgumentException>(() => AdminCommand.Parse([name]));

    [Fact]
    public void Refuses_extra_arguments() =>
        Assert.Throws<ArgumentException>(() => AdminCommand.Parse([AdminCommand.Sfc, "/purgecache"]));

    [Fact]
    public void Accepts_only_pipe_names_the_app_makes()
    {
        Assert.True(AdminCommand.IsPipeName(AdminCommand.NewPipeName()));
        Assert.False(AdminCommand.IsPipeName(@"\\attacker\pipe\x"));
        Assert.False(AdminCommand.IsPipeName("DriverUpdater-" + new string('a', 31)));
        Assert.False(AdminCommand.IsPipeName("DriverUpdater-" + new string('a', 32) + "\n"));
    }

    [Fact]
    public void Accepts_only_published_driver_package_names()
    {
        Assert.Equal(["oem12.inf"], AdminCommand.Parse([AdminCommand.DeleteDrivers, "OEM12.INF"]).Args);
        Assert.Throws<ArgumentException>(() => AdminCommand.Parse([AdminCommand.DeleteDrivers, "nvlddmkm.inf"]));
        Assert.Throws<ArgumentException>(() => AdminCommand.Parse([AdminCommand.DeleteDrivers, @"..\oem1.inf"]));
        Assert.Throws<ArgumentException>(() => AdminCommand.Parse([AdminCommand.DeleteDrivers, "oem1.inf\n"]));
    }

    [Fact]
    public void Install_all_accepts_only_known_sources_and_guids()
    {
        var command = AdminCommand.Parse([AdminCommand.InstallAll, "wu:" + Guid1.ToUpperInvariant(), "catalog:" + Guid1]);

        Assert.Equal(["wu:" + Guid1, "catalog:" + Guid1], command.Args);
        Assert.Throws<ArgumentException>(() => AdminCommand.Parse([AdminCommand.InstallAll, "http:" + Guid1]));
        Assert.Throws<ArgumentException>(() => AdminCommand.Parse([AdminCommand.InstallAll, "wu:not-a-guid"]));
        Assert.Throws<ArgumentException>(() => AdminCommand.Parse([AdminCommand.InstallAll]));
    }
}
