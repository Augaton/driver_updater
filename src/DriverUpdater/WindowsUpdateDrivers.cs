namespace DriverUpdater;

public sealed class DriverUpdate
{
    /// <summary>The Windows Update <c>IUpdate</c> COM object, handed back to install it.</summary>
    public required object Com { get; init; }
    public required string Title { get; init; }
    public required string UpdateId { get; init; }
    public string? HardwareId { get; init; }
    public DateTime? Date { get; init; }
    public long Size { get; init; }

    /// <summary>
    /// Optional ("browse only") drivers are ones Microsoft does not push automatically:
    /// only worth installing to fix a known problem.
    /// </summary>
    public bool Optional { get; init; }

    public bool Selected { get; set; }

    /// <summary>The driver this update would replace, filled in after the search.</summary>
    public string Installed { get; set; } = "—";

    public string Kind => Optional ? "Facultatif" : "Recommandé";
    public string SizeText => Size > 0 ? Bytes.Format(Size) : "—";
}

public sealed record InstallOutcome(DriverUpdate Driver, bool Succeeded, string Detail, bool RebootRequired);

/// <summary>
/// Driver updates through the Windows Update Agent COM API, late-bound with
/// <c>dynamic</c> so no interop assembly is needed.
/// </summary>
public static class WindowsUpdateDrivers
{
    // ssWindowsUpdate: ask Microsoft directly, even on a PC pointed at a WSUS server
    // (WSUS servers rarely carry drivers).
    const int ServerWindowsUpdate = 2;
    const int ResultSucceeded = 2, ResultSucceededWithErrors = 3;

    static readonly Lazy<dynamic> Session = new(() =>
    {
        dynamic session = Create("Microsoft.Update.Session");
        session.ClientApplicationID = "Driver Updater";
        return session;
    });

    public static List<DriverUpdate> Search()
    {
        dynamic updates = Searcher().Search("IsInstalled=0 and Type='Driver' and IsHidden=0").Updates;

        var list = new List<DriverUpdate>();
        int count = updates.Count;
        for (int i = 0; i < count; i++)
        {
            dynamic update = updates[i];
            bool optional = update.BrowseOnly;
            list.Add(new DriverUpdate
            {
                Com = update,
                Title = update.Title,
                UpdateId = update.Identity.UpdateID,
                HardwareId = update.DriverHardwareID,
                Date = update.DriverVerDate,
                Size = Convert.ToInt64(update.MaxDownloadSize),
                Optional = optional,
                Selected = !optional,
            });
        }
        return list;
    }

    /// <summary>
    /// Downloads and installs one driver. One at a time, so the UI can show real progress
    /// and one failure does not stop the others.
    /// </summary>
    public static InstallOutcome Install(DriverUpdate driver)
    {
        try
        {
            dynamic update = driver.Com;
            if (!update.EulaAccepted) update.AcceptEula();
            dynamic collection = Create("Microsoft.Update.UpdateColl");
            collection.Add(update);

            dynamic downloader = Session.Value.CreateUpdateDownloader();
            downloader.Updates = collection;
            dynamic download = downloader.Download();
            if (!update.IsDownloaded)
                return new(driver, false, $"téléchargement échoué, 0x{(int)download.HResult:X8}", false);

            dynamic installer = Session.Value.CreateUpdateInstaller();
            installer.Updates = collection;
            dynamic result = installer.Install();
            int code = result.ResultCode;
            bool ok = code is ResultSucceeded or ResultSucceededWithErrors;
            return new(driver, ok, ok ? "" : $"code {code}, 0x{(int)result.HResult:X8}", (bool)result.RebootRequired);
        }
        catch (Exception e)
        {
            // Late-bound COM maps an error code to any .NET exception type (COMException,
            // UnauthorizedAccessException…): report it, and let the next driver go ahead.
            return new(driver, false, $"{e.Message} (0x{e.HResult:X8})", false);
        }
    }

    /// <summary>Stops offering this driver, here and in Windows Update settings.</summary>
    public static void Hide(DriverUpdate driver)
    {
        dynamic update = driver.Com;
        update.IsHidden = true;
    }

    /// <summary>Offers every hidden driver again; returns how many there were.</summary>
    public static int UnhideAll()
    {
        dynamic updates = Searcher().Search("IsInstalled=0 and Type='Driver' and IsHidden=1").Updates;
        int count = updates.Count;
        for (int i = 0; i < count; i++)
            updates[i].IsHidden = false;
        return count;
    }

    static dynamic Searcher()
    {
        dynamic searcher = Session.Value.CreateUpdateSearcher();
        searcher.ServerSelection = ServerWindowsUpdate;
        return searcher;
    }

    static dynamic Create(string progId) =>
        Activator.CreateInstance(
            Type.GetTypeFromProgID(progId)
            ?? throw new InvalidOperationException($"Composant Windows Update introuvable ({progId})."))!;
}
