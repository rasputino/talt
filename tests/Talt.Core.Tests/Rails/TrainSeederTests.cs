using Talt.Core.Commands;
using Talt.Core.Construction;
using Talt.Core.Maps;
using Talt.Core.Rails;
using Talt.Core.Scenarios;
using Talt.Core.Simulation;
using Talt.Core.Tests.Construction;
using Talt.Tests.Support;

namespace Talt.Core.Tests.Rails;

public sealed class TrainSeederTests
{
    private static RailKit Seeded(int trains, int size = 256)
    {
        var kit = new RailKit(new TestMap(size));
        foreach (var command in TrainSeeder.Plan(kit.Simulation, trains))
        {
            kit.Simulation.Submit(command);
        }

        kit.Simulation.ProcessPendingCommands();
        return kit;
    }

    [Fact]
    public void SeededLoopsRunSafelyAndDeliverTheirStops()
    {
        var kit = Seeded(48);
        Assert.Equal(48, kit.Trains.Count);
        var loading = 0;
        kit.Ticks(4000, each: () =>
        {
            for (var slot = 0; slot < kit.Trains.Count; slot++)
            {
                loading += kit.Trains.TrainAt(slot).Mode == TrainMode.Loading ? 1 : 0;
            }
        });

        var running = 0;
        for (var slot = 0; slot < kit.Trains.Count; slot++)
        {
            running += kit.Trains.TrainAt(slot).Mode != TrainMode.InDepot ? 1 : 0;
        }

        Assert.Equal(48, running);
        Assert.True(loading > 0);
    }

    [Fact]
    public void TheTrainTickAllocatesNothingInSteadyState()
    {
        var kit = Seeded(24);
        kit.Ticks(3000, parallel: false);
        var tick = kit.Simulation.CurrentTick;
        AllocationAssert.NoSteadyStateAllocations(() => kit.Trains.Tick(tick++, parallel: false), 400, "400 train ticks with 24 trains");
        AllocationAssert.NoSteadyStateAllocations(() => kit.Simulation.Tick(parallel: false), 400, "400 ticks with 24 trains");
    }

    /// <summary>
    /// Regression: on generated maps the loops were checked one command at a time on the unbuilt land, so on slopes a corner
    /// diagonal joining a straight that ended on a ramp was refused once the straight was built. The loop stayed open and
    /// every seeded train waited in its depot with no route. Seeds 1, 2, 6 and 7 of a 256² map hit it; the seeder now
    /// levels sloping footprints first.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(6)]
    [InlineData(7)]
    public void SeededTrainsOnGeneratedLandLeaveTheirDepots(int seed)
    {
        var map = MapGenerator.Generate(new MapSettings(256, 256, seed));
        var settings = new ScenarioSettings(Seed: seed, MapSizeTiles: 256, VehicleCount: 0, Map: map.Settings);
        var simulation = GameSimulation.Create(settings, map);
        var rejected = new List<string>();
        simulation.CommandProcessed += processed =>
        {
            if (!processed.Result.Succeeded)
            {
                rejected.Add($"{processed.Command}: {processed.Result.ReasonCode}");
            }
        };

        var commands = TrainSeeder.Plan(simulation, 16);
        Assert.Contains(commands, command => command is BuildRailDepotCommand);
        foreach (var command in commands)
        {
            simulation.Submit(command);
        }

        simulation.ProcessPendingCommands();
        var trains = simulation.Trains!;
        Assert.Equal(16, trains.Count);
        for (var tick = 0; tick < 100; tick++)
        {
            simulation.Tick();
        }

        for (var slot = 0; slot < trains.Count; slot++)
        {
            simulation.Submit(new StartTrainCommand(Company.PlayerId, trains.TrainAt(slot).Id, Start: true));
        }

        var left = new HashSet<int>();
        for (var tick = 0; tick < 2000; tick++)
        {
            simulation.Tick();
            for (var slot = 0; slot < trains.Count; slot++)
            {
                var train = trains.TrainAt(slot);
                Assert.Equal(0, (int)(train.Flags & TrainBits.NoRoute));
                if (train.Mode != TrainMode.InDepot)
                {
                    left.Add(train.Id);
                }
            }
        }

        Assert.Empty(rejected);
        Assert.Equal(16, left.Count);
    }
}
