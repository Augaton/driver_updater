using System.IO;
using System.Management;
using System.Net.Http;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;

namespace DriverUpdater;

public sealed class CatalogDriver
{
    public required string Device { get; init; }
    public required string Installed { get; init; }
    public required CatalogEntry Entry { get; init; }
    /// <summary>Made for this vendor/device in general, not for this PC's exact model.</summary>
    public required bool Generic { get; init; }
    public bool Selected { get; set; }

    public string Available => Entry.Version!.ToString();
    public string Scope => Generic ? "Générique" : "Pour votre PC";
    public string SizeText => Entry.Size > 0 ? Bytes.Format(Entry.Size) : "—";
}

public sealed record CatalogInstallResult(string UpdateId, bool Succeeded, bool RebootRequired, string Outcome);
public sealed record CatalogInstallReport(string RestorePoint, bool Cancelled, List<CatalogInstallResult> Results);

/// <summary>
/// Newer drivers from every maker (NVIDIA, AMD, Intel, Realtek…), from the Microsoft Update
/// Catalog: signed by Microsoft like Windows Update's, but not necessarily offered to this PC.
/// </summary>
public static class DriverCatalog
{
    sealed record Device(string Name, List<string> HardwareIds, Version? Version, DateTime? Date);

    /// <param name="followRedirects">
    /// Off for downloads: a redirect would lead away from the Microsoft server that was
    /// checked, so the download fails instead.
    /// </param>
    static HttpClient NewClient(TimeSpan timeout, bool followRedirects = true)
    {
        var http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = followRedirects }) { Timeout = timeout };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) DriverUpdater");
        return http;
    }

    public static async Task<List<CatalogDriver>> FindAsync(IProgress<string> progress)
    {
        var devices = await Task.Run(Devices);
        using var http = NewClient(TimeSpan.FromSeconds(60));
        using var gate = new SemaphoreSlim(3); // a few requests at a time: the catalog is a shared public site
        int done = 0;
        var found = await Task.WhenAll(devices.Select(async device =>
        {
            await gate.WaitAsync();
            try
            {
                return await NewestFor(http, device);
            }
            finally
            {
                gate.Release();
                progress.Report($"Catalogue Microsoft : {Interlocked.Increment(ref done)} périphérique(s) sur {devices.Count}…");
            }
        }));
        return found.OfType<CatalogDriver>().OrderBy(d => d.Device, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    static async Task<CatalogDriver?> NewestFor(HttpClient http, Device device)
    {
        var queries = Catalog.Queries(device.HardwareIds);
        CatalogDriver? best = null;
        for (int i = 0; i < queries.Count; i++)
        {
            List<CatalogEntry> entries;
            try
            {
                entries = Catalog.ParseSearch(await http.GetStringAsync(Catalog.SearchUrl + Uri.EscapeDataString(queries[i])));
            }
            catch (HttpRequestException)
            {
                continue; // one failed search must not sink the others
            }
            var newest = entries.Where(Catalog.Fits).OrderByDescending(e => e.Version).ThenByDescending(e => e.Date).FirstOrDefault();
            if (newest is null || (device.Version is { } installed && newest.Version <= installed)) continue;
            if (best is null || newest.Version > best.Entry.Version)
                best = new CatalogDriver
                {
                    Device = device.Name,
                    Installed = device.Version is null ? "aucun" : new InstalledDriver(device.Version.ToString(), device.Date).ToString(),
                    Entry = newest,
                    Generic = i > 0,
                };
        }
        return best;
    }

    /// <summary>
    /// PCI, audio and USB devices whose driver comes from their maker, or that have none:
    /// those Windows' own built-in drivers do not cover.
    /// </summary>
    static List<Device> Devices()
    {
        var drivers = new Dictionary<string, (string? Version, DateTime? Date, string Provider)>(StringComparer.OrdinalIgnoreCase);
        using (var searcher = new ManagementObjectSearcher("SELECT DeviceID, DriverVersion, DriverDate, DriverProviderName FROM Win32_PnPSignedDriver"))
        using (var results = searcher.Get())
            foreach (ManagementBaseObject driver in results)
                using (driver)
                    if (driver["DeviceID"] is string id)
                        drivers[id] = (driver["DriverVersion"] as string, SystemInfo.ParseWmiDate(driver["DriverDate"] as string),
                            driver["DriverProviderName"] as string ?? "");

        var devices = new List<Device>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var searcher = new ManagementObjectSearcher("SELECT DeviceID, Name, HardwareID FROM Win32_PnPEntity"))
        using (var results = searcher.Get())
            foreach (ManagementBaseObject entity in results)
                using (entity)
                {
                    if (entity["DeviceID"] is not string id || !(id.StartsWith(@"PCI\") || id.StartsWith(@"HDAUDIO\") || id.StartsWith(@"USB\"))) continue;
                    if (entity["HardwareID"] is not string[] { Length: > 0 } hardwareIds || !seen.Add(hardwareIds[0])) continue;
                    drivers.TryGetValue(id, out var driver);
                    if (driver.Provider.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase)) continue;
                    devices.Add(new Device(entity["Name"] as string ?? id, hardwareIds.ToList(),
                        Version.TryParse(driver.Version, out var version) ? version : null, driver.Date));
                }
        return devices;
    }

    // ---- Administrator side ------------------------------------------------------------

    /// <summary>
    /// Downloads one catalog driver from Microsoft's servers, checks it, and lets pnputil
    /// install it. The download link is looked up here from the update ID, never taken
    /// from the window; Windows then keeps whichever driver suits the device best.
    /// </summary>
    public static async Task<CatalogInstallResult> InstallAsync(string updateId, Action<string> progress)
    {
        using var http = NewClient(TimeSpan.FromMinutes(30), followRedirects: false); // graphics drivers weigh close to 1 GB
        var dialog = await http.PostAsync(Catalog.DownloadDialogUrl,
            new FormUrlEncodedContent([new("updateIDs", Catalog.DownloadRequest(updateId))]));
        var download = Catalog.ParseDownload(await dialog.Content.ReadAsStringAsync());
        if (download is not { } file || !Catalog.IsTrustedDownload(file.Url))
            return new(updateId, false, false, "lien de téléchargement introuvable dans le catalogue");

        var folder = ProtectedFolder();
        try
        {
            var cab = Path.Combine(folder.FullName, "driver.cab");
            if (!await DownloadAsync(http, file.Url, cab, file.Digest, progress))
                return new(updateId, false, false, "fichier téléchargé altéré (empreinte différente)");

            progress("Extraction du pilote…");
            var files = Directory.CreateDirectory(Path.Combine(folder.FullName, "files"));
            int expand = await RunAsync(Path.Combine(Environment.SystemDirectory, "expand.exe"), cab, "-F:*", files.FullName);
            if (expand != 0) return new(updateId, false, false, $"extraction impossible (code {expand})");

            progress("Installation du pilote…");
            int code = await DriverBackup.PnpUtilAsync("/add-driver", Path.Combine(files.FullName, "*.inf"), "/subdirs", "/install");
            return code switch
            {
                0 => new(updateId, true, false, "installé"),
                3010 => new(updateId, true, true, "installé, redémarrage nécessaire"),
                259 => new(updateId, true, false, "ajouté, mais Windows garde le pilote actuel, plus adapté à votre PC"),
                _ => new(updateId, false, false, $"refusé par Windows (pnputil, code {code})"),
            };
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    static async Task<bool> DownloadAsync(HttpClient http, string url, string path, string digest, Action<string> progress)
    {
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        long? total = response.Content.Headers.ContentLength;
        // The catalog only publishes SHA-1. It is enough here: forging a file to match an
        // existing digest (a second preimage) is still out of reach, unlike making two new
        // files collide. HTTPS and pnputil's signature check come on top.
#pragma warning disable CA5350
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
#pragma warning restore CA5350
        await using (var source = await response.Content.ReadAsStreamAsync())
        await using (var target = File.Create(path))
        {
            var buffer = new byte[1 << 16];
            long read = 0;
            int lastPercent = -1;
            for (int n; (n = await source.ReadAsync(buffer)) > 0;)
            {
                await target.WriteAsync(buffer.AsMemory(0, n));
                hash.AppendData(buffer, 0, n);
                read += n;
                int percent = total > 0 ? (int)(100 * read / total.Value) : -1;
                if (percent != lastPercent && percent % 5 == 0)
                {
                    lastPercent = percent;
                    progress($"Téléchargement du pilote : {percent} % de {Bytes.Format(total ?? 0)}…");
                }
            }
        }
        return Convert.ToBase64String(hash.GetHashAndReset()) == digest;
    }

    /// <summary>
    /// A new folder only SYSTEM and Administrators can write to, created with those rights
    /// in one step: nobody can slip a file in between the checks and pnputil.
    /// </summary>
    static DirectoryInfo ProtectedFolder()
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var sid in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null), FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp", "DriverUpdater-" + Guid.NewGuid().ToString("N"));
        if (Directory.Exists(path)) throw new IOException("Dossier de travail déjà présent.");
        return security.CreateDirectory(path);
    }

    static async Task<int> RunAsync(string program, params string[] arguments)
    {
        var start = new System.Diagnostics.ProcessStartInfo(program) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = System.Diagnostics.Process.Start(start)!;
        await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return process.ExitCode;
    }
}
