using System.Text.RegularExpressions;

namespace DriverUpdater.Software;

public sealed class SoftwareUpdate
{
    public required string Name { get; init; }
    public required string Id { get; init; }
    public required string Version { get; init; }
    public required string Available { get; init; }
    public required string Source { get; init; }
    public bool Selected { get; set; } = true;
}

/// <summary>
/// Reads the table printed by <c>winget upgrade</c>. winget has no machine-readable output
/// for this, so the columns are found from the header, whatever its language:
/// <code>
/// Nom        ID             Version  Disponible Source
/// ----------------------------------------------------
/// Git        Git.Git        2.46.0   2.47.0     winget
/// </code>
/// </summary>
public static partial class WingetOutput
{
    public static List<SoftwareUpdate> ParseUpgrades(string output)
    {
        // Colour codes ("ESC[0m") would shift the columns; a spinner drawn with \r precedes
        // the table: keep only what is left on each line once both are gone. Redirected, winget
        // ends its lines with \r\n: that \r is not a spinner's and must not empty the line.
        var lines = AnsiEscape().Replace(output, "").Replace("\r\n", "\n").Split('\n')
            .Select(l => new string(l[(l.LastIndexOf('\r') + 1)..].Where(c => c >= ' ').ToArray()).TrimEnd())
            .ToList();

        int rule = lines.FindIndex(l => l.Length >= 10 && l.All(c => c == '-'));
        if (rule < 1) return [];
        // With nothing else to update, the first table is the one of packages that need explicit
        // targeting (pinned ones…), under its title "… explicit targeting for upgrade:".
        if (rule >= 2 && lines[rule - 2].EndsWith(':')) return [];
        var header = lines[rule - 1];
        var starts = Enumerable.Range(0, header.Length)
            .Where(i => header[i] != ' ' && (i == 0 || header[i - 1] == ' '))
            .ToList();
        if (starts.Count < 4) return [];

        var updates = new List<SoftwareUpdate>();
        // The first blank line ends the table; another one may follow (packages that need
        // explicit targeting), which a plain "upgrade" would not touch anyway.
        foreach (var line in lines.Skip(rule + 1).TakeWhile(l => l.Length > 0))
        {
            if (line.Length <= starts[3]) break; // footer, e.g. "2 mises à jour disponibles."
            string Column(int i) => i + 1 < starts.Count
                ? line[starts[i]..Math.Min(starts[i + 1], line.Length)].Trim()
                : line.Length > starts[i] ? line[starts[i]..].Trim() : "";
            var id = Column(1);
            // A truncated ("…") or misaligned ID cannot be upgraded by name, and one starting
            // with "-" would read as an option on winget's command line: skip them.
            if (id.Length == 0 || id.Contains(' ') || id.Contains('…') || id.StartsWith('-')) continue;
            updates.Add(new SoftwareUpdate
            {
                Name = Column(0),
                Id = id,
                Version = Column(2),
                Available = Column(3),
                Source = starts.Count > 4 ? Column(4) : "",
            });
        }
        return updates;
    }

    [GeneratedRegex(@"\x1B\[[0-9;?]*[A-Za-z]")]
    private static partial Regex AnsiEscape();
}
