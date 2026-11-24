using Talt.Core.Construction;
using Talt.Core.Network;
using Talt.Core.Roads;

namespace Talt.Core.Tests.Roads;

/// <summary>User story 5: road types over time and motorways (FR-040..FR-048).</summary>
public class RoadTypeMotorwayTests
{
    [Fact]
    public void Upgrading_ChargesTheDifference_AndChangesTheSpeedLimit()
    {
        var kit = new RoadKit(startYear: 1970);
        kit.Road(5, 5, 15, 5, "dirt");
        var balance = kit.State.Player.Balance;
        var rules = kit.State.RoadRules;

        var result = kit.Road(5, 5, 15, 5, "fast");

        Assert.True(result.Succeeded, result.ReasonCode);
        Assert.Equal(balance - (11 * (rules.Types[2].BuildCost - rules.Types[0].BuildCost)), kit.State.Player.Balance);
        Assert.Equal(2, kit.Roads.TypeAt(kit.Tile(10, 5)));
        Assert.All(kit.State.Network.Canonical(), e => Assert.Equal(100, e.SpeedKmh));
    }

    [Fact]
    public void Downgrading_ChangesNothing()
    {
        var kit = new RoadKit(startYear: 1970);
        kit.Road(5, 5, 15, 5, "fast");

        Assert.Equal(ConstructionReasons.NothingToDo, kit.Road(5, 5, 15, 5, "dirt").ReasonCode);
        Assert.Equal(2, kit.Roads.TypeAt(kit.Tile(10, 5)));
    }

    [Fact]
    public void Motorway_IsTwoOneWayCarriageways()
    {
        var kit = new RoadKit(startYear: 1990);

        var result = kit.Run(new BuildMotorwayCommand(Company.PlayerId, 10, 20, 40, 20));

        Assert.True(result.Succeeded, result.ReasonCode);
        // Travel east on z = 20; the carriageway back is on its left (north, z = 19) travelling west.
        Assert.Equal(Dir8.Bit(Dir8.E), kit.Piece(20, 20).Exits);
        Assert.Equal(Dir8.Bit(Dir8.W), kit.Piece(20, 19).Exits);
        Assert.True(kit.Route(10, 20, 40, 20) > 0);
        Assert.Equal(-1, kit.Route(40, 20, 10, 20));
        Assert.True(kit.Route(40, 19, 10, 19) > 0);
        Assert.Equal(-1, kit.Route(10, 19, 40, 19));
        kit.AssertNetworkMatchesRebuild();
    }

    [Fact]
    public void Motorway_IsUnavailableBefore1980()
    {
        var kit = new RoadKit(startYear: 1970);

        Assert.Equal(ConstructionReasons.RoadTypeUnavailable, kit.Run(new BuildMotorwayCommand(Company.PlayerId, 10, 20, 40, 20)).ReasonCode);
    }

    [Fact]
    public void ARunStartingAtACarriagewayEnd_ContinuesIt()
    {
        var kit = new RoadKit(startYear: 1990);
        kit.Run(new BuildMotorwayCommand(Company.PlayerId, 10, 20, 30, 20));

        Assert.True(kit.Run(new BuildMotorwayCommand(Company.PlayerId, 31, 20, 50, 20)).Succeeded, kit.Last.ReasonCode);

        Assert.True(kit.Route(10, 20, 50, 20) > 0);
        Assert.True(kit.Route(50, 19, 10, 19) > 0);
    }

    [Fact]
    public void RoadsJoinMotorwaysOnlyAtEnds()
    {
        var kit = new RoadKit(startYear: 1990);
        kit.Run(new BuildMotorwayCommand(Company.PlayerId, 10, 20, 40, 20));

        var middle = kit.Road(25, 30, 25, 20);
        var end = kit.Road(40, 26, 40, 20);

        Assert.Equal(ConstructionReasons.MotorwayJunction, middle.ReasonCode);
        Assert.True(end.Succeeded, end.ReasonCode);
        Assert.True(kit.Route(10, 20, 40, 26) > 0);
        Assert.Equal(-1, kit.Route(40, 26, 10, 20));
    }

    [Fact]
    public void Connectors_AddOneWayRampsOnTheOuterSide()
    {
        var kit = new RoadKit(startYear: 1990);
        kit.Run(new BuildMotorwayCommand(Company.PlayerId, 10, 20, 40, 20));

        var exit = kit.Run(new BuildMotorwayConnectorCommand(Company.PlayerId, 20, 20, true));
        var entry = kit.Run(new BuildMotorwayConnectorCommand(Company.PlayerId, 30, 20, false));
        kit.Road(20, 21, 20, 30);
        kit.Road(30, 21, 30, 30);

        Assert.True(exit.Succeeded, exit.ReasonCode);
        Assert.True(entry.Succeeded, entry.ReasonCode);
        Assert.True(kit.Piece(20, 20).IsConnector);
        Assert.True(kit.Route(10, 20, 20, 30) > 0);
        Assert.True(kit.Route(30, 30, 40, 20) > 0);
        Assert.Equal(-1, kit.Route(20, 30, 40, 20));
        kit.AssertNetworkMatchesRebuild();
    }

    [Fact]
    public void Motorway_IsElevatedOverARoad()
    {
        var kit = new RoadKit(startYear: 1990);
        kit.Road(25, 5, 25, 40);
        var below = kit.Piece(25, 20).Links;

        var result = kit.Run(new BuildMotorwayCommand(Company.PlayerId, 10, 20, 40, 20));

        Assert.True(result.Succeeded, result.ReasonCode);
        Assert.Equal(below, kit.Piece(25, 20).Links);
        Assert.True((kit.Piece(25, 20).Flags & RoadFeatures.OverBridgeX) != 0);
        Assert.True((kit.Piece(25, 19).Flags & RoadFeatures.OverBridgeX) != 0);
        Assert.True(kit.Route(10, 20, 40, 20) > 0);
        Assert.True(kit.Route(25, 5, 25, 40) > 0);
    }

    [Fact]
    public void DiagonalMotorway_KeepsItsCarriagewaysApart()
    {
        var kit = new RoadKit(startYear: 1990);

        Assert.True(kit.Run(new BuildMotorwayCommand(Company.PlayerId, 10, 40, 30, 20)).Succeeded, kit.Last.ReasonCode);

        Assert.True(kit.Route(10, 40, 30, 20) > 0);
        Assert.True(kit.Route(29, 20, 9, 40) > 0);
        var edges = kit.State.Network.Canonical();
        Assert.Equal(2, edges.Count);
        Assert.All(edges, e => Assert.Equal(20 * Dir8.DiagonalLength, e.Length));
    }
}
