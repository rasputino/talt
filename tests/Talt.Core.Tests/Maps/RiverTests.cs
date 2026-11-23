using Talt.Core.Maps;

namespace Talt.Core.Tests.Maps;

public class RiverTests
{
    private const TileTraits Water = TileTraits.Sea | TileTraits.Lake | TileTraits.River;

    /// <summary>
    /// Follows a river downstream through neighbouring water tiles whose surface never rises. Every
    /// river tile must reach the sea or a lake that way.
    /// </summary>
    private static bool ReachesStillWater(MapData map, int startX, int startZ)
    {
        var seen = new HashSet<int>();
        var stack = new Stack<(int X, int Z)>();
        stack.Push((startX, startZ));
        while (stack.Count > 0)
        {
            var (x, z) = stack.Pop();
            var flags = map.FlagsAt(x, z);
            if ((flags & (TileTraits.Sea | TileTraits.Lake)) != 0)
            {
                return true;
            }

            if (!seen.Add(map.Index(x, z)))
            {
                continue;
            }

            foreach (var (nx, nz) in new[] { (x - 1, z), (x + 1, z), (x, z - 1), (x, z + 1) })
            {
                if (map.Contains(nx, nz) && (map.FlagsAt(nx, nz) & Water) != 0 && map.WaterLevelAt(nx, nz) <= map.WaterLevelAt(x, z))
                {
                    stack.Push((nx, nz));
                }
            }
        }

        return false;
    }

    [Theory]
    [MemberData(nameof(MapTestSettings.MatrixData), MemberType = typeof(MapTestSettings))]
    public void EveryRiverEndsInTheSeaOrALake_WithoutEverRising(MapSettings settings)
    {
        var map = MapTestSettings.Generate(settings);
        for (var z = 0; z < map.Depth; z++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                if (map.FlagsAt(x, z).HasFlag(TileTraits.River))
                {
                    Assert.True(ReachesStillWater(map, x, z), $"River tile ({x},{z}) is a dead end.");
                }
            }
        }
    }

    [Fact]
    public void WetMapsHaveRivers_AndSpringsAreCounted()
    {
        var map = MapTestSettings.Generate(new MapSettings(512, 512, 12, WaterAmount: 40));
        Assert.True(map.Statistics.RiverTiles > 0);
        Assert.InRange(map.Statistics.Springs, 1, map.Statistics.RiverTiles);
    }

    [Fact]
    public void RiverDensity_GrowsWithWaterAmount()
    {
        var dry = MapTestSettings.Generate(new MapSettings(512, 512, 12, WaterAmount: 10));
        var wet = MapTestSettings.Generate(new MapSettings(512, 512, 12, WaterAmount: 60));

        // Wetter maps have more rivers per land tile (they also have less land).
        static double Density(MapData map) => map.Statistics.RiverTiles / (double)(map.Width * map.Depth - map.Statistics.SeaTiles);
        Assert.True(Density(wet) > Density(dry));
    }

    [Fact]
    public void FlatMap_HasNoSnowAndNoRiverCrisis()
    {
        var map = MapTestSettings.Generate(new MapSettings(256, 256, 3, Roughness: 0, MaxHeight: 8, SeaLevel: 6));
        for (var i = 0; i < map.Terrain.Length; i++)
        {
            Assert.NotEqual((byte)TerrainType.Snow, map.Terrain[i]);
        }
    }

    [Fact]
    public void LakesAreLevel_AndSitBelowTheirSurface()
    {
        var map = MapTestSettings.Generate(new MapSettings(512, 512, 31, Roughness: 90, MaxHeight: 80));
        var lakeTiles = 0;
        for (var z = 0; z < map.Depth; z++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                if (map.FlagsAt(x, z).HasFlag(TileTraits.Lake))
                {
                    lakeTiles++;
                    Assert.True(map.WaterLevelAt(x, z) >= map.HeightAt(x, z) + 3);
                    if (x + 1 < map.Width && map.FlagsAt(x + 1, z).HasFlag(TileTraits.Lake))
                    {
                        // Neighbouring lake tiles of one lake share a surface (or belong to another lake).
                        Assert.Equal(map.WaterLevelAt(x, z), map.WaterLevelAt(x + 1, z));
                    }
                }
            }
        }

        Assert.True(lakeTiles > 0, "The test map should contain lakes.");
    }
}
