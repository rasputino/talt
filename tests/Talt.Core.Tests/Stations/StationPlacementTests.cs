using Talt.Core.Construction;
using Talt.Core.Roads;
using Talt.Core.Stations;
using Talt.Core.Tests.Construction;

namespace Talt.Core.Tests.Stations;

/// <summary>User story 1: building and removing bays and drive-through stops (FR-001–FR-007, research R2).</summary>
public sealed class StationPlacementTests
{
    [Fact]
    public void Bay_BesideRoad_IsBuiltLinkedAndPaid()
    {
        var kit = StationKit.Flat();
        Assert.True(kit.Road(10, 20, 30, 20).Succeeded);
        var before = kit.State.Player.Balance;

        var result = kit.Build("bus-bay", 15, 21);

        Assert.True(result.Succeeded, result.ReasonCode);
        var tile = kit.Tile(15, 21);
        Assert.Equal(TileObjectKind.Station, kit.State.Objects.KindAt(tile));
        Assert.True(kit.State.Objects.OwnerAt(tile).IsCompany(Company.PlayerId));
        Assert.Equal(Dir8.Bit(Dir8.N), kit.State.Roads.LinksAt(tile));
        Assert.True(kit.State.Roads.PieceAt(kit.Tile(15, 20)).HasLink(Dir8.S));
        Assert.Equal(before - kit.Stations.Rules.Pieces[kit.Stations.Rules.FindPiece("bus-bay")].Cost, kit.State.Player.Balance);
        Assert.Equal(1, kit.Stations.Count);
        Assert.True(kit.State.Network.TryGetNode(tile, out _));
    }

    [Fact]
    public void Bay_WithoutRoad_IsRefused()
    {
        var kit = StationKit.Flat();
        var result = kit.Build("truck-bay", 40, 40);
        Assert.Equal(ConstructionReasons.NoRoad, result.ReasonCode);
        Assert.Equal(0, kit.Stations.Count);
    }

    [Fact]
    public void Bay_OnWaterOrSlopeOrOccupied_IsRefused()
    {
        var kit = StationKit.Flat(new TestMap().Water(15, 21, 4, 8).Height(16, 21, 11));
        Assert.True(kit.Road(10, 20, 30, 20).Succeeded);
        Assert.Equal(ConstructionReasons.Water, kit.Build("bus-bay", 15, 21).ReasonCode);
        Assert.Equal(ConstructionReasons.NotFlat, kit.Build("bus-bay", 16, 21).ReasonCode);
        Assert.Equal(ConstructionReasons.ObjectInTheWay, kit.Build("bus-bay", 20, 20).ReasonCode); // the road itself
    }

    [Fact]
    public void Bay_NotEnoughMoney_IsRefused()
    {
        var kit = StationKit.Flat(balance: 100_000);
        Assert.True(kit.Road(10, 20, 12, 20).Succeeded);
        while (kit.State.Player.Balance > 1000)
        {
            kit.State.Player.Spend(kit.State.Player.Balance - 500);
        }

        Assert.Equal(ConstructionReasons.NotEnoughMoney, kit.Build("bus-bay", 11, 21).ReasonCode);
    }

    [Fact]
    public void DriveThrough_OnStraightRoad_KeepsTheRoad()
    {
        var kit = StationKit.Flat();
        Assert.True(kit.Road(10, 20, 30, 20).Succeeded);
        var piece = kit.State.Roads.PieceAt(kit.Tile(20, 20));

        Assert.True(kit.Build("truck-stop", 20, 20).Succeeded, kit.Last.ReasonCode);

        Assert.Equal(piece, kit.State.Roads.PieceAt(kit.Tile(20, 20)));
        Assert.Equal(TileObjectKind.Road, kit.State.Objects.KindAt(kit.Tile(20, 20)));
        Assert.True(kit.Stations.HasPiece(kit.Tile(20, 20)));
    }

    [Fact]
    public void DriveThrough_OnJunctionOrEndOrNoRoad_IsRefused()
    {
        var kit = StationKit.Flat();
        Assert.True(kit.Road(10, 20, 30, 20).Succeeded);
        Assert.True(kit.Road(20, 20, 20, 30).Succeeded);
        Assert.Equal(ConstructionReasons.NotStraightRoad, kit.Build("bus-stop", 20, 20).ReasonCode); // junction
        Assert.Equal(ConstructionReasons.NotStraightRoad, kit.Build("bus-stop", 10, 20).ReasonCode); // dead end
        Assert.Equal(ConstructionReasons.NotARoad, kit.Build("bus-stop", 40, 40).ReasonCode);
    }

    [Fact]
    public void StationRoads_CannotBeRemovedOrJoined()
    {
        var kit = StationKit.Flat();
        Assert.True(kit.Road(10, 20, 30, 20).Succeeded);
        Assert.True(kit.Build("bus-stop", 25, 20).Succeeded);
        Assert.True(kit.Build("bus-bay", 15, 21).Succeeded);

        Assert.Equal(ConstructionReasons.StationRoad, kit.Run(new RemoveRoadCommand(Company.PlayerId, 25, 20, 25, 20)).ReasonCode);
        Assert.Equal(ConstructionReasons.StationRoad, kit.Run(new RemoveRoadCommand(Company.PlayerId, 15, 20, 15, 20)).ReasonCode);
        Assert.Equal(ConstructionReasons.StationRoad, kit.Road(25, 20, 25, 25).ReasonCode); // would make a junction
        Assert.True(kit.Road(15, 20, 15, 15).Succeeded); // the bay's road may gain links
    }

    [Fact]
    public void ClearTool_SkipsStationTiles()
    {
        var kit = StationKit.Flat();
        Assert.True(kit.Road(10, 20, 30, 20).Succeeded);
        Assert.True(kit.Build("bus-bay", 15, 21).Succeeded);
        var plan = new ConstructionPlan();
        new ConstructionPlanner().PlanClear(kit.State, Company.PlayerId, new TileRect(14, 21, 16, 22), plan);
        Assert.Contains(plan.Problems.ToArray(), p => p.Index == kit.Tile(15, 21) && p.Reason == ConstructionReasons.UseStationTool);
    }

    [Fact]
    public void RemovingABay_RestoresTheTilesAndTheStationGoes()
    {
        var kit = StationKit.Flat();
        Assert.True(kit.Road(10, 20, 30, 20).Succeeded);
        var road = kit.State.Roads.PieceAt(kit.Tile(15, 20));
        Assert.True(kit.Build("bus-bay", 15, 21).Succeeded);

        Assert.True(kit.Remove(15, 21).Succeeded, kit.Last.ReasonCode);

        Assert.Equal(TileObjectKind.None, kit.State.Objects.KindAt(kit.Tile(15, 21)));
        Assert.False(kit.State.Roads.HasRoad(kit.Tile(15, 21)));
        Assert.Equal(road, kit.State.Roads.PieceAt(kit.Tile(15, 20)));
        Assert.Equal(0, kit.Stations.Count);
        Assert.Equal(ConstructionReasons.NoStation, kit.Remove(15, 21).ReasonCode);
    }

    [Fact]
    public void StationActions_ClearTheUndoRecord()
    {
        var kit = StationKit.Flat();
        Assert.True(kit.Road(10, 20, 30, 20).Succeeded);
        Assert.True(kit.Build("bus-bay", 15, 21).Succeeded);
        Assert.False(kit.State.Undo.HasValue);
    }

    [Fact]
    public void Names_AreTownTypeAndNumber()
    {
        var kit = StationKit.Generated();
        var first = kit.ServeTown(0);
        var second = kit.ServeTown(0);
        var a = kit.Stations.NameAt(kit.Stations.SlotOf(first));
        var b = kit.Stations.NameAt(kit.Stations.SlotOf(second));
        Assert.Equal(StationNameKind.BusStop, a.Kind);
        Assert.Equal(0, a.Town);
        Assert.Equal(1, a.Number);
        Assert.Equal(2, b.Number);
    }

    [Fact]
    public void Rename_SetsAndClearsTheCustomName()
    {
        var kit = StationKit.Flat();
        Assert.True(kit.Road(10, 20, 30, 20).Succeeded);
        Assert.True(kit.Build("bus-bay", 15, 21).Succeeded);
        var id = kit.StationAt(15, 21);

        Assert.True(kit.Run(new RenameStationCommand(Company.PlayerId, id, "  Harbour Gate ")).Succeeded);
        Assert.Equal("Harbour Gate", kit.Stations.CustomNameAt(kit.Stations.SlotOf(id)));
        Assert.Equal(ConstructionReasons.NameTooLong, kit.Run(new RenameStationCommand(Company.PlayerId, id, new string('x', 33))).ReasonCode);
        Assert.True(kit.Run(new RenameStationCommand(Company.PlayerId, id, "")).Succeeded);
        Assert.Null(kit.Stations.CustomNameAt(kit.Stations.SlotOf(id)));
    }

    [Fact]
    public void ToolForm_DependsOnTheTile()
    {
        var kit = StationKit.Flat();
        Assert.True(kit.Road(10, 20, 30, 20).Succeeded);
        var rules = kit.Stations.Rules;
        Assert.Equal(rules.FindPiece("bus-stop"), StationPlanner.ChoosePiece(kit.State, StationPieceKind.Bus, 20, 20));
        Assert.Equal(rules.FindPiece("truck-bay"), StationPlanner.ChoosePiece(kit.State, StationPieceKind.Truck, 20, 21));
    }
}
