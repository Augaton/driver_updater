using Xunit;

public sealed class CommandLineTests
{
    [Theory]
    [InlineData("\"C:\\Program Files\\App\\app.exe\" --minimized", "C:\\Program Files\\App\\app.exe", "--minimized")]
    [InlineData("C:\\Program Files\\App\\uninstall.exe /S", "C:\\Program Files\\App\\uninstall.exe", "/S")]
    [InlineData("MsiExec.exe /X{1234}", "MsiExec.exe", "/X{1234}")]
    [InlineData("\"C:\\App\\app.exe\"", "C:\\App\\app.exe", "")]
    public void Splits_program_and_arguments(string command, string program, string arguments) =>
        Assert.Equal((program, arguments), CommandLine.Split(command));
}

public sealed class StartupApprovalTests
{
    [Fact]
    public void A_missing_value_means_enabled() => Assert.True(StartupApproval.IsEnabled(null));

    [Theory]
    [InlineData(0x02, true)]
    [InlineData(0x06, true)]
    [InlineData(0x03, false)]
    [InlineData(0x07, false)]
    public void Reads_task_manager_values(byte first, bool enabled) =>
        Assert.Equal(enabled, StartupApproval.IsEnabled([first, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]));

    [Fact]
    public void Writes_values_task_manager_reads_back()
    {
        var when = new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);
        var off = StartupApproval.Value(false, when);

        Assert.False(StartupApproval.IsEnabled(off));
        Assert.Equal(when.ToFileTimeUtc(), BitConverter.ToInt64(off, 4));
        Assert.True(StartupApproval.IsEnabled(StartupApproval.Value(true, when)));
    }
}

public sealed class PerfVerdictTests
{
    static SystemUsage Calm => new(Cpu: 10, MemoryUsed: 4L << 30, MemoryTotal: 16L << 30, DiskActive: 5, Gpu: 3,
        CpuPerformance: 100, PowerPlan: "Utilisation normale", PowerSaver: false);

    static readonly AppUsage[] Apps =
    [
        new("Chrome", Cpu: 12, Memory: 3L << 30, DiskBytesPerSecond: 0, Gpu: 4, Processes: 30),
        new("Teams", Cpu: 70, Memory: 1L << 30, DiskBytesPerSecond: 5e6, Gpu: null, Processes: 6),
    ];

    [Fact]
    public void Says_so_when_nothing_is_wrong() => Assert.Single(PerfVerdict.Findings(Calm, Apps), f => f.StartsWith("Rien d'anormal"));

    [Fact]
    public void Names_the_top_consumer_of_a_saturated_resource()
    {
        var findings = PerfVerdict.Findings(Calm with { Cpu = 95, MemoryUsed = 15L << 30 }, Apps);

        Assert.Contains(findings, f => f.StartsWith("Processeur saturé") && f.Contains("Teams"));
        Assert.Contains(findings, f => f.StartsWith("Mémoire presque pleine") && f.Contains("Chrome"));
    }

    [Fact]
    public void Points_at_power_saving_and_throttling()
    {
        Assert.Contains(PerfVerdict.Findings(Calm with { PowerSaver = true, PowerPlan = "Économie d'énergie" }, Apps),
            f => f.Contains("Économie d'énergie"));
        Assert.Contains(PerfVerdict.Findings(Calm with { Cpu = 60, CpuPerformance = 40 }, Apps),
            f => f.Contains("40 % de sa vitesse"));
    }
}

public sealed class BootEventsTests
{
    const string Ns = "http://schemas.microsoft.com/win/2004/08/events/event";

    static string Event(int id, string time, params (string Name, string Value)[] data) =>
        $"<Event xmlns='{Ns}'><System><EventID>{id}</EventID><TimeCreated SystemTime='{time}'/></System><EventData>" +
        string.Concat(data.Select(d => $"<Data Name='{d.Name}'>{d.Value}</Data>")) + "</EventData></Event>";

    [Fact]
    public void Reads_boot_times_newest_first_and_ranks_slowdowns()
    {
        var report = BootEvents.Parse(
        [
            Event(100, "2026-10-01T08:00:00.000Z", ("BootTime", "42000"), ("MainPathBootTime", "15000"), ("BootPostBootTime", "27000")),
            Event(100, "2026-10-14T08:00:00.000Z", ("BootTime", "38000"), ("MainPathBootTime", "14000"), ("BootPostBootTime", "24000")),
            Event(101, "2026-10-14T08:00:00.000Z", ("Name", "OneDrive.exe"), ("FriendlyName", "Microsoft OneDrive"), ("DegradationTime", "3000")),
            Event(101, "2026-10-01T08:00:00.000Z", ("Name", "onedrive.exe"), ("FriendlyName", "Microsoft OneDrive"), ("DegradationTime", "5000")),
            Event(103, "2026-10-01T08:00:00.000Z", ("Name", "Spooler"), ("FriendlyName", ""), ("DegradationTime", "1000")),
            "<not xml",
        ]);

        Assert.Equal([38.0, 42.0], report.Boots.Select(b => b.Seconds));
        var onedrive = report.Slowdowns[0];
        Assert.Equal(("Microsoft OneDrive", "Application", 2, 4.0), (onedrive.FriendlyName, onedrive.Kind, onedrive.Times, onedrive.AverageSeconds));
        Assert.Equal(("Spooler", "Service"), (report.Slowdowns[1].FriendlyName, report.Slowdowns[1].Kind));
    }
}
