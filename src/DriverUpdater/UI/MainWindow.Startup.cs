using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace DriverUpdater.UI;

// "Démarrage" tab: startup programs and Windows' own boot measurements.
public partial class MainWindow
{
    List<StartupEntry> startupEntries = [];
    BootReport? bootReport;

    async Task LoadStartup()
    {
        try
        {
            startupEntries = await Task.Run(StartupApps.List);
            ApplyImpact();
            StartupGrid.ItemsSource = startupEntries;
        }
        catch (Exception e)
        {
            SetStatus(L.T("Lecture des programmes au démarrage impossible : ") + Errors.Describe(e));
        }
    }

    async void OnRefreshStartup(object sender, RoutedEventArgs e) => await LoadStartup();

    async void OnStartupToggle(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { DataContext: StartupEntry entry } box) return;
        bool enable = box.IsChecked == true;
        box.IsChecked = entry.Enabled; // until it is really done
        await RunBusy(entry.Machine ? L.T("En attente de l'autorisation administrateur…") : L.T("Modification…"), async () =>
        {
            if (entry.Machine)
                await RunAdminAsync<bool>(AdminCommand.StartupSet, entry.Kind, entry.Name, enable ? "on" : "off");
            else
                StartupApps.SetEnabled(entry.Kind, entry.Name, machine: false, enable);
            entry.Enabled = enable;
            box.IsChecked = enable;
            Settings.StartupChanged = DateTime.Now; // the boots after this one show what it saved
            return enable ? L.F("{0} démarrera avec Windows.", entry.Title) : L.F("{0} ne démarrera plus avec Windows.", entry.Title);
        });
    }

    async void OnBootReport(object sender, RoutedEventArgs e) =>
        await RunBusy(L.T("Lecture des mesures de démarrage…"), async () =>
        {
            try
            {
                bootReport = await Task.Run(BootLog.Read);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or EventLogException)
            {
                // On many PCs only administrators may read this log.
                SetStatus(L.T("En attente de l'autorisation administrateur…"));
                bootReport = await RunAdminAsync<BootReport>(AdminCommand.BootReport);
            }
            ShowBootReport(bootReport);
            return bootReport.Boots.Count == 0
                ? L.T("Windows n'a enregistré aucune mesure de démarrage ces 30 derniers jours.")
                : L.T("Mesures de démarrage lues.");
        });

    void ShowBootReport(BootReport report)
    {
        BootCard.Visibility = Visibility.Visible;
        if (report.Boots.Count == 0)
        {
            BootSummary.Text = L.T("Aucune mesure ces 30 derniers jours (Windows ne mesure pas les démarrages rapides ni les sorties de veille).");
        }
        else
        {
            var last = report.Boots[0];
            BootSummary.Text = L.F("Dernier démarrage mesuré ({0:dd/MM HH:mm}) : {1:0} s, dont {2:0} s ", last.Date, last.Seconds, last.PostBootSeconds) +
                               L.F("après l'apparition du bureau. Moyenne sur {0} démarrage(s) : {1:0} s.", report.Boots.Count, report.Boots.Average(b => b.Seconds)) +
                               (report.Slowdowns.Count > 0 ? L.T(" Ce qui l'a ralenti :") : L.T(" Rien ne l'a nettement ralenti."));
        }
        if (Settings.StartupChanged is { } changed && BootGain.Since(report.Boots, changed) is { } gain)
            BootSummary.Text += L.F("\nDepuis vos changements du {0:dd/MM} : {1:0} s → {2:0} s en moyenne ", changed, gain.BeforeSeconds, gain.AfterSeconds) +
                                (gain.Saved >= 1 ? L.F("({0:0} s de gagnées", gain.Saved) : gain.Saved <= -1 ? L.F("({0:0} s de plus", -gain.Saved) : L.T("(pas de différence nette")) +
                                L.F(", {0} démarrage(s) mesuré(s) depuis).", gain.BootsAfter);
        ShowBootChart(report.Boots);
        SlowdownList.ItemsSource = report.Slowdowns.Take(10).ToList();
        ApplyImpact();
        StartupGrid.Items.Refresh();
    }

    /// <summary>One bar: its height in pixels, a value only on the latest, and a tooltip for each.</summary>
    public sealed record BootBar(double Height, string Label, string Tip);

    const double BootBarMax = 110; // the chart's top line, in pixels (MainWindow.xaml)

    // The last 30 boots, oldest to newest, on a scale rounded up to 20 s so its half is a round number.
    void ShowBootChart(List<BootEntry> boots)
    {
        var shown = boots.Take(30).Reverse().ToList();
        BootChart.Visibility = shown.Count >= 2 ? Visibility.Visible : Visibility.Collapsed;
        if (shown.Count < 2) return;
        double top = Math.Max(20, Math.Ceiling(shown.Max(b => b.Seconds) / 20) * 20);
        BootScaleTop.Text = $"{top:0} s";
        BootScaleMid.Text = $"{top / 2:0} s";
        BootBars.ItemsSource = shown.Select((b, i) => new BootBar(
            Math.Max(2, b.Seconds / top * BootBarMax),
            i == shown.Count - 1 ? $"{b.Seconds:0} s" : "",
            L.F("{0:dd/MM HH:mm} : {1:0} s, dont {2:0} s après l'apparition du bureau", b.Date, b.Seconds, b.PostBootSeconds))).ToList();
        BootFirstDate.Text = $"{shown[0].Date:dd/MM}";
        BootLastDate.Text = $"{shown[^1].Date:dd/MM}";
    }

    // Match each startup program with what Windows measured for its file.
    void ApplyImpact()
    {
        if (bootReport is null) return;
        var byFile = bootReport.Slowdowns.Where(s => s.Kind == "Application")
            .GroupBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var entry in startupEntries)
            entry.Impact = byFile.TryGetValue(Path.GetFileName(entry.Program), out var slow)
                ? L.F("{0} ({1} fois)", slow.AverageText, slow.Times)
                : L.T("non mesuré");
    }
}
