namespace Talt.Core.Towns;

/// <summary>What a town building is used for (data-model.md BuildingType.Category).</summary>
public enum BuildingCategory : byte
{
    House,
    Apartment,
    Shop,
    Office,
    Civic,
}

/// <summary>Distance bands from a town centre, as a share of the town radius (research R5).</summary>
[Flags]
public enum TownZones : byte
{
    None = 0,
    Centre = 1,
    Inner = 2,
    Outer = 4,
}

/// <summary>How the client draws a building's roof (render only).</summary>
public enum RoofStyle : byte
{
    Flat,
    Pitched,
}

/// <summary>How the client draws a building's windows (render only).</summary>
public enum WindowStyle : byte
{
    None,
    Rows,
    Grid,
}

/// <summary>The procedural model of a building type (render only; colours 0xRRGGBB).</summary>
public readonly record struct BuildingModel(int WallColor, int RoofColor, RoofStyle Roof, WindowStyle Windows);

/// <summary>
/// A town building definition from <c>town-buildings.json</c> (data-model.md BuildingType). Immutable.
/// </summary>
public sealed record BuildingType(
    string Id,
    BuildingCategory Category,
    int Width,
    int Depth,
    int Height,
    int Residents,
    int PassengersPerMonth,
    int MailPerMonth,
    int GoodsAcceptance,
    int FoodAcceptance,
    int EraStart,
    int EraEnd,
    int MinTownPopulation,
    TownZones Zones,
    int Weight,
    int Level,
    BuildingModel Model)
{
    /// <summary>True when the type may be built in <paramref name="year"/>.</summary>
    public bool InEra(int year) => year >= EraStart && year <= EraEnd;

    /// <summary>Number of tiles the building covers.</summary>
    public int TileCount => Width * Depth;
}
