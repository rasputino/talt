using Talt.Core.Commands;
using Talt.Core.Simulation;
using Talt.Core.Tests.Simulation;
using Talt.Core.Tests.TestCommands;

namespace Talt.Core.Tests.Determinism;

/// <summary>SC-001 / FR-012 / FR-016: recorded sessions reproduce identical state at every tick.</summary>
public class ReplayDeterminismTests
{
    private const int Ticks = 10_000;

    [Fact]
    public void TwoRuns_WithTheSameCommands_MatchAtEveryTick()
    {
        var a = Run(parallel: true);
        var b = Run(parallel: true);

        Assert.Equal(a, b);
    }

    [Fact]
    public void ParallelAndSequential_MatchAtEveryTick()
    {
        Assert.Equal(Run(parallel: false), Run(parallel: true));
    }

    [Fact]
    public void RecordedSession_ReplaysToTheSameFingerprints()
    {
        TestCommandRegistration.EnsureRegistered();
        var simulation = GameSimulation.Create(SimulationTestScenarios.Small());
        var recorder = new SessionRecorder(new RecordedSession(simulation.Settings));
        simulation.AttachRecorder(recorder);

        for (var t = 0; t < 3_000; t++)
        {
            Script(simulation, t);
            simulation.Tick();
            if (simulation.CurrentTick % 500 == 0)
            {
                recorder.AddCheckpoint(simulation);
            }
        }

        var json = recorder.Session.ToJson();
        var replay = SessionReplayer.Replay(RecordedSession.FromJson(json), parallel: false);

        Assert.True(replay.Succeeded, replay.FailureReason);
        Assert.Equal(6, replay.Checkpoints.Count);
        Assert.All(replay.Checkpoints, c => Assert.True(c.Matches));
        Assert.Equal(simulation.ComputeFingerprint(), replay.Simulation.ComputeFingerprint());
    }

    [Fact]
    public void Replay_DetectsATamperedCheckpoint()
    {
        var simulation = GameSimulation.Create(SimulationTestScenarios.Tiny());
        var recorder = new SessionRecorder(new RecordedSession(simulation.Settings));
        simulation.AttachRecorder(recorder);
        for (var t = 0; t < 100; t++)
        {
            simulation.Tick();
        }

        recorder.Session.Checkpoints.Add(new RecordedCheckpoint(100, simulation.ComputeFingerprint() ^ 1));

        var replay = SessionReplayer.Replay(recorder.Session);

        Assert.False(replay.Succeeded);
        Assert.Equal(100, replay.FirstFailingTick);
    }

    /// <summary>Fingerprint at every tick, as one combined list.</summary>
    private static List<ulong> Run(bool parallel)
    {
        var simulation = GameSimulation.Create(SimulationTestScenarios.Small());
        var fingerprints = new List<ulong>(Ticks);
        for (var t = 0; t < Ticks; t++)
        {
            Script(simulation, t);
            simulation.Tick(parallel);
            fingerprints.Add(simulation.ComputeFingerprint());
        }

        return fingerprints;
    }

    /// <summary>Fixed command script: adds, removes, invalid commands and RNG use.</summary>
    private static void Script(GameSimulation simulation, int tick)
    {
        if (tick % 97 == 0)
        {
            simulation.Submit(new AddVehicleCommand(tick % simulation.Vehicles.LineCount, SyntheticTraffic.MinCruiseSpeed + tick));
        }

        if (tick % 131 == 0 && simulation.Vehicles.VehicleCount > 0)
        {
            simulation.Submit(new RemoveVehicleCommand(simulation.Vehicles.VehicleIdAt(tick % simulation.Vehicles.VehicleCount)));
        }

        if (tick % 499 == 0)
        {
            simulation.Submit(new RemoveVehicleCommand(-tick)); // Always rejected.
            simulation.Submit(new ConsumeRandomCommand(3));
        }
    }
}
