using System.IO;
using DriverUpdater;
using Xunit;

// Nothing here installs or deletes anything on the machine, except a scheduled task and a
// startup entry that their own tests create and remove.
public sealed class SmokeTests
{
    [Fact]
    public void Main_window_and_its_resources_load_with_the_Windows_11_theme()
    {
        // XAML errors only surface at run time, so build the real window on a UI thread.
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = new App();
                app.InitializeComponent();
                app.ApplyTheme();
                new MainWindow().Close();
            }
            catch (Exception e)
            {
                error = e;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(error);
    }

    [Fact]
    public void Lists_disks()
    {
        var disks = SystemInfo.Disks();

        Assert.NotEmpty(disks);
        Assert.All(disks, d => Assert.False(string.IsNullOrEmpty(d.Health)));
    }

    [Fact]
    public void Lists_problem_devices_without_failing() => SystemInfo.ProblemDevices();

    [Fact]
    public void Lists_graphics_cards_without_failing() => SystemInfo.Gpus();

    [Fact]
    public void Identifies_the_computer() =>
        Assert.False(string.IsNullOrEmpty(SystemInfo.Computer().Name));

    [Fact]
    public void Reads_the_driver_history_without_failing() => WindowsUpdateDrivers.History();

    // The whole elevation path for real: runas, private pipe, client check, JSON, result.
    // Task.Run and the timeout: should Windows ever show a consent prompt here, nobody would
    // answer it, and the test must fail rather than hang the pipeline.
    [Fact(Timeout = 120_000)]
    public async Task Administrator_helper_answers_through_its_pipe()
    {
        var exe = AppExe();
        var version = await Task.Run(() => Elevation.RunAsync<string>(AdminCommand.Version, [], new Progress<string>(), _ => false, exe));

        Assert.Equal(typeof(App).Assembly.GetName().Version!.ToString(), version);
    }

    // The tests run in another program than Driver Updater's window: any real command must
    // be refused, even though Windows let the helper start as administrator.
    [Fact(Timeout = 120_000)]
    public async Task Administrator_helper_obeys_only_driver_updater_s_window()
    {
        var exe = AppExe();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Task.Run(() =>
            Elevation.RunAsync<HealthReport>(AdminCommand.Health, [], new Progress<string>(), _ => false, exe)));

        Assert.Contains("Refusé", error.Message);
    }

    [Fact]
    public void Lists_startup_programs_without_failing() => StartupApps.List();

    [Fact]
    public void Lists_installed_programs() => Assert.NotEmpty(InstalledPrograms.List());

    [Fact]
    public async Task Measures_performance()
    {
        var report = await PerfAnalyzer.MeasureAsync(TimeSpan.FromSeconds(1), new Progress<double>());

        Assert.True(report.System.MemoryTotal > 0);
        Assert.NotEmpty(report.Apps);
        Assert.NotEmpty(report.Findings);
    }

    [Fact]
    public void Reads_boot_measurements_without_failing() => BootLog.Read();

    [Fact]
    public void Switches_a_startup_entry_off_and_on_like_task_manager()
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        var name = "DriverUpdater-test-" + Guid.NewGuid().ToString("N");
        using (var run = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey))
            run.SetValue(name, @"C:\does-not-exist\test.exe"); // never a real program, should cleanup fail
        try
        {
            StartupApps.SetEnabled(StartupApps.Run, name, machine: false, enabled: false);
            Assert.False(StartupApps.List().Single(e => e.Name == name).Enabled);
            StartupApps.SetEnabled(StartupApps.Run, name, machine: false, enabled: true);
            Assert.True(StartupApps.List().Single(e => e.Name == name).Enabled);
        }
        finally
        {
            using (var run = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, writable: true))
                run?.DeleteValue(name, throwOnMissingValue: false);
            using var approved = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run", writable: true);
            approved?.DeleteValue(name, throwOnMissingValue: false);
        }
    }

    // A canary on the live Microsoft Update Catalog: fails as soon as its pages change shape.
    [Fact]
    public async Task Microsoft_update_catalog_still_reads_and_resolves_downloads()
    {
        using var http = new System.Net.Http.HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) DriverUpdater");
        var entries = Catalog.ParseSearch(await http.GetStringAsync(Catalog.SearchUrl + Uri.EscapeDataString(@"PCI\VEN_10EC&DEV_8168")));
        var entry = Assert.Single(entries.Where(Catalog.Fits).Take(1));

        var dialog = await http.PostAsync(Catalog.DownloadDialogUrl,
            new System.Net.Http.FormUrlEncodedContent([new("updateIDs", Catalog.DownloadRequest(entry.UpdateId))]));
        var download = Catalog.ParseDownload(await dialog.Content.ReadAsStringAsync());

        Assert.NotNull(download);
        Assert.True(Catalog.IsTrustedDownload(download.Value.Url), download.Value.Url);
    }

    // DriverUpdater.exe as built for this configuration, found from the repository root.
    static string AppExe()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DriverUpdater.sln")))
            directory = directory.Parent;
        Assert.NotNull(directory);
        var configuration = AppContext.BaseDirectory.Contains(@"\Release\") ? "Release" : "Debug";
        var exe = Path.Combine(directory.FullName, "src", "DriverUpdater", "bin", configuration, "net10.0-windows", "DriverUpdater.exe");
        Assert.True(File.Exists(exe), $"Introuvable : {exe}");
        return exe;
    }

    [Fact]
    public void Lists_batteries_without_failing() => SystemInfo.Batteries();

    [Fact]
    public void Reads_installed_driver_versions()
    {
        var drivers = SystemInfo.InstalledDrivers();

        Assert.NotEmpty(drivers);
        Assert.All(drivers.Values, d => Assert.False(string.IsNullOrEmpty(d.Version)));
    }

    [Theory]
    [InlineData("Microsoft.Update.Session")]
    [InlineData("Microsoft.Update.UpdateColl")]
    [InlineData("Schedule.Service")]
    public void Windows_components_exist(string progId) =>
        Assert.NotNull(Type.GetTypeFromProgID(progId));

    [Fact]
    public void Task_Scheduler_accepts_the_weekly_task()
    {
        var name = "Driver Updater test " + Guid.NewGuid().ToString("N");
        WeeklyCheck.Enable(@"C:\Program Files\Driver Updater\DriverUpdater.exe", name);
        try
        {
            Assert.True(WeeklyCheck.IsEnabled(name));
        }
        finally
        {
            WeeklyCheck.Disable(name);
        }
        Assert.False(WeeklyCheck.IsEnabled(name));
    }
}
