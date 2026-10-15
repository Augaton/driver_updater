using DriverUpdater;
using Xunit;

// Nothing here installs, deletes or creates anything on the machine.
public sealed class SmokeTests
{
    [Fact]
    public void Main_window_and_its_resources_load()
    {
        // XAML errors only surface at run time, so build the real window on a UI thread.
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                new App().InitializeComponent();
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
    public void Reads_installed_driver_versions()
    {
        var drivers = SystemInfo.InstalledDrivers();

        Assert.NotEmpty(drivers);
        Assert.All(drivers.Values, d => Assert.False(string.IsNullOrEmpty(d.Version)));
    }

    [Theory]
    [InlineData("Microsoft.Update.Session")]
    [InlineData("Microsoft.Update.UpdateColl")]
    public void Windows_Update_components_exist(string progId) =>
        Assert.NotNull(Type.GetTypeFromProgID(progId));
}
