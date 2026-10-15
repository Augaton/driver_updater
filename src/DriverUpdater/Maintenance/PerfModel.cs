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
            ranked.FirstOrDefault() is { } app ? $" Le plus gourmand : {app.Name} ({amount(app)})." : "";

        if (system.Cpu >= 80)
            findings.Add($"Processeur saturé ({system.Cpu:0} %)." + Top(apps.OrderByDescending(a => a.Cpu), a => a.CpuText));
        if (system.MemoryPercent >= 85)
            findings.Add($"Mémoire presque pleine ({system.MemoryPercent:0} %) : Windows ralentit en écrivant sur le disque." +
                         Top(apps.OrderByDescending(a => a.Memory), a => a.MemoryText));
        if (system.DiskActive >= 90)
            findings.Add($"Disque occupé en permanence ({system.DiskActive:0} %)." +
                         Top(apps.OrderByDescending(a => a.DiskBytesPerSecond), a => a.DiskText));
        if (system.Gpu >= 90)
            findings.Add($"Carte graphique à pleine charge ({system.Gpu:0} %)." +
                         Top(apps.Where(a => a.Gpu is not null).OrderByDescending(a => a.Gpu), a => a.GpuText));
        if (system.PowerSaver)
            findings.Add($"Mode d'alimentation « {system.PowerPlan} » : il bride volontairement le processeur. " +
                         "Changez-le dans Réglages Windows utiles → Alimentation.");
        // Busy but running well under its rated speed: heat or power limits.
        if (system.Cpu >= 50 && system.CpuPerformance is < 60 && !system.PowerSaver)
            findings.Add($"Le processeur tourne à {system.CpuPerformance:0} % de sa vitesse alors qu'il est chargé : " +
                         "surchauffe (ventilation encrassée ?) ou limite d'alimentation (portable sur batterie ?).");
        if (findings.Count == 0)
            findings.Add("Rien d'anormal pendant la mesure. Si le PC rame à un moment précis, relancez l'analyse à ce moment-là.");
        return findings;
    }
}
