using System.Diagnostics;
using System.IO;
using System.Net.Http;

namespace DriverUpdater.Drivers;

/// <summary>
/// Asks NVIDIA for the newest Game Ready driver of each GeForce card, and installs it with
/// NVIDIA's own installer. The installing side runs in the administrator helper: it asks NVIDIA
/// again (the window's word is not taken), downloads from NVIDIA's servers only, without
/// following redirects, into a folder only administrators can write to, and runs the
/// installer only if Windows accepts its signature and the signer is NVIDIA.
/// </summary>
public static class NvidiaDrivers
{
    /// <summary>The newest release for each NVIDIA graphics card (by device instance ID); cards NVIDIA does not answer for are left out.</summary>
    public static async Task<Dictionary<string, NvidiaRelease>> LatestAsync(IEnumerable<DeviceDriver> devices, bool laptop)
    {
        var found = new Dictionary<string, NvidiaRelease>(StringComparer.OrdinalIgnoreCase);
        foreach (var device in devices.Where(d => d.Class.Equals("Display", StringComparison.OrdinalIgnoreCase)))
            if (CardId(device) is { } id && await LatestForAsync(id, laptop).ConfigureAwait(false) is { } release)
                found[device.DeviceId] = release;
        return found;
    }

    /// <summary>The card's PCI device ID ("2684"), or null for another maker's device.</summary>
    public static string? CardId(DeviceDriver device) => device.HardwareIds.Select(NvidiaModel.DeviceId).FirstOrDefault(id => id is not null);

    /// <summary>The newest driver of the kind the user chose (Game Ready, or Studio in Paramètres).</summary>
    public static async Task<NvidiaRelease?> LatestForAsync(string deviceId, bool laptop)
    {
        using var http = Client(TimeSpan.FromSeconds(20));
        bool studio = Settings.NvidiaStudio;
        try
        {
            var answer = await http.GetStringAsync(NvidiaModel.LookupUrl(deviceId, Environment.OSVersion.Version.Build, laptop, studio)).ConfigureAwait(false);
            return NvidiaModel.Parse(answer, studio);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            return null; // NVIDIA unreachable, or no driver for this card: the catalog still answers
        }
    }

    /// <summary>Administrator helper side: download, signature, NVIDIA's installer; returns its version and exit code.</summary>
    public static async Task<(string Version, int ExitCode)> InstallAsync(string deviceId, Action<string> progress)
    {
        progress(L.T("Recherche du pilote chez NVIDIA…"));
        var release = await LatestForAsync(deviceId, Machine.HasBattery)
            ?? throw new InvalidOperationException(L.T("NVIDIA ne propose pas de pilote pour cette carte en ce moment."));
        if (!NvidiaModel.IsTrustedDownload(release.DownloadUrl)) throw new InvalidOperationException(L.T("Adresse de téléchargement refusée."));

        var folder = DriverCatalog.ProtectedFolder();
        try
        {
            var path = Path.Combine(folder.FullName, Path.GetFileName(new Uri(release.DownloadUrl).LocalPath));
            using (var http = Client(TimeSpan.FromMinutes(2)))
            using (var response = await http.GetAsync(release.DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                if (response.StatusCode != System.Net.HttpStatusCode.OK)
                    throw new InvalidOperationException(L.F("NVIDIA a répondu {0} au téléchargement.", (int)response.StatusCode));
                long total = response.Content.Headers.ContentLength ?? 0;
                if (total is <= 0 or > 3L << 30) throw new InvalidOperationException(L.T("Taille du pilote NVIDIA inattendue."));
                int shown = -1;
                long done = await Download.ToFileAsync(response, path, (bytes, all) =>
                {
                    int percent = (int)(bytes * 100 / all);
                    if (percent == shown || percent % 5 != 0) return;
                    shown = percent;
                    progress(L.F("Téléchargement du pilote NVIDIA {0} : {1} % ({2} sur {3})…", release.Version, percent, Bytes.Format(bytes), Bytes.Format(all)));
                }, TimeSpan.FromSeconds(60));
                if (done != total) throw new IOException(L.T("Téléchargement interrompu."));
            }

            progress(L.T("Vérification de la signature de NVIDIA…"));
            Process installer;
            // Checked through the handle that keeps it read-only until it has started.
            using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                using var signer = Authenticode.Signer(file.SafeFileHandle, path);
                if (signer is null || !Authenticode.IsSignedBy(signer, "NVIDIA Corporation"))
                    throw new InvalidOperationException(L.T("L'installateur téléchargé n'est pas signé par NVIDIA : il n'est pas lancé."));
                progress(L.F("Installateur NVIDIA {0} ouvert : suivez ses étapes.", release.Version));
                installer = Process.Start(new ProcessStartInfo(path) { UseShellExecute = false, WorkingDirectory = folder.FullName })!;
            }
            using (installer)
            {
                await installer.WaitForExitAsync();
                return (release.Version, installer.ExitCode);
            }
        }
        finally
        {
            try
            {
                folder.Delete(recursive: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Still in use: Windows' temporary folder is cleaned anyway.
            }
        }
    }

    // HTTPS only, revocation checked, and no redirects: a moved file is a failure, never another host.
    static HttpClient Client(TimeSpan timeout)
    {
        var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, CheckCertificateRevocationList = true }) { Timeout = timeout };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"DriverUpdater/{typeof(NvidiaDrivers).Assembly.GetName().Version?.ToString(3)}");
        return http;
    }
}
