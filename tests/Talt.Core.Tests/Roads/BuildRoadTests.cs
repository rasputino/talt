using Talt.Core.Construction;
using Talt.Core.Maps;
using Talt.Core.Roads;
using Talt.Core.Tests.Construction;

namespace Talt.Core.Tests.Roads;

/// <summary>User story 1: build a road by dragging (FR-010..FR-016, SC-001, SC-004).</summary>
public class BuildRoadTests
{
    /// <summary>Previews planned while measuring (and while warming up), for the allocation test.</summary>
    private const int PlansPerWindow = 200;

    [Fact]
    public void LShapedDrag_BuildsThePreviewedPath_ChargesItsCost_AndRoutesInTheSameTick()
    {
        var kit = new RoadKit();
        var (preview, plan) = kit.Preview(new BuildRoadCommand(Company.PlayerId, "paved", 5, 5, 20, 12));
        var balance = kit.State.Player.Balance;

        var result = kit.Road(5, 5, 20, 12);

        Assert.True(preview.Succeeded, preview.ReasonCode);
        Assert.True(result.Succeeded, result.ReasonCode);
        Assert.Equal(balance - plan.Cost, kit.State.Player.Balance);
        Assert.Equal(plan.ChangeCount, kit.Roads.RoadTileCount);
        Assert.True(kit.Route(5, 5, 20, 12) > 0);
        Assert.True(kit.Route(20, 12, 5, 5) > 0);
        Assert.Equal(TileOwner.Company(Company.PlayerId), kit.State.Objects.OwnerAt(kit.Tile(5, 5)));
        kit.AssertNetworkMatchesRebuild();
    }

    [Fact]
    public void DiagonalDrag_UsesDiagonalPieces()
    {
        var kit = new RoadKit();

        Assert.True(kit.Road(5, 5, 25, 25).Succeeded);

        Assert.True(kit.Piece(10, 10).HasLink(Dir8.SE));
        Assert.True(kit.Piece(10, 10).HasLink(Dir8.NW));
        Assert.Equal(20L * Dir8.DiagonalLength, kit.Route(5, 5, 25, 25));
    }

    [Fact]
    public void CrossingAnExistingRoad_FormsAJunction()
    {
        var kit = new RoadKit();
        kit.Road(5, 10, 25, 10);

        Assert.True(kit.Road(15, 3, 15, 20).Succeeded);

        var junction = kit.Piece(15, 10);
        Assert.Equal(4, junction.LinkCount);
        Assert.True(kit.State.Network.TryGetNode(kit.Tile(15, 10), out _));
        Assert.True(kit.Route(5, 10, 15, 3) > 0);
        kit.AssertNetworkMatchesRebuild();
    }

    [Fact]
    public void ReusingAnExistingRoad_ChangesNothing()
    {
        var kit = new RoadKit();
        kit.Road(5, 10, 25, 10);

        var (result, plan) = kit.Preview(new BuildRoadCommand(Company.PlayerId, "paved", 8, 10, 20, 10));

        Assert.Equal(ConstructionReasons.NothingToDo, result.ReasonCode);
        Assert.Equal(0, plan.Cost);
    }

    [Fact]
    public void TreesOnThePath_AreClearedAndPaid()
    {
        var kit = new RoadKit(new TestMap().Tree(10, 5));
        var (_, withTree) = kit.Preview(new BuildRoadCommand(Company.PlayerId, "paved", 5, 5, 15, 5));
        var (_, without) = new RoadKit().Preview(new BuildRoadCommand(Company.PlayerId, "paved", 5, 5, 15, 5));

        kit.Road(5, 5, 15, 5);

        Assert.Equal(Scenery.None, kit.State.Map.SceneryAt(10, 5));
        Assert.Equal(TestMap.Rules.ClearTreeCost, withTree.Cost - without.Cost);
    }

    [Fact]
    public void OthersObjects_AreGoneAround()
    {
        var kit = new RoadKit();
        for (var z = 3; z <= 7; z++)
        {
            kit.State.Objects.Set(kit.Tile(10, z), TileObjectKind.Test, TileOwner.Town(1));
        }

        Assert.True(kit.Road(5, 5, 15, 5).Succeeded);

        Assert.False(kit.Roads.HasRoad(kit.Tile(10, 5)));
        Assert.True(kit.Route(5, 5, 15, 5) > 10L * Dir8.StraightLength);
    }

    [Fact]
    public void StartingOnWater_IsRefusedWithTheReason()
    {
        var kit = new RoadKit(new TestMap().Water(5, 5, 6, 9));

        Assert.Equal(ConstructionReasons.Water, kit.Road(5, 5, 15, 5).ReasonCode);
        Assert.Equal(0, kit.Roads.RoadTileCount);
    }

    [Fact]
    public void NotEnoughMoney_BuildsNothing()
    {
        var kit = new RoadKit(balance: 1000);

        Assert.Equal(ConstructionReasons.NotEnoughMoney, kit.Road(5, 5, 30, 5).ReasonCode);
        Assert.Equal(0, kit.Roads.RoadTileCount);
    }

    [Fact]
    public void Undo_RemovesTheRoad_RefundsIt_AndUpdatesTheNetwork()
    {
        var kit = new RoadKit();
        var balance = kit.State.Player.Balance;
        kit.Road(5, 5, 20, 5);

        Assert.True(kit.Run(new UndoConstructionCommand(Company.PlayerId)).Succeeded);

        Assert.Equal(0, kit.Roads.RoadTileCount);
        Assert.Equal(balance, kit.State.Player.Balance);
        Assert.Equal(0, kit.State.Network.NodeCount);
        Assert.Equal(-1, kit.Route(5, 5, 20, 5));
    }

    [Fact]
    public void ClearTool_LeavesRoadsAlone()
    {
        var kit = new RoadKit();
        kit.Road(5, 5, 20, 5);

        var result = kit.Run(new ClearAreaCommand(Company.PlayerId, 4, 4, 21, 6));

        Assert.Equal(ConstructionReasons.UseRoadRemoval, result.ReasonCode);
        Assert.True(kit.Roads.HasRoad(kit.Tile(10, 5)));
    }

    [Fact]
    public void MotorwayType_IsNotForTheRoadTool_AndFutureTypesAreRefused()
    {
        var kit = new RoadKit(startYear: 1900);

        Assert.Equal(ConstructionReasons.RoadTypeUnavailable, kit.Road(5, 5, 9, 5, "paved").ReasonCode);
        Assert.Equal(ConstructionReasons.RoadTypeUnavailable, kit.Road(5, 5, 9, 5, "motorway").ReasonCode);
        Assert.Equal(ConstructionReasons.UnknownRoadType, kit.Road(5, 5, 9, 5, "rainbow").ReasonCode);
        Assert.True(kit.Road(5, 5, 9, 5, "dirt").Succeeded);
    }

    [Fact]
    public void RepeatedPreviews_AllocateNothing()
    {
        var kit = new RoadKit();
        kit.Road(5, 20, 40, 20);
        var planner = new ConstructionPlanner();
        var plan = new ConstructionPlan();
        var type = kit.State.RoadRules.FindType("paved");
        // Warm up with as many previews as the measurement plans: tiered compilation and the runtime's own
        // one-off allocations then happen before the measured window (macOS CI once measured 496 bytes).
        for (var i = 0; i < PlansPerWindow; i++)
        {
            planner.Roads.PlanRoad(kit.State, Company.PlayerId, type, (3, 3), (50, 40 + (i % 3)), plan);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < PlansPerWindow; i++)
        {
            planner.Roads.PlanRoad(kit.State, Company.PlayerId, type, (3, 3), (50, 40 + (i % 3)), plan);
        }

        // A preview that allocated would add at least the 24 bytes of the smallest object on every call, so
        // this bound still fails on a real regression while tolerating a one-off allocation of the runtime.
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 24 * PlansPerWindow, $"{PlansPerWindow} road previews allocated {allocated} bytes.");
        Assert.True(plan.IsValid, plan.Reason);
    }
}
