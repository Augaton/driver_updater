namespace DriverUpdater.Maintenance;

public sealed record AppUsage(string Name, double Cpu, long Memory, double DiskBytesPerSecond, double? Gpu, int Processes)
{
    public string CpuText => $"{Cpu:0.#} %";
    public string MemoryText => Bytes.Format(Memory);
    public string DiskText => DiskBytesPerSecond >= 1024 ? Bytes.Format((long)DiskBytesPerSecond) + "/s" : "—";
    public string GpuText => Gpu is { } gpu ? $"{gpu:0.#} %" : "—";
}

/// <param name="CpuPerformance">Speed relative to the processor's nominal frequency (100 = nominal, over 100 = boost).</param>
public sealed record SystemUsage(double Cpu, long MemoryUsed, long MemoryTotal, double DiskActive, double? Gpu,
    double? CpuPerformance, string PowerPlan, bool PowerSaver)
{
    public double MemoryPercent => MemoryTotal > 0 ? 100.0 * MemoryUsed / MemoryTotal : 0;
}

public sealed record PerfReport(SystemUsage System, List<AppUsage> Apps, List<string> Findings);

/// <summary>Plain-language findings from one measurement: what is wrong, and who causes it.</summary>
public static class PerfVerdict
{
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
            findings.Add(L.F("Disque occupé en permanence ({0:0} %).", system.DiskActive) +
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
