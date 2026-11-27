namespace DriverUpdater.Drivers;

/// <summary>The page a click on the weekly notification opens.</summary>
public enum WeeklyPage { Drivers, Nvidia, Software }

/// <summary>What the weekly check found, and whether any of it was not told about yet.</summary>
/// <param name="Nvidia">The newer NVIDIA driver's version, if there is one.</param>
/// <param name="Page">Where the news is: the first kind with something not told about yet.</param>
public sealed record WeeklyNews(int Drivers, string? Nvidia, int Software, bool HasNew, WeeklyPage Page = WeeklyPage.Drivers)
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
        bool newDrivers = drivers.Any(d => !notifiedDrivers.Contains(d));
        bool newNvidia = nvidia is not null && nvidia != notifiedNvidia;
        bool newSoftware = software.Any(s => !notifiedSoftware.Contains(s));
        // A driver already told about must not send the click away from the new program it is about.
        var page = newDrivers ? WeeklyPage.Drivers : newNvidia ? WeeklyPage.Nvidia : WeeklyPage.Software;
        return new WeeklyNews(drivers.Count, nvidia, software.Count, newDrivers || newNvidia || newSoftware, page);
    }
}
