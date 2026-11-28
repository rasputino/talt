using Talt.Core.Commands;
using Talt.Core.Determinism;
using Talt.Core.Maps;
using Talt.Core.Scenarios;
using Talt.Core.Simulation;
using Talt.Core.Stations;

namespace Talt.Core.Tests.Determinism;

/// <summary>
/// Feature 010 (SC-004): a game with seeded stations, a warehouse and the debug service records and replays to the
/// same state; parallel and sequential runs agree and the fingerprint is pinned so every CI platform must agree.
/// </summary>
/// <remarks>
/// Regenerate the golden only when station rules, their data file, industries, towns, construction, finance or map
/// generation change on purpose.
/// </remarks>
public class StationReplayTests
{
    // Feature 017 hashes the expanded company ledger and the station and town observations from the January close.
    private const string ExpectedAtTick2000 = "3961153F48E862C1";

    private static readonly ScenarioSettings Settings = new(
        Seed: 31,
        StartYear: 1960,
        MapSizeTiles: 256,
        VehicleCount: 0,
        Map: new MapSettings(256, 256, Seed: 31, Roughness: 40, MaxHeight: 36, WaterAmount: 15, SeaLevel: 6),
        Stations: new StationSettings(40));

    [Fact]
    public void ParallelAndSequential_Match_AndEqualTheGolden()
    {
        var parallel = Run(parallel: true, recorder: null);
        var sequential = Run(parallel: false, recorder: null);

        Assert.True(parallel.Stations!.Count >= 4);
        Assert.True(parallel.Stations.TotalSupplied > 0);
        Assert.Equal(parallel.ComputeFingerprint(), sequential.ComputeFingerprint());
        Assert.Equal(ExpectedAtTick2000, StateHasher.Format(parallel.ComputeFingerprint()));
    }

    [Fact]
    public void RecordedStationSession_Replays()
    {
        var simulation = GameSimulation.Create(Settings);
        var recorder = new SessionRecorder(new RecordedSession(simulation.Settings));
        simulation.AttachRecorder(recorder);
        Drive(simulation, recorder, parallel: true);

        var replay = SessionReplayer.Replay(RecordedSession.FromJson(recorder.Session.ToJson()));

        Assert.True(replay.Succeeded, replay.FailureReason);
        Assert.All(replay.Checkpoints, c => Assert.True(c.Matches));
        Assert.Equal(simulation.ComputeFingerprint(), replay.Simulation.ComputeFingerprint());
    }

    private static GameSimulation Run(bool parallel, SessionRecorder? recorder)
    {
        var simulation = GameSimulation.Create(Settings);
        Drive(simulation, recorder, parallel);
        return simulation;
    }

    /// <summary>Seeds stations, adds a warehouse beside the first one, renames it and runs 2 000 ticks.</summary>
    private static void Drive(GameSimulation simulation, SessionRecorder? recorder, bool parallel)
    {
        foreach (var command in StationSeeder.Plan(simulation, 8))
        {
            simulation.Submit(command);
        }

        simulation.Tick(parallel);
        var stations = simulation.Stations!;
        var state = simulation.Construction!;
        var plan = new Talt.Core.Construction.ConstructionPlan();
        var (x0, z0, _, _) = stations.BoundsAt(0);
        foreach (var (dx, dz) in new[] { (3, 0), (-4, 0), (0, 3), (0, -4), (3, 3), (-4, -4) })
        {
            StationPlanner.PlanWarehouse(state, Talt.Core.Construction.Company.PlayerId, 0, x0 + dx, z0 + dz, plan);
            if (plan.IsValid)
            {
                simulation.Submit(new BuildWarehouseCommand(Talt.Core.Construction.Company.PlayerId, "small", x0 + dx, z0 + dz));
                break;
            }
        }

        simulation.Submit(new RenameStationCommand(Talt.Core.Construction.Company.PlayerId, stations.IdAt(0), "Golden Gate"));
        while (simulation.CurrentTick < 2000)
        {
            simulation.Tick(parallel);
            if (recorder is not null && simulation.CurrentTick % 500 == 0)
            {
                recorder.AddCheckpoint(simulation);
            }
        }
    }
}
