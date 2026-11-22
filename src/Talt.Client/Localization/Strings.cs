using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Talt.Core.Simulation;

namespace Talt.Client.Localization;

/// <summary>
/// Minimal localizable string table (English and Spanish) for player-facing text, until the user
/// interface feature (005) provides full localization. Texts live in embedded JSON resources
/// <c>Strings.en.json</c> and <c>Strings.es.json</c>; placeholders are written as <c>{name}</c>.
/// </summary>
internal sealed class Strings
{
    public const string DefaultLanguage = "en";

    public static readonly IReadOnlyList<string> SupportedLanguages = ["en", "es"];

    private readonly IReadOnlyDictionary<string, string> _texts;
    private readonly IReadOnlyDictionary<string, string> _fallback;

    private Strings(string language, IReadOnlyDictionary<string, string> texts, IReadOnlyDictionary<string, string> fallback)
    {
        Language = language;
        Culture = CultureInfo.GetCultureInfo(language);
        _texts = texts;
        _fallback = fallback;
    }

    public string Language { get; }

    public CultureInfo Culture { get; }

    /// <summary>Spanish when the system language is Spanish, English otherwise.</summary>
    public static string DetectLanguage() =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "es" ? "es" : DefaultLanguage;

    public static Strings Load(string language)
    {
        if (!SupportedLanguages.Contains(language))
        {
            throw new ArgumentException($"Unsupported language '{language}'. Use one of: {string.Join(", ", SupportedLanguages)}.", nameof(language));
        }

        var fallback = LoadTable(DefaultLanguage);
        var texts = language == DefaultLanguage ? fallback : LoadTable(language);
        return new Strings(language, texts, fallback);
    }

    /// <summary>Raw key → text table of a language (used by tests to compare languages).</summary>
    internal static IReadOnlyDictionary<string, string> LoadTable(string language)
    {
        var name = $"Talt.Client.Localization.Strings.{language}.json";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing string table resource '{name}'.");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidOperationException($"String table '{name}' is empty.");
    }

    /// <summary>Text for a key; falls back to English, then to the key itself in brackets.</summary>
    public string Get(string key) =>
        _texts.TryGetValue(key, out var text) ? text :
        _fallback.TryGetValue(key, out var english) ? english :
        $"[{key}]";

    public bool Contains(string key) => _texts.ContainsKey(key) || _fallback.ContainsKey(key);

    /// <summary>Text for a key with <c>{name}</c> placeholders replaced (numbers use the language's culture).</summary>
    public string Format(string key, params (string Name, object Value)[] values)
    {
        var text = Get(key);
        foreach (var (name, value) in values)
        {
            var formatted = value switch
            {
                int or long => ((IFormattable)value).ToString("N0", Culture), // Thousands separators.
                IFormattable formattable => formattable.ToString(null, Culture),
                _ => value?.ToString() ?? string.Empty,
            };
            text = text.Replace("{" + name + "}", formatted, StringComparison.Ordinal);
        }

        return text;
    }

    public string FormatDate(GameDate date) =>
        // Day and year as plain digits (no thousands separator in years).
        Format("date.format",
            ("day", date.Day.ToString(CultureInfo.InvariantCulture)),
            ("month", Get($"month.{date.Month}")),
            ("year", date.Year.ToString(CultureInfo.InvariantCulture)));

    public string FormatSpeed(GameSpeed speed) => Get(speed switch
    {
        GameSpeed.Paused => "speed.paused",
        GameSpeed.Fast => "speed.fast",
        GameSpeed.VeryFast => "speed.veryFast",
        GameSpeed.FastForward => "speed.fastForward",
        _ => "speed.normal",
    });
}
