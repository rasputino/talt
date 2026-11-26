using Talt.Core.Commands;
using Talt.Core.Determinism;
using Talt.Core.Maps;
using Talt.Core.Scenarios;
using Talt.Core.Simulation;
using Talt.Core.Vehicles;

namespace Talt.Core.Tests.Determinism;

/// <summary>
/// Feature 012 (FR-029, FR-030, SC-004): a game with seeded bus bays, depots, lines and buses records and replays to the
/// same state; parallel and sequential runs agree tick for tick and the fingerprint is pinned so every CI platform must
/// agree.
/// </summary>
/// <remarks>Regenerate the golden only when vehicle rules or their data file change on purpose (or anything earlier goldens depend on).</remarks>
public class RoadVehicleReplayTests
{
    private const string ExpectedAtTick4000 = "91E0F1189491671C";

    private static readonly ScenarioSettings Settings = new(
        Seed: 12,
        StartYear: 1955,
        MapSizeTiles: 256,
        VehicleCount: 0,
        Map: new MapSettings(256, 256, Seed: 12, Roughness: 30, MaxHeight: 32, WaterAmount: 10, SeaLevel: 6));

    [Fact]
    public void ParallelAndSequential_Match_AndEqualTheGolden()
    {
        var parallel = GameSimulation.Create(Settings);
        var sequential = GameSimulation.Create(Settings);
        Seed(parallel);
        Seed(sequential);
        while (parallel.CurrentTick < 4000)
        {
            parallel.Tick(parallel: true);
            sequential.Tick(parallel: false);
            if (parallel.CurrentTick % 250 == 0)
            {
                Assert.Equal(sequential.ComputeFingerprint(), parallel.ComputeFingerprint());
                Assert.Null(parallel.RoadVehicles!.CheckInvariants());
            }
        }

        Assert.True(parallel.RoadVehicles!.Count > 0);
        Assert.True(parallel.RoadVehicles.TotalDistance > 0);
        Assert.Equal(ExpectedAtTick4000, StateHasher.Format(parallel.ComputeFingerprint()));
    }

    [Fact]
    public void RecordedVehicleSession_Replays()
    {
        var simulation = GameSimulation.Create(Settings);
        var recorder = new SessionRecorder(new RecordedSession(simulation.Settings));
        simulation.AttachRecorder(recorder);
        Seed(simulation);
        while (simulation.CurrentTick < 3000)
        {
            simulation.Tick();
            if (simulation.CurrentTick % 1000 == 0)
            {
                recorder.AddCheckpoint(simulation);
            }

            if (simulation.CurrentTick == 1500 && simulation.RoadVehicles!.Count > 0)
            {
                // A player action mid-game: send the first bus to its depot.
                simulation.Submit(new SendToDepotCommand(0, simulation.RoadVehicles.IdAt(0)));
            }
        }

        var replay = SessionReplayer.Replay(RecordedSession.FromJson(recorder.Session.ToJson()));

        Assert.True(replay.Succeeded, replay.FailureReason);
        Assert.All(replay.Checkpoints, c => Assert.True(c.Matches));
        Assert.Equal(simulation.ComputeFingerprint(), replay.Simulation.ComputeFingerprint());
    }

    [Fact]
    public void SteadyTicks_AllocateNothing()
    {
        var simulation = GameSimulation.Create(Settings);
        Seed(simulation);
        for (var i = 0; i < 2000; i++)
        {
            simulation.Tick(parallel: false);
        }

        // Vehicle ticks only. A route search the cache cannot answer allocates the new route (a rare event, not per
        // tick), so a window counts only when no search ran in it; driving, queues, junctions and loading must not allocate.
        var vehicles = simulation.RoadVehicles!;
        var tick = simulation.CurrentTick;
        var clean = 0;
        for (var round = 0; round < 40 && clean < 3; round++)
        {
            var searches = vehicles.RouteSearches;
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 50; i++)
            {
                vehicles.Tick(tick++, parallel: false);
            }

            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            if (vehicles.RouteSearches == searches)
            {
                clean++;
                if (allocated == 0)
                {
                    return;
                }
            }
        }

        Assert.Fail(clean == 0 ? "no window without route searches" : "vehicle ticks without route searches still allocated");
    }

    private static void Seed(GameSimulation simulation)
    {
        foreach (var command in VehicleSeeder.Plan(simulation, 60, spacing: 20))
        {
            simulation.Submit(command);
        }
    }
}
