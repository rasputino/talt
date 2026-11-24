using Talt.Core.Determinism;

namespace Talt.Core.Roads;

/// <summary>
/// The eight directions a road piece can connect towards (data-model.md Dir8): 0 N (−Z), 1 NE, 2 E (+X),
/// 3 SE, 4 S (+Z), 5 SW, 6 W (−X), 7 NW. Even directions are straight (along a grid axis), odd ones are
/// 45° diagonals. Directions are bit positions in the link and exit masks of <see cref="RoadLayer"/>.
/// </summary>
public static class Dir8
{
    public const int N = 0;
    public const int NE = 1;
    public const int E = 2;
    public const int SE = 3;
    public const int S = 4;
    public const int SW = 5;
    public const int W = 6;
    public const int NW = 7;

    /// <summary>Length of a straight step, in units (one tile).</summary>
    public const int StraightLength = FixedMath.UnitsPerTile;

    /// <summary>Length of a diagonal step, in units: 65 536 · √2 rounded to the nearest unit.</summary>
    public const int DiagonalLength = 92_682;

    private static readonly int[] Dx = [0, 1, 1, 1, 0, -1, -1, -1];
    private static readonly int[] Dz = [-1, -1, 0, 1, 1, 1, 0, -1];

    public static int OffsetX(int direction) => Dx[direction];

    public static int OffsetZ(int direction) => Dz[direction];

    public static int Opposite(int direction) => (direction + 4) & 7;

    /// <summary>Rotates clockwise (seen from above, north up) by <paramref name="steps"/> × 45°.</summary>
    public static int Rotate(int direction, int steps) => (direction + steps) & 7;

    public static bool IsStraight(int direction) => (direction & 1) == 0;

    public static int StepLength(int direction) => IsStraight(direction) ? StraightLength : DiagonalLength;

    public static byte Bit(int direction) => (byte)(1 << direction);

    /// <summary>The direction of a unit step (dx, dz in −1..1, not both 0), or −1.</summary>
    public static int FromOffset(int dx, int dz)
    {
        for (var d = 0; d < 8; d++)
        {
            if (Dx[d] == dx && Dz[d] == dz)
            {
                return d;
            }
        }

        return -1;
    }

    /// <summary>
    /// The direction of the 45° octant closest to the vector (dx, dz) (for straight-line drags), or −1 for
    /// a zero vector. Exact integer comparisons: a step is diagonal when the smaller component is more
    /// than tan(22.5°) ≈ 0.414 of the larger one (5/12 is used, close enough and exact).
    /// </summary>
    public static int Octant(int dx, int dz)
    {
        if (dx == 0 && dz == 0)
        {
            return -1;
        }

        var ax = Math.Abs(dx);
        var az = Math.Abs(dz);
        var sx = Math.Sign(dx);
        var sz = Math.Sign(dz);
        if (ax * 12L < az * 5L)
        {
            sx = 0;
        }
        else if (az * 12L < ax * 5L)
        {
            sz = 0;
        }

        return FromOffset(sx, sz);
    }

    /// <summary>Number of links in a mask.</summary>
    public static int Count(byte mask) => System.Numerics.BitOperations.PopCount(mask);
}
