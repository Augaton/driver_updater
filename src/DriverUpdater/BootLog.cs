using System.Diagnostics.Eventing.Reader;

namespace DriverUpdater;

/// <summary>Reads the last 30 days of Windows' own start-up measurements (see <see cref="BootEvents"/>).</summary>
public static class BootLog
{
    const string Query = "*[System[(EventID=100 or EventID=101 or EventID=102 or EventID=103) " +
                         "and TimeCreated[timediff(@SystemTime) <= 2592000000]]]"; // 30 days, in ms

    /// <exception cref="UnauthorizedAccessException">The log is only readable as administrator on many PCs.</exception>
    public static BootReport Read()
    {
        try
        {
            using var reader = new EventLogReader(new EventLogQuery(BootEvents.Log, PathType.LogName, Query) { ReverseDirection = true });
            var events = new List<string>();
            for (var record = reader.ReadEvent(); record is not null && events.Count < 1000; record = reader.ReadEvent())
                using (record)
                    events.Add(record.ToXml());
            return BootEvents.Parse(events);
        }
        catch (EventLogNotFoundException)
        {
            return new BootReport([], []);
        }
    }
}
