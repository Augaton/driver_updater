using Microsoft.Win32;

namespace DriverUpdater.Common;

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

    static object? Read(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(Key);
        return key?.GetValue(name);
    }

    static void Write(string name, object value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(Key);
        if (value is int number) key.SetValue(name, number, RegistryValueKind.DWord);
        else key.SetValue(name, (string)value, RegistryValueKind.String);
    }
}
