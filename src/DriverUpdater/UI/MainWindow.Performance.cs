using System.Media;
using System.Windows;

namespace DriverUpdater.UI;

// "Performances" tab.
public partial class MainWindow
{
    async void OnMeasure(object sender, RoutedEventArgs e) =>
        await Measure(TimeSpan.Zero, TimeSpan.FromSeconds(10), L.T("Analyse en cours, utilisez le PC normalement pendant 10 secondes…"));

    // A game left to click here would be measured in the background. The time to go back into it, a
    // longer measure, and a sound when it starts and when it ends: the window is behind the game.
    async void OnMeasureGame(object sender, RoutedEventArgs e) =>
        await Measure(TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30),
            L.T("Retournez dans le jeu : la mesure commence dans 15 secondes et dure 30 secondes, un son marque son début et sa fin."));

    Task Measure(TimeSpan delay, TimeSpan duration, string message) =>
        RunBusy(message, async stop =>
        {
            BusyBar.IsIndeterminate = false;
            BusyBar.Maximum = 1;
            if (delay > TimeSpan.Zero)
            {
                var start = DateTime.UtcNow;
                while (DateTime.UtcNow - start < delay)
                {
                    await Task.Delay(250, stop);
                    BusyBar.Value = (DateTime.UtcNow - start) / delay;
                }
                SystemSounds.Asterisk.Play();
                BusyBar.Value = 0;
            }
            var report = await PerfAnalyzer.MeasureAsync(duration, new Progress<double>(p => BusyBar.Value = p), stop);
            if (delay > TimeSpan.Zero) SystemSounds.Asterisk.Play();

            var system = report.System;
            (CpuGauge.Value, CpuValue.Text) = (system.Cpu, $"{system.Cpu:0} %");
            CpuDetail.Text = system.CpuPerformance is { } speed ? L.F("vitesse {0:0} %", speed) : "";
            (MemoryGauge.Value, MemoryValue.Text) = (system.MemoryPercent, $"{system.MemoryPercent:0} %");
            MemoryDetail.Text = $"{Bytes.Format(system.MemoryUsed)} / {Bytes.Format(system.MemoryTotal)}";
            (DiskGauge.Value, DiskValue.Text, DiskDetail.Text) = (system.DiskActive, $"{system.DiskActive:0} %",
                system.Disk is { } disk ? L.F("occupé ({0})", disk) : L.T("occupé"));
            (GpuGauge.Value, GpuValue.Text) = (system.Gpu ?? 0, system.Gpu is { } gpu ? $"{gpu:0} %" : "—");
            GpuDetail.Text = system.Gpu is null ? L.T("non mesurable") : "";
            PowerValue.Text = system.PowerPlan;
            UsagePanel.Visibility = Visibility.Visible;
            FindingList.ItemsSource = report.Findings;
            UsageGrid.ItemsSource = report.Apps.Take(40).ToList();
            UsagePlaceholder.Visibility = report.Apps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            return L.T("Analyse terminée.");
        });
}
