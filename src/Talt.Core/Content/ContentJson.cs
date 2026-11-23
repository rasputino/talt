using System.Text.Json;

namespace Talt.Core.Content;

/// <summary>JSON shapes of the content files. They are mapped and validated by <see cref="GameContent"/>.</summary>
internal sealed class CargoFileJson
{
    public List<CargoJson>? Cargo { get; set; }
}

internal sealed class CargoJson
{
    public string? Id { get; set; }

    public string? NameKey { get; set; }

    public string? Category { get; set; }

    public string? UnitKey { get; set; }

    public bool? DestinationRequired { get; set; }

    public int? ValueDecayPerDayPerMille { get; set; }

    public int? MinimumValuePercent { get; set; }

    public string? Color { get; set; }

    public List<string>? VehicleKinds { get; set; }
}

internal sealed class IndustryFileJson
{
    public EconomyJson? Economy { get; set; }

    public List<IndustryJson>? Industries { get; set; }
}

internal sealed class EconomyJson
{
    public int? InitialOccupancyPercent { get; set; }

    public int? DriftPerMille { get; set; }

    public int? GrowthPerMille { get; set; }

    public int? DeclineAfterMonths { get; set; }

    public int? DeclinePerMille { get; set; }

    public int? CloseAfterMonths { get; set; }

    public int? CloseChancePerMille { get; set; }

    public int? CloseAtRatePerMille { get; set; }

    public int? OpeningChancePerMillePerSite { get; set; }

    public int? MaxOpeningsPerMonth { get; set; }

    public int? MinRatePerMille { get; set; }

    public int? MaxRatePerMille { get; set; }
}

internal sealed class IndustryJson
{
    public string? Id { get; set; }

    public string? NameKey { get; set; }

    public List<int>? Footprint { get; set; }

    public List<string>? SiteKinds { get; set; }

    public List<int>? Era { get; set; }

    public int? Weight { get; set; }

    public List<ProductionJson>? Production { get; set; }

    public List<string>? Accepts { get; set; }

    public List<RecipeJson>? Recipes { get; set; }

    public List<StockJson>? Stock { get; set; }

    public List<ModelBoxJson>? Model { get; set; }
}

internal sealed class ProductionJson
{
    public string? Cargo { get; set; }

    public int? PerMonth { get; set; }
}

internal sealed class RecipeJson
{
    public string? Input { get; set; }

    public int? InputAmount { get; set; }

    public string? Output { get; set; }

    public int? OutputAmount { get; set; }
}

internal sealed class StockJson
{
    public string? Cargo { get; set; }

    public int? Capacity { get; set; }
}

internal sealed class ModelBoxJson
{
    public int? X { get; set; }

    public int? Y { get; set; }

    public int? Z { get; set; }

    public int? Width { get; set; }

    public int? Depth { get; set; }

    public int? Height { get; set; }

    public string? Color { get; set; }
}

/// <summary>Parsing helpers shared by the embedded and test content loaders.</summary>
internal static class ContentJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static T Deserialize<T>(string json, string fileName)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, Options)
                ?? throw new InvalidDataException($"Data file '{fileName}' is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"Data file '{fileName}' is not valid JSON: {exception.Message}", exception);
        }
    }
}
