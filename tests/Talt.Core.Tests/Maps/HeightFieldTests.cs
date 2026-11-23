using Talt.Core.Maps;

namespace Talt.Core.Tests.Maps;

public class HeightFieldTests
{
    private static double WaterShare(MapData map) => map.Statistics.SeaTiles / (double)(map.Width * map.Depth);

    /// <summary>Mean absolute height difference between neighbouring land tiles: the local relief.</summary>
    private static double Relief(MapData map)
    {
        double sum = 0;
        long count = 0;
        for (var z = 0; z < map.Depth - 1; z++)
        {
            for (var x = 0; x < map.Width - 1; x++)
            {
                if (map.WaterLevelAt(x, z) == 0 && map.WaterLevelAt(x + 1, z) == 0)
                {
                    sum += Math.Abs(map.HeightAt(x, z) - map.HeightAt(x + 1, z));
                    count++;
                }
            }
        }

        return count == 0 ? 0 : sum / count;
    }

    [Theory]
    [InlineData(256, 256)]
    [InlineData(1024, 512)]
    [InlineData(256, 1024)]
    public void Extent_IsExactlyTheRequestedSize(int width, int depth)
    {
        var map = MapTestSettings.Generate(new MapSettings(width, depth, 3));
        Assert.Equal((width, depth), (map.Width, map.Depth));
        Assert.Equal(width * depth, map.Heights.Length);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(25)]
    [InlineData(50)]
    [InlineData(80)]
    public void WaterShare_MatchesTheRequestedAmount(int water)
    {
        var map = MapTestSettings.Generate(new MapSettings(512, 512, 11, WaterAmount: water));
        Assert.InRange(WaterShare(map), (water - 1) / 100.0, (water + 1) / 100.0);
    }

    [Fact]
    public void MoreWaterSetting_MeansMoreSea_ForEverySeed()
    {
        for (var seed = 1; seed <= 4; seed++)
        {
            var dry = MapTestSettings.Generate(new MapSettings(256, 256, seed, WaterAmount: 15));
            var wet = MapTestSettings.Generate(new MapSettings(256, 256, seed, WaterAmount: 45));
            Assert.True(WaterShare(wet) > WaterShare(dry));
        }
    }

    [Fact]
    public void HigherRoughness_MeansMoreLocalRelief_ForEverySeed()
    {
        for (var seed = 1; seed <= 4; seed++)
        {
            var smooth = MapTestSettings.Generate(new MapSettings(256, 256, seed, Roughness: 10, MaxHeight: 60));
            var rough = MapTestSettings.Generate(new MapSettings(256, 256, seed, Roughness: 90, MaxHeight: 60));
            Assert.True(Relief(rough) > Relief(smooth), $"Seed {seed}: {Relief(rough)} vs {Relief(smooth)}");
        }
    }

    [Fact]
    public void HigherMaxHeight_MeansHigherPeaks()
    {
        var low = MapTestSettings.Generate(new MapSettings(512, 512, 5, MaxHeight: 24));
        var high = MapTestSettings.Generate(new MapSettings(512, 512, 5, MaxHeight: 120));
        Assert.True(Peak(high) > Peak(low));
        Assert.True(Peak(low) <= 24);
    }

    private static int Peak(MapData map)
    {
        var peak = 0;
        foreach (var h in map.Heights)
        {
            peak = Math.Max(peak, h);
        }

        return peak;
    }

    [Theory]
    [InlineData(0, 8, 2, 0)]
    [InlineData(100, 127, 125, 90)]
    [InlineData(50, 8, 6, 90)]
    [InlineData(0, 28, 7, 50)]
    [InlineData(100, 28, 2, 10)]
    public void ExtremeSettings_StillGenerateValidMaps(int roughness, int maxHeight, int seaLevel, int water)
    {
        var map = MapTestSettings.Generate(new MapSettings(256, 256, 77, roughness, maxHeight, water, seaLevel));
        Assert.Equal(256 * 256, map.Heights.Length);
        foreach (var h in map.Heights)
        {
            Assert.InRange(h, 1, maxHeight);
        }
    }

    [Fact]
    public void NoSeaWanted_StillGivesWaterSomewhereToGo()
    {
        var map = MapTestSettings.Generate(new MapSettings(256, 256, 9, WaterAmount: 0));
        Assert.True(map.Statistics.SeaTiles >= 1);
    }

    [Fact]
    public void LandIsMostlyBuildable_WithDefaultSettings()
    {
        var map = MapTestSettings.Generate(new MapSettings(512, 512, 21));
        long land = 0, buildable = 0;
        for (var z = 0; z < map.Depth; z++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                if (map.TerrainAt(x, z) != TerrainType.Water)
                {
                    land++;
                    if (map.IsBuildable(x, z))
                    {
                        buildable++;
                    }
                }
            }
        }

        Assert.True(buildable >= land * 0.9, $"{buildable} of {land} land tiles are buildable.");
    }
}
