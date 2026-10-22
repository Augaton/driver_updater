using System.Text.Json;
using Xunit;

// The journal must keep what the app changed, survive a damaged line, and offer "Annuler" only
// where it is still the last word.
public sealed class JournalTests : IDisposable
{
    readonly string file = Path.Combine(Path.GetTempPath(), "journal-" + Guid.NewGuid().ToString("N"), "journal.jsonl");

    public void Dispose()
    {
        if (Directory.Exists(Path.GetDirectoryName(file))) Directory.Delete(Path.GetDirectoryName(file)!, recursive: true);
    }

    static JournalEntry Entry(string text, JournalUndo? undo = null, int minute = 0) =>
        new(new DateTime(2026, 10, 22, 17, minute, 0), Journal.Tuning, text, undo);

    [Fact]
    public void Gives_back_what_was_written_newest_first()
    {
        Journal.Add(file, Entry("Mode jeu : appliqué.", new JournalUndo(JournalUndo.Tweak, "game-mode"), minute: 1));
        Journal.Add(file, Entry("Écran réglé à 165 Hz.", minute: 2));

        var entries = Journal.Read(file);

        Assert.Equal(["Écran réglé à 165 Hz.", "Mode jeu : appliqué."], entries.Select(e => e.Text));
        Assert.Equal(new JournalUndo(JournalUndo.Tweak, "game-mode"), entries[1].Undo);
        Assert.Equal(new DateTime(2026, 10, 22, 17, 1, 0), entries[1].When);
    }

    [Fact]
    public void Keeps_only_the_last_entries()
    {
        for (int i = 0; i < Journal.Kept + 3; i++) Journal.Add(file, Entry($"change {i}"));

        var entries = Journal.Read(file);

        Assert.Equal(Journal.Kept, entries.Count);
        Assert.Equal($"change {Journal.Kept + 2}", entries[0].Text);
        Assert.Equal("change 3", entries[^1].Text);
    }

    [Fact]
    public void Skips_a_damaged_line_and_reads_no_file_as_empty()
    {
        Assert.Empty(Journal.Read(file));

        Journal.Add(file, Entry("avant"));
        File.AppendAllText(file, "{\"When\":\"2026-10-22T17:00:00\",\"Are\n");
        Journal.Add(file, Entry("après"));

        Assert.Equal(["après", "avant"], Journal.Read(file).Select(e => e.Text));
    }

    [Fact]
    public void Offers_undo_only_on_the_last_change_of_each_target()
    {
        var off = Entry("Discord ne démarrera plus avec Windows.", new JournalUndo(JournalUndo.Startup, "Run|Discord|user", "on"), 1);
        var on = Entry("Discord démarrera avec Windows.", new JournalUndo(JournalUndo.Startup, "Run|Discord|user", "off"), 2);
        var tweak = Entry("Mode jeu : appliqué.", new JournalUndo(JournalUndo.Tweak, "game-mode"), 3);
        var plain = Entry("Écran réglé à 165 Hz.", minute: 4);

        var latest = Journal.LatestUndos([plain, tweak, on, off]); // newest first

        Assert.True(latest.SetEquals([tweak, on]));
    }

    [Fact]
    public void Keeps_the_figures_of_the_last_analysis()
    {
        var summary = new ScanSummary(180, 176, 2, 1) { When = new DateTime(2026, 10, 22, 17, 10, 0), Complete = false };

        var read = JsonSerializer.Deserialize<ScanSummary>(JsonSerializer.Serialize(summary));

        Assert.Equal(summary, read);
    }
}
