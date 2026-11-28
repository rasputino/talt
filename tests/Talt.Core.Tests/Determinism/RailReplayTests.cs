using Talt.Core.Commands;
using Talt.Core.Determinism;
using Talt.Core.Maps;
using Talt.Core.Rails;
using Talt.Core.Scenarios;
using Talt.Core.Simulation;

namespace Talt.Core.Tests.Determinism;

/// <summary>
/// Feature 015 (SC-003): a generated map game with seeded racetrack loops (track, stations, one-way path signals, depots,
/// trains on lines) records and replays to the same state; parallel and sequential runs agree and the fingerprint is
/// pinned so every CI platform must agree.
/// </summary>
/// <remarks>Regenerate the golden only when rail rules, their data file, construction, stations or map generation change on purpose.</remarks>
public class RailReplayTests
{
    // Changed on purpose: feature 013, trains carry destination cargo (051623FE7E98467D before); then the seeder levels
    // sloping loops, so none is left open with its trains stuck in the depot (51B7F8F782F00DFE before).
    // Feature 016 extends the hashed road model rules and sale windows (A71D30A0AD70B669 before).
    // Feature 017 includes attributed line accounts, dispatch clocks, rail passages, and company, station and town histories.
    private const string ExpectedAtTick3000 = "4EEC5CCF4CEE70FA";

    private static readonly ScenarioSettings Settings = new(
        Seed: 41,
        StartYear: 1960,
        MapSizeTiles: 256,
        VehicleCount: 0,
        Map: new MapSettings(256, 256, Seed: 41, Roughness: 20, MaxHeight: 24, WaterAmount: 10, SeaLevel: 4));

    [Fact]
    public void ParallelAndSequential_Match_AndEqualTheGolden()
    {
        var parallel = Run(parallel: true);
        var sequential = Run(parallel: false);

        Assert.True(parallel.Trains!.Count >= 8, $"{parallel.Trains.Count} trains");
        Assert.Equal(parallel.ComputeFingerprint(), sequential.ComputeFingerprint());
        Assert.Equal(ExpectedAtTick3000, StateHasher.Format(parallel.ComputeFingerprint()));
    }

    [Fact]
    public void RecordedRailSession_Replays()
    {
        var simulation = GameSimulation.Create(Settings);
        var recorder = new SessionRecorder(new RecordedSession(simulation.Settings));
        simulation.AttachRecorder(recorder);
        Drive(simulation, parallel: true);
        recorder.AddCheckpoint(simulation);

        var replay = SessionReplayer.Replay(RecordedSession.FromJson(recorder.Session.ToJson()));

        Assert.True(replay.Succeeded, replay.FailureReason);
        Assert.All(replay.Checkpoints, c => Assert.True(c.Matches));
        Assert.Equal(simulation.ComputeFingerprint(), replay.Simulation.ComputeFingerprint());
    }

    [Fact]
    public void TrainLines_AreRoutedForDestinationCargo()
    {
        // Feature 013: trains are a link source of the router (mode-agnostic routing).
        var simulation = GameSimulation.Create(Settings);
        foreach (var command in TrainSeeder.Plan(simulation, 24))
        {
            simulation.Submit(command);
        }

        for (var t = 0; t < 400; t++)
        {
            simulation.Tick();
        }

        Assert.Equal(0, simulation.RoadVehicles!.Count);
        Assert.True(simulation.Router!.AppliedCount > 0, "routes applied for train lines");
        Assert.True(simulation.Router.BuildProblemsNow() > 0, "train lines give a routing problem");
        Assert.True(simulation.Router.ProblemSize().Edges > 0);
    }

    private static GameSimulation Run(bool parallel)
    {
        var simulation = GameSimulation.Create(Settings);
        Drive(simulation, parallel);
        return simulation;
    }

    private static void Drive(GameSimulation simulation, bool parallel)
    {
        foreach (var command in TrainSeeder.Plan(simulation, 24))
        {
            simulation.Submit(command);
        }

        for (var t = 0; t < 3000; t++)
        {
            simulation.Tick(parallel);
        }

        Assert.Null(simulation.Trains!.CheckInvariants());
    }
}
