using System.Numerics;
using Talt.Client.Picking;
using Talt.Core.Content;
using Talt.Core.Determinism;
using Talt.Core.Industries;
using Talt.Core.Maps;
using Talt.Core.Terrain;

namespace Talt.Client.Tests.Picking;

public class IndustryPickTests
{
    [Fact]
    public void RayHitsAnIndustryAndReturnsItsId()
    {
        var map = MapGenerator.Generate(new MapSettings(256, 256, Seed: 7));
        var fleet = IndustryFleet.Create(GameContent.Default, map, 1950, new DeterministicRandom(7), initialOccupancyPerMille: 1000);
        Assert.True(fleet.Count > 0);
        var slot = 0;
        var x = fleet.XAt(slot);
        var z = fleet.ZAt(slot);
        var heightmap = Heightmap.FromMapData(map);
        var context = new PickContext
        {
            Heightmap = heightmap,
            World = Talt.Core.Maps.WorldBuilder.Build(map),
            Industries = fleet,
        };
        var ground = heightmap.Get(x, z);
        var ray = new Ray(new Vector3(x + 0.5f, ground + 20f, z + 0.5f), -Vector3.UnitY);

        Assert.True(new IndustryPickLayer().TryPick(ray, context, out var result));
        Assert.Equal(PickKind.Industry, result.Kind);
        Assert.Equal(fleet.IdAt(slot), result.IndustryId);
        Assert.Equal($"industry:{result.IndustryId}", result.Key);
        Assert.True(result.SameObject(result));
        Assert.False(result.SameObject(PickResult.None));
    }

    [Fact]
    public void RayAwayFromEveryIndustryMisses()
    {
        var map = MapGenerator.Generate(new MapSettings(256, 256, Seed: 7));
        var fleet = IndustryFleet.Create(GameContent.Default, map, 1950, new DeterministicRandom(7), initialOccupancyPerMille: 1000);
        var context = new PickContext
        {
            Heightmap = Heightmap.FromMapData(map),
            World = Talt.Core.Maps.WorldBuilder.Build(map),
            Industries = fleet,
        };
        var ray = new Ray(new Vector3(-100f, 100f, -100f), -Vector3.UnitX);

        Assert.False(new IndustryPickLayer().TryPick(ray, context, out _));
    }
}
