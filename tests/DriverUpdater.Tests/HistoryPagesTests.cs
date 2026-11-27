using Xunit;

// The "Historique" page: Windows Update's history is mostly not drivers (Defender's updates come
// several times a day), so reading only its first page would lose the older drivers.
public sealed class HistoryPagesTests
{
    // A history of `total` entries where only every tenth one is a driver.
    static Func<int, int, IEnumerable<string?>> History(List<(int Start, int Count)> asked) => (start, count) =>
    {
        asked.Add((start, count));
        return Enumerable.Range(start, count).Select(i => i % 10 == 0 ? $"driver {i}" : null);
    };

    [Fact]
    public void Reads_on_until_enough_drivers_are_found()
    {
        var asked = new List<(int, int)>();
        var drivers = HistoryPages.Collect(total: 1000, pageSize: 100, max: 25, History(asked));

        Assert.Equal(25, drivers.Count);
        Assert.Equal("driver 0", drivers[0]);
        Assert.Equal("driver 240", drivers[^1]);
        Assert.Equal(new[] { (0, 100), (100, 100), (200, 100) }, asked); // and no further
    }

    [Fact]
    public void Stops_at_the_end_of_the_history()
    {
        var asked = new List<(int, int)>();
        var drivers = HistoryPages.Collect(total: 250, pageSize: 100, max: 300, History(asked));

        Assert.Equal(25, drivers.Count);
        Assert.Equal(new[] { (0, 100), (100, 100), (200, 50) }, asked);
    }

    [Fact]
    public void An_empty_history_reads_nothing()
    {
        var asked = new List<(int, int)>();
        Assert.Empty(HistoryPages.Collect(total: 0, pageSize: 100, max: 300, History(asked)));
        Assert.Empty(asked);
    }
}
