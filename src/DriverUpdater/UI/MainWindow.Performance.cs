using System.Windows;

namespace DriverUpdater.UI;

// "Performances" tab.
public partial class MainWindow
{
    async void OnMeasure(object sender, RoutedEventArgs e) =>
        await RunBusy("Analyse en cours, utilisez le PC normalement pendant 10 secondes…", async () =>
        {
            BusyBar.IsIndeterminate = false;
            BusyBar.Maximum = 1;
            var report = await PerfAnalyzer.MeasureAsync(TimeSpan.FromSeconds(10), new Progress<double>(p => BusyBar.Value = p));

            var system = report.System;
            (CpuGauge.Value, CpuValue.Text) = (system.Cpu, $"{system.Cpu:0} %");
            CpuDetail.Text = system.CpuPerformance is { } speed ? $"vitesse {speed:0} %" : "";
            (MemoryGauge.Value, MemoryValue.Text) = (system.MemoryPercent, $"{system.MemoryPercent:0} %");
            MemoryDetail.Text = $"{Bytes.Format(system.MemoryUsed)} / {Bytes.Format(system.MemoryTotal)}";
            (DiskGauge.Value, DiskValue.Text, DiskDetail.Text) = (system.DiskActive, $"{system.DiskActive:0} %", "occupé");
            (GpuGauge.Value, GpuValue.Text) = (system.Gpu ?? 0, system.Gpu is { } gpu ? $"{gpu:0} %" : "—");
            GpuDetail.Text = system.Gpu is null ? "non mesurable" : "";
            PowerValue.Text = system.PowerPlan;
            UsagePanel.Visibility = Visibility.Visible;
            FindingList.ItemsSource = report.Findings;
            UsageGrid.ItemsSource = report.Apps.Take(40).ToList();
            UsagePlaceholder.Visibility = report.Apps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            return "Analyse terminée.";
        });
}
