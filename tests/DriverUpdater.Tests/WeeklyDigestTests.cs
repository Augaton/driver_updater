using Xunit;

// The weekly notification must come once per novelty, never every week for the same thing.
public sealed class WeeklyDigestTests
{
    static readonly HashSet<string> None = [];

    [Fact]
    public void Tells_about_what_is_new_and_counts_everything()
    {
        var news = WeeklyDigest.Compute(["a", "b"], new HashSet<string> { "a" }, "617.42", "617.42", ["git@2.52"], None);

        Assert.True(news.HasNew);
        Assert.Equal("2 pilote(s) Windows Update, pilote NVIDIA 617.42, 1 logiciel(s)", news.Message);
    }

    [Fact]
    public void Stays_quiet_when_everything_was_already_told()
    {
        var news = WeeklyDigest.Compute(["a"], new HashSet<string> { "a" }, "617.42", "617.42", ["git@2.52"], new HashSet<string> { "git@2.52" });

        Assert.False(news.HasNew);
    }

    [Fact]
    public void A_new_version_of_the_same_software_is_news() =>
        Assert.True(WeeklyDigest.Compute([], None, null, null, ["git@2.53"], new HashSet<string> { "git@2.52" }).HasNew);

    [Fact]
    public void A_new_nvidia_driver_is_news_once() =>
        Assert.Equal((true, false),
            (WeeklyDigest.Compute([], None, "618.10", "617.42", [], None).HasNew,
             WeeklyDigest.Compute([], None, "618.10", "618.10", [], None).HasNew));
}
