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
            SetStatus("Lecture des programmes au démarrage impossible : " + Errors.Describe(e));
        }
    }

    async void OnRefreshStartup(object sender, RoutedEventArgs e) => await LoadStartup();

    async void OnStartupToggle(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { DataContext: StartupEntry entry } box) return;
        bool enable = box.IsChecked == true;
        box.IsChecked = entry.Enabled; // until it is really done
        await RunBusy(entry.Machine ? "En attente de l'autorisation administrateur…" : "Modification…", async () =>
        {
            if (entry.Machine)
                await RunAdminAsync<bool>(AdminCommand.StartupSet, entry.Kind, entry.Name, enable ? "on" : "off");
            else
                StartupApps.SetEnabled(entry.Kind, entry.Name, machine: false, enable);
            entry.Enabled = enable;
            box.IsChecked = enable;
            return $"{entry.Title} {(enable ? "démarrera" : "ne démarrera plus")} avec Windows.";
        });
    }

    async void OnBootReport(object sender, RoutedEventArgs e) =>
        await RunBusy("Lecture des mesures de démarrage…", async () =>
        {
            try
            {
                bootReport = await Task.Run(BootLog.Read);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or EventLogException)
            {
                // On many PCs only administrators may read this log.
                SetStatus("En attente de l'autorisation administrateur…");
                bootReport = await RunAdminAsync<BootReport>(AdminCommand.BootReport);
            }
            ShowBootReport(bootReport);
            return bootReport.Boots.Count == 0
                ? "Windows n'a enregistré aucune mesure de démarrage ces 30 derniers jours."
                : "Mesures de démarrage lues.";
        });

    void ShowBootReport(BootReport report)
    {
        BootCard.Visibility = Visibility.Visible;
        if (report.Boots.Count == 0)
        {
            BootSummary.Text = "Aucune mesure ces 30 derniers jours (Windows ne mesure pas les démarrages rapides ni les sorties de veille).";
        }
        else
        {
            var last = report.Boots[0];
            BootSummary.Text = $"Dernier démarrage mesuré ({last.Date:dd/MM HH:mm}) : {last.Seconds:0} s, dont {last.PostBootSeconds:0} s " +
                               $"après l'apparition du bureau. Moyenne sur {report.Boots.Count} démarrage(s) : {report.Boots.Average(b => b.Seconds):0} s." +
                               (report.Slowdowns.Count > 0 ? " Ce qui l'a ralenti :" : " Rien ne l'a nettement ralenti.");
        }
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
            $"{b.Date:dd/MM HH:mm} : {b.Seconds:0} s, dont {b.PostBootSeconds:0} s après l'apparition du bureau")).ToList();
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
                ? $"{slow.AverageText} ({slow.Times} fois)"
                : "non mesuré";
    }
}
