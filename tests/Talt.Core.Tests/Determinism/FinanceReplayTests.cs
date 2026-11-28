using Talt.Core.Commands;
using Talt.Core.Construction;
using Talt.Core.Determinism;
using Talt.Core.Finance;
using Talt.Core.Maps;
using Talt.Core.Scenarios;
using Talt.Core.Simulation;
using Talt.Core.Stations;

namespace Talt.Core.Tests.Determinism;

/// <summary>
/// Feature 011 (SC-001): a game with seeded stations, the paid debug service, inflation, loans and month ends
/// records and replays to the same state; parallel and sequential runs agree and the fingerprint is pinned so
/// every CI platform must agree.
/// </summary>
/// <remarks>Regenerate the golden only when finance rules or their data file change on purpose (or anything 010's golden depends on).</remarks>
public class FinanceReplayTests
{
    // Feature 017 includes period valuations, physical cargo delivery totals, and closed station and town histories.
    private const string ExpectedAtTick6000 = "B6FA665A5A3DB0DA";

    private static readonly ScenarioSettings Settings = new(
        Seed: 33,
        StartYear: 1960,
        MapSizeTiles: 256,
        VehicleCount: 0,
        Map: new MapSettings(256, 256, Seed: 33, Roughness: 40, MaxHeight: 36, WaterAmount: 15, SeaLevel: 6),
        Stations: new StationSettings(50),
        Finance: new FinanceSettings(Inflation: true, DebugServicePays: true));

    [Fact]
    public void ParallelAndSequential_Match_AndEqualTheGolden()
    {
        var parallel = Run(parallel: true, recorder: null);
        var sequential = Run(parallel: false, recorder: null);

        Assert.True(parallel.Finance!.TotalPaid > 0);
        Assert.Equal(parallel.ComputeFingerprint(), sequential.ComputeFingerprint());
        Assert.Equal(ExpectedAtTick6000, StateHasher.Format(parallel.ComputeFingerprint()));
    }

    [Fact]
    public void RecordedFinanceSession_Replays()
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

    /// <summary>Seeds stations, borrows, repays later and runs 6 000 ticks (five months).</summary>
    private static void Drive(GameSimulation simulation, SessionRecorder? recorder, bool parallel)
    {
        foreach (var command in StationSeeder.Plan(simulation, 8))
        {
            simulation.Submit(command);
        }

        simulation.Submit(new ChangeLoanCommand(Company.PlayerId, LoanMode.Borrow));
        simulation.Submit(new ChangeLoanCommand(Company.PlayerId, LoanMode.Borrow));
        while (simulation.CurrentTick < 6000)
        {
            simulation.Tick(parallel);
            if (simulation.CurrentTick == 3000)
            {
                simulation.Submit(new ChangeLoanCommand(Company.PlayerId, LoanMode.Repay));
            }

            if (recorder is not null && simulation.CurrentTick % 1000 == 0)
            {
                recorder.AddCheckpoint(simulation);
            }
        }
    }
}
