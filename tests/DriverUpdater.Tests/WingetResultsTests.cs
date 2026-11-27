using Xunit;

// "Échec : Discord (0x8A150101)" told nothing: winget's codes are said in plain words, and an
// install that only waits for a restart counts as done.
public sealed class WingetResultsTests
{
    [Fact]
    public void A_restart_to_finish_is_a_success() =>
        Assert.Equal(WingetOutcome.DoneRestart, WingetResults.Of(unchecked((int)0x8A150109)).Outcome);

    [Fact]
    public void Already_installed_is_not_a_failure() =>
        Assert.Equal(WingetOutcome.AlreadyThere, WingetResults.Of(unchecked((int)0x8A15010D)).Outcome);

    [Fact]
    public void An_open_program_is_said_in_words() =>
        Assert.Equal("Discord (le programme est ouvert : fermez-le puis réessayez)",
            WingetResults.Failure("Discord", unchecked((int)0x8A150101)));

    [Fact]
    public void An_unknown_code_stays_a_failure_with_its_code() =>
        Assert.Equal(("Truc (0x8A150006)", WingetOutcome.Failed),
            (WingetResults.Failure("Truc", unchecked((int)0x8A150006)), WingetResults.Of(unchecked((int)0x8A150006)).Outcome));

    [Fact]
    public void Zero_is_done() => Assert.Equal(WingetOutcome.Done, WingetResults.Of(0).Outcome);
}
