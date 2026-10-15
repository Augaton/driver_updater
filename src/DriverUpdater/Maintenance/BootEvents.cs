using System.Globalization;
using System.Xml.Linq;

namespace DriverUpdater.Maintenance;

public sealed record BootEntry(DateTime Date, double Seconds, double MainPathSeconds, double PostBootSeconds);

/// <param name="Name">File or service name as Windows logs it, e.g. "OneDrive.exe".</param>
public sealed record Slowdown(string Name, string FriendlyName, string Kind, int Times, double AverageSeconds)
{
    public string AverageText => $"+{AverageSeconds:0.0} s";
}

public sealed record BootReport(List<BootEntry> Boots, List<Slowdown> Slowdowns);

/// <summary>
/// Reads the events Windows itself writes about each start-up
/// (Microsoft-Windows-Diagnostics-Performance): how long it took, and which application,
/// driver or service slowed it down, measured, not guessed.
/// </summary>
public static class BootEvents
{
    public const string Log = "Microsoft-Windows-Diagnostics-Performance/Operational";
    const int BootDone = 100, AppSlow = 101, DriverSlow = 102, ServiceSlow = 103;

    public static BootReport Parse(IEnumerable<string> eventXmls)
    {
        var boots = new List<BootEntry>();
        var slow = new List<(string Name, string Friendly, string Kind, double Seconds)>();
        foreach (var xml in eventXmls)
        {
            XElement root;
            try
            {
                root = XElement.Parse(xml);
            }
            catch (System.Xml.XmlException)
            {
                continue;
            }
            var ns = root.Name.Namespace;
            var system = root.Element(ns + "System");
            if (!int.TryParse(system?.Element(ns + "EventID")?.Value, out int id)) continue;
            var data = root.Element(ns + "EventData")?.Elements(ns + "Data")
                .Where(d => d.Attribute("Name") is not null)
                .GroupBy(d => (string)d.Attribute("Name")!)
                .ToDictionary(g => g.Key, g => g.First().Value) ?? [];
            double Seconds(string field) =>
                data.TryGetValue(field, out var ms) && double.TryParse(ms, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                    ? value / 1000 : 0;

            if (id == BootDone)
            {
                var when = DateTime.TryParse((string?)system?.Element(ns + "TimeCreated")?.Attribute("SystemTime"),
                    CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var utc) ? utc.ToLocalTime() : DateTime.MinValue;
                boots.Add(new BootEntry(when, Seconds("BootTime"), Seconds("MainPathBootTime"), Seconds("BootPostBootTime")));
            }
            else if (id is AppSlow or DriverSlow or ServiceSlow && data.TryGetValue("Name", out var name) && name.Length > 0)
            {
                var kind = id switch { AppSlow => "Application", DriverSlow => "Pilote", _ => "Service" };
                slow.Add((name, data.GetValueOrDefault("FriendlyName") is { Length: > 0 } friendly ? friendly : name, kind, Seconds("DegradationTime")));
            }
        }

        var slowdowns = slow
            .GroupBy(s => (s.Name.ToLowerInvariant(), s.Kind))
            .Select(g => new Slowdown(g.First().Name, g.First().Friendly, g.Key.Kind, g.Count(), g.Average(s => s.Seconds)))
            .OrderByDescending(s => s.AverageSeconds * s.Times)
            .ToList();
        return new BootReport(boots.OrderByDescending(b => b.Date).ToList(), slowdowns);
    }
}
