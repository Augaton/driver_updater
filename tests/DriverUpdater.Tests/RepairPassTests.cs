using Xunit;

// "Réparer Windows" runs DISM then SFC: one verdict says how both went, and which one to look at.
public sealed class RepairPassTests
{
    static RepairPass Pass(int dism, int sfc) => new(new RepairReport(dism, ""), new RepairReport(sfc, ""));

    [Fact]
    public void Both_fine_is_done() => Assert.Equal("Terminé.", Pass(0, 0).Verdict);

    [Fact]
    public void A_restart_from_either_is_said() =>
        Assert.Equal("Terminé : redémarrez l'ordinateur.", Pass(3010, 0).Verdict);

    [Fact]
    public void A_dism_failure_is_named_in_hexadecimal() =>
        // 0x800F081F: DISM found no source to repair from (no Windows Update, no install media).
        Assert.Contains("DISM n'a pas pu réparer l'image de Windows (code 0x800F081F)", Pass(unchecked((int)0x800F081F), 0).Verdict);

    [Fact]
    public void An_sfc_failure_after_a_good_dism_is_named() =>
        Assert.Contains("SFC s'est terminé avec le code 2", Pass(0, 2).Verdict);
}
