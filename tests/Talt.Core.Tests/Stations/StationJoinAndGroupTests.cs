using Talt.Core.Construction;
using Talt.Core.Stations;

namespace Talt.Core.Tests.Stations;

/// <summary>User stories 5 and 6: joining pieces, spread, warehouses and their groups (FR-005, FR-007a–c).</summary>
public sealed class StationJoinAndGroupTests
{
    private static StationKit Street()
    {
        var kit = StationKit.Flat();
        Assert.True(kit.Road(2, 20, 60, 20).Succeeded);
        return kit;
    }

    [Fact]
    public void AdjacentPiece_JoinsTheStation()
    {
        var kit = Street();
        Assert.True(kit.Build("bus-bay", 15, 21).Succeeded);
        Assert.True(kit.Build("truck-bay", 16, 21).Succeeded);
        Assert.True(kit.Build("truck-stop", 17, 20).Succeeded);

        Assert.Equal(1, kit.Stations.Count);
        var id = kit.StationAt(15, 21);
        Assert.Equal(id, kit.StationAt(17, 20));
        var slot = kit.Stations.SlotOf(id);
        Assert.Equal(3, kit.Stations.TilesAt(slot).Count);
        Assert.True(kit.Stations.Handles(slot, kit.Cargo("coal")));
        Assert.True(kit.Stations.Handles(slot, kit.Cargo("passengers")));
    }

    [Fact]
    public void PieceTouchingTwoStations_IsRefused()
    {
        var kit = Street();
        Assert.True(kit.Build("bus-bay", 15, 21).Succeeded);
        Assert.True(kit.Build("bus-bay", 17, 21).Succeeded);
        Assert.Equal(2, kit.Stations.Count);
        Assert.Equal(ConstructionReasons.AdjacentTwoStations, kit.Build("bus-bay", 16, 21).ReasonCode);
    }

    [Fact]
    public void Spread_IsLimited()
    {
        var kit = Street();
        var spread = kit.Stations.Rules.MaxSpread;
        for (var x = 10; x < 10 + spread; x++)
        {
            Assert.True(kit.Build("bus-bay", x, 21).Succeeded, kit.Last.ReasonCode);
        }

        Assert.Equal(ConstructionReasons.StationTooSpread, kit.Build("bus-bay", 10 + spread, 21).ReasonCode);
    }

    [Fact]
    public void RemovingAMiddlePiece_NeverSplitsTheStation()
    {
        var kit = Street();
        Assert.True(kit.Build("bus-bay", 15, 21).Succeeded);
        Assert.True(kit.Build("bus-bay", 16, 21).Succeeded);
        Assert.True(kit.Build("bus-bay", 17, 21).Succeeded);
        Assert.True(kit.Remove(16, 21).Succeeded);
        Assert.Equal(1, kit.Stations.Count);
        Assert.Equal(kit.StationAt(15, 21), kit.StationAt(17, 21));
    }

    [Fact]
    public void Warehouse_GroupsStationsInItsRadius_AndShareStock()
    {
        var kit = Street();
        Assert.True(kit.Build("truck-bay", 10, 21).Succeeded);
        Assert.True(kit.Build("truck-bay", 30, 21).Succeeded);
        var a = kit.StationAt(10, 21);
        var b = kit.StationAt(30, 21);
        Assert.NotEqual(kit.Stations.GroupOf(a), kit.Stations.GroupOf(b));

        Assert.True(kit.Warehouse("medium", 19, 21).Succeeded, kit.Last.ReasonCode);

        Assert.Equal(kit.Stations.GroupOf(a), kit.Stations.GroupOf(b));
        var coal = kit.Cargo("coal");
        var hold = new CargoHold();
        Assert.Equal(0, kit.Stations.Load(a, coal, 10, hold, 50));
        // Transfer coal into the group at A; B loads it from the shared stock.
        var other = new CargoHold();
        Transfer(kit, a, coal, 120);
        Assert.Equal(120, kit.Stations.WaitingAt(b, coal));
        Assert.Equal(120, kit.Stations.Load(b, coal, 500, other, 50));
        Assert.Equal(0, kit.Stations.WaitingAt(a, coal));
    }

    [Fact]
    public void OverlappingWarehouses_MergeAndSplitConservesStock()
    {
        var kit = Street();
        Assert.True(kit.Build("truck-bay", 4, 21).Succeeded);
        Assert.True(kit.Build("truck-bay", 40, 21).Succeeded);
        var a = kit.StationAt(4, 21);
        var b = kit.StationAt(40, 21);
        var coal = kit.Cargo("coal");
        Transfer(kit, a, coal, 300);
        Transfer(kit, b, coal, 100);

        Assert.True(kit.Warehouse("small", 10, 21).Succeeded, kit.Last.ReasonCode);
        Assert.True(kit.Warehouse("small", 34, 21).Succeeded, kit.Last.ReasonCode);
        Assert.NotEqual(kit.Stations.GroupOf(a), kit.Stations.GroupOf(b));
        Assert.True(kit.Warehouse("medium", 18, 21).Succeeded, kit.Last.ReasonCode); // overlaps both

        Assert.Equal(kit.Stations.GroupOf(a), kit.Stations.GroupOf(b));
        Assert.Equal(400, kit.Stations.WaitingAt(a, coal));

        Assert.True(kit.Remove(18, 21).Succeeded);
        Assert.NotEqual(kit.Stations.GroupOf(a), kit.Stations.GroupOf(b));
        Assert.Equal(400, kit.Stations.WaitingAt(a, coal) + kit.Stations.WaitingAt(b, coal));
        Assert.Equal(0, kit.Stations.TotalLost);
    }

    [Fact]
    public void LoneStation_IsItsOwnGroup_AndRemovingItLosesItsCargo()
    {
        var kit = Street();
        Assert.True(kit.Build("truck-bay", 10, 21).Succeeded);
        var a = kit.StationAt(10, 21);
        var coal = kit.Cargo("coal");
        Transfer(kit, a, coal, 50);
        Assert.Equal(1, kit.Stations.GroupCount);
        Assert.True(kit.Remove(10, 21).Succeeded);
        Assert.Equal(50, kit.Stations.TotalLost);
    }

    [Fact]
    public void Warehouse_NeedsRoadAndFlatEmptyLand()
    {
        var kit = Street();
        Assert.Equal(ConstructionReasons.NoRoad, kit.Warehouse("small", 30, 40).ReasonCode);
        Assert.Equal(ConstructionReasons.ObjectInTheWay, kit.Warehouse("small", 30, 19).ReasonCode);
    }

    /// <summary>Puts cargo waiting at a station by unloading a hold from a far station with a transfer order.</summary>
    internal static void Transfer(StationKit kit, int stationId, int cargo, int amount)
    {
        var hold = new CargoHold();
        hold.Add(new HeldCargo(cargo, amount, 9999, -1, -1, kit.Stations.Today));
        var result = kit.Stations.Unload(stationId, hold, UnloadMode.Transfer);
        Assert.Equal(amount, result.Transferred);
    }
}
