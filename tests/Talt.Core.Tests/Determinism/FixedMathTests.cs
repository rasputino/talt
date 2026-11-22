using Talt.Core.Determinism;

namespace Talt.Core.Tests.Determinism;

public class FixedMathTests
{
    [Theory]
    [InlineData(1.5, 246)]   // 1.5 × 65536 / 400 = 245.76
    [InlineData(2.5, 410)]   // 409.6
    public void AccelerationConversion_RoundsToNearest(double tilesPerSecondSquared, int expected)
    {
        Assert.Equal(expected, FixedMath.AccelerationFromTilesPerSecondSquared(tilesPerSecondSquared));
    }

    [Theory]
    [InlineData(0.5, 1638)]   // 1638.4
    [InlineData(10.0, 32768)]
    [InlineData(4.0, 13107)]  // 13107.2
    public void SpeedConversion_RoundsToNearest(double tilesPerSecond, int expected)
    {
        Assert.Equal(expected, FixedMath.SpeedFromTilesPerSecond(tilesPerSecond));
    }

    [Fact]
    public void SecondsToTicks_UsesTwentyTicksPerSecond()
    {
        Assert.Equal(60, FixedMath.SecondsToTicks(3));
        Assert.Equal(40, FixedMath.SecondsToTicks(2));
    }

    [Fact]
    public void IntegerSqrt_IsExactForPerfectSquares()
    {
        for (ulong r = 0; r < 100_000; r += 7)
        {
            Assert.Equal(r, FixedMath.IntegerSqrt(r * r));
        }

        Assert.Equal(uint.MaxValue, FixedMath.IntegerSqrt((ulong)uint.MaxValue * uint.MaxValue));
    }

    [Fact]
    public void IntegerSqrt_RoundsDown()
    {
        for (ulong value = 2; value < (1UL << 40); value = value * 3 + 1)
        {
            var root = FixedMath.IntegerSqrt(value);
            Assert.True(root * root <= value);
            Assert.True((root + 1) * (root + 1) > value);
        }
    }

    [Fact]
    public void Length_OfAxisAndDiagonalVectors()
    {
        var tile = FixedMath.UnitsPerTile;
        Assert.Equal(16L * tile, FixedMath.Length(16L * tile, 0));
        Assert.Equal(5L * tile, FixedMath.Length(3L * tile, 4L * tile));
    }

    [Fact]
    public void BrakingDistance_UsesLongArithmetic()
    {
        // 40 tiles/s would overflow an int when squared.
        var speed = FixedMath.SpeedFromTilesPerSecond(40);
        var expected = (long)speed * speed / (2L * 410);
        Assert.Equal(expected, FixedMath.BrakingDistance(speed, 410));
        Assert.True(expected > int.MaxValue / 1000);
    }

    [Fact]
    public void TilesToUnits_ThrowsOnOverflow()
    {
        Assert.Throws<OverflowException>(() => FixedMath.TilesToUnits(1 << 20));
    }
}
