using System.Windows;
using System.Windows.Controls;

namespace DriverUpdater.UI;

/// <summary>One line of the journal, and whether its "Annuler" still means something.</summary>
public sealed class JournalRow(JournalEntry entry, bool canUndo)
{
    public JournalEntry Entry { get; } = entry;
    public bool CanUndo { get; } = canUndo;
    public string Area => Entry.Area switch
    {
        Journal.Drivers => L.T("Pilotes"),
        Journal.Tuning => L.T("Réglages"),
        Journal.Startup => L.T("Démarrage"),
        Journal.Cleanup => L.T("Nettoyage"),
        Journal.Software => L.T("Logiciels"),
        _ => L.T("Système"),
    };
}

// "Journal" page: what this app changed on the PC, and the way back where there is one.
public partial class MainWindow
{
    async Task ShowJournal()
    {
        var entries = Journal.Read();
        var latest = Journal.LatestUndos(entries);
        // Whether a startup program can be set back depends on how it is now.
        if (startupEntries.Count == 0 && latest.Any(e => e.Undo!.Kind == JournalUndo.Startup)) await LoadStartup();
        JournalGrid.ItemsSource = entries.Select(e => new JournalRow(e, latest.Contains(e) && StillUndoable(e.Undo!))).ToList();
        JournalPlaceholder.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>A setting this app still holds the original of; a startup program not yet set back.</summary>
    bool StillUndoable(JournalUndo undo) => undo.Kind switch
    {
        JournalUndo.Tweak => TweakCatalog.Find(undo.Target) is { } tweak && Tuner.CanUndo(tweak),
        JournalUndo.Startup => StartupEntryFor(undo) is { } entry && entry.Enabled != (undo.Value == "on"),
        _ => false,
    };

    StartupEntry? StartupEntryFor(JournalUndo undo) => startupEntries.FirstOrDefault(s => StartupTarget(s) == undo.Target);

    static string StartupTarget(StartupEntry entry) => $"{entry.Kind}|{entry.Name}|{(entry.Machine ? "machine" : "user")}";

    async void OnJournalUndo(object sender, RoutedEventArgs e)
    {
        if (busy || sender is not Button { DataContext: JournalRow { Entry.Undo: { } undo } }) return;
        if (undo.Kind == JournalUndo.Tweak && TweakCatalog.Find(undo.Target) is { } tweak)
        {
            await ChangeTweak(tweak, apply: false);
        }
        else if (undo.Kind == JournalUndo.Startup && StartupEntryFor(undo) is { } entry)
        {
            await SetStartup(entry, undo.Value == "on");
            StartupGrid.Items.Refresh();
        }
        await ShowJournal();
    }

    /// <summary>Notes a change in the journal, and gives its text back for the status bar.</summary>
    static string Logged(string area, string text, JournalUndo? undo = null)
    {
        Journal.Add(area, text, undo);
        return text;
    }
}
