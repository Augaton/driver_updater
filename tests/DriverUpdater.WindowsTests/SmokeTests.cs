using DriverUpdater;
using Xunit;

// Nothing here installs or deletes anything on the machine, except one scheduled task
// that its own test creates and removes.
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
