namespace Talt.Core.Maps;

/// <summary>What a reserved site is meant for (features 008 and 009 found towns and industries there).</summary>
public enum SiteKind : byte
{
    Town = 0,
    IndustryCoast,
    IndustryForest,
    IndustryHills,
    IndustryPlain,
}

/// <summary>
/// A reserved location. The square of tiles within <paramref name="FreeRadius"/> of the centre is
/// guaranteed to be buildable land.
/// </summary>
/// <param name="X">Tile column of the centre.</param>
/// <param name="Z">Tile row of the centre.</param>
/// <param name="Kind">What the site suits.</param>
/// <param name="FreeRadius">Radius in tiles of the guaranteed free square (towns 4, industries 2).</param>
/// <param name="Score">0 to 255: how well the spot suits its kind (flatness, nearness to coast or forest).</param>
public readonly record struct Site(int X, int Z, SiteKind Kind, int FreeRadius, int Score);
