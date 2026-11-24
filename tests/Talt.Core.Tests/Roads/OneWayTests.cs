using Talt.Core.Construction;
using Talt.Core.Roads;

namespace Talt.Core.Tests.Roads;

/// <summary>User story 6: one-way roads (FR-043).</summary>
public class OneWayTests
{
    [Fact]
    public void OneWay_CyclesThroughBothDirections_AndBack()
    {
        var kit = new RoadKit();
        kit.Road(5, 5, 25, 5);

        kit.Run(new SetOneWayCommand(Company.PlayerId, 15, 5));
        var first = (kit.Route(5, 5, 25, 5), kit.Route(25, 5, 5, 5));
        kit.Run(new SetOneWayCommand(Company.PlayerId, 15, 5));
        var second = (kit.Route(5, 5, 25, 5), kit.Route(25, 5, 5, 5));
        kit.Run(new SetOneWayCommand(Company.PlayerId, 15, 5));
        var third = (kit.Route(5, 5, 25, 5), kit.Route(25, 5, 5, 5));

        // Links E (2) and W (6): the lower direction, east, first.
        Assert.True(first.Item1 > 0);
        Assert.Equal(-1, first.Item2);
        Assert.Equal(-1, second.Item1);
        Assert.True(second.Item2 > 0);
        Assert.True(third.Item1 > 0 && third.Item2 > 0);
        kit.AssertNetworkMatchesRebuild();
    }

    [Fact]
    public void Junctions_CannotBeOneWay()
    {
        var kit = new RoadKit();
        kit.Road(5, 10, 25, 10);
        kit.Road(15, 3, 15, 20);

        Assert.Equal(ConstructionReasons.NotStraight, kit.Run(new SetOneWayCommand(Company.PlayerId, 15, 10)).ReasonCode);
    }

    [Fact]
    public void OthersRoads_CannotBeSwitched()
    {
        var kit = new RoadKit();
        kit.Road(5, 10, 25, 10);
        kit.State.Objects.Set(kit.Tile(12, 10), TileObjectKind.Road, TileOwner.Town(2));

        Assert.Equal(ConstructionReasons.OwnedByOther, kit.Run(new SetOneWayCommand(Company.PlayerId, 12, 10)).ReasonCode);
    }
}
