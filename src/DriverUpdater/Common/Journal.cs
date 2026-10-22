using System.IO;
using System.Text.Json;

namespace DriverUpdater.Common;

/// <summary>
/// How a change is taken back from the journal: a setting's own undo (<see cref="Tweak"/>, the
/// setting's ID), or a startup program set back on or off (<see cref="Startup"/>, Value "on" or "off").
/// </summary>
public sealed record JournalUndo(string Kind, string Target, string? Value = null)
{
    public const string Tweak = "tweak", Startup = "startup";
}

/// <summary>One change this app made to the PC.</summary>
/// <param name="Area">One of the <see cref="Journal"/> areas, named when shown.</param>
public sealed record JournalEntry(DateTime When, string Area, string Text, JournalUndo? Undo = null);

/// <summary>
/// What this app changed on the PC, for the "Journal" page: one JSON line per change in the
/// user's own folder, the last <see cref="Kept"/> kept. Only the window writes it, never the
/// administrator helper. A line that cannot be written is lost; the change itself stands.
/// </summary>
public static class Journal
{
    public const string Drivers = "drivers", Tuning = "tuning", Startup = "startup", Cleanup = "cleanup",
        Software = "software", Maintenance = "maintenance";

    public const int Kept = 500;

    public static readonly string DefaultFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Driver Updater", "journal.jsonl");

    public static void Add(string area, string text, JournalUndo? undo = null) =>
        Add(DefaultFile, new JournalEntry(DateTime.Now, area, text, undo));

    public static void Add(string file, JournalEntry entry)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            var lines = File.Exists(file) ? File.ReadAllLines(file).ToList() : [];
            lines.Add(JsonSerializer.Serialize(entry));
            // Written aside, then swapped in: a crash halfway leaves the old journal whole.
            var written = file + ".tmp";
            File.WriteAllLines(written, lines.TakeLast(Kept));
            File.Move(written, file, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Newest first. A line that cannot be read (cut short, edited by hand) is skipped.</summary>
    public static List<JournalEntry> Read(string? file = null)
    {
        string[] lines;
        try
        {
            lines = File.ReadAllLines(file ?? DefaultFile);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return []; // none yet
        }
        var entries = new List<JournalEntry>();
        foreach (var line in lines)
        {
            try
            {
                if (JsonSerializer.Deserialize<JournalEntry>(line) is { Area: not null, Text: not null } entry) entries.Add(entry);
            }
            catch (JsonException)
            {
            }
        }
        entries.Reverse();
        return entries;
    }

    /// <summary>
    /// Of the entries that can be undone, those that are still the last word on what they changed:
    /// an older one would bring back a state that a later change has replaced.
    /// </summary>
    public static HashSet<JournalEntry> LatestUndos(IEnumerable<JournalEntry> newestFirst) =>
        newestFirst.Where(e => e.Undo is not null)
            .GroupBy(e => (e.Undo!.Kind, e.Undo.Target))
            .Select(g => g.First())
            .ToHashSet();
}
