using Talt.Core.Maps;

namespace Talt.Core.Tests.Maps;

public class SiteSelectorTests
{
    private const TileTraits Water = TileTraits.Sea | TileTraits.Lake | TileTraits.River;

    /// <summary>Id of the connected land component of every tile (-1 for water) and the largest id.</summary>
    private static (int[] Ids, int Largest) Components(MapData map)
    {
        var ids = new int[map.Width * map.Depth];
        Array.Fill(ids, -1);
        var sizes = new List<int>();
        var queue = new Queue<int>();
        for (var start = 0; start < ids.Length; start++)
        {
            if (ids[start] != -1 || (map.Flags[start] & (byte)Water) != 0)
            {
                continue;
            }

            var id = sizes.Count;
            var size = 0;
            ids[start] = id;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                size++;
                var x = c % map.Width;
                var z = c / map.Width;
                foreach (var (nx, nz) in new[] { (x - 1, z), (x + 1, z), (x, z - 1), (x, z + 1) })
                {
                    if (map.Contains(nx, nz) && ids[map.Index(nx, nz)] == -1 && (map.Flags[map.Index(nx, nz)] & (byte)Water) == 0)
                    {
                        ids[map.Index(nx, nz)] = id;
                        queue.Enqueue(map.Index(nx, nz));
                    }
                }
            }

            sizes.Add(size);
        }

        var largest = sizes.Count == 0 ? -1 : sizes.IndexOf(sizes.Max());
        return (ids, largest);
    }

    private static double Distance(Site a, Site b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Z - b.Z, 2));

    [Theory]
    [MemberData(nameof(MapTestSettings.MatrixData), MemberType = typeof(MapTestSettings))]
    public void TownSites_AreOnTheLargestLandmass_AtLeast95Percent(MapSettings settings)
    {
        var map = MapTestSettings.Generate(settings);
        var towns = map.TownSites.ToList();
        if (towns.Count == 0)
        {
            return;
        }

        var (ids, largest) = Components(map);
        var connected = towns.Count(t => ids[map.Index(t.X, t.Z)] == largest);
        Assert.True(connected >= towns.Count * 0.95, $"{connected} of {towns.Count} towns are on the main landmass.");
    }

    [Theory]
    [MemberData(nameof(MapTestSettings.MatrixData), MemberType = typeof(MapTestSettings))]
    public void MinimumDistancesHold(MapSettings settings)
    {
        var map = MapTestSettings.Generate(settings);
        var tuning = MapGenerationTuning.Default;
        foreach (var a in map.Sites)
        {
            foreach (var b in map.Sites)
            {
                if (a == b)
                {
                    continue;
                }

                var minimum = (a.Kind == SiteKind.Town, b.Kind == SiteKind.Town) switch
                {
                    (true, true) => tuning.MinTownDistance,
                    (false, false) => tuning.MinIndustryDistance,
                    _ => tuning.MinTownIndustryDistance,
                };
                Assert.True(Distance(a, b) >= minimum, $"Sites at ({a.X},{a.Z}) and ({b.X},{b.Z}) are too close.");
            }
        }
    }

    [Theory]
    [MemberData(nameof(MapTestSettings.MatrixData), MemberType = typeof(MapTestSettings))]
    public void EveryIndustrySite_IsNearATown_ButNotTooNear(MapSettings settings)
    {
        var map = MapTestSettings.Generate(settings);
        var tuning = MapGenerationTuning.Default;
        var towns = map.TownSites.ToList();
        foreach (var industry in map.IndustrySites)
        {
            var nearest = towns.Min(t => Distance(t, industry));
            Assert.InRange(nearest, tuning.MinTownIndustryDistance, tuning.MaxTownIndustryDistance);
        }
    }

    [Theory]
    [InlineData(-1, 120)]
    [InlineData(60, 60)]
    [InlineData(80, 40)]
    public void InconsistentTownIndustryDistances_AreRefused(int min, int max)
    {
        var tuning = MapGenerationTuning.Default with { MinTownIndustryDistance = min, MaxTownIndustryDistance = max };
        Assert.Throws<ArgumentException>(() => MapGenerator.Generate(new MapSettings(256, 256, 1), tuning: tuning));
    }

    [Fact]
    public void LargerMaps_GetMoreSites()
    {
        var small = MapTestSettings.Generate(new MapSettings(256, 256, 8));
        var large = MapTestSettings.Generate(new MapSettings(1024, 1024, 8));
        Assert.True(large.Sites.Count > small.Sites.Count * 4, $"{large.Sites.Count} vs {small.Sites.Count}");
        Assert.True(large.TownSites.Count() >= 5);
    }

    [Fact]
    public void WaterWorlds_YieldFewSitesWithoutErrors()
    {
        var map = MapTestSettings.Generate(new MapSettings(256, 256, 8, WaterAmount: 90));
        Assert.All(map.Sites, s => Assert.True(map.IsBuildable(s.X, s.Z)));
        Assert.True(map.Sites.Count <= 3);
    }

    [Fact]
    public void Sites_AreSortedAndTheyAreDeterministic()
    {
        var map = MapTestSettings.Generate(new MapSettings(1024, 512, 15));
        var sorted = map.Sites.OrderBy(s => s.Kind).ThenBy(s => s.Z).ThenBy(s => s.X).ToList();
        Assert.Equal(sorted, map.Sites);
    }

    [Fact]
    public void IndustryKinds_SuitTheirSurroundings()
    {
        var map = MapTestSettings.Generate(new MapSettings(1024, 1024, 19, WaterAmount: 30));
        var industries = map.IndustrySites.ToList();
        Assert.NotEmpty(industries);
        foreach (var site in industries.Where(s => s.Kind == SiteKind.IndustryCoast))
        {
            var near = false;
            for (var dz = -8; dz <= 8 && !near; dz++)
            {
                for (var dx = -8; dx <= 8 && !near; dx++)
                {
                    var (x, z) = (site.X + dx, site.Z + dz);
                    near = map.Contains(x, z) && (map.FlagsAt(x, z) & (TileTraits.Sea | TileTraits.Lake)) != 0;
                }
            }

            Assert.True(near, "A coastal site must have water within 8 tiles.");
        }
    }
}
