using Talt.Core.Content;
using Talt.Core.Determinism;
using Talt.Core.Industries;
using Talt.Core.Maps;

namespace Talt.Core.Tests.Industries;

public class IndustryOccupancyTests
{
    [Fact]
    public void EveryFootprintTileBelongsToTheIndustryAndNothingElse()
    {
        var map = IndustryTestData.Map(256, 256, new Site(64, 64, SiteKind.IndustryPlain, 2, 128));
        var fleet = IndustryFleet.Create(GameContent.Default, map, 1950, new DeterministicRandom(5), initialOccupancyPerMille: 1000);
        Assert.Equal(1, fleet.Count);
        var id = fleet.IdAt(0);
        Assert.True(fleet.FootprintBounds(0, out var minX, out var minZ, out var maxX, out var maxZ));

        for (var z = minZ; z < maxZ; z++)
        {
            for (var x = minX; x < maxX; x++)
            {
                Assert.True(fleet.IsOccupied(x, z));
                Assert.True(fleet.TryGetIndustryAt(x, z, out var owner));
                Assert.Equal(id, owner);
            }
        }

        Assert.False(fleet.IsOccupied(minX - 1, minZ));
        Assert.False(fleet.IsOccupied(minX, minZ - 1));
        Assert.False(fleet.IsOccupied(maxX, minZ));
        Assert.False(fleet.IsOccupied(minX, maxZ));
        Assert.False(fleet.IsOccupied(-1, 0));
        Assert.False(fleet.IsOccupied(0, -1));
        Assert.False(fleet.IsOccupied(256, 0));
    }
}
