using System.IO;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DriverUpdater;
using Xunit;

// Nothing here installs or deletes anything on the machine, except a scheduled task and a
// startup entry that their own tests create and remove.
public sealed class SmokeTests
{
    // XAML errors only surface at run time: build the real window, load what it can read
    // locally, and render every section to a PNG. The pipeline publishes them, so the look of
    // the app can be checked without a Windows PC at hand. One test for both: WPF allows a
    // single Application per process.
    [Fact]
    public void Main_window_loads_and_every_section_renders()
    {
        var folder = Path.Combine(RepoRoot(), "screenshots");
        Directory.CreateDirectory(folder);
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                // The captures in French, or in English when the pipeline asks (DRIVERUPDATER_LANG=en).
                var language = Environment.GetEnvironmentVariable("DRIVERUPDATER_LANG") ?? "fr";
                App.UseLanguage(language);
                var app = new App();
                app.InitializeComponent();
                app.ApplyTheme();
                var window = new MainWindow
                {
                    WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = 0,
                    ShowActivated = false, ShowInTaskbar = false,
                };
                window.Show();
                Settle((FrameworkElement)window.Content);
                // Opens on the dashboard, with each page's text style applied (not Windows' black).
                Assert.Same(window.HomeTab, window.Tabs.SelectedItem);
                Assert.Equal(((SolidColorBrush)app.Resources["Ink"]).Color, ((SolidColorBrush)window.HomeTab.Foreground).Color);
                Pump(window.LoadLocalData());

                // The menu lists every page once, in order, under its branch.
                var pages = window.Tabs.Items.OfType<TabItem>().Where(t => t.IsEnabled).ToList();
                var menu = window.Nav.Items.OfType<TreeViewItem>()
                    .SelectMany(i => i.Tag is TabItem ? [i] : i.Items.OfType<TreeViewItem>())
                    .Select(i => i.Tag).ToList();
                Assert.Equal(pages, menu);

                var root = (FrameworkElement)window.Content;
                var background = (Brush)app.Resources["Void"];
                int index = 0;
                foreach (var tab in pages.Where(t => t.Name.Length > 0))
                {
                    window.Tabs.SelectedItem = tab;
                    Settle(root);
                    Assert.Same(tab, (window.Nav.SelectedItem as TreeViewItem)?.Tag); // the menu follows the page
                    var prefix = language == "en" ? "en-" : "";
                    Save(Render(root, background), Path.Combine(folder, $"{prefix}{++index:00}-{tab.Name}.png"));
                    AssertAccessible(tab);
                    if (tab.Content is ScrollViewer { ScrollableHeight: > 0 } scroller)
                    {
                        scroller.ScrollToEnd();
                        Settle(root);
                        Save(Render(root, background), Path.Combine(folder, $"{prefix}{index:00}-{tab.Name}-fin.png"));
                        scroller.ScrollToHome();
                    }
                }
                DriveLikeAUser(window, root);
                window.Close();
            }
            catch (Exception e)
            {
                error = e;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromMinutes(4)), "Le rendu des sections n'a pas fini à temps.");

        Assert.Null(error);
        Assert.NotEmpty(Directory.GetFiles(folder, "*.png"));
    }

    // Runs the dispatcher until the task is done: its continuations need the UI thread.
    static void Pump(Task task)
    {
        var frame = new DispatcherFrame();
        task.ContinueWith(_ => frame.Continue = false, TaskScheduler.Default);
        Dispatcher.PushFrame(frame);
        task.GetAwaiter().GetResult();
    }

    // Every button, field and choice on the page has a name a screen reader can say.
    static void AssertAccessible(TabItem page)
    {
        // Parts of standard controls (scroll bar arrows, column headers, the list of a combo box)
        // are named by Windows itself; what this app's own XAML and templates put on the page is
        // checked.
        foreach (var element in Descendants((DependencyObject)page.Content).OfType<Control>()
                     .Where(c => c.IsVisible && c is ButtonBase or TextBox or ComboBox
                                 && c is not DataGridColumnHeader && c.TemplatedParent is null or ContentPresenter))
        {
            var peer = UIElementAutomationPeer.CreatePeerForElement(element);
            Assert.False(string.IsNullOrWhiteSpace(peer?.GetName()),
                $"Page « {page.Header} » : un {element.GetType().Name} sans nom accessible ({(element as ContentControl)?.Content}).");
        }
    }

    static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var deeper in Descendants(child)) yield return deeper;
        }
    }

    // Through UI Automation, as Narrator or a test tool would: the menu opens every page, a
    // branch closes and opens, and the dashboard's tiles lead where they say.
    static void DriveLikeAUser(MainWindow window, FrameworkElement root)
    {
        foreach (var item in window.Nav.Items.OfType<TreeViewItem>()
                     .SelectMany(i => i.Tag is TabItem ? [i] : i.Items.OfType<TreeViewItem>()))
        {
            item.IsSelected = true;
            Settle(root);
            Assert.Same(item.Tag, window.Tabs.SelectedItem);
        }

        var branch = window.Nav.Items.OfType<TreeViewItem>().First(i => i.Tag is null);
        var page = (TreeViewItem)branch.Items[0]!;
        var expander = (IExpandCollapseProvider)UIElementAutomationPeer.CreatePeerForElement(branch).GetPattern(PatternInterface.ExpandCollapse)!;
        expander.Collapse();
        Settle(root);
        Assert.False(page.IsVisible);
        expander.Expand();
        Settle(root);
        Assert.True(page.IsVisible);

        window.Tabs.SelectedItem = window.HomeTab;
        Settle(root);
        foreach (var tile in Descendants((DependencyObject)window.HomeTab.Content).OfType<Button>().Where(b => b.Tag is string { Length: > 0 }))
        {
            window.Tabs.SelectedItem = window.HomeTab;
            Settle(root);
            ((IInvokeProvider)UIElementAutomationPeer.CreatePeerForElement(tile).GetPattern(PatternInterface.Invoke)!).Invoke();
            Settle(root);
            Assert.Same(window.FindName((string)tile.Tag), window.Tabs.SelectedItem);
        }
    }

    // Layout, then everything the dispatcher queued behind it (bindings, Loaded, column
    // sizing), then the layout that work asked for.
    static void Settle(UIElement root)
    {
        for (int pass = 0; pass < 3; pass++)
        {
            root.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        }
    }

    // The window's content as it is on screen, clipped to the window like the user sees it.
    static RenderTargetBitmap Render(FrameworkElement root, Brush background)
    {
        var size = new Size(root.ActualWidth, root.ActualHeight);
        var backdrop = new DrawingVisual();
        using (var drawing = backdrop.RenderOpen())
            drawing.DrawRectangle(background, null, new Rect(size));
        var bitmap = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(backdrop);
        bitmap.Render(root);
        return bitmap;
    }

    static void Save(BitmapSource image, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DriverUpdater.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? AppContext.BaseDirectory;
    }

    [Fact]
    public void Lists_disks()
    {
        var disks = SystemInfo.Disks();

        Assert.NotEmpty(disks);
        Assert.All(disks, d => Assert.False(string.IsNullOrEmpty(d.Health)));
    }


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
    public void Takes_the_inventory_of_every_device()
    {
        var devices = DriverInventory.Scan();

        Assert.Contains(devices, d => d.HasDriver);
        CatalogScope.Select(devices); // must not fail on real hardware IDs
    }

    // Reads the real driver store through Get-WindowsDriver (JSON), deletes nothing.
    [Fact]
    public async Task Reads_the_driver_store() => await DriverStore.RemovableAsync(_ => { });

    [Fact]
    public void Watches_devices_without_failing() => new DeviceWatcher().Dispose();

    [Fact]
    public void Reports_on_every_device()
    {
        var devices = DriverInventory.Scan();

        var report = DriverReport.Build(devices, [], new Dictionary<string, DriverOffer>());

        Assert.Equal(devices.Count, report.Count);
        Assert.DoesNotContain(report, r => r.Selected); // nothing on offer, nothing ticked
    }

    [Fact]
    public void Detects_game_components_from_installed_programs()
    {
        var names = InstalledPrograms.List().Select(p => p.Name).ToList();

        foreach (var component in GameComponent.All) component.IsIn(names); // must not fail on real names
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
        var drivers = InstalledDrivers.ByHardwareId(DriverInventory.Scan());

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

    // ---- "PC de guerre" and security -------------------------------------------------------

    [Fact]
    public void Reads_the_state_of_every_tweak_without_changing_it()
    {
        foreach (var tweak in TweakCatalog.All)
        {
            Tuner.State(tweak);
            Tuner.CanUndo(tweak);
        }
    }

    // The real registry, as the app uses it: undoing puts back exactly what was there.
    [Fact]
    public void Applies_and_undoes_a_user_tweak_exactly()
    {
        var tweak = TweakCatalog.Find("advertising-id-off")!;
        var registry = new WindowsRegistry();
        var before = tweak.Values.Select(v => registry.Get(v.Hive, v.Key, v.Name)).ToList();
        Tuner.Apply(tweak);
        Assert.Equal(TweakState.Applied, Tuner.State(tweak));
        Assert.True(Tuner.CanUndo(tweak));
        Tuner.Undo(tweak);
        Assert.Equal(before, tweak.Values.Select(v => registry.Get(v.Hive, v.Key, v.Name)).ToList());
        Assert.False(Tuner.CanUndo(tweak));
    }

    [Fact]
    public void Runs_the_hardware_checks() => Assert.NotEmpty(PcChecks.Run());

    [Fact]
    public void Lists_screens_without_failing() => Displays.Screens();

    [Fact]
    public void Looks_for_steam_games_without_failing() => SteamGames.Find();

    // What was checked is what runs: nobody can rewrite, rename or delete the installer meanwhile.
    [Fact]
    public void A_checked_installer_cannot_be_swapped_before_it_starts()
    {
        var folder = Directory.CreateTempSubdirectory("DriverUpdater-test-");
        var path = Path.Combine(folder.FullName, "setup.exe");
        File.WriteAllText(path, "checked");
        try
        {
            using (new VerifiedInstaller(path, new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)))
            {
                Assert.Throws<IOException>(() => File.WriteAllText(path, "swapped"));
                Assert.Throws<IOException>(() => File.Move(path, path + ".old"));
                Assert.Throws<IOException>(() => File.Delete(path));
                Assert.Equal("checked", File.ReadAllText(path));
            }
            File.Delete(path);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    // What keeps the administrator helper from being steered by a planted link.
    [Fact]
    public void Refuses_folders_swapped_for_links()
    {
        // Next to the tests, not in %TEMP%: its path may hold short 8.3 names (RUNNER~1).
        var root = Directory.CreateDirectory(Path.Combine(AppContext.BaseDirectory, "links-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var real = Directory.CreateDirectory(Path.Combine(root, "real", "sub")).FullName;
            Assert.NotNull(NativeFiles.GenuineFolder(real));
            using (NativeFiles.PinParents(root, real))
                Assert.Throws<IOException>(() => Directory.Move(Path.Combine(root, "real"), Path.Combine(root, "moved")));
            Directory.Move(Path.Combine(root, "real"), Path.Combine(root, "moved")); // free again once unpinned
            Directory.Move(Path.Combine(root, "moved"), Path.Combine(root, "real"));

            var link = Path.Combine(root, "link");
            Directory.CreateSymbolicLink(link, Path.Combine(root, "real"));
            Assert.Null(NativeFiles.GenuineFolder(link));
            Assert.Throws<InvalidOperationException>(() => NativeFiles.PinParents(root, Path.Combine(link, "sub", "x")));
            Assert.ThrowsAny<Exception>(() => NativeFiles.CreateNewFolder(real));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    // Old installers go (an NVIDIA driver is close to 1 GB); anything else stays.
    [Fact]
    public void Cleans_only_its_own_old_downloads()
    {
        var temp = Path.GetTempPath();
        var old = Directory.CreateDirectory(Path.Combine(temp, "DriverUpdater-" + Guid.NewGuid().ToString("N")));
        var recent = Directory.CreateDirectory(Path.Combine(temp, "DriverUpdater-" + Guid.NewGuid().ToString("N")));
        var other = Directory.CreateDirectory(Path.Combine(temp, "DriverUpdater-keep-" + Guid.NewGuid().ToString("N")));
        try
        {
            File.WriteAllText(Path.Combine(old.FullName, "setup.exe"), "old");
            Directory.SetCreationTimeUtc(old.FullName, DateTime.UtcNow.AddDays(-3));
            Directory.SetCreationTimeUtc(other.FullName, DateTime.UtcNow.AddDays(-3));

            UpdateCheck.CleanOldDownloads();

            Assert.False(old.Exists && Directory.Exists(old.FullName));
            Assert.True(Directory.Exists(recent.FullName));
            Assert.True(Directory.Exists(other.FullName));
        }
        finally
        {
            foreach (var folder in new[] { old, recent, other })
                if (Directory.Exists(folder.FullName)) folder.Delete(recursive: true);
        }
    }

    [Fact]
    public void Reads_the_machine_facts_the_gaming_checks_need()
    {
        Machine.IsDualDieX3D();
        Machine.GameBarInstalled();
        Assert.NotNull(Machine.ActivePowerPlan());
        Machine.OnHardDisk(Environment.SystemDirectory); // null is fine: virtual disks may not say
        Displays.Adapters();
    }

    [Fact]
    public void Checks_signatures_like_windows_does()
    {
        // dotnet.exe carries Microsoft's own Authenticode signature (certificate ".NET", organization
        // Microsoft Corporation): the strict check wants both names to be the company's.
        var signed = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe");
        using (var file = new FileStream(signed, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (var signer = Authenticode.Signer(file.SafeFileHandle, signed))
        {
            Assert.NotNull(signer);
            Assert.Contains("O=Microsoft Corporation", signer.Subject);
            Assert.False(Authenticode.IsSignedBy(signer, "NVIDIA Corporation"));
        }

        var unsigned = Path.Combine(Directory.CreateTempSubdirectory("DriverUpdater-test-").FullName, "setup.exe");
        File.WriteAllBytes(unsigned, [0x4D, 0x5A, 1, 2, 3]);
        using (var file = new FileStream(unsigned, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.Null(Authenticode.Signer(file.SafeFileHandle, unsigned));
    }

    // NVIDIA's lookup is undocumented: this fails the day it changes, before users notice.
    [Fact]
    public async Task Nvidia_still_answers_for_a_current_card()
    {
        var card = new DeviceDriver
        {
            DeviceId = @"PCI\VEN_10DE&DEV_2684\TEST", Name = "NVIDIA GeForce RTX 4090", Class = "Display", Provider = "NVIDIA",
            Version = "32.0.15.6094", Date = null, Signed = true, Problem = 0, InfName = "oem1.inf", HardwareIds = [@"PCI\VEN_10DE&DEV_2684"],
        };
        var found = await NvidiaDrivers.LatestAsync([card], laptop: false);

        var release = Assert.Single(found).Value;
        Assert.True(Version.TryParse(release.Version, out var version) && version.Major >= 500);
        Assert.True(NvidiaModel.IsTrustedDownload(release.DownloadUrl));
    }

    [Fact]
    public void Reads_the_security_status()
    {
        var state = SecurityStatus.Read(askWindowsUpdate: false);
        Assert.NotNull(state.FirewallOn);
        Assert.Null(state.PendingUpdates);
    }
}
