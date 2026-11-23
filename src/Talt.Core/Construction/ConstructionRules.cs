using System.Text.Json;

namespace Talt.Core.Construction;

/// <summary>
/// Construction balancing values from <c>construction.json</c> (data-model.md ConstructionRules,
/// research R13): starting money, costs, undo window and limits. Validated at load; errors name the
/// field (constitution VII).
/// </summary>
public sealed record ConstructionRules
{
    public const string FileName = "construction.json";
    public const int HardMaxHeight = 127;

    private static readonly Lazy<ConstructionRules> DefaultRules = new(() =>
    {
        using var stream = typeof(ConstructionRules).Assembly.GetManifestResourceStream("Talt.Core.Construction.Data." + FileName)
            ?? throw new InvalidDataException($"Missing embedded data file '{FileName}'.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    });

    private static readonly string[] KnownFields =
    [
        "startingBalance", "terraformStepCost", "clearTreeCost", "demolishCosts", "undoWindowTicks",
        "maxHeight", "maxTilesPerAction", "maxActionCost",
    ];

    /// <summary>Player company money at game start.</summary>
    public long StartingBalance { get; init; } = 500_000;

    /// <summary>Cost per tile per voxel step of height changed.</summary>
    public long TerraformStepCost { get; init; } = 250;

    /// <summary>Cost per tree removed.</summary>
    public long ClearTreeCost { get; init; } = 40;

    /// <summary>Cost of demolishing one object, by kind (kinds not listed cost nothing).</summary>
    public IReadOnlyDictionary<TileObjectKind, long> DemolishCosts { get; init; } = new Dictionary<TileObjectKind, long>();

    /// <summary>Ticks after an action during which it can be undone.</summary>
    public int UndoWindowTicks { get; init; } = 200;

    /// <summary>Highest ground height a terraform action may create (2 to <see cref="HardMaxHeight"/>).</summary>
    public int MaxHeight { get; init; } = 120;

    /// <summary>Most tiles one action may change.</summary>
    public int MaxTilesPerAction { get; init; } = 16_384;

    /// <summary>Largest total cost one action may have.</summary>
    public long MaxActionCost { get; init; } = 1_000_000_000_000;

    /// <summary>The rules shipped with the game (embedded <c>construction.json</c>).</summary>
    public static ConstructionRules Default => DefaultRules.Value;

    public long DemolishCost(TileObjectKind kind) => DemolishCosts.TryGetValue(kind, out var cost) ? cost : 0;

    /// <summary>Parses and validates the data file. Throws <see cref="InvalidDataException"/> naming the field.</summary>
    public static ConstructionRules Parse(string json)
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

            foreach (var property in root.EnumerateObject())
            {
                if (Array.IndexOf(KnownFields, property.Name) < 0)
                {
                    throw Invalid($"unknown field '{property.Name}'");
                }
            }

            var rules = new ConstructionRules
            {
                StartingBalance = Long(root, "startingBalance", 0, long.MaxValue),
                TerraformStepCost = Long(root, "terraformStepCost", 0, long.MaxValue),
                ClearTreeCost = Long(root, "clearTreeCost", 0, long.MaxValue),
                DemolishCosts = Costs(root),
                UndoWindowTicks = (int)Long(root, "undoWindowTicks", 0, int.MaxValue),
                MaxHeight = (int)Long(root, "maxHeight", 2, HardMaxHeight),
                MaxTilesPerAction = (int)Long(root, "maxTilesPerAction", 1, 1_048_576),
                MaxActionCost = Long(root, "maxActionCost", 1, long.MaxValue),
            };
            return rules;
        }
    }

    private static long Long(JsonElement root, string name, long min, long max)
    {
        if (!root.TryGetProperty(name, out var value))
        {
            throw Invalid($"'{name}' is missing");
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var number))
        {
            throw Invalid($"'{name}' must be a whole number");
        }

        if (number < min || number > max)
        {
            throw Invalid(max == long.MaxValue
                ? $"'{name}' must be {min} or more (was {number})"
                : $"'{name}' must be between {min} and {max} (was {number})");
        }

        return number;
    }

    private static Dictionary<TileObjectKind, long> Costs(JsonElement root)
    {
        if (!root.TryGetProperty("demolishCosts", out var costs) || costs.ValueKind != JsonValueKind.Object)
        {
            throw Invalid("'demolishCosts' must be an object of object kind → cost");
        }

        var result = new Dictionary<TileObjectKind, long>();
        foreach (var entry in costs.EnumerateObject())
        {
            if (!Enum.TryParse<TileObjectKind>(entry.Name, ignoreCase: true, out var kind) || kind == TileObjectKind.None
                || !char.IsLower(entry.Name[0]) || int.TryParse(entry.Name, out _))
            {
                throw Invalid($"'demolishCosts' names an unknown object kind '{entry.Name}'");
            }

            if (entry.Value.ValueKind != JsonValueKind.Number || !entry.Value.TryGetInt64(out var cost) || cost < 0)
            {
                throw Invalid($"'demolishCosts.{entry.Name}' must be 0 or more");
            }

            result[kind] = cost;
        }

        return result;
    }

    private static InvalidDataException Invalid(string problem) => new($"{FileName}: {problem}.");
}
