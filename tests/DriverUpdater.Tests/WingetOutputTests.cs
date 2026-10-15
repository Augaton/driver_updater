using Xunit;

public sealed class WingetOutputTests
{
    // As winget prints it when redirected: spinner frames overwritten with \r, then the table.
    const string French =
        "   - \r   \\ \r   | \r" +
        "Nom                                Id                          Version        Disponible     Source\n" +
        "---------------------------------------------------------------------------------------------------\n" +
        "Microsoft Edge                     Microsoft.Edge              129.0.2792.65  130.0.2849.46  winget\n" +
        "Git                                Git.Git                     2.46.0         2.47.0         winget\n" +
        "Un logiciel au nom très long qui… UnEditeur.UnLogicielAuNomTr… 1.0            2.0            winget\n" +
        "3 mises à jour disponibles.\n" +
        "\n" +
        "Les packages suivants ont une mise à niveau disponible, mais nécessitent un ciblage explicite :\n" +
        "Nom      Id           Version Disponible Source\n" +
        "-----------------------------------------------\n" +
        "Épinglé  Pinned.App   1.0     2.0        winget\n";

    const string English =
        "Name               Id                  Version  Available Source\n" +
        "----------------------------------------------------------------\n" +
        "Mozilla Firefox    Mozilla.Firefox     130.0    131.0.2   winget\n" +
        "1 upgrades available.\n";

    [Fact]
    public void Reads_the_french_table()
    {
        var updates = WingetOutput.ParseUpgrades(French);

        Assert.Equal(["Microsoft.Edge", "Git.Git"], updates.Select(u => u.Id));
        var git = updates[1];
        Assert.Equal("Git", git.Name);
        Assert.Equal("2.46.0", git.Version);
        Assert.Equal("2.47.0", git.Available);
        Assert.Equal("winget", git.Source);
    }

    [Fact]
    public void Reads_the_english_table()
    {
        var firefox = Assert.Single(WingetOutput.ParseUpgrades(English));

        Assert.Equal(("Mozilla Firefox", "Mozilla.Firefox", "130.0", "131.0.2"),
            (firefox.Name, firefox.Id, firefox.Version, firefox.Available));
    }

    [Fact]
    public void Skips_truncated_ids_and_pinned_packages()
    {
        var ids = WingetOutput.ParseUpgrades(French).Select(u => u.Id).ToList();

        Assert.DoesNotContain(ids, id => id.Contains('…'));
        Assert.DoesNotContain("Pinned.App", ids);
    }

    [Fact]
    public void Ignores_colour_codes()
    {
        var coloured = "\u001b[2K\u001b[0m" + English.Replace("Mozilla Firefox ", "\u001b[1mMozilla Firefox\u001b[0m ");

        Assert.Equal("Mozilla.Firefox", Assert.Single(WingetOutput.ParseUpgrades(coloured)).Id);
    }

    [Theory]
    [InlineData("Aucune mise à niveau disponible.\n")]
    [InlineData("No installed package found matching input criteria.\n")]
    [InlineData("")]
    public void Nothing_to_upgrade_gives_an_empty_list(string output) =>
        Assert.Empty(WingetOutput.ParseUpgrades(output));
}
