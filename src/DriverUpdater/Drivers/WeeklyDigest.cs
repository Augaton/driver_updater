namespace DriverUpdater.Drivers;

/// <summary>What the weekly check found, and whether any of it was not told about yet.</summary>
/// <param name="Nvidia">The newer NVIDIA driver's version, if there is one.</param>
public sealed record WeeklyNews(int Drivers, string? Nvidia, int Software, bool HasNew)
{
    public string Message => string.Join(", ", new[]
    {
        Drivers > 0 ? L.F("{0} pilote(s) Windows Update", Drivers) : null,
        Nvidia is not null ? L.F("pilote NVIDIA {0}", Nvidia) : null,
        Software > 0 ? L.F("{0} logiciel(s)", Software) : null,
    }.OfType<string>());
}

/// <summary>Tells once about each thing found: a driver, an NVIDIA version, a software version.</summary>
public static class WeeklyDigest
{
    public static WeeklyNews Compute(IReadOnlyCollection<string> drivers, IReadOnlySet<string> notifiedDrivers,
        string? nvidia, string? notifiedNvidia,
        IReadOnlyCollection<string> software, IReadOnlySet<string> notifiedSoftware)
    {
        bool hasNew = drivers.Any(d => !notifiedDrivers.Contains(d))
                      || nvidia is not null && nvidia != notifiedNvidia
                      || software.Any(s => !notifiedSoftware.Contains(s));
        return new WeeklyNews(drivers.Count, nvidia, software.Count, hasNew);
    }
}
