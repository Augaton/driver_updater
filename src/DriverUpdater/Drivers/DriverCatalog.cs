using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;

namespace DriverUpdater.Drivers;

public sealed class CatalogDriver : INotifyPropertyChanged
{
    public required string DeviceId { get; init; }
    public required string HardwareId { get; init; }
    public required string Device { get; init; }
    public required string Installed { get; init; }
    public required bool NoDriver { get; init; }
    /// <summary>Every catalog version newer than the installed one, newest first.</summary>
    public required IReadOnlyList<CatalogEntry> Versions { get; init; }
    /// <summary>Updates found only by the generic vendor/device search, not for this PC's exact model.</summary>
    public required IReadOnlySet<string> GenericIds { get; init; }
    public bool Selected { get; set; }

    CatalogEntry? chosen;

    /// <summary>The version to install: the newest unless the user picks another.</summary>
    public CatalogEntry Entry
    {
        get => chosen ?? Versions[0];
        set
        {
            chosen = value;
            foreach (var property in new[] { nameof(Entry), nameof(Scope), nameof(SizeText) })
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        }
    }

    public string Scope => GenericIds.Contains(Entry.UpdateId) ? L.T("Générique") : L.T("Pour votre PC");
    public string SizeText => Entry.Size > 0 ? Bytes.Format(Entry.Size) : "—";

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed record CatalogInstallResult(string UpdateId, bool Succeeded, bool RebootRequired, string Outcome);
public sealed record CatalogInstallReport(string RestorePoint, bool Cancelled, List<CatalogInstallResult> Results);

/// <summary>
/// Newer drivers from every maker (NVIDIA, AMD, Intel, Realtek…), from the Microsoft Update
/// Catalog: signed by Microsoft like Windows Update's, but not necessarily offered to this PC.
/// </summary>
public static class DriverCatalog
{
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

    // One client for every search, as .NET recommends: sockets are reused, not exhausted.
    static readonly HttpClient SearchClient = NewClient(TimeSpan.FromSeconds(60));

    /// <summary>Newer catalog drivers for the devices <see cref="CatalogScope"/> keeps.</summary>
    public static async Task<List<CatalogDriver>> FindAsync(IEnumerable<DeviceDriver> devices, IProgress<string> progress)
    {
        var selected = CatalogScope.Select(devices);
        var http = SearchClient;
        using var gate = new SemaphoreSlim(3); // a few requests at a time: the catalog is a shared public site
        // Devices often share their generic ID (two ports of the same controller…): search each ID once.
        var searches = new ConcurrentDictionary<string, Lazy<Task<List<CatalogEntry>>>>(StringComparer.OrdinalIgnoreCase);
        Task<List<CatalogEntry>> Search(string query) =>
            searches.GetOrAdd(query, q => new Lazy<Task<List<CatalogEntry>>>(() => SearchAsync(http, gate, q))).Value;

        int done = 0;
        var found = await Task.WhenAll(selected.Select(async device =>
        {
            var driver = await NewerFor(device, Search);
            progress.Report(L.F("Catalogue Microsoft : {0} périphérique(s) sur {1}…", Interlocked.Increment(ref done), selected.Count));
            return driver;
        }));
        return found.OfType<CatalogDriver>()
            .OrderByDescending(d => d.NoDriver)
            .ThenBy(d => d.Device, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    static async Task<List<CatalogEntry>> SearchAsync(HttpClient http, SemaphoreSlim gate, string query)
    {
        await gate.WaitAsync();
        try
        {
            return Catalog.ParseSearch(await http.GetStringAsync(Catalog.SearchUrl + Uri.EscapeDataString(query)).ConfigureAwait(false))
                .Where(Catalog.Fits).ToList();
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return []; // one failed search must not sink the others
        }
        finally
        {
            gate.Release();
        }
    }

    static async Task<CatalogDriver?> NewerFor(DeviceDriver device, Func<string, Task<List<CatalogEntry>>> search)
    {
        var queries = Catalog.Queries(device.HardwareIds);
        var specific = await search(queries[0]);
        var generic = queries.Count > 1 ? await search(queries[1]) : [];
        var genericOnly = generic.Select(e => e.UpdateId).Except(specific.Select(e => e.UpdateId)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var installed = device.HasDriver ? device.ParsedVersion : null;

        var versions = specific.Concat(generic)
            .Where(e => installed is null || e.Version > installed)
            // One entry per version: the one made for this PC if there is one, else the newest.
            .GroupBy(e => e.Version)
            .Select(g => g.OrderBy(e => genericOnly.Contains(e.UpdateId)).ThenByDescending(e => e.Date).First())
            .OrderByDescending(e => e.Version)
            .ToList();
        if (versions.Count == 0) return null;
        return new CatalogDriver
        {
            DeviceId = device.DeviceId,
            HardwareId = device.HardwareIds[0],
            Device = device.Name,
            Installed = device.HasDriver ? new InstalledDriver(device.Version, device.Date).ToString() : L.T("aucun"),
            NoDriver = !device.HasDriver,
            Versions = versions,
            GenericIds = genericOnly,
        };
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
            return new(updateId, false, false, L.T("lien de téléchargement introuvable dans le catalogue"));

        var folder = ProtectedFolder();
        try
        {
            var cab = Path.Combine(folder.FullName, "driver.cab");
            if (!await DownloadAsync(http, file.Url, cab, file.Digest, progress))
                return new(updateId, false, false, L.T("fichier téléchargé altéré (empreinte différente)"));

            progress(L.T("Extraction du pilote…"));
            var files = Directory.CreateDirectory(Path.Combine(folder.FullName, "files"));
            int expand = await RunAsync(Path.Combine(Environment.SystemDirectory, "expand.exe"), cab, "-F:*", files.FullName);
            if (expand != 0) return new(updateId, false, false, L.F("extraction impossible (code {0})", expand));

            // pnputil installs every INF of the package on every device it fits, and a platform
            // bundle can hold a storage-controller driver next to the one chosen: those never
            // go in this way (see CatalogScope.BootCritical).
            var infs = Directory.GetFiles(files.FullName, "*.inf", SearchOption.AllDirectories);
            if (infs.Length == 0) return new(updateId, false, false, L.T("le paquet ne contient aucun pilote (.inf)"));
            var bootCritical = infs.Where(inf => Catalog.IsBootCritical(File.ReadAllText(inf))).ToList();
            foreach (var inf in bootCritical) File.Delete(inf);
            if (bootCritical.Count == infs.Length)
                return new(updateId, false, false, L.T("ce paquet ne contient que des pilotes nécessaires au démarrage (stockage) : laissé à Windows Update"));

            progress(L.T("Installation du pilote…"));
            int code = await DriverBackup.PnpUtilAsync("/add-driver", Path.Combine(files.FullName, "*.inf"), "/subdirs", "/install");
            return code switch
            {
                0 => new(updateId, true, false, L.T("installé")),
                3010 => new(updateId, true, true, L.T("installé, redémarrage nécessaire")),
                259 => new(updateId, true, false, L.T("ajouté, mais Windows garde le pilote actuel, plus adapté à votre PC")),
                _ => new(updateId, false, false, L.F("refusé par Windows (pnputil, code {0})", code)),
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
        // The catalog only publishes SHA-1. It is enough here: forging a file to match an
        // existing digest (a second preimage) is still out of reach, unlike making two new
        // files collide. HTTPS and pnputil's signature check come on top.
#pragma warning disable CA5350
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
#pragma warning restore CA5350
        int lastPercent = -1;
        await Download.ToFileAsync(response, path, (read, all) =>
        {
            int percent = all > 0 ? (int)(100 * read / all) : -1;
            if (percent == lastPercent || percent % 5 != 0) return;
            lastPercent = percent;
            progress(L.F("Téléchargement du pilote : {0} % de {1}…", percent, Bytes.Format(all)));
        }, TimeSpan.FromSeconds(60), data: (buffer, count) => hash.AppendData(buffer, 0, count));
        return Convert.ToBase64String(hash.GetHashAndReset()) == digest;
    }

    /// <summary>
    /// A new folder only SYSTEM and Administrators can write to, created with those rights
    /// in one step: nobody can slip a file in between the checks and pnputil.
    /// </summary>
    internal static DirectoryInfo ProtectedFolder()
    {
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var sid in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null), FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp", "DriverUpdater-" + Guid.NewGuid().ToString("N"));
        if (Directory.Exists(path)) throw new IOException(L.T("Dossier de travail déjà présent."));
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
