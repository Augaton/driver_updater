using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace DriverUpdater.Drivers;

public sealed record StoreDeleteResult(string PublishedName, bool Deleted, string Detail);

/// <summary>
/// Old driver packages in Windows' driver store (administrator side). Listed with
/// Get-WindowsDriver, Windows' own tool, whose JSON output does not depend on the language;
/// deleted with pnputil, without /force, so Windows refuses anything still in use.
/// </summary>
public static class DriverStore
{
    // Fixed text, nothing from outside goes into it. UTF-8 output so providers' names keep their accents.
    const string ListScript =
        "[Console]::OutputEncoding = [Text.Encoding]::UTF8; " +
        "$d = @(Get-WindowsDriver -Online | ForEach-Object { [pscustomobject]@{ " +
        "Driver = $_.Driver; OriginalFileName = $_.OriginalFileName; ProviderName = $_.ProviderName; " +
        "ClassName = $_.ClassName; Version = $_.Version; Date = $_.Date.ToString('yyyy-MM-dd'); BootCritical = $_.BootCritical } }); " +
        "ConvertTo-Json -InputObject $d -Compress";

    sealed record Listed(string Driver, string OriginalFileName, string? ProviderName, string? ClassName,
        string? Version, string? Date, bool BootCritical);

    /// <summary>Packages that can go, biggest first (see <see cref="DriverStoreRules"/>).</summary>
    public static async Task<List<StorePackage>> RemovableAsync(Action<string> progress)
    {
        progress("Lecture du magasin de pilotes…");
        var packages = await ListAsync();
        progress("Recherche des pilotes utilisés…");
        var inUse = (await Task.Run(DriverInventory.Scan)).Select(d => d.InfName).Where(n => n.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return DriverStoreRules.Removable(packages, inUse);
    }

    /// <summary>
    /// Deletes packages, but only those the rules still allow right now: the list the window
    /// sends is checked again rather than trusted.
    /// </summary>
    public static async Task<List<StoreDeleteResult>> DeleteAsync(IReadOnlyList<string> publishedNames, Action<string> progress)
    {
        var allowed = (await RemovableAsync(progress)).Select(p => p.PublishedName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var results = new List<StoreDeleteResult>();
        foreach (var name in publishedNames)
        {
            if (!allowed.Contains(name))
            {
                results.Add(new(name, false, "n'est plus supprimable (utilisé, ou plus récent que les autres)"));
                continue;
            }
            progress($"Suppression de {name} ({results.Count + 1} sur {publishedNames.Count})…");
            int code = await DriverBackup.PnpUtilAsync("/delete-driver", name);
            results.Add(new(name, code == 0, code == 0 ? "supprimé" : $"refusé par Windows (code {code}), sans doute encore utilisé"));
        }
        return results;
    }

    static async Task<List<StorePackage>> ListAsync()
    {
        var powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        var start = new ProcessStartInfo(powershell)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-Command", ListScript }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var json = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new InvalidOperationException($"Lecture du magasin de pilotes impossible (code {process.ExitCode}).");

        var listed = JsonSerializer.Deserialize<List<Listed>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        return listed.Select(p => new StorePackage(
            p.Driver,
            Path.GetFileName(p.OriginalFileName),
            p.ProviderName ?? "",
            p.ClassName ?? "",
            Version.TryParse(p.Version, out var version) ? version : null,
            DateTime.TryParseExact(p.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null,
            p.BootCritical,
            FolderSize(Path.GetDirectoryName(p.OriginalFileName)))).ToList();
    }

    static long FolderSize(string? folder)
    {
        try
        {
            return folder is not null && Directory.Exists(folder)
                ? new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length)
                : 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }
}
