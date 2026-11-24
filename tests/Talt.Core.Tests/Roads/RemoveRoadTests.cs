using Talt.Core.Construction;
using Talt.Core.Roads;

namespace Talt.Core.Tests.Roads;

/// <summary>User story 4: remove roads (FR-030..FR-032).</summary>
public class RemoveRoadTests
{
    [Fact]
    public void RemovingAGap_BreaksRoutesInTheSameTick_AndCharges()
    {
        var kit = new RoadKit();
        kit.Road(5, 5, 25, 5);
        var balance = kit.State.Player.Balance;

        var result = kit.Run(new RemoveRoadCommand(Company.PlayerId, 14, 5, 16, 5));

        Assert.True(result.Succeeded, result.ReasonCode);
        Assert.Equal(balance - (3 * kit.State.RoadRules.RemoveCost), kit.State.Player.Balance);
        Assert.False(kit.Roads.HasRoad(kit.Tile(15, 5)));
        Assert.False(kit.Piece(13, 5).HasLink(Dir8.E));
        Assert.Equal(-1, kit.Route(5, 5, 25, 5));
        Assert.True(kit.Route(5, 5, 13, 5) > 0);
        kit.AssertNetworkMatchesRebuild();
    }

    [Fact]
    public void TownRoads_AreRemovedOnlyWhenTheTownStaysConnected()
    {
        var kit = new RoadKit();
        var town = TileOwner.Town(0);
        var type = kit.State.RoadRules.FindType("paved");
        for (var x = 10; x <= 14; x++)
        {
            var links = (byte)((x > 10 ? Dir8.Bit(Dir8.W) : 0) | (x < 14 ? Dir8.Bit(Dir8.E) : 0));
            kit.State.PlaceInitialRoad(kit.Tile(x, 10), new RoadPiece(links, links, (byte)(type + 1), RoadFeatures.None, 0, 0), town);
        }

        kit.State.RebuildNetwork();

        var middle = kit.Run(new RemoveRoadCommand(Company.PlayerId, 12, 10, 12, 10));
        var end = kit.Run(new RemoveRoadCommand(Company.PlayerId, 14, 10, 14, 10));

        Assert.Equal(ConstructionReasons.TownNeedsRoad, middle.ReasonCode);
        Assert.True(kit.Roads.HasRoad(kit.Tile(12, 10)));
        Assert.True(end.Succeeded, end.ReasonCode);
        Assert.False(kit.Roads.HasRoad(kit.Tile(14, 10)));
    }

    [Fact]
    public void RemovingAPlayerRoadAtATownJunction_KeepsTheTownRoad()
    {
        var kit = new RoadKit();
        var town = TileOwner.Town(0);
        var type = kit.State.RoadRules.FindType("paved");
        for (var x = 10; x <= 20; x++)
        {
            var links = (byte)((x > 10 ? Dir8.Bit(Dir8.W) : 0) | (x < 20 ? Dir8.Bit(Dir8.E) : 0));
            kit.State.PlaceInitialRoad(kit.Tile(x, 10), new RoadPiece(links, links, (byte)(type + 1), RoadFeatures.None, 0, 0), town);
        }

        kit.State.RebuildNetwork();
        kit.Road(15, 10, 15, 20);

        kit.Run(new RemoveRoadCommand(Company.PlayerId, 15, 11, 15, 20));

        Assert.Equal(Dir8.Bit(Dir8.W) | Dir8.Bit(Dir8.E), kit.Piece(15, 10).Links);
        Assert.True(kit.Route(10, 10, 20, 10) > 0);
        kit.AssertNetworkMatchesRebuild();
    }

    [Fact]
    public void RemovingABridgeEnd_RemovesTheWholeBridge()
    {
        var kit = new RoadKit();
        kit.Run(new BuildBridgeCommand(Company.PlayerId, "paved", 17, 10, 23, 10));

        Assert.True(kit.Run(new RemoveRoadCommand(Company.PlayerId, 23, 10, 23, 10)).Succeeded);

        Assert.False(kit.Roads.HasRoad(kit.Tile(17, 10)));
        Assert.Equal(RoadFeatures.None, kit.Piece(20, 10).Flags);
        Assert.Equal(0, kit.State.Network.NodeCount);
    }

    [Fact]
    public void NothingToRemove_IsNotARoad()
    {
        var kit = new RoadKit();

        Assert.Equal(ConstructionReasons.NotARoad, kit.Run(new RemoveRoadCommand(Company.PlayerId, 3, 3, 9, 3)).ReasonCode);
    }
}
