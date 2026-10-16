using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace DriverUpdater.Maintenance;

/// <summary>
/// A plain-text report for bug reports: app and Windows versions, hardware, and what the app
/// did recently. Built in memory and handed to the clipboard, never written to disk.
/// </summary>
public static class Diagnostics
{
    public static string Build(IEnumerable<string> log)
    {
        var text = new StringBuilder();
        text.AppendLine($"Driver Updater {typeof(Diagnostics).Assembly.GetName().Version?.ToString(3)}");
        text.AppendLine(L.F("Windows : {0}", WindowsVersion()));
        text.AppendLine(L.F("Droits : {0}", IsAdministrator() ? L.T("administrateur") : L.T("utilisateur standard")));
        Section(text, L.T("Ordinateur"), () => [Describe(SystemInfo.Computer())]);
        Section(text, L.T("Cartes graphiques"), () => SystemInfo.Gpus().Select(Describe));
        Section(text, L.T("Disques"), () => SystemInfo.Disks().Select(d => L.F("{0} ({1}, {2}) : {3}", d.Name, d.Type, d.Size, d.Health)));
        Section(text, L.T("Périphériques en erreur"), () => DriverInventory.Scan().Where(d => d.NeedsAttention)
            .Select(d => L.F("{0} : {1}", d.Name, SystemInfo.ErrorMeaning(d.Problem))));
        Section(text, L.T("Journal"), () => log);
        return text.ToString();
    }

    static string Describe(Hardware hardware) => $"{hardware.Name} — {hardware.Detail}";

    // One failing section must not cost the whole report.
    static void Section(StringBuilder text, string title, Func<IEnumerable<string>> lines)
    {
        text.AppendLine().AppendLine(title + " :");
        try
        {
            var items = lines().ToList();
            if (items.Count == 0) text.AppendLine("  (rien)");
            foreach (var item in items) text.AppendLine("  " + item);
        }
        catch (Exception e)
        {
            text.AppendLine(L.T("  lecture impossible : ") + Errors.Describe(e));
        }
    }

    static string WindowsVersion()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        var build = key?.GetValue("CurrentBuild") as string ?? "?";
        // ProductName still says "Windows 10" on Windows 11: the build number tells them apart.
        var name = int.TryParse(build, out var number) && number >= 22000 ? "Windows 11" : "Windows 10";
        return $"{name} {key?.GetValue("EditionID")} {key?.GetValue("DisplayVersion")} " +
               $"(build {build}.{key?.GetValue("UBR")}), {RuntimeInformation.OSArchitecture}";
    }

    static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
}
