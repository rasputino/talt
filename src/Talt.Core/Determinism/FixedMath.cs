namespace Talt.Core.Determinism;

/// <summary>
/// Integer fixed-point helpers for simulation state.
/// </summary>
/// <remarks>
/// <para>
/// The simulation never stores <c>float</c> or <c>double</c> values, so results are bit-identical on
/// every platform (see specs/001-simulation-foundation/research.md, R1).
/// </para>
/// <para>
/// Distances are in <b>units</b>: 1 tile = <see cref="UnitsPerTile"/> units. Time is in <b>ticks</b>
/// (<see cref="TicksPerSecond"/> per second at normal speed). Speeds are units per tick and
/// accelerations units per tick per tick.
/// </para>
/// <para>
/// Methods taking <c>double</c> are for <b>setup only</b>: they convert designer-friendly constants
/// (tiles per second) once, with explicit rounding. Basic IEEE operations and
/// <see cref="Math.Round(double, MidpointRounding)"/> are deterministic, and the result is an integer
/// that is never recomputed during ticks.
/// </para>
/// </remarks>
public static class FixedMath
{
    public const int UnitsPerTileBits = 16;
    public const int UnitsPerTile = 1 << UnitsPerTileBits; // 65 536

    /// <summary>Simulation ticks per second of real time at normal speed.</summary>
    public const int TicksPerSecond = 20;

    /// <summary>Converts whole tiles to units.</summary>
    public static int TilesToUnits(int tiles) => checked(tiles * UnitsPerTile);

    /// <summary>Setup only: converts a distance in tiles to units, rounding to nearest.</summary>
    public static long TilesToUnits(double tiles) => RoundToLong(tiles * UnitsPerTile);

    /// <summary>Setup only: converts tiles per second to units per tick, rounding to nearest.</summary>
    public static int SpeedFromTilesPerSecond(double tilesPerSecond) =>
        checked((int)RoundToLong(tilesPerSecond * UnitsPerTile / TicksPerSecond));

    /// <summary>Setup only: converts tiles per second squared to units per tick squared.</summary>
    public static int AccelerationFromTilesPerSecondSquared(double tilesPerSecondSquared) =>
        checked((int)RoundToLong(tilesPerSecondSquared * UnitsPerTile / (TicksPerSecond * TicksPerSecond)));

    /// <summary>Setup only: converts seconds to whole ticks, rounding to nearest.</summary>
    public static int SecondsToTicks(double seconds) => checked((int)RoundToLong(seconds * TicksPerSecond));

    /// <summary>Rendering only: converts units to tiles.</summary>
    public static float UnitsToTiles(long units) => units / (float)UnitsPerTile;

    /// <summary>Rendering only: converts a speed in units per tick to tiles per second.</summary>
    public static float SpeedToTilesPerSecond(int unitsPerTick) => unitsPerTick * TicksPerSecond / (float)UnitsPerTile;

    /// <summary>
    /// Distance needed to stop from <paramref name="speed"/> with constant
    /// <paramref name="deceleration"/>: v² / (2a), in integer arithmetic.
    /// </summary>
    public static long BrakingDistance(int speed, int deceleration)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(deceleration);
        return (long)speed * speed / (2L * deceleration);
    }

    /// <summary>Largest integer r such that r² ≤ <paramref name="value"/> (exact, deterministic).</summary>
    public static ulong IntegerSqrt(ulong value)
    {
        if (value < 2)
        {
            return value;
        }

        // Bit-by-bit method: no floating point, exact for the whole ulong range.
        ulong result = 0;
        var bit = 1UL << 62;
        while (bit > value)
        {
            bit >>= 2;
        }

        while (bit != 0)
        {
            if (value >= result + bit)
            {
                value -= result + bit;
                result = (result >> 1) + bit;
            }
            else
            {
                result >>= 1;
            }

            bit >>= 2;
        }

        return result;
    }

    /// <summary>Euclidean length of an integer vector, rounded down.</summary>
    public static long Length(long dx, long dz) =>
        (long)IntegerSqrt((ulong)(dx * dx) + (ulong)(dz * dz));

    private static long RoundToLong(double value) => (long)Math.Round(value, MidpointRounding.AwayFromZero);
}
