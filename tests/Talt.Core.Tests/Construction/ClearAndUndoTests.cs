using Talt.Core.Construction;
using Talt.Core.Maps;

namespace Talt.Core.Tests.Construction;

/// <summary>Clearing trees, demolishing objects (US3) and undoing the last action (US5).</summary>
public class ClearAndUndoTests
{
    [Fact]
    public void Clear_RemovesTreesAndChargesPerTree()
    {
        var simulation = ConstructionCommandTests.Simulation(new TestMap().Tree(10, 10).Tree(11, 12).Tree(30, 30));
        var state = simulation.Construction!;
        var balance = state.Player.Balance;

        simulation.Submit(new ClearAreaCommand(Company.PlayerId, 10, 10, 14, 14));
        simulation.ProcessPendingCommands();

        Assert.Equal(Scenery.None, state.Map.SceneryAt(10, 10));
        Assert.Equal(Scenery.None, state.Map.SceneryAt(11, 12));
        Assert.NotEqual(Scenery.None, state.Map.SceneryAt(30, 30));
        Assert.Equal(balance - (2 * TestMap.Rules.ClearTreeCost), state.Player.Balance);
    }

    [Fact]
    public void Clear_DemolishesOwnObjects_AndSkipsOthers()
    {
        var simulation = ConstructionCommandTests.Simulation(new TestMap().Tree(9, 10));
        var state = simulation.Construction!;
        var own = state.Map.Index(10, 10);
        state.Objects.Set(own, TileObjectKind.Test, TileOwner.Company(Company.PlayerId));
        var road = state.Map.Index(8, 10);
        state.Objects.Set(road, TileObjectKind.Test, TileOwner.Town(0));
        var balance = state.Player.Balance;
        var plan = new ConstructionPlan();
        var command = new ClearAreaCommand(Company.PlayerId, 7, 10, 10, 10);

        var result = command.Preview(state, new ConstructionPlanner(), plan);
        simulation.Submit(command);
        simulation.ProcessPendingCommands();

        Assert.True(result.Succeeded);
        Assert.Contains(plan.Problems.ToArray(), p => p.Index == road && p.Reason == ConstructionReasons.OwnedByOther && !p.Blocking);
        Assert.Equal(TileObjectKind.None, state.Objects.KindAt(own));
        Assert.Equal(TileObjectKind.Test, state.Objects.KindAt(road));
        Assert.Equal(balance - 500 - TestMap.Rules.ClearTreeCost, state.Player.Balance);
    }

    [Fact]
    public void Clear_OnlyOthersObjects_IsRefused()
    {
        var state = ConstructionCommandTests.Simulation().Construction!;
        for (var z = 10; z <= 12; z++)
        {
            state.Objects.Set(state.Map.Index(8, z), TileObjectKind.Test, TileOwner.Town(0));
        }

        var plan = new ConstructionPlan();

        var result = new ClearAreaCommand(Company.PlayerId, 8, 10, 8, 12).Preview(state, new ConstructionPlanner(), plan);

        Assert.Equal(ConstructionReasons.OwnedByOther, result.ReasonCode);
    }

    [Fact]
    public void Clear_EmptyArea_IsNothingToDo_AndFree()
    {
        var simulation = ConstructionCommandTests.Simulation();
        var state = simulation.Construction!;
        var balance = state.Player.Balance;

        var result = new ClearAreaCommand(Company.PlayerId, 10, 10, 12, 12).Validate(simulation);

        Assert.Equal(ConstructionReasons.NothingToDo, result.ReasonCode);
        Assert.Equal(balance, state.Player.Balance);
    }

    [Fact]
    public void Undo_RestoresTheTilesExactly_AndRefunds()
    {
        var simulation = ConstructionCommandTests.Simulation(new TestMap().Tree(12, 12).Terrain(13, 12, TerrainType.Rock));
        var state = simulation.Construction!;
        var fingerprint = simulation.ComputeFingerprint();
        var heights = state.Map.Heights.ToArray();
        var balance = state.Player.Balance;

        simulation.Submit(new TerraformCommand(Company.PlayerId, TerraformMode.Raise, 11, 11, 15, 14));
        simulation.Tick();
        Assert.NotEqual(balance, state.Player.Balance);

        simulation.Submit(new UndoConstructionCommand(Company.PlayerId));
        simulation.ProcessPendingCommands();

        Assert.Equal(heights, state.Map.Heights.ToArray());
        Assert.NotEqual(Scenery.None, state.Map.SceneryAt(12, 12));
        Assert.Equal(TerrainType.Rock, state.Map.TerrainAt(13, 12));
        Assert.Equal(balance, state.Player.Balance);
        Assert.False(state.Undo.HasValue);

        // Only the tick advanced since the start.
        var again = ConstructionCommandTests.Simulation(new TestMap().Tree(12, 12).Terrain(13, 12, TerrainType.Rock));
        again.Tick();
        Assert.NotEqual(fingerprint, again.ComputeFingerprint());
        Assert.Equal(again.ComputeFingerprint(), simulation.ComputeFingerprint());
    }

    [Fact]
    public void Undo_AfterTheWindow_IsExpired()
    {
        var simulation = ConstructionCommandTests.Simulation();
        simulation.Submit(new TerraformCommand(Company.PlayerId, TerraformMode.Raise, 12, 12, 12, 12));
        simulation.Tick();
        for (var t = 0; t < TestMap.Rules.UndoWindowTicks + 1; t++)
        {
            simulation.Tick();
        }

        var result = new UndoConstructionCommand(Company.PlayerId).Validate(simulation);

        Assert.Equal(ConstructionReasons.UndoExpired, result.ReasonCode);
    }

    [Fact]
    public void Undo_WhilePaused_DoesNotExpire()
    {
        var simulation = ConstructionCommandTests.Simulation();
        simulation.Submit(new TerraformCommand(Company.PlayerId, TerraformMode.Raise, 12, 12, 12, 12));
        for (var i = 0; i < 1000; i++)
        {
            simulation.ProcessPendingCommands(); // Paused frames: no time passes.
        }

        Assert.True(new UndoConstructionCommand(Company.PlayerId).Validate(simulation).Succeeded);
    }

    [Fact]
    public void Undo_WhenATileChangedSince_IsRefused()
    {
        var simulation = ConstructionCommandTests.Simulation();
        var state = simulation.Construction!;
        simulation.Submit(new TerraformCommand(Company.PlayerId, TerraformMode.Raise, 12, 12, 12, 12));
        simulation.ProcessPendingCommands();

        // Something else (a future town, another company) touches the tile.
        state.Objects.Set(state.Map.Index(12, 12), TileObjectKind.Test, TileOwner.Town(1));

        Assert.Equal(ConstructionReasons.ChangedSince, new UndoConstructionCommand(Company.PlayerId).Validate(simulation).ReasonCode);
    }

    [Fact]
    public void Undo_IsOneLevel_AndALaterActionReplacesTheRecord()
    {
        var simulation = ConstructionCommandTests.Simulation(new TestMap().Tree(30, 30));
        var state = simulation.Construction!;
        simulation.Submit(new TerraformCommand(Company.PlayerId, TerraformMode.Raise, 12, 12, 12, 12));
        simulation.Submit(new ClearAreaCommand(Company.PlayerId, 30, 30, 30, 30));
        simulation.Submit(new UndoConstructionCommand(Company.PlayerId));
        simulation.ProcessPendingCommands();

        Assert.NotEqual(Scenery.None, state.Map.SceneryAt(30, 30)); // The clear was undone...
        Assert.Equal(11, state.Map.HeightAt(12, 12));                // ...the raise before it stays.
        Assert.Equal(ConstructionReasons.NothingToUndo, new UndoConstructionCommand(Company.PlayerId).Validate(simulation).ReasonCode);
    }
}
