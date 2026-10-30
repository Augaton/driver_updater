namespace DriverUpdater.Maintenance;

public sealed record AppUsage(string Name, double Cpu, long Memory, double DiskBytesPerSecond, double? Gpu, int Processes)
{
    public string CpuText => $"{Cpu:0.#} %";
    public string MemoryText => Bytes.Format(Memory);
    public string DiskText => DiskBytesPerSecond >= 1024 ? Bytes.Format((long)DiskBytesPerSecond) + "/s" : "—";
    public string GpuText => Gpu is { } gpu ? $"{gpu:0.#} %" : "—";
}

/// <param name="CpuPerformance">Speed relative to the processor's nominal frequency (100 = nominal, over 100 = boost).</param>
/// <param name="DiskActive">The busiest disk's active time: the average of all would hide one at 100 %.</param>
/// <param name="Disk">Which one it is ("C:"), when there are several.</param>
public sealed record SystemUsage(double Cpu, long MemoryUsed, long MemoryTotal, double DiskActive, double? Gpu,
    double? CpuPerformance, string PowerPlan, bool PowerSaver, string? Disk = null)
{
    public double MemoryPercent => MemoryTotal > 0 ? 100.0 * MemoryUsed / MemoryTotal : 0;
}

public sealed record PerfReport(SystemUsage System, List<AppUsage> Apps, List<string> Findings);

/// <summary>Plain-language findings from one measurement: what is wrong, and who causes it.</summary>
public static class PerfVerdict
{
    /// <summary>
    /// The busiest physical disk, from each disk's active time by its counter instance ("1 D:"),
    /// named by its letters ("D:"), or by its number when it has none. Null without any disk.
    /// </summary>
    public static (string Name, double Active)? BusiestDisk(IReadOnlyDictionary<string, double> activeByInstance)
    {
        var busiest = activeByInstance.OrderByDescending(d => d.Value).FirstOrDefault();
        if (busiest.Key is null) return null;
        var parts = busiest.Key.Split(' ', 2);
        return (parts.Length == 2 ? parts[1] : L.F("n° {0}", parts[0]), Math.Clamp(busiest.Value, 0, 100));
    }

    public static List<string> Findings(SystemUsage system, IReadOnlyList<AppUsage> apps)
    {
        var findings = new List<string>();
        static string Top(IEnumerable<AppUsage> ranked, Func<AppUsage, string> amount) =>
            ranked.FirstOrDefault() is { } app ? L.F(" Le plus gourmand : {0} ({1}).", app.Name, amount(app)) : "";

        if (system.Cpu >= 80)
            findings.Add(L.F("Processeur saturé ({0:0} %).", system.Cpu) + Top(apps.OrderByDescending(a => a.Cpu), a => a.CpuText));
        if (system.MemoryPercent >= 85)
            findings.Add(L.F("Mémoire presque pleine ({0:0} %) : Windows ralentit en écrivant sur le disque.", system.MemoryPercent) +
                         Top(apps.OrderByDescending(a => a.Memory), a => a.MemoryText));
        if (system.DiskActive >= 90)
            findings.Add((system.Disk is { } disk ? L.F("Disque {0} occupé en permanence ({1:0} %).", disk, system.DiskActive)
                             : L.F("Disque occupé en permanence ({0:0} %).", system.DiskActive)) +
                         Top(apps.OrderByDescending(a => a.DiskBytesPerSecond), a => a.DiskText));
        if (system.Gpu >= 90)
            findings.Add(L.F("Carte graphique à pleine charge ({0:0} %).", system.Gpu) +
                         Top(apps.Where(a => a.Gpu is not null).OrderByDescending(a => a.Gpu), a => a.GpuText));
        if (system.PowerSaver)
            findings.Add(L.F("Mode d'alimentation « {0} » : il bride volontairement le processeur. ", system.PowerPlan) +
                         L.T("Changez-le dans les paramètres de Windows → Alimentation."));
        // Busy but running well under its rated speed: heat or power limits.
        if (system.Cpu >= 50 && system.CpuPerformance is < 60 && !system.PowerSaver)
            findings.Add(L.F("Le processeur tourne à {0:0} % de sa vitesse alors qu'il est chargé : ", system.CpuPerformance) +
                         L.T("surchauffe (ventilation encrassée ?) ou limite d'alimentation (portable sur batterie ?)."));
        if (findings.Count == 0)
            findings.Add(L.T("Rien d'anormal pendant la mesure. Si le PC rame à un moment précis, relancez l'analyse à ce moment-là."));
        return findings;
    }
}
