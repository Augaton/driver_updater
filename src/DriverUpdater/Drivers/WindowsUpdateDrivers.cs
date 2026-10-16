namespace DriverUpdater.Drivers;

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

public sealed record HistoryEntry(DateTime Date, string Title, string Result, string Via);

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
        session.ClientApplicationID = ClientName;
        return session;
    });

    public static List<DriverUpdate> Search() => Find("IsInstalled=0 and Type='Driver' and IsHidden=0");

    /// <summary>
    /// The updates with these IDs, as the administrator helper finds them again: COM objects
    /// cannot cross from the window's process. IDs must come from <see cref="AdminCommand"/>,
    /// which lets only GUIDs into this search string.
    /// </summary>
    public static List<DriverUpdate> FindByIds(IEnumerable<string> ids) =>
        Find(string.Join(" or ", ids.Select(id => $"UpdateID='{id}'")));

    static List<DriverUpdate> Find(string criteria)
    {
        dynamic updates = Searcher().Search(criteria).Updates;

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

    /// <summary>Windows updates (not drivers) waiting to be installed, as the security check counts them.</summary>
    public static int CountPendingSoftware()
    {
        // Settings' own count: no optional "browse only" updates (previews, feature updates),
        // asked of the server the PC is set to use (a company's own, if any).
        dynamic searcher = Session.Value.CreateUpdateSearcher();
        return (int)searcher.Search("IsInstalled=0 and Type='Software' and IsHidden=0 and BrowseOnly=0").Updates.Count;
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

    // The "Drivers" update classification of Windows Update.
    const string DriversClassification = "ebfc1fc5-71a4-4f7b-9aca-3b9a503104a0";
    const string ClientName = "Driver Updater";

    /// <summary>Driver installs Windows Update remembers, newest first, whoever started them.</summary>
    public static List<HistoryEntry> History(int max = 300)
    {
        dynamic searcher = Session.Value.CreateUpdateSearcher();
        int total = searcher.GetTotalHistoryCount();
        var list = new List<HistoryEntry>();
        if (total == 0) return list;
        dynamic entries = searcher.QueryHistory(0, Math.Min(total, max));
        int count = entries.Count;
        for (int i = 0; i < count; i++)
        {
            dynamic entry = entries[i];
            const int OperationInstall = 1;
            if ((int)entry.Operation != OperationInstall) continue;
            bool ours = (entry.ClientApplicationID as string) == ClientName;
            if (!ours && !IsDriver(entry)) continue;
            int code = entry.ResultCode;
            list.Add(new HistoryEntry(
                ((DateTime)entry.Date).ToLocalTime(),
                (string)entry.Title,
                code switch
                {
                    ResultSucceeded => "Réussi",
                    ResultSucceededWithErrors => "Réussi avec erreurs",
                    5 => "Annulé",
                    _ => $"Échec (0x{(int)entry.HResult:X8})",
                },
                ours ? ClientName : "Windows Update"));
        }
        return list;
    }

    static bool IsDriver(dynamic entry)
    {
        try
        {
            dynamic categories = entry.Categories;
            int count = categories.Count;
            for (int i = 0; i < count; i++)
                if (string.Equals((string)categories[i].CategoryID, DriversClassification, StringComparison.OrdinalIgnoreCase))
                    return true;
        }
        catch (Exception)
        {
            // Older entries may carry no categories: not identifiable as a driver.
        }
        return false;
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
