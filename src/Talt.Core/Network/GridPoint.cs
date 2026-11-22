using System.Numerics;
using Talt.Core.Determinism;

namespace Talt.Core.Network;

/// <summary>
/// A position on the ground plane in fixed-point units (1 tile = <see cref="FixedMath.UnitsPerTile"/>).
/// </summary>
public readonly record struct GridPoint(int X, int Z)
{
    /// <summary>Position at the corner of tile (<paramref name="x"/>, <paramref name="z"/>).</summary>
    public static GridPoint FromTiles(int x, int z) => new(FixedMath.TilesToUnits(x), FixedMath.TilesToUnits(z));

    /// <summary>Rendering only: the position in tiles.</summary>
    public Vector2 ToTiles() => new(FixedMath.UnitsToTiles(X), FixedMath.UnitsToTiles(Z));
}
