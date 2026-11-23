using Talt.Core.Maps;

namespace Talt.Core.Tests.Maps;

public class ScenerySeederTests
{
    [Fact]
    public void Trees_GrowOnlyOnPlainGrass_AndKindsAreValid()
    {
        var map = MapTestSettings.Generate(new MapSettings(512, 512, 14));
        var trees = 0;
        for (var z = 0; z < map.Depth; z++)
        {
            for (var x = 0; x < map.Width; x++)
            {
                var scenery = map.SceneryAt(x, z);
                if (scenery == Scenery.None)
                {
                    continue;
                }

                trees++;
                Assert.Equal(TerrainType.Grass, map.TerrainAt(x, z));
                Assert.InRange(Scenery.Kind(scenery), 1, 3);
                Assert.InRange(Scenery.Variant(scenery), 0, 31);
            }
        }

        Assert.True(trees > 0);
    }

    [Fact]
    public void Forests_AreDenserThanOpenLand()
    {
        var map = MapTestSettings.Generate(new MapSettings(512, 512, 14));

        // Density per 16 x 16 block of grass: the densest blocks must be far denser than the sparsest.
        var densities = new List<double>();
        for (var bz = 0; bz < map.Depth; bz += 16)
        {
            for (var bx = 0; bx < map.Width; bx += 16)
            {
                int grass = 0, trees = 0;
                for (var z = bz; z < bz + 16; z++)
                {
                    for (var x = bx; x < bx + 16; x++)
                    {
                        if (map.TerrainAt(x, z) == TerrainType.Grass)
                        {
                            grass++;
                            if (map.SceneryAt(x, z) != Scenery.None)
                            {
                                trees++;
                            }
                        }
                    }
                }

                if (grass >= 200)
                {
                    densities.Add(trees / (double)grass);
                }
            }
        }

        densities.Sort();
        Assert.True(densities[^1] > densities[0] * 5 + 0.1, $"Densest {densities[^1]:F2}, sparsest {densities[0]:F2}.");
    }

    [Fact]
    public void WetterMaps_HaveMoreTrees()
    {
        static int Count(MapData map) => map.SceneryData.ToArray().Count(s => s != Scenery.None);
        var dry = MapTestSettings.Generate(new MapSettings(256, 256, 3, WaterAmount: 5));
        var wet = MapTestSettings.Generate(new MapSettings(256, 256, 3, WaterAmount: 40));
        Assert.True(Count(wet) * (256 * 256 - dry.Statistics.SeaTiles) > Count(dry) * (256 * 256 - wet.Statistics.SeaTiles) * 0.9);
    }

    [Fact]
    public void Scenery_PackingRoundTrips()
    {
        var packed = Scenery.Pack(Scenery.KindConifer, 21);
        Assert.Equal(Scenery.KindConifer, Scenery.Kind(packed));
        Assert.Equal(21, Scenery.Variant(packed));
    }
}
