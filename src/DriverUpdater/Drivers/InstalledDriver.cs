namespace DriverUpdater.Drivers;

/// <summary>A driver's version and date, as shown everywhere: "32.0.15.6094 (13/08/2024)".</summary>
public sealed record InstalledDriver(string Version, DateTime? Date)
{
    public override string ToString() => Date is { } date ? $"{Version} ({date:dd/MM/yyyy})" : Version;
}
