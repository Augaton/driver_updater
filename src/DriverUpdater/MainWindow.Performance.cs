using System.Windows;

namespace DriverUpdater;

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
            CpuValue.Text = $"{system.Cpu:0} %" + (system.CpuPerformance is { } speed ? $" (vitesse {speed:0} %)" : "");
            MemoryValue.Text = $"{system.MemoryPercent:0} % de {Bytes.Format(system.MemoryTotal)}";
            DiskValue.Text = $"{system.DiskActive:0} % occupé";
            GpuValue.Text = system.Gpu is { } gpu ? $"{gpu:0} %" : "—";
            PowerValue.Text = system.PowerPlan;
            UsagePanel.Visibility = Visibility.Visible;
            FindingList.ItemsSource = report.Findings;
            UsageGrid.ItemsSource = report.Apps.Take(40).ToList();
            UsagePlaceholder.Visibility = report.Apps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            return "Analyse terminée.";
        });
}
