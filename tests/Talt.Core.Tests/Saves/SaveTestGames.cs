using Talt.Core.Construction;
using Talt.Core.Finance;
using Talt.Core.Maps;
using Talt.Core.Rails;
using Talt.Core.Scenarios;
using Talt.Core.Simulation;
using Talt.Core.Stations;
using Talt.Core.Vehicles;

namespace Talt.Core.Tests.Saves;

/// <summary>
/// Games for save tests (research R13). <see cref="Rich"/> uses every system of the game: a generated map with towns and
/// industries, terraforming and roads from construction, stations, buses on lines, trains with signals, destination cargo
/// routed over both modes, a loan, a removed station and an undo record. Later features add their state here
/// (contracts/save-sections.md, rule 5).
/// </summary>
internal static class SaveTestGames
{
    public static readonly ScenarioSettings Settings = new(
        Seed: 21,
        StartYear: 1955,
        MapSizeTiles: 256,
        VehicleCount: 0,
        Map: new MapSettings(256, 256, Seed: 21, Roughness: 20, MaxHeight: 24, WaterAmount: 10, SeaLevel: 4));

    /// <summary>A game with every system in use, run <paramref name="ticks"/> ticks after seeding.</summary>
    public static GameSimulation Rich(int ticks = 400, bool parallel = false, int startYear = 1955)
    {
        var simulation = GameSimulation.Create(Settings with { StartYear = startYear });
        simulation.Submit(new ChangeLoanCommand(Company.PlayerId, LoanMode.Borrow)); // Before the seeders switch to infinite money.
        simulation.Tick(parallel);
        Submit(simulation, VehicleSeeder.Plan(simulation, 40, spacing: 20));
        simulation.Tick(parallel);
        Submit(simulation, TrainSeeder.Plan(simulation, 12));
        simulation.Tick(parallel);

        simulation.Submit(new SetReplacementCommand(Company.PlayerId, false, simulation.RoadVehicles!.ModelAt(0).Id, string.Empty, false, true));
        simulation.Submit(new SetReplacementCommand(Company.PlayerId, true, simulation.Trains!.ModelOf(simulation.Trains.TrainAt(0).Parts[0]).Id, string.Empty, true, true));

        // A station that is built and removed again (removed-centre bookkeeping, feature 011).
        var extra = StationSeeder.Plan(simulation, 2);
        Submit(simulation, extra);
        simulation.Tick(parallel);
        if (extra.Count > 0)
        {
            simulation.Submit(new RemoveStationCommand(Company.PlayerId, extra[0].X, extra[0].Z));
        }

        simulation.Tick(parallel);
        for (var t = 0; t < ticks; t++)
        {
            simulation.Tick(parallel);
        }

        return simulation;
    }

    /// <summary>Ticks until <paramref name="condition"/> holds (at most <paramref name="limit"/> ticks).</summary>
    public static void RunUntil(GameSimulation simulation, Func<GameSimulation, bool> condition, int limit = 20_000, bool parallel = false)
    {
        for (var t = 0; t < limit && !condition(simulation); t++)
        {
            simulation.Tick(parallel);
        }

        Assert.True(condition(simulation), "the condition was never reached");
    }

    private static void Submit(GameSimulation simulation, IEnumerable<global::Talt.Core.Commands.ISimulationCommand> commands)
    {
        foreach (var command in commands)
        {
            simulation.Submit(command);
        }
    }
}
