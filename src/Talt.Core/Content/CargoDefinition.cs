namespace Talt.Core.Content;

/// <summary>What a cargo type is for (economy classification shown to the player and used by pricing).</summary>
public enum CargoCategory : byte
{
    /// <summary>Mined, grown or pumped material that processing industries turn into goods.</summary>
    RawMaterial = 0,

    /// <summary>Goods made by a processing industry.</summary>
    ProcessedGood,

    /// <summary>People travelling between places.</summary>
    Passenger,

    /// <summary>Letters and parcels travelling between places.</summary>
    Mail,

    /// <summary>Consumer goods that want a specific destination (feature 013).</summary>
    Goods,
}

/// <summary>Kinds of vehicle that may carry a cargo (feature 016 decides the actual vehicles).</summary>
[Flags]
public enum VehicleKind : byte
{
    None = 0,
    Road = 1,
    Rail = 2,
    Ship = 4,
    Air = 8,
}

/// <summary>
/// One cargo type loaded from <c>cargo.json</c> (data-model.md, CargoDefinition). Amounts of this cargo
/// are whole numbers in its unit (tonnes, passengers, heads, bags or crates).
/// </summary>
/// <param name="Id">Stable data id such as <c>iron-ore</c>.</param>
/// <param name="NameKey">Localization key for the player-facing name.</param>
/// <param name="Category">Economic category.</param>
/// <param name="UnitKey">Localization key for the unit shown after amounts.</param>
/// <param name="DestinationRequired">True when the player must choose where the cargo goes.</param>
/// <param name="ValueDecayPerDayPerMille">Value lost per in-game day of travel, in per mille.</param>
/// <param name="MinimumValuePercent">Lowest remaining value after decay, in percent.</param>
/// <param name="Color">Packed <c>0xRRGGBB</c> colour for icons and overlays.</param>
/// <param name="VehicleKinds">Vehicle kinds that may carry it.</param>
public readonly record struct CargoDefinition(
    string Id,
    string NameKey,
    CargoCategory Category,
    string UnitKey,
    bool DestinationRequired,
    int ValueDecayPerDayPerMille,
    int MinimumValuePercent,
    uint Color,
    VehicleKind VehicleKinds)
{
    /// <summary>Unit used by this cargo (short form, for console tools; the UI uses <see cref="UnitKey"/>).</summary>
    public string Unit => UnitKey.StartsWith("unit.", StringComparison.Ordinal) ? UnitKey[5..] : UnitKey;
}

/// <summary>Monthly production of one cargo by an industry kind.</summary>
/// <param name="CargoId">Cargo produced.</param>
/// <param name="PerMonth">Units produced per in-game month at 100 % condition.</param>
public readonly record struct ProductionEntry(string CargoId, int PerMonth);

/// <summary>
/// One automatic conversion: a whole batch of <paramref name="InputCargoId"/> becomes
/// <paramref name="OutputAmount"/> of <paramref name="OutputCargoId"/> (single input, R6).
/// </summary>
public readonly record struct RecipeDefinition(string InputCargoId, int InputAmount, string OutputCargoId, int OutputAmount);

/// <summary>Storage limit for one cargo at an industry kind.</summary>
public readonly record struct StockDefinition(string CargoId, int Capacity);

/// <summary>
/// One coloured box of the placeholder model of an industry kind. Coordinates are relative to the
/// footprint centre in tiles; Y is height above the ground in voxels (data-model.md, ModelBox).
/// </summary>
public readonly record struct ModelBox(int X, int Y, int Z, int Width, int Depth, int Height, uint Color);
