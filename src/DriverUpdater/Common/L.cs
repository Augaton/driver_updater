using System.Globalization;
using System.Text.Json;

namespace DriverUpdater.Common;

/// <summary>
/// The app's texts in the user's language. French is the source: each text is its own key, and
/// English comes from Localization/en.json (embedded). A text missing there stays in French
/// rather than disappearing; a test checks that none is missing.
/// </summary>
public static class L
{
    static Dictionary<string, string>? english;

    /// <summary>"fr" or "en": what the texts are shown in.</summary>
    public static string Language { get; private set; } = "fr";

    /// <summary>Chosen once, at start-up, before any text is read.</summary>
    public static void Use(string language)
    {
        Language = language == "en" ? "en" : "fr";
        english = Language == "en" ? Load() : null;
    }

    /// <summary>"auto" follows Windows: French for a French Windows, English for any other.</summary>
    public static string Resolve(string setting, CultureInfo windows) =>
        setting is "fr" or "en" ? setting : windows.TwoLetterISOLanguageName == "fr" ? "fr" : "en";

    public static string T(string french) => english is not null && english.TryGetValue(french, out var text) ? text : french;

    /// <summary>A text with values in it ("{0} pilote(s)"), formatted after translation.</summary>
    public static string F(string french, params object?[] values) => string.Format(CultureInfo.CurrentCulture, T(french), values);

    /// <summary>Whether this text has an English translation (for the XAML texts, translated as they appear).</summary>
    public static bool TryTranslate(string french, out string text)
    {
        text = french;
        return english is not null && english.TryGetValue(french, out text!);
    }

    static Dictionary<string, string> Load()
    {
        using var stream = typeof(L).Assembly.GetManifestResourceStream("DriverUpdater.Localization.en.json");
        return stream is null ? [] : JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
    }
}
