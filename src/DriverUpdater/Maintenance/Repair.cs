using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace DriverUpdater.Maintenance;

public sealed record RepairReport(int ExitCode, string Summary);

/// <summary>
/// Microsoft's own repair tools: sfc (system files), DISM (the Windows image it repairs
/// them from, and the cleanup of superseded components). Their output is streamed so the
/// window can show progress.
/// </summary>
public static class Repair
{
    public static async Task<RepairReport> RunAsync(string command, Action<string> progress)
    {
        // sfc writes UTF-16 when redirected; DISM writes in the console's legacy code page.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var oem = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        var (exe, args, encoding) = command switch
        {
            AdminCommand.Sfc => ("sfc.exe", new[] { "/scannow" }, Encoding.Unicode),
            AdminCommand.DismRestore => ("Dism.exe", new[] { "/Online", "/Cleanup-Image", "/RestoreHealth" }, oem),
            AdminCommand.DismCleanup => ("Dism.exe", new[] { "/Online", "/Cleanup-Image", "/StartComponentCleanup" }, oem),
            _ => throw new ArgumentException($"Outil de réparation inconnu : {command}"),
        };

        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, exe))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            StandardOutputEncoding = encoding,
        };
        foreach (var argument in args) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;

        // Progress lines are rewritten in place with \r, so split on it as well as on \n.
        var lastLines = new Queue<string>();
        var line = new StringBuilder();
        var buffer = new char[1024];
        int read;
        while ((read = await process.StandardOutput.ReadAsync(buffer)) > 0)
            foreach (var c in buffer.AsSpan(0, read))
            {
                if (c is not ('\r' or '\n'))
                {
                    if (c != '\0') line.Append(c);
                    continue;
                }
                var text = line.ToString().Trim();
                line.Clear();
                if (text.Length == 0) continue;
                progress(text);
                if (!text.Contains('%'))
                {
                    lastLines.Enqueue(text);
                    if (lastLines.Count > 4) lastLines.Dequeue();
                }
            }
        await process.WaitForExitAsync();
        return new RepairReport(process.ExitCode, string.Join("\n", lastLines));
    }
}
