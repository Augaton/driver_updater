namespace DriverUpdater.Maintenance;

public sealed record Antivirus(string Name, bool Enabled, bool UpToDate);

/// <param name="PendingUpdates">Windows updates (not drivers) waiting, or null if Windows Update could not be asked.</param>
public sealed record SecurityState(IReadOnlyList<Antivirus> Antiviruses, bool? FirewallOn, int? PendingUpdates)
{
    /// <summary>What needs doing, in plain words; empty when all is well.</summary>
    public IReadOnlyList<string> Problems
    {
        get
        {
            var problems = new List<string>();
            if (Antiviruses.Count > 0 && !Antiviruses.Any(a => a.Enabled)) problems.Add(L.T("Aucun antivirus actif"));
            problems.AddRange(Antiviruses.Where(a => a.Enabled && !a.UpToDate).Select(a => L.F("{0} n'est pas à jour", a.Name)));
            if (FirewallOn == false) problems.Add(L.T("Pare-feu Windows désactivé"));
            if (PendingUpdates > 0) problems.Add(L.F("{0} mise(s) à jour de Windows en attente", PendingUpdates));
            return problems;
        }
    }
}

/// <summary>
/// Reads the "productState" Windows Security Center keeps for each antivirus: a packed number
/// whose middle nibble says on/off and low byte says whether its definitions are current.
/// For Microsoft Defender running and up to date, it is 0x061100 (397568).
/// </summary>
public static class SecurityCenter
{
    public static bool IsEnabled(uint productState) => ((productState >> 12) & 0xF) == 1;
    public static bool IsUpToDate(uint productState) => ((productState >> 4) & 0xF) == 0;
}
