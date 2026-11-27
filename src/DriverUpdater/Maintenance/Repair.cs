using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace DriverUpdater.Maintenance;

public sealed record RepairReport(int ExitCode, string Summary);

/// <summary>"Réparer Windows": DISM on the Windows image, then SFC on the system files.</summary>
public sealed record RepairPass(RepairReport Image, RepairReport Files)
{
    const int RestartNeeded = 3010;

    /// <summary>
    /// One verdict for both. SFC runs even when DISM fails (often for want of Windows Update): it
    /// can still repair from what is on the PC.
    /// </summary>
    public string Verdict =>
        Image.ExitCode is not (0 or RestartNeeded)
            ? L.F("SFC a vérifié les fichiers, mais DISM n'a pas pu réparer l'image de Windows (code {0}) : voir le résumé.", Code(Image.ExitCode))
        : Files.ExitCode is not (0 or RestartNeeded)
            ? L.F("DISM a réparé l'image de Windows ; SFC s'est terminé avec le code {0} : voir le résumé.", Code(Files.ExitCode))
        : Image.ExitCode == RestartNeeded || Files.ExitCode == RestartNeeded
            ? L.T("Terminé : redémarrez l'ordinateur.")
            : L.T("Terminé.");

    // DISM's failures are HRESULTs (0x800F081F: repair files not found), read in hexadecimal.
    static string Code(int code) => code < 0 ? $"0x{code:X8}" : code.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// Microsoft's own repair tools: sfc (system files), DISM (the Windows image it repairs
/// them from, and the cleanup of superseded components). Their output is streamed so the
/// window can show progress.
/// </summary>
public static class Repair
{
    // sfc writes UTF-16 when redirected.
    public static Task<RepairReport> SystemFilesAsync(Action<string> progress) =>
        RunAsync("sfc.exe", ["/scannow"], Encoding.Unicode, progress);

    public static Task<RepairReport> ImageAsync(Action<string> progress) =>
        RunAsync("Dism.exe", ["/Online", "/Cleanup-Image", "/RestoreHealth"], Oem(), progress);

    public static Task<RepairReport> ComponentCleanupAsync(Action<string> progress) =>
        RunAsync("Dism.exe", ["/Online", "/Cleanup-Image", "/StartComponentCleanup"], Oem(), progress);

    /// <summary>
    /// Microsoft's order: DISM first repairs the image SFC takes its good copies from, then SFC the
    /// files. Under one administrator authorization, each step named in the progress.
    /// </summary>
    public static async Task<RepairPass> WindowsAsync(Action<string> progress)
    {
        var image = await ImageAsync(line => progress(L.F("1/2, image de Windows (DISM) : {0}", line)));
        var files = await SystemFilesAsync(line => progress(L.F("2/2, fichiers système (SFC) : {0}", line)));
        return new RepairPass(image, files);
    }

    // DISM writes in the console's legacy code page.
    static Encoding Oem()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
    }

    static async Task<RepairReport> RunAsync(string exe, string[] args, Encoding encoding, Action<string> progress)
    {

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
