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

/// <param name="Unreadable">
/// Rows winget printed but cut short ("…"): real updates, which cannot be named to winget from
/// here. Said, never silently dropped.
/// </param>
public sealed record UpgradeList(List<SoftwareUpdate> Updates, int Unreadable);

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
    public static UpgradeList ParseUpgrades(string output)
    {
        int unreadable = 0;
        // Colour codes ("ESC[0m") would shift the columns; a spinner drawn with \r precedes
        // the table: keep only what is left on each line once both are gone. Redirected, winget
        // ends its lines with \r\n: that \r is not a spinner's and must not empty the line.
        var lines = AnsiEscape().Replace(output, "").Replace("\r\n", "\n").Split('\n')
            .Select(l => new string(l[(l.LastIndexOf('\r') + 1)..].Where(c => c >= ' ').ToArray()).TrimEnd())
            .ToList();

        int rule = lines.FindIndex(l => l.Length >= 10 && l.All(c => c == '-'));
        if (rule < 1) return new([], 0);
        // With nothing else to update, the first table is the one of packages that need explicit
        // targeting (pinned ones…), under its title "… explicit targeting for upgrade:".
        if (rule >= 2 && lines[rule - 2].EndsWith(':')) return new([], 0);
        var header = Cells(lines[rule - 1]);
        var starts = header
            .Where((cell, i) => cell.Char != ' ' && (i == 0 || header[i - 1].Char == ' '))
            .Select(cell => cell.Column)
            .ToList();
        if (starts.Count < 4) return new([], 0);

        var updates = new List<SoftwareUpdate>();
        // The first blank line ends the table; another one may follow (packages that need
        // explicit targeting), which a plain "upgrade" would not touch anyway.
        foreach (var line in lines.Skip(rule + 1).TakeWhile(l => l.Length > 0))
        {
            var cells = Cells(line);
            if (Width(cells) <= starts[3]) break; // footer, e.g. "2 mises à jour disponibles."
            string Column(int i) => Slice(cells, starts[i], i + 1 < starts.Count ? starts[i + 1] : int.MaxValue);
            var id = Column(1);
            // A truncated ("…") or misaligned ID cannot be upgraded by name, and one starting
            // with "-" would read as an option on winget's command line: skip them.
            if (id.Length == 0 || id.Contains(' ') || id.Contains('…') || id.StartsWith('-'))
            {
                unreadable++;
                continue;
            }
            updates.Add(new SoftwareUpdate
            {
                Name = Column(0),
                Id = id,
                Version = Column(2),
                Available = Column(3),
                Source = starts.Count > 4 ? Column(4) : "",
            });
        }
        return new(updates, unreadable);
    }

    /// <summary>
    /// Each character with the screen column it starts at. winget lines its table up by
    /// display width: a Chinese, Japanese or Korean character takes two columns, so a name
    /// written in one would otherwise shift every column after it.
    /// </summary>
    static List<(char Char, int Column)> Cells(string line)
    {
        var cells = new List<(char, int)>(line.Length);
        int column = 0;
        foreach (var c in line)
        {
            cells.Add((c, column));
            column += CharWidth(c);
        }
        return cells;
    }

    static int Width(List<(char Char, int Column)> cells) => cells.Count == 0 ? 0 : cells[^1].Column + CharWidth(cells[^1].Char);

    static string Slice(List<(char Char, int Column)> cells, int from, int to) =>
        new string(cells.Where(c => c.Column >= from && c.Column < to).Select(c => c.Char).ToArray()).Trim();

    // East Asian wide characters (Unicode's "W" and "F" ranges, the common ones); a character
    // outside the basic plane comes as two halves: the first counts for both.
    static int CharWidth(char c) => c switch
    {
        >= '\u1100' and <= '\u115F' or >= '\u2E80' and <= '\u303E' or >= '\u3041' and <= '\uA4CF' or >= '\uAC00' and <= '\uD7A3'
            or >= '\uF900' and <= '\uFAFF' or >= '\uFE30' and <= '\uFE4F' or >= '\uFF00' and <= '\uFF60' or >= '\uFFE0' and <= '\uFFE6'
            or >= '\uD800' and <= '\uDBFF' => 2,
        >= '\uDC00' and <= '\uDFFF' => 0,
        _ => 1,
    };

    [GeneratedRegex(@"\x1B\[[0-9;?]*[A-Za-z]")]
    private static partial Regex AnsiEscape();
}

public enum WingetOutcome { Done, DoneRestart, AlreadyThere, Failed }

/// <summary>
/// What winget's exit code means for the user: done, done but a restart finishes it, already there,
/// or failed with the reason in plain words where winget gives one. The codes are winget's own
/// (github.com/microsoft/winget-cli, doc/windows/package-manager/winget/returnCodes.md).
/// </summary>
public static class WingetResults
{
    public static (WingetOutcome Outcome, string? Reason) Of(int code) => unchecked((uint)code) switch
    {
        0 => (WingetOutcome.Done, null),
        // An installer's 3010 is already 0 with a warning; this is the same, said by the installer's own table.
        0x8A150109 => (WingetOutcome.DoneRestart, null),
        0x8A15010D or 0x8A150061 => (WingetOutcome.AlreadyThere, null),
        0x8A150101 or 0x8A150111 or 0x8A150103 => (WingetOutcome.Failed, L.T("le programme est ouvert : fermez-le puis réessayez")),
        0x8A150102 => (WingetOutcome.Failed, L.T("une autre installation est en cours : réessayez quand elle aura fini")),
        0x8A15010A => (WingetOutcome.Failed, L.T("un redémarrage est nécessaire avant : redémarrez puis réessayez")),
        0x8A15010B => (WingetOutcome.Failed, L.T("l'installateur a lancé un redémarrage")),
        0x8A15010C => (WingetOutcome.Failed, L.T("annulé")),
        0x8A15010E => (WingetOutcome.Failed, L.T("une version plus récente est déjà installée")),
        0x8A15002B => (WingetOutcome.Failed, L.T("aucune version plus récente ne convient à ce PC")),
        0x8A150010 => (WingetOutcome.Failed, L.T("aucun installateur pour ce PC")),
        0x8A150104 or 0x8A150110 => (WingetOutcome.Failed, L.T("un composant dont il a besoin n'a pas pu s'installer")),
        0x8A150105 => (WingetOutcome.Failed, L.T("disque plein")),
        0x8A150106 => (WingetOutcome.Failed, L.T("mémoire insuffisante")),
        0x8A150107 or 0x8A150008 => (WingetOutcome.Failed, L.T("téléchargement impossible : vérifiez la connexion")),
        0x8A150011 => (WingetOutcome.Failed, L.T("le fichier téléchargé n'est pas celui attendu : il n'a pas été lancé")),
        0x8A15010F or 0x8A15003A => (WingetOutcome.Failed, L.T("bloqué par une stratégie de l'entreprise ou du PC")),
        0x8A150068 => (WingetOutcome.Failed, L.T("épinglé : winget le garde tel quel")),
        _ => (WingetOutcome.Failed, null),
    };

    /// <summary>"Discord (le programme est ouvert…)", or the code itself when winget gives no reason.</summary>
    public static string Failure(string name, int code) =>
        Of(code).Reason is { } reason ? $"{name} ({reason})" : $"{name} (0x{code:X8})";
}
