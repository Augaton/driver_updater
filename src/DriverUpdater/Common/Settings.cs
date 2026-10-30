using Microsoft.Win32;

namespace DriverUpdater.Common;

/// <summary>The window's place and size, in device-independent units; Maximized keeps the size it had before.</summary>
public sealed record WindowPlacement(double Left, double Top, double Width, double Height, bool Maximized);

/// <summary>The user's choices, kept in HKEY_CURRENT_USER\Software\Driver Updater.</summary>
public static class Settings
{
    const string Key = @"Software\Driver Updater";

    /// <summary>"auto" (Windows' language), "fr" or "en". Takes effect at the next start.</summary>
    public static string Language
    {
        get => Read("Language") is "fr" or "en" ? (string)Read("Language")! : "auto";
        set => Write("Language", value is "fr" or "en" ? value : "auto");
    }

    /// <summary>NVIDIA's Studio driver (tested longer with creative apps) rather than Game Ready.</summary>
    public static bool NvidiaStudio
    {
        get => Read("NvidiaStudio") is 1;
        set => Write("NvidiaStudio", value ? 1 : 0);
    }

    /// <summary>The weekly check also tells about a new NVIDIA driver.</summary>
    public static bool WeeklyNvidia
    {
        get => Read("WeeklyNvidia") is not 0;
        set => Write("WeeklyNvidia", value ? 1 : 0);
    }

    /// <summary>The weekly check also tells about software updates (winget).</summary>
    public static bool WeeklySoftware
    {
        get => Read("WeeklySoftware") is not 0;
        set => Write("WeeklySoftware", value ? 1 : 0);
    }

    /// <summary>When the user last changed what starts with Windows: boots before and after it compare.</summary>
    public static DateTime? StartupChanged
    {
        get => Read("StartupChanged") is string s && DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.RoundtripKind, out var d) ? d : null;
        set
        {
            if (value is { } d) Write("StartupChanged", d.ToString("o", System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    /// <summary>The last analysis's figures and date, shown on the dashboard until a new one.</summary>
    public static ScanSummary? LastScan
    {
        get
        {
            try
            {
                return Read("LastScan") is string json ? System.Text.Json.JsonSerializer.Deserialize<ScanSummary>(json) : null;
            }
            catch (System.Text.Json.JsonException)
            {
                return null; // unreadable: no figures rather than wrong ones
            }
        }
        set
        {
            if (value is not null) Write("LastScan", System.Text.Json.JsonSerializer.Serialize(value));
        }
    }

    /// <summary>Where the window was left: it opens there again.</summary>
    public static WindowPlacement? Window
    {
        get
        {
            try
            {
                return Read("Window") is string json ? System.Text.Json.JsonSerializer.Deserialize<WindowPlacement>(json) : null;
            }
            catch (System.Text.Json.JsonException)
            {
                return null;
            }
        }
        set
        {
            if (value is not null) Write("Window", System.Text.Json.JsonSerializer.Serialize(value));
        }
    }

    /// <summary>Catalog updates the user hid (the "Fabricants" page): not offered again, there or in the report.</summary>
    public static IReadOnlySet<string> CatalogHidden
    {
        get => ReadList("CatalogHidden");
        set => Write("CatalogHidden", value.ToArray());
    }

    /// <summary>
    /// Catalog updates Windows added but did not pick, keeping a better driver: neither they nor
    /// older versions are offered again (<see cref="Catalog.Newer"/>).
    /// </summary>
    public static IReadOnlySet<string> CatalogKept
    {
        get => ReadList("CatalogKept");
        set => Write("CatalogKept", value.ToArray());
    }

    static HashSet<string> ReadList(string name) =>
        (Read(name) as string[] ?? []).ToHashSet(StringComparer.OrdinalIgnoreCase);

    static object? Read(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(Key);
        return key?.GetValue(name);
    }

    static void Write(string name, object value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(Key);
        if (value is int number) key.SetValue(name, number, RegistryValueKind.DWord);
        else if (value is string[] list) key.SetValue(name, list, RegistryValueKind.MultiString);
        else key.SetValue(name, (string)value, RegistryValueKind.String);
    }
}
