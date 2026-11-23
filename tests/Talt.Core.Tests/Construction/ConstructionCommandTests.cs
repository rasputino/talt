using System.Text.Json;
using Talt.Core.Commands;
using Talt.Core.Construction;
using Talt.Core.Maps;
using Talt.Core.Scenarios;
using Talt.Core.Simulation;

namespace Talt.Core.Tests.Construction;

/// <summary>Construction commands through the simulation (US1–US3): money, all-or-nothing, JSON, paused.</summary>
public class ConstructionCommandTests
{
    /// <summary>A 64² test map; the prototype roads run along x/z = 8, 24, 40, 56.</summary>
    internal static GameSimulation Simulation(TestMap? map = null, ConstructionRules? rules = null)
    {
        map ??= new TestMap();
        var data = map.Build();
        var settings = new ScenarioSettings(Seed: 3, MapSizeTiles: 64, RoadSpacing: 16, RoadRemovalChancePerMille: 0, VehicleCount: 0, LineCount: 1, Map: data.Settings);
        return GameSimulation.Create(settings, data, rules ?? TestMap.Rules);
    }

    [Fact]
    public void Terraform_SpendsExactlyThePreviewedCost()
    {
        var simulation = Simulation();
        var state = simulation.Construction!;
        var command = new TerraformCommand(Company.PlayerId, TerraformMode.Raise, 10, 10, 14, 13);
        var preview = new ConstructionPlan();

        var result = command.Preview(state, new ConstructionPlanner(), preview);
        var balance = state.Player.Balance;
        simulation.Submit(command);
        simulation.Tick();

        Assert.True(result.Succeeded);
        Assert.True(preview.Cost > 0);
        Assert.Equal(balance - preview.Cost, state.Player.Balance);
        Assert.Equal(11, state.Map.HeightAt(12, 12));
    }

    [Fact]
    public void NotEnoughMoney_IsRefused_AndNothingChanges()
    {
        var simulation = Simulation(rules: TestMap.Rules with { StartingBalance = 150 });
        var state = simulation.Construction!;
        var fingerprint = simulation.ComputeFingerprint();
        CommandProcessed? processed = null;
        simulation.CommandProcessed += p => processed = p;

        var preview = new ConstructionPlan();
        var command = new TerraformCommand(Company.PlayerId, TerraformMode.Raise, 10, 10, 11, 10);
        var result = command.Preview(state, new ConstructionPlanner(), preview);
        simulation.Submit(command);
        simulation.ProcessPendingCommands();

        Assert.Equal(ConstructionReasons.NotEnoughMoney, result.ReasonCode);
        Assert.Equal(ConstructionReasons.NotEnoughMoney, preview.Reason);
        Assert.Equal(ConstructionReasons.NotEnoughMoney, processed!.Value.Result.ReasonCode);
        Assert.Equal(150, state.Player.Balance);
        Assert.Equal(fingerprint, simulation.ComputeFingerprint());
    }

    [Fact]
    public void InvalidPart_RefusesTheWholeAction()
    {
        var simulation = Simulation(new TestMap().Water(12, 12, 7, 9));
        var state = simulation.Construction!;
        var fingerprint = simulation.ComputeFingerprint();

        simulation.Submit(new TerraformCommand(Company.PlayerId, TerraformMode.Raise, 10, 10, 14, 14));
        simulation.ProcessPendingCommands();

        Assert.Equal(10, state.Map.HeightAt(10, 10));
        Assert.Equal(fingerprint, simulation.ComputeFingerprint());
    }

    [Fact]
    public void PrototypeRoads_AreTownObjects()
    {
        var state = Simulation().Construction!;
        var road = state.Map.Index(8, 20);

        Assert.Equal(TileObjectKind.PublicRoad, state.Objects.KindAt(road));
        Assert.Equal(TileOwnerKind.Town, state.Objects.OwnerAt(road).Kind);
        Assert.Equal(TileObjectKind.None, state.Objects.KindAt(state.Map.Index(12, 12)));
    }

    [Fact]
    public void ProcessPendingCommands_AppliesWithoutAdvancingTime()
    {
        var simulation = Simulation();

        simulation.Submit(new TerraformCommand(Company.PlayerId, TerraformMode.Lower, 12, 12, 12, 12));
        simulation.ProcessPendingCommands();

        Assert.Equal(0, simulation.CurrentTick);
        Assert.Equal(9, simulation.Construction!.Map.HeightAt(12, 12));
    }

    [Fact]
    public void AppliedCommand_LogsTheChangedTiles_AndUpdatesFlags()
    {
        var simulation = Simulation(new TestMap().Terrain(12, 12, TerrainType.Rock));
        var state = simulation.Construction!;

        simulation.Submit(new TerraformCommand(Company.PlayerId, TerraformMode.Raise, 12, 12, 12, 12));
        simulation.ProcessPendingCommands();
        var changed = new List<int>();

        Assert.True(state.TryTakeChangedTiles(changed));
        Assert.Equal([state.Map.Index(12, 12)], changed);
        Assert.Equal(TerrainType.Grass, state.Map.TerrainAt(12, 12));
        Assert.True(state.Map.IsBuildable(12, 12));
        Assert.False(state.TryTakeChangedTiles(changed));
    }

    [Fact]
    public void ConstructionStateChanges_ChangeTheFingerprint()
    {
        var simulation = Simulation();
        var before = simulation.ComputeFingerprint();

        simulation.Submit(new TerraformCommand(Company.PlayerId, TerraformMode.Raise, 12, 12, 12, 12));
        simulation.ProcessPendingCommands();

        Assert.NotEqual(before, simulation.ComputeFingerprint());
    }

    [Fact]
    public void WithoutAMap_CommandsAreRejected()
    {
        var simulation = GameSimulation.Create(new ScenarioSettings(MapSizeTiles: 64, RoadSpacing: 16, VehicleCount: 0, LineCount: 1));
        CommandProcessed? processed = null;
        simulation.CommandProcessed += p => processed = p;

        simulation.Submit(new ClearAreaCommand(Company.PlayerId, 0, 0, 3, 3));
        simulation.Tick();

        Assert.Null(simulation.Construction);
        Assert.Equal(ConstructionReasons.NoMap, processed!.Value.Result.ReasonCode);
    }

    [Fact]
    public void UnknownCompany_IsRejected()
    {
        var simulation = Simulation();

        var result = new TerraformCommand(7, TerraformMode.Raise, 12, 12, 12, 12).Validate(simulation);

        Assert.Equal(ConstructionReasons.CompanyNotFound, result.ReasonCode);
    }

    [Fact]
    public void MapGeneratedFromOtherSettings_IsRejected()
    {
        var data = new TestMap().Build();
        var settings = new ScenarioSettings(MapSizeTiles: 64, RoadSpacing: 16, VehicleCount: 0, LineCount: 1, Map: data.Settings with { Seed = 99 });

        Assert.Throws<ArgumentException>(() => GameSimulation.Create(settings, data));
    }

    [Theory]
    [MemberData(nameof(Commands))]
    public void Commands_RoundTripThroughJson(ISimulationCommand command)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            CommandSerializer.Write(writer, command);
        }

        using var document = JsonDocument.Parse(stream.ToArray());
        Assert.Equal(command, CommandSerializer.Read(document.RootElement));
    }

    public static TheoryData<ISimulationCommand> Commands() => new()
    {
        new TerraformCommand(0, TerraformMode.Raise, 1, 2, 3, 4),
        new TerraformCommand(0, TerraformMode.Lower, -1, 2, 30, 4),
        new TerraformCommand(0, TerraformMode.Level, 5, 6, 7, 8, 5, 6),
        new ClearAreaCommand(0, 1, 2, 3, 4),
        new UndoConstructionCommand(0),
    };
}
