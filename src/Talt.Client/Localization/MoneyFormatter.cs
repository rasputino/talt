using System.Globalization;

namespace Talt.Client.Localization;

/// <summary>A display currency (data-model.md Currency).</summary>
internal sealed record Currency(string Code, string Symbol, decimal Rate, int Decimals)
{
    /// <summary>Localization key of the currency's name.</summary>
    public string NameKey => $"currency.{Code}";
}

/// <summary>
/// The supported display currencies (FR-021a) from <c>Localization/Data/currencies.json</c>, and money
/// formatting: internal units are converted with the currency's fixed rate and formatted with the
/// language's number conventions and the currency's symbol.
/// </summary>
internal sealed class MoneyFormatter
{
    private const string FileName = "currencies.json";

    private readonly Dictionary<string, Currency> _byCode;
    private readonly Dictionary<(string Code, string Culture), NumberFormatInfo> _formats = [];

    private MoneyFormatter(IReadOnlyList<Currency> currencies)
    {
        Currencies = currencies;
        _byCode = currencies.ToDictionary(c => c.Code, StringComparer.Ordinal);
    }

    public IReadOnlyList<Currency> Currencies { get; }

    public static MoneyFormatter Load() => FromData(DataFiles.Load<CurrenciesData>(FileName));

    internal static MoneyFormatter FromData(CurrenciesData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var list = data.Currencies ?? throw DataFiles.Invalid(FileName, "missing 'currencies'.");
        var currencies = new List<Currency>();
        foreach (var c in list)
        {
            if (string.IsNullOrWhiteSpace(c.Code) || c.Code.Length != 3 || string.IsNullOrEmpty(c.Symbol) || c.Rate <= 0 || c.Decimals is < 0 or > 4)
            {
                throw DataFiles.Invalid(FileName, $"currency '{c.Code}' needs a 3-letter code, a symbol, a positive rate and 0–4 decimals.");
            }

            if (currencies.Any(existing => existing.Code == c.Code))
            {
                throw DataFiles.Invalid(FileName, $"currency '{c.Code}' is listed twice.");
            }

            currencies.Add(new Currency(c.Code, c.Symbol, c.Rate, c.Decimals));
        }

        if (!currencies.Any(c => c.Code == "USD") || !currencies.Any(c => c.Code == "EUR"))
        {
            throw DataFiles.Invalid(FileName, "USD and EUR are required (language defaults).");
        }

        return new MoneyFormatter(currencies);
    }

    /// <summary>The currency with this code, or null.</summary>
    public Currency? Find(string? code) => code is not null && _byCode.TryGetValue(code, out var c) ? c : null;

    /// <summary>Default currency of a language until the player chooses one (FR-021a).</summary>
    public Currency DefaultFor(string language) => _byCode[language == "es" ? "EUR" : "USD"];

    /// <summary>The player's currency if valid, else the language default.</summary>
    public Currency Resolve(string? code, string language) => Find(code) ?? DefaultFor(language);

    /// <summary>Formats an amount of internal units in <paramref name="currency"/> for <paramref name="culture"/>.</summary>
    public string Format(long internalAmount, Currency currency, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(currency);
        ArgumentNullException.ThrowIfNull(culture);
        var value = DisplayAmount(internalAmount, currency);
        return value.ToString("C", FormatFor(currency, culture));
    }

    internal static decimal DisplayAmount(long amount, Currency currency) => Math.Round(amount * currency.Rate, currency.Decimals, MidpointRounding.AwayFromZero);

    internal NumberFormatInfo FormatFor(Currency currency, CultureInfo culture)
    {
        var key = (currency.Code, culture.Name);
        if (!_formats.TryGetValue(key, out var format))
        {
            format = (NumberFormatInfo)culture.NumberFormat.Clone();
            format.CurrencySymbol = currency.Symbol;
            format.CurrencyDecimalDigits = currency.Decimals;
            _formats[key] = format;
        }

        return format;
    }

    /// <summary>JSON shape of currencies.json.</summary>
    internal sealed class CurrenciesData
    {
        public List<CurrencyData>? Currencies { get; set; }
    }

    internal sealed class CurrencyData
    {
        public string Code { get; set; } = string.Empty;
        public string Symbol { get; set; } = string.Empty;
        public decimal Rate { get; set; }
        public int Decimals { get; set; }
    }
}
