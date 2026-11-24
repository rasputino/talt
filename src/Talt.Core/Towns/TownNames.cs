using System.Text.Json;
using Talt.Core.Determinism;

namespace Talt.Core.Towns;

/// <summary>The name parts of one style (data-model.md TownNameParts).</summary>
public sealed record TownNameStyleParts(
    IReadOnlyList<string> Starts,
    IReadOnlyList<string> Middles,
    IReadOnlyList<string> Ends,
    IReadOnlyList<string> PrefixWords,
    IReadOnlyList<string> SuffixWords,
    int StartEndWeight,
    int StartMiddleEndWeight,
    int PrefixNameWeight,
    int NameSuffixWeight)
{
    public int TotalWeight => StartEndWeight + StartMiddleEndWeight + PrefixNameWeight + NameSuffixWeight;
}

/// <summary>
/// Generates original, unique town names from <c>town-names.json</c> (research R4). Deterministic: the names
/// depend only on the random stream passed in and on the names already used.
/// </summary>
public sealed class TownNames
{
    public const string FileName = "town-names.json";

    private const int MaxRedraws = 32;

    private static readonly Lazy<TownNames> DefaultNames = new(() => Parse(TownJson.ReadResource(FileName)), LazyThreadSafetyMode.ExecutionAndPublication);

    private TownNames(TownNameStyleParts english, TownNameStyleParts spanish)
    {
        English = english;
        Spanish = spanish;
    }

    public static TownNames Default => DefaultNames.Value;

    public TownNameStyleParts English { get; }

    public TownNameStyleParts Spanish { get; }

    /// <summary>
    /// A name not in <paramref name="used"/> (which it is added to). Mixed style picks English or Spanish
    /// from the stream first.
    /// </summary>
    public string Generate(TownNameStyle style, DeterministicRandom random, HashSet<string> used)
    {
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(used);
        var parts = style switch
        {
            TownNameStyle.English => English,
            TownNameStyle.Spanish => Spanish,
            _ => random.NextInt(2) == 0 ? English : Spanish,
        };

        string name = "";
        for (var attempt = 0; attempt < MaxRedraws; attempt++)
        {
            name = Compose(parts, random);
            if (used.Add(name))
            {
                return name;
            }
        }

        // Extremely large maps: number the last attempt so the name is still unique.
        for (var number = 2; ; number++)
        {
            var numbered = $"{name} {ToRoman(number)}";
            if (used.Add(numbered))
            {
                return numbered;
            }
        }
    }

    /// <summary>Parses and validates the name file; errors name the field.</summary>
    public static TownNames Parse(string json)
    {
        var reader = new TownJson(FileName);
        using var document = reader.Parse(json);
        var root = document.RootElement;
        reader.Known(root, "", "english", "spanish");
        return new TownNames(ParseStyle(reader, root, "english"), ParseStyle(reader, root, "spanish"));
    }

    private static string Compose(TownNameStyleParts parts, DeterministicRandom random)
    {
        var pick = random.NextInt(parts.TotalWeight);
        var core = Pick(parts.Starts, random);
        if (pick < parts.StartEndWeight)
        {
            return Join(core, Pick(parts.Ends, random));
        }

        pick -= parts.StartEndWeight;
        if (pick < parts.StartMiddleEndWeight)
        {
            return Join(Join(core, Pick(parts.Middles, random)), Pick(parts.Ends, random));
        }

        pick -= parts.StartMiddleEndWeight;
        var name = Join(core, Pick(parts.Ends, random));
        return pick < parts.PrefixNameWeight
            ? $"{Pick(parts.PrefixWords, random)} {name}"
            : $"{name} {Pick(parts.SuffixWords, random)}";
    }

    /// <summary>Joins two parts, avoiding three equal letters in a row ("Kell" + "ley" → "Kelley").</summary>
    private static string Join(string a, string b)
    {
        if (a.Length >= 2 && b.Length >= 1 && char.ToLowerInvariant(a[^1]) == b[0] && char.ToLowerInvariant(a[^2]) == b[0])
        {
            return a + b[1..];
        }

        return a + b;
    }

    private static string Pick(IReadOnlyList<string> list, DeterministicRandom random) => list[random.NextInt(list.Count)];

    private static string ToRoman(int number) => number switch
    {
        2 => "II",
        3 => "III",
        4 => "IV",
        5 => "V",
        6 => "VI",
        7 => "VII",
        8 => "VIII",
        9 => "IX",
        _ => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    private static TownNameStyleParts ParseStyle(TownJson reader, JsonElement root, string style)
    {
        var element = reader.Object(root, style, "");
        var p = style + ".";
        reader.Known(element, p, "starts", "middles", "ends", "prefixWords", "suffixWords", "patterns");
        var patterns = reader.Object(element, "patterns", p);
        reader.Known(patterns, p + "patterns.", "startEnd", "startMiddleEnd", "prefixName", "nameSuffix");
        var startEnd = reader.Int(patterns, "startEnd", p + "patterns.", 0, 1000);
        var startMiddleEnd = reader.Int(patterns, "startMiddleEnd", p + "patterns.", 0, 1000);
        var prefixName = reader.Int(patterns, "prefixName", p + "patterns.", 0, 1000);
        var nameSuffix = reader.Int(patterns, "nameSuffix", p + "patterns.", 0, 1000);
        if (startEnd + startMiddleEnd + prefixName + nameSuffix == 0)
        {
            throw reader.Invalid($"'{p}patterns' weights must not all be 0");
        }

        return new TownNameStyleParts(
            Words(reader, element, "starts", p, allowEmpty: false, words: false),
            Words(reader, element, "middles", p, allowEmpty: startMiddleEnd == 0, words: false),
            Words(reader, element, "ends", p, allowEmpty: false, words: false),
            Words(reader, element, "prefixWords", p, allowEmpty: prefixName == 0, words: true),
            Words(reader, element, "suffixWords", p, allowEmpty: nameSuffix == 0, words: true),
            startEnd,
            startMiddleEnd,
            prefixName,
            nameSuffix);
    }

    private static string[] Words(TownJson reader, JsonElement parent, string name, string prefix, bool allowEmpty, bool words)
    {
        var array = reader.ArrayOf(parent, name, prefix, allowEmpty);
        var result = new List<string>();
        var index = 0;
        foreach (var item in array.EnumerateArray())
        {
            var path = $"{prefix}{name}[{index}]";
            if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
            {
                throw reader.Invalid($"'{path}' must be a non-empty text");
            }

            var text = item.GetString()!;
            foreach (var c in text)
            {
                if (!(char.IsLetter(c) || c == '\'' || c == '-' || (words && c == ' ')))
                {
                    throw reader.Invalid($"'{path}' may only use letters, apostrophes and hyphens{(words ? " and spaces" : "")} (was '{text}')");
                }
            }

            if (result.Contains(text))
            {
                throw reader.Invalid($"'{path}' repeats '{text}'");
            }

            result.Add(text);
            index++;
        }

        return [.. result];
    }
}
