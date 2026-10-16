using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace DriverUpdater.Maintenance;

/// <summary>
/// Measures what uses the PC for a few seconds, from Windows' performance counters (the
/// ones Task Manager and Resource Monitor read). Needs no administrator rights.
/// </summary>
public static partial class PerfAnalyzer
{
    const string PowerSaverPlan = "a1841308-3541-4fab-bc81-f71556f20b4a";
    const int AppsShown = 40;

    sealed record ProcessSample(string Name, CounterSample Cpu, long Memory, CounterSample Io);

    /// <summary>Everything read at one instant; two of them, some seconds apart, give the rates.</summary>
    sealed record Snapshot(CounterSample Cpu, CounterSample? CpuUtility, CounterSample DiskIdle, CounterSample? CpuSpeed,
        Dictionary<int, ProcessSample> Processes, Dictionary<string, CounterSample>? Gpu);

    public static async Task<PerfReport> MeasureAsync(TimeSpan duration, IProgress<double> progress)
    {
        // Counters are slow to read: all of it runs off the window's thread.
        var before = await Task.Run(Take);
        const int steps = 20;
        for (int i = 1; i <= steps; i++)
        {
            await Task.Delay(duration / steps);
            progress.Report((double)i / steps);
        }
        var after = await Task.Run(Take);
        return await Task.Run(() => Report(before, after));
    }

    static Snapshot Take()
    {
        using var cpu = new PerformanceCounter("Processor", "% Processor Time", "_Total");
        // What Task Manager shows: time weighted by the actual frequency, so turbo counts.
        using var utility = Counter("Processor Information", "% Processor Utility", "_Total");
        using var disk = new PerformanceCounter("PhysicalDisk", "% Idle Time", "_Total");
        using var speed = Counter("Processor Information", "% Processor Performance", "_Total");
        return new Snapshot(cpu.NextSample(), utility?.NextSample(), disk.NextSample(), speed?.NextSample(), Processes(), GpuEngines());
    }

    static PerfReport Report(Snapshot before, Snapshot after)
    {
        int cores = Environment.ProcessorCount;
        // Apps only have "time" counters: they get the same turbo factor as the total, as in
        // Task Manager, so the list adds up to the gauge.
        double time = CounterSample.Calculate(before.Cpu, after.Cpu);
        double? utility = before.CpuUtility is { } u0 && after.CpuUtility is { } u1 ? CounterSample.Calculate(u0, u1) : null;
        double turbo = utility is { } u && time > 0.5 ? u / time : 1;
        var perProcess = after.Processes
            .Where(p => before.Processes.ContainsKey(p.Key))
            .Select(p => (Pid: p.Key, p.Value.Name,
                Cpu: Math.Min(100, CounterSample.Calculate(before.Processes[p.Key].Cpu, p.Value.Cpu) / cores * turbo),
                p.Value.Memory,
                Io: CounterSample.Calculate(before.Processes[p.Key].Io, p.Value.Io)))
            .ToList();
        var gpuByPid = GpuByProcess(before.Gpu, after.Gpu);

        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var apps = perProcess
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Group: g, Cpu: g.Sum(p => p.Cpu), Memory: g.Sum(p => p.Memory)))
            .OrderByDescending(a => a.Cpu).ThenByDescending(a => a.Memory)
            .Take(AppsShown) // only the shown apps get their friendly name looked up
            .Select(a => new AppUsage(
                FriendlyName(a.Group.Key, a.Group.Select(p => p.Pid), names),
                a.Cpu,
                a.Memory,
                a.Group.Sum(p => p.Io),
                gpuByPid is null ? null : Math.Min(100, a.Group.Sum(p => gpuByPid.GetValueOrDefault(p.Pid))),
                a.Group.Count()))
            .ToList();

        var memory = new Microsoft.VisualBasic.Devices.ComputerInfo();
        long total = (long)memory.TotalPhysicalMemory;
        var (plan, powerSaver) = PowerPlan();
        var system = new SystemUsage(
            Math.Clamp(utility ?? time, 0, 100),
            total - (long)memory.AvailablePhysicalMemory,
            total,
            Math.Clamp(100 - CounterSample.Calculate(before.DiskIdle, after.DiskIdle), 0, 100),
            before.Gpu is null || after.Gpu is null ? null : Math.Min(100, GpuTotal(before.Gpu, after.Gpu)),
            before.CpuSpeed is { } s0 && after.CpuSpeed is { } s1 ? CounterSample.Calculate(s0, s1) : null,
            plan,
            powerSaver);
        return new PerfReport(system, apps, PerfVerdict.Findings(system, apps));
    }

    /// <summary>All processes at once, keyed by process ID: instance names ("chrome#3") shift as processes come and go.</summary>
    static Dictionary<int, ProcessSample> Processes()
    {
        var data = new PerformanceCounterCategory("Process").ReadCategory();
        var result = new Dictionary<int, ProcessSample>();
        foreach (InstanceData id in data["ID Process"].Values)
        {
            int pid = (int)id.RawValue;
            if (pid == 0 || id.InstanceName is "_Total" or "Idle") continue;
            // A process can start or end while the counters are read: skip one seen only in part.
            if (data["% Processor Time"][id.InstanceName] is not { } cpu || data["IO Data Bytes/sec"][id.InstanceName] is not { } io)
                continue;
            result[pid] = new ProcessSample(id.InstanceName.Split('#')[0], cpu.Sample,
                data["Working Set - Private"][id.InstanceName]?.RawValue ?? 0, io.Sample);
        }
        return result;
    }

    /// <summary>GPU engines ("pid_1234_…_engtype_3D"), or null where Windows has no GPU counters.</summary>
    static Dictionary<string, CounterSample>? GpuEngines()
    {
        try
        {
            return new PerformanceCounterCategory("GPU Engine").ReadCategory()["Utilization Percentage"]
                .Values.Cast<InstanceData>().ToDictionary(d => d.InstanceName, d => d.Sample);
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    // As Task Manager does: a process's GPU use is its busiest engine type.
    static Dictionary<int, double>? GpuByProcess(Dictionary<string, CounterSample>? before, Dictionary<string, CounterSample>? after)
    {
        if (before is null || after is null) return null;
        return Engines(before, after)
            .GroupBy(e => e.Pid)
            .ToDictionary(g => g.Key, g => g.GroupBy(e => e.Type).Max(t => t.Sum(e => e.Value)));
    }

    static double GpuTotal(Dictionary<string, CounterSample> before, Dictionary<string, CounterSample> after) =>
        Engines(before, after).GroupBy(e => e.Type).Select(t => t.Sum(e => e.Value)).DefaultIfEmpty(0).Max();

    static IEnumerable<(int Pid, string Type, double Value)> Engines(Dictionary<string, CounterSample> before, Dictionary<string, CounterSample> after)
    {
        foreach (var (instance, sample) in after)
        {
            var match = GpuInstance().Match(instance);
            if (match.Success && before.TryGetValue(instance, out var earlier))
                yield return (int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), match.Groups[2].Value,
                    CounterSample.Calculate(earlier, sample));
        }
    }

    // "Google Chrome" rather than "chrome": the description inside the exe, read once per name.
    static string FriendlyName(string processName, IEnumerable<int> pids, Dictionary<string, string> cache)
    {
        if (cache.TryGetValue(processName, out var known)) return known;
        var name = processName;
        foreach (var pid in pids.Take(3))
        {
            if (NativeProcess.ImagePath(pid) is not { } exe) continue;
            try
            {
                if (FileVersionInfo.GetVersionInfo(exe).FileDescription is { Length: > 0 } description) name = description.Trim();
                break;
            }
            catch (FileNotFoundException)
            {
                // Gone meanwhile: try the next process.
            }
        }
        return cache[processName] = name;
    }

    static (string Name, bool PowerSaver) PowerPlan()
    {
        try
        {
            // powercfg writes in the console's legacy code page (accents in the plan's name).
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "powercfg.exe"), "/getactivescheme")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
                StandardOutputEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage),
            };
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            var match = PlanLine().Match(output);
            return match.Success
                ? (match.Groups[2].Value.Trim(), match.Groups[1].Value.Equals(PowerSaverPlan, StringComparison.OrdinalIgnoreCase))
                : (L.T("inconnu"), false);
        }
        catch (Exception)
        {
            return (L.T("inconnu"), false);
        }
    }

    static PerformanceCounter? Counter(string category, string counter, string instance)
    {
        try
        {
            return new PerformanceCounter(category, counter, instance);
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    [GeneratedRegex(@"^pid_(\d+)_.*_engtype_(.+)$")]
    private static partial Regex GpuInstance();

    [GeneratedRegex(@"([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})\s+\((.+)\)")]
    private static partial Regex PlanLine();
}
