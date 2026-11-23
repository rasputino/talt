using Talt.Core.Maps;

namespace Talt.Core.Tests.Maps;

/// <summary>The structural guarantees of data-model.md, checked on a matrix of seeds and sizes.</summary>
public class MapInvariantTests
{
    private const TileTraits Water = TileTraits.Sea | TileTraits.Lake | TileTraits.River;

    private static bool IsWater(MapData map, int x, int z) => (map.FlagsAt(x, z) & Water) != 0;

    private static IEnumerable<(int X, int Z)> Neighbours(MapData map, int x, int z)
    {
        if (x > 0)
        {
            yield return (x - 1, z);
        }

        if (x < map.Width - 1)
        {
            yield return (x + 1, z);
        }

        if (z > 0)
        {
            yield return (x, z - 1);
        }

        if (z < map.Depth - 1)
        {
            yield return (x, z + 1);
        }
    }

    [Theory]
    [MemberData(nameof(MapTestSettings.MatrixData), MemberType = typeof(MapTestSettings))]
    public void HeightsAreWithinRange(MapSettings settings)
    {
        var map = MapTestSettings.Generate(settings);
        for (var z = 0; z < map.Depth; z++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                var h = map.HeightAt(x, z);
                Assert.InRange(h, 1, settings.MaxHeight);
            }
        }
    }

    [Theory]
    [MemberData(nameof(MapTestSettings.MatrixData), MemberType = typeof(MapTestSettings))]
    public void BuildableLandHasNoStepsOverOneExceptCliffs(MapSettings settings)
    {
        var map = MapTestSettings.Generate(settings);
        for (var z = 0; z < map.Depth; z++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                if (!map.IsBuildable(x, z))
                {
                    continue;
                }

                foreach (var (nx, nz) in Neighbours(map, x, z))
                {
                    if (!IsWater(map, nx, nz))
                    {
                        Assert.True(Math.Abs(map.HeightAt(nx, nz) - map.HeightAt(x, z)) <= 1, $"Step over 1 at ({x},{z}).");
                    }
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(MapTestSettings.MatrixData), MemberType = typeof(MapTestSettings))]
    public void WaterNeverFloatsAboveLowerLand(MapSettings settings)
    {
        var map = MapTestSettings.Generate(settings);
        for (var z = 0; z < map.Depth; z++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                if (!IsWater(map, x, z))
                {
                    Assert.Equal(0, map.WaterLevelAt(x, z));
                    continue;
                }

                var level = map.WaterLevelAt(x, z);
                Assert.True(level > map.HeightAt(x, z), $"Water at ({x},{z}) is not above its bed.");
                foreach (var (nx, nz) in Neighbours(map, x, z))
                {
                    if (!IsWater(map, nx, nz))
                    {
                        Assert.True(map.HeightAt(nx, nz) >= level, $"Water at ({x},{z}) level {level} floats above land at ({nx},{nz}) height {map.HeightAt(nx, nz)}.");
                    }
                    else if (map.WaterLevelAt(nx, nz) != level)
                    {
                        var isRiver = map.FlagsAt(x, z).HasFlag(TileTraits.River) || map.FlagsAt(nx, nz).HasFlag(TileTraits.River);
                        Assert.True(isRiver, $"Neighbouring water bodies at ({x},{z}) differ without a river.");
                    }
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(MapTestSettings.MatrixData), MemberType = typeof(MapTestSettings))]
    public void TerrainClassesMatchRules(MapSettings settings)
    {
        var map = MapTestSettings.Generate(settings);
        var range = settings.MaxHeight - settings.SeaLevel;
        var minSnow = settings.SeaLevel + 8 - 2;
        for (var z = 0; z < map.Depth; z++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                var type = map.TerrainAt(x, z);
                Assert.Equal(IsWater(map, x, z), type == TerrainType.Water);
                var h = map.HeightAt(x, z);
                if (type == TerrainType.Snow)
                {
                    Assert.True(h >= minSnow && range >= 6, $"Snow too low at ({x},{z}).");
                }

                if (type == TerrainType.Beach)
                {
                    Assert.True(map.FlagsAt(x, z).HasFlag(TileTraits.Coast) && h <= settings.SeaLevel + 1);
                }

                if (map.SceneryAt(x, z) != Scenery.None)
                {
                    Assert.Equal(TerrainType.Grass, type);
                    Assert.False(map.FlagsAt(x, z).HasFlag(TileTraits.Cliff));
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(MapTestSettings.MatrixData), MemberType = typeof(MapTestSettings))]
    public void SitesAreOnFreeBuildableLand(MapSettings settings)
    {
        var map = MapTestSettings.Generate(settings);
        foreach (var site in map.Sites)
        {
            Assert.InRange(site.X - site.FreeRadius, 0, map.Width - 1);
            Assert.InRange(site.X + site.FreeRadius, 0, map.Width - 1);
            Assert.InRange(site.Z - site.FreeRadius, 0, map.Depth - 1);
            Assert.InRange(site.Z + site.FreeRadius, 0, map.Depth - 1);
            for (var dz = -site.FreeRadius; dz <= site.FreeRadius; dz++)
            {
                for (var dx = -site.FreeRadius; dx <= site.FreeRadius; dx++)
                {
                    Assert.True(map.IsBuildable(site.X + dx, site.Z + dz), $"Site at ({site.X},{site.Z}) is not on buildable land.");
                }
            }
        }
    }
}
