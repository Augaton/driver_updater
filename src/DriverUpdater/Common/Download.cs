using System.IO;
using System.Net.Http;

namespace DriverUpdater.Common;

/// <summary>Writes a download to a file, and gives up when the server stops sending.</summary>
public static class Download
{
    /// <summary>
    /// Copies the response body to <paramref name="path"/>. HttpClient's timeout stops at the
    /// headers: without this, a stalled connection would leave the app waiting forever.
    /// </summary>
    /// <param name="progress">Bytes received so far, and the total announced (0 if unknown).</param>
    /// <param name="data">Sees each block as it is written (to hash the file on the way).</param>
    /// <returns>The number of bytes written.</returns>
    public static async Task<long> ToFileAsync(HttpResponseMessage response, string path, Action<long, long>? progress, TimeSpan stall,
        Action<byte[], int>? data = null)
    {
        long total = response.Content.Headers.ContentLength ?? 0;
        await using var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        await using var target = File.Create(path);
        var buffer = new byte[1 << 20];
        long done = 0;
        using var stalled = new CancellationTokenSource();
        while (true)
        {
            stalled.CancelAfter(stall);
            int read;
            try
            {
                read = await source.ReadAsync(buffer, stalled.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stalled.IsCancellationRequested)
            {
                throw new IOException("Le téléchargement s'est arrêté : le serveur ne répond plus.");
            }
            if (read == 0) return done;
            await target.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);
            data?.Invoke(buffer, read);
            done += read;
            progress?.Invoke(done, total);
        }
    }
}
