using Xunit;

// Deleting the wrong folder loses data: these pin down what may ever be offered.
public sealed class LeftoversTests : IDisposable
{
    readonly string root = Directory.CreateTempSubdirectory("leftovers-").FullName;
    readonly LeftoverRoots roots;

    public LeftoversTests()
    {
        string Make(string name) => Directory.CreateDirectory(Path.Combine(root, name)).FullName;
        roots = new LeftoverRoots(Make("ProgramFiles"), Make("ProgramFilesX86"), Make("ProgramData"), Make("AppData"), Make("LocalAppData"));
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    string Folder(params string[] parts) => Directory.CreateDirectory(Path.Combine(parts)).FullName;

    // Office records the same install folder for each of its languages.
    [Fact]
    public void Never_offers_a_folder_another_installed_program_still_uses()
    {
        var office = Folder(roots.ProgramFiles, "Microsoft Office 365");
        var inside = Folder(office, "root");

        Assert.Empty(Leftovers.Find("Microsoft Office 365 - fr-fr", null, office, roots, inUse: [office]));
        Assert.Empty(Leftovers.Find("Some Tool", null, inside, roots, inUse: [office]));
        Assert.Empty(Leftovers.Find("Office", null, office, roots, inUse: [Path.Combine(office, "root", "Office16")]));
    }

    [Fact]
    public void Offers_the_install_folder_ticked_and_same_name_folders_unticked()
    {
        var install = Folder(roots.ProgramFiles, "Discord");
        var data = Folder(roots.AppData, "Discord");

        var found = Leftovers.Find("Discord 1.0.9165", "Discord Inc.", install, roots);

        Assert.Contains(found, l => l.Path == install && l.Selected && l.NeedsAdmin);
        Assert.Contains(found, l => l.Path == data && !l.Selected && !l.NeedsAdmin);
    }

    [Fact]
    public void Finds_a_folder_under_the_publisher()
    {
        var folder = Folder(roots.LocalAppData, "Valve", "Some Game");

        Assert.Contains(Leftovers.Find("Some Game", "Valve Corporation", null, roots), l => l.Path == folder);
    }

    [Fact]
    public void Never_offers_a_root_or_a_shared_folder()
    {
        Folder(roots.ProgramFiles, "Common Files", "Thing");
        Folder(roots.ProgramFiles, "Microsoft");

        Assert.False(Leftovers.IsSafeToRecycle(roots.ProgramFiles, roots, out _));
        Assert.False(Leftovers.IsSafeToRecycle(Path.Combine(roots.ProgramFiles, "Common Files", "Thing"), roots, out _));
        Assert.Empty(Leftovers.Find("Microsoft", "Microsoft Corporation", roots.ProgramFiles, roots));
    }

    [Fact]
    public void Never_offers_a_folder_outside_the_roots()
    {
        var outside = Folder(root, "Documents", "Discord");

        Assert.False(Leftovers.IsSafeToRecycle(outside, roots, out _));
        Assert.Empty(Leftovers.Find("Discord", null, outside, roots));
    }

    [Fact]
    public void Ignores_names_too_short_to_be_specific()
    {
        Folder(roots.AppData, "Go");

        Assert.Empty(Leftovers.Find("Go", null, null, roots));
    }

    [Fact]
    public void Never_offers_a_link()
    {
        var target = Folder(root, "Elsewhere");
        var link = Path.Combine(roots.AppData, "Linked App");
        Directory.CreateSymbolicLink(link, target);

        Assert.False(Leftovers.IsSafeToRecycle(link, roots, out _));
    }

    [Fact]
    public void Refuses_relative_and_network_paths()
    {
        Assert.False(Leftovers.IsSafeToRecycle("ProgramFiles/App", roots, out _));
        Assert.False(Leftovers.IsSafeToRecycle(@"\\server\share\App", roots, out _));
    }

    [Fact]
    public void Never_offers_this_app_s_own_folder()
    {
        var self = new LeftoverRoots(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory))!, "", "", "", "");

        Assert.False(Leftovers.IsSafeToRecycle(AppContext.BaseDirectory, self, out _));
    }
}
