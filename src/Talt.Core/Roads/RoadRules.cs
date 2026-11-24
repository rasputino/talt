using System.Globalization;
using System.Text.Json;

namespace Talt.Core.Roads;

/// <summary>How a road type is drawn (sRGB colours as 0xRRGGBB; data-model.md RoadType.look).</summary>
public enum RoadMarkings
{
    None,
    Centre,
    Edges,
}

/// <summary>A road type from <c>roads.json</c> (data-model.md RoadType).</summary>
public sealed record RoadType(
    string Id,
    int IntroYear,
    int SpeedKmh,
    long BuildCost,
    bool Motorway,
    int SurfaceColor,
    int MarkingColor,
    RoadMarkings Markings);

/// <summary>A bridge era: the longest span from a year on.</summary>
public readonly record struct BridgeEra(int FromYear, int MaxSpan);

/// <summary>
/// Road balancing from <c>roads.json</c> (data-model.md RoadRules): types, costs, bridge and tunnel limits
/// and the path search limits. Validated at load; errors name the field (constitution VII).
/// </summary>
public sealed record RoadRules
{
    public const string FileName = "roads.json";

    /// <summary>Spans and tunnel lengths are stored in a byte.</summary>
    public const int HardMaxSpan = 250;

    private static readonly Lazy<RoadRules> DefaultRules = new(() =>
    {
        using var stream = typeof(RoadRules).Assembly.GetManifestResourceStream("Talt.Core.Roads.Data." + FileName)
            ?? throw new InvalidDataException($"Missing embedded data file '{FileName}'.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    });

    public IReadOnlyList<RoadType> Types { get; init; } = [];

    public long SlopeCost { get; init; }

    public long RemoveCost { get; init; }

    public long TownRoadRemoveCost { get; init; }

    public long OneWayCost { get; init; }

    public long ConnectorCost { get; init; }

    public long BridgeCostPerTile { get; init; }

    public IReadOnlyList<BridgeEra> BridgeEras { get; init; } = [];

    public long ElevatedCostPerTile { get; init; }

    public int ElevatedMaxSpan { get; init; }

    public long TunnelCostPerTile { get; init; }

    public int TunnelMaxLength { get; init; }

    public int MaxSearchNodes { get; init; }

    public int SearchMargin { get; init; }

    public int MaxPathTiles { get; init; }

    /// <summary>The rules shipped with the game (embedded <c>roads.json</c>).</summary>
    public static RoadRules Default => DefaultRules.Value;

    /// <summary>Index of a type id, or −1.</summary>
    public int FindType(string id)
    {
        for (var i = 0; i < Types.Count; i++)
        {
            if (string.Equals(Types[i].Id, id, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    public bool IsAvailable(int typeIndex, int year) => (uint)typeIndex < (uint)Types.Count && Types[typeIndex].IntroYear <= year;

    public bool IsMotorway(int typeIndex) => (uint)typeIndex < (uint)Types.Count && Types[typeIndex].Motorway;

    /// <summary>The best (latest introduced) non-motorway type available in a year, or −1.</summary>
    public int BestOrdinaryType(int year)
    {
        var best = -1;
        for (var i = 0; i < Types.Count; i++)
        {
            if (!Types[i].Motorway && Types[i].IntroYear <= year && (best < 0 || Types[i].IntroYear >= Types[best].IntroYear))
            {
                best = i;
            }
        }

        return best;
    }

    /// <summary>The motorway type, or −1.</summary>
    public int MotorwayType()
    {
        for (var i = 0; i < Types.Count; i++)
        {
            if (Types[i].Motorway)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// True when <paramref name="candidate"/> is better than <paramref name="current"/>: introduced later
    /// (types are ranked by introduction year, then by file order).
    /// </summary>
    public bool IsBetter(int candidate, int current) =>
        Types[candidate].IntroYear > Types[current].IntroYear
        || (Types[candidate].IntroYear == Types[current].IntroYear && candidate > current);

    /// <summary>Longest bridge span (tiles between the ends, ends included) in a year; 0 before the first era.</summary>
    public int MaxBridgeSpan(int year)
    {
        var span = 0;
        for (var i = 0; i < BridgeEras.Count; i++)
        {
            if (BridgeEras[i].FromYear <= year)
            {
                span = BridgeEras[i].MaxSpan;
            }
        }

        return span;
    }

    /// <summary>Parses and validates the data file. Throws <see cref="InvalidDataException"/> naming the field.</summary>
    public static RoadRules Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"{FileName}: not valid JSON: {exception.Message}", exception);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw Invalid("the file must contain one object");
            }

            Known(root, "", "types", "slopeCost", "removeCost", "townRoadRemoveCost", "oneWayCost", "connectorCost", "bridge", "tunnel", "planner");
            var bridge = Object(root, "bridge", "");
            Known(bridge, "bridge.", "costPerTile", "eras", "elevatedCostPerTile", "elevatedMaxSpan");
            var tunnel = Object(root, "tunnel", "");
            Known(tunnel, "tunnel.", "costPerTile", "maxLength");
            var planner = Object(root, "planner", "");
            Known(planner, "planner.", "maxSearchNodes", "searchMargin", "maxPathTiles");

            var rules = new RoadRules
            {
                Types = ParseTypes(root),
                SlopeCost = Long(root, "slopeCost", "", 0, long.MaxValue),
                RemoveCost = Long(root, "removeCost", "", 0, long.MaxValue),
                TownRoadRemoveCost = Long(root, "townRoadRemoveCost", "", 0, long.MaxValue),
                OneWayCost = Long(root, "oneWayCost", "", 0, long.MaxValue),
                ConnectorCost = Long(root, "connectorCost", "", 0, long.MaxValue),
                BridgeCostPerTile = Long(bridge, "costPerTile", "bridge.", 0, long.MaxValue),
                BridgeEras = ParseEras(bridge),
                ElevatedCostPerTile = Long(bridge, "elevatedCostPerTile", "bridge.", 0, long.MaxValue),
                ElevatedMaxSpan = (int)Long(bridge, "elevatedMaxSpan", "bridge.", 2, HardMaxSpan),
                TunnelCostPerTile = Long(tunnel, "costPerTile", "tunnel.", 0, long.MaxValue),
                TunnelMaxLength = (int)Long(tunnel, "maxLength", "tunnel.", 2, HardMaxSpan),
                MaxSearchNodes = (int)Long(planner, "maxSearchNodes", "planner.", 100, 10_000_000),
                SearchMargin = (int)Long(planner, "searchMargin", "planner.", 0, 1024),
                MaxPathTiles = (int)Long(planner, "maxPathTiles", "planner.", 2, 65_536),
            };
            return rules;
        }
    }

    private static List<RoadType> ParseTypes(JsonElement root)
    {
        if (!root.TryGetProperty("types", out var types) || types.ValueKind != JsonValueKind.Array || types.GetArrayLength() == 0)
        {
            throw Invalid("'types' must be a non-empty array");
        }

        if (types.GetArrayLength() > 250)
        {
            throw Invalid("'types' may have at most 250 entries");
        }

        var result = new List<RoadType>();
        var index = 0;
        foreach (var entry in types.EnumerateArray())
        {
            var prefix = $"types[{index}].";
            if (entry.ValueKind != JsonValueKind.Object)
            {
                throw Invalid($"'types[{index}]' must be an object");
            }

            Known(entry, prefix, "id", "introYear", "speedKmh", "buildCost", "motorway", "look");
            var id = String(entry, "id", prefix);
            if (result.Exists(t => t.Id == id))
            {
                throw Invalid($"'{prefix}id' repeats the id '{id}'");
            }

            var look = Object(entry, "look", prefix);
            Known(look, prefix + "look.", "surface", "marking", "markings");
            var markings = String(look, "markings", prefix + "look.") switch
            {
                "none" => RoadMarkings.None,
                "centre" => RoadMarkings.Centre,
                "edges" => RoadMarkings.Edges,
                var other => throw Invalid($"'{prefix}look.markings' must be none, centre or edges (was '{other}')"),
            };
            if (!entry.TryGetProperty("motorway", out var motorway) || (motorway.ValueKind != JsonValueKind.True && motorway.ValueKind != JsonValueKind.False))
            {
                throw Invalid($"'{prefix}motorway' must be true or false");
            }

            result.Add(new RoadType(
                id,
                (int)Long(entry, "introYear", prefix, 1, 9000),
                (int)Long(entry, "speedKmh", prefix, 0, 1000),
                Long(entry, "buildCost", prefix, 0, long.MaxValue),
                motorway.GetBoolean(),
                Color(look, "surface", prefix + "look."),
                Color(look, "marking", prefix + "look."),
                markings));
            index++;
        }

        if (result.Count(t => t.Motorway) > 1)
        {
            throw Invalid("'types' may have at most one motorway type");
        }

        var earliest = result.Min(t => t.IntroYear);
        if (!result.Exists(t => !t.Motorway && t.IntroYear == earliest))
        {
            throw Invalid("'types' needs a non-motorway type from the earliest introduction year");
        }

        return result;
    }

    private static List<BridgeEra> ParseEras(JsonElement bridge)
    {
        if (!bridge.TryGetProperty("eras", out var eras) || eras.ValueKind != JsonValueKind.Array || eras.GetArrayLength() == 0)
        {
            throw Invalid("'bridge.eras' must be a non-empty array");
        }

        var result = new List<BridgeEra>();
        var index = 0;
        foreach (var entry in eras.EnumerateArray())
        {
            var prefix = $"bridge.eras[{index}].";
            Known(entry, prefix, "fromYear", "maxSpan");
            var era = new BridgeEra((int)Long(entry, "fromYear", prefix, 1, 9000), (int)Long(entry, "maxSpan", prefix, 2, HardMaxSpan));
            if (result.Count > 0 && era.FromYear <= result[^1].FromYear)
            {
                throw Invalid("'bridge.eras' must be sorted by fromYear");
            }

            result.Add(era);
            index++;
        }

        return result;
    }

    private static void Known(JsonElement element, string prefix, params string[] fields)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw Invalid($"'{prefix.TrimEnd('.')}' must be an object");
        }

        foreach (var property in element.EnumerateObject())
        {
            if (Array.IndexOf(fields, property.Name) < 0)
            {
                throw Invalid($"unknown field '{prefix}{property.Name}'");
            }
        }
    }

    private static JsonElement Object(JsonElement parent, string name, string prefix)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object)
        {
            throw Invalid($"'{prefix}{name}' must be an object");
        }

        return value;
    }

    private static string String(JsonElement parent, string name, string prefix)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw Invalid($"'{prefix}{name}' must be a non-empty text");
        }

        return value.GetString()!;
    }

    private static int Color(JsonElement parent, string name, string prefix)
    {
        var text = String(parent, name, prefix);
        if (text.Length != 6 || !int.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var color))
        {
            throw Invalid($"'{prefix}{name}' must be six hex digits RRGGBB (was '{text}')");
        }

        return color;
    }

    private static long Long(JsonElement parent, string name, string prefix, long min, long max)
    {
        if (!parent.TryGetProperty(name, out var value))
        {
            throw Invalid($"'{prefix}{name}' is missing");
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var number))
        {
            throw Invalid($"'{prefix}{name}' must be a whole number");
        }

        if (number < min || number > max)
        {
            throw Invalid(max == long.MaxValue
                ? $"'{prefix}{name}' must be {min} or more (was {number})"
                : $"'{prefix}{name}' must be between {min} and {max} (was {number})");
        }

        return number;
    }

    private static InvalidDataException Invalid(string problem) => new($"{FileName}: {problem}.");
}
