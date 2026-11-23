using Talt.Client.Localization;

namespace Talt.Client.Ui;

/// <summary>
/// Text shown by a widget: always a localization key with arguments, or a dynamic text composed at
/// run time from localized formats (data-model.md TextRef). Widgets never take literal strings
/// (SC-003). The resolved text is cached and only recomputed when the language or the dynamic
/// value's version changes, so steady frames do not allocate.
/// </summary>
internal sealed class TextRef
{
    private readonly string? _key;
    private readonly (string Name, object Value)[] _arguments;
    private readonly Func<long>? _version;
    private readonly Func<Strings, string>? _compose;
    private string _cached = string.Empty;
    private int _cachedLanguage = -1;
    private long _cachedVersion = long.MinValue;

    private TextRef(string? key, (string, object)[] arguments, Func<long>? version, Func<Strings, string>? compose)
    {
        _key = key;
        _arguments = arguments;
        _version = version;
        _compose = compose;
    }

    /// <summary>The localization key, or null for dynamic texts.</summary>
    public string? Key => _key;

    /// <summary>Localized text for <paramref name="key"/> with <c>{name}</c> placeholders.</summary>
    public static TextRef Of(string key, params (string Name, object Value)[] arguments)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        return new TextRef(key, arguments, null, null);
    }

    /// <summary>
    /// Text composed by <paramref name="compose"/> (which must build it from localized formats), recomputed
    /// when <paramref name="version"/> returns a different value or the language changes.
    /// </summary>
    public static TextRef Dynamic(Func<long> version, Func<Strings, string> compose)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(compose);
        return new TextRef(null, [], version, compose);
    }

    /// <summary>The text in the current language.</summary>
    public string Resolve(UiContext ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        var version = _version?.Invoke() ?? 0;
        if (_cachedLanguage != ui.LanguageVersion || version != _cachedVersion)
        {
            _cached = _compose is not null ? _compose(ui.Strings) : ui.Strings.Format(_key!, _arguments);
            _cachedLanguage = ui.LanguageVersion;
            _cachedVersion = version;
        }

        return _cached;
    }
}
