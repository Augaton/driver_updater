using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

// The English version stays complete: every French text the app shows (L.T / L.F in the code,
// literal texts in the XAML) has its translation, with the same values in it, and en.json keeps
// no text the app no longer uses.
public sealed class LocalizationTests
{
    static readonly Regex Call = new(@"L\.[TF]\(\s*""((?:[^""\\\r\n]|\\.)*)""");
    static readonly Regex Slot = new(@"(?<!\{)\{(\d+)");

    // What the Translator swaps in the XAML (UI/Translator.cs).
    static readonly HashSet<string> TextAttributes =
        ["Text", "Content", "Header", "ToolTip", "Title", "Description", "Note", "ContentStringFormat", "AutomationProperties.Name"];

    // Names that stay as they are in both languages.
    static readonly HashSet<string> Untranslated = ["DRIVER", "UPDATER", "Driver Updater", "Français", "English"];

    static string AppSource()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var app = Path.Combine(dir.FullName, "src", "DriverUpdater");
            if (Directory.Exists(app)) return app;
        }
        throw new DirectoryNotFoundException("src/DriverUpdater not found above the test folder.");
    }

    static IEnumerable<string> SourceFiles(string pattern) =>
        Directory.EnumerateFiles(AppSource(), pattern, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    static HashSet<string> FrenchTexts()
    {
        var texts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in SourceFiles("*.cs"))
            foreach (Match m in Call.Matches(File.ReadAllText(file)))
                texts.Add(Regex.Unescape(m.Groups[1].Value));

        foreach (var file in SourceFiles("*.xaml"))
            foreach (var element in XDocument.Load(file).Descendants())
            {
                var values = element.Attributes()
                    .Where(a => TextAttributes.Contains(a.Name.LocalName))
                    .Select(a => a.Value)
                    .ToList();
                if (element.Name.LocalName == "Setter" && (string?)element.Attribute("Property") is "Text" or "Content" or "ToolTip")
                    values.Add((string?)element.Attribute("Value") ?? "");
                foreach (var value in values)
                    if (value.Length > 0 && !value.StartsWith('{') && !Untranslated.Contains(value)
                        && Regex.IsMatch(value, "[A-Za-zÀ-ÿ]{2}"))
                        texts.Add(value);
            }
        return texts;
    }

    static Dictionary<string, string> English() =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(Path.Combine(AppSource(), "Localization", "en.json")))!;

    [Fact]
    public void Every_french_text_has_its_english_translation()
    {
        var english = English();
        var missing = FrenchTexts().Where(t => !english.ContainsKey(t)).Order(StringComparer.Ordinal).ToList();
        Assert.True(missing.Count == 0, "Missing from en.json:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void English_keeps_no_text_the_app_no_longer_shows()
    {
        var used = FrenchTexts();
        var stale = English().Keys.Where(k => !used.Contains(k)).Order(StringComparer.Ordinal).ToList();
        Assert.True(stale.Count == 0, "No longer used, remove from en.json:\n" + string.Join("\n", stale));
    }

    [Fact]
    public void Translations_keep_the_values_and_the_spacing()
    {
        var zeros = Enumerable.Repeat<object?>(0, 10).ToArray();
        foreach (var (french, english) in English())
        {
            Assert.False(string.IsNullOrWhiteSpace(english), $"Empty translation for “{french}”");
            Assert.Equal(Slots(french), Slots(english));
            Assert.True(char.IsWhiteSpace(french[0]) == char.IsWhiteSpace(english[0]), $"Leading space differs: “{english}”");
            Assert.True(char.IsWhiteSpace(french[^1]) == char.IsWhiteSpace(english[^1]), $"Trailing space differs: “{english}”");
            if (Slots(french).Length > 0)
                _ = string.Format(CultureInfo.InvariantCulture, english, zeros); // a malformed {…} throws here
        }
    }

    static string Slots(string text) =>
        string.Join(",", Slot.Matches(text).Select(m => m.Groups[1].Value).Distinct().Order());

    [Fact]
    public void Text_calls_use_plain_literals()
    {
        // L.T($"…"), L.T("…" + x) or L.T(variable) would look up a key no scan can see: it would
        // stay French without this test noticing.
        var risky = new Regex(@"L\.[TF]\(\s*[^""\s]|L\.[TF]\(\s*""(?:[^""\\]|\\.)*""\s*\+");
        var found = SourceFiles("*.cs")
            .SelectMany(f => File.ReadLines(f).Where(line => risky.IsMatch(line)).Select(line => $"{Path.GetFileName(f)}: {line.Trim()}"))
            .ToList();
        Assert.True(found.Count == 0, string.Join("\n", found));
    }

    [Fact]
    public void English_replaces_french_and_falls_back_to_it()
    {
        L.Use("en");
        try
        {
            Assert.Equal("Ready.", L.T("Prêt."));
            Assert.Equal("3 driver(s) of 4 installed.", L.F("{0} pilote(s) sur {1} installé(s).", 3, 4));
            Assert.Equal("texte inconnu", L.T("texte inconnu"));
        }
        finally { L.Use("fr"); }
        Assert.Equal("Prêt.", L.T("Prêt."));
    }

    [Theory]
    [InlineData("fr", "en-US", "fr")]
    [InlineData("en", "fr-FR", "en")]
    [InlineData("auto", "fr-CA", "fr")]
    [InlineData("auto", "de-DE", "en")]
    [InlineData("", "fr-FR", "fr")]
    public void Language_follows_the_setting_then_windows(string setting, string windows, string expected) =>
        Assert.Equal(expected, L.Resolve(setting, CultureInfo.GetCultureInfo(windows)));
}
