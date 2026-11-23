using Talt.Core.Commands;
using Talt.Core.Construction;
using Talt.Core.Determinism;
using Talt.Core.Maps;
using Talt.Core.Scenarios;
using Talt.Core.Simulation;

namespace Talt.Core.Tests.Determinism;

/// <summary>
/// FR-014 / SC-006: a game with a map and construction commands (some processed while paused) records
/// and replays to the same state, and its fingerprint is pinned so every CI platform must agree.
/// </summary>
/// <remarks>
/// Regenerate the golden only when construction rules, industries or map generation change on purpose.
/// It changed when features 006 and 009 were merged: games with a map now also have industries.
/// </remarks>
public class ConstructionReplayTests
{
    private const string ExpectedAtTick600 = "349DFBE5EE272168";

    private static readonly ScenarioSettings Settings = new(
        Seed: 9,
        MapSizeTiles: 256,
        RoadSpacing: 16,
        VehicleCount: 300,
        LineCount: 30,
        Map: new MapSettings(256, 256, Seed: 9, Roughness: 70, MaxHeight: 48, WaterAmount: 20, SeaLevel: 6));

    [Fact]
    public void RecordedConstructionSession_ReplaysToTheSameFingerprints()
    {
        var simulation = GameSimulation.Create(Settings);
        var recorder = new SessionRecorder(new RecordedSession(simulation.Settings));
        simulation.AttachRecorder(recorder);
        Run(simulation, recorder, parallel: true);

        var session = RecordedSession.FromJson(recorder.Session.ToJson());
        Assert.Equal(Settings.Map, session.Settings.Map);
        var replay = SessionReplayer.Replay(session);

        Assert.True(replay.Succeeded, replay.FailureReason);
        Assert.All(replay.Checkpoints, c => Assert.True(c.Matches));
        var log = string.Join("; ", session.Entries.Select(e => $"{e.Tick} {e.Command.TypeName} {e.Outcome} {e.Reason}"));
        Assert.True(session.Entries.Count(e => e.Outcome == CommandOutcome.Applied && e.Command is TerraformCommand) >= 2, log);
        Assert.True(session.Entries.Any(e => e.Outcome == CommandOutcome.Applied && e.Command is UndoConstructionCommand), log);
        Assert.True(session.Entries.Any(e => e.Outcome == CommandOutcome.Applied && e.Command is ClearAreaCommand), log);
    }

    [Fact]
    public void ParallelAndSequential_Match_AndEqualTheGolden()
    {
        var parallel = GameSimulation.Create(Settings);
        Run(parallel, null, parallel: true);
        var sequential = GameSimulation.Create(Settings);
        Run(sequential, null, parallel: false);

        Assert.Equal(parallel.ComputeFingerprint(), sequential.ComputeFingerprint());
        Assert.Equal(ExpectedAtTick600, StateHasher.Format(parallel.ComputeFingerprint()));
    }

    /// <summary>The first block between the prototype roads where the command is valid (deterministic search).</summary>
    private static ConstructionCommand FirstValid(GameSimulation simulation, Func<int, int, ConstructionCommand> create)
    {
        var state = simulation.Construction!;
        var planner = new ConstructionPlanner();
        var plan = new ConstructionPlan();
        for (var bz = 0; bz < 15; bz++)
        {
            for (var bx = 0; bx < 15; bx++)
            {
                var command = create(9 + (bx * 16), 9 + (bz * 16));
                if (command.Preview(state, planner, plan).Succeeded)
                {
                    return command;
                }
            }
        }

        throw new InvalidOperationException("No block of the test map accepts the command.");
    }

    /// <summary>A fixed script: terraform, clear, undo, a refused action and paused processing.</summary>
    private static void Run(GameSimulation simulation, SessionRecorder? recorder, bool parallel)
    {
        const int Player = Company.PlayerId;
        for (var t = 0; t < 600; t++)
        {
            switch (t)
            {
                case 10:
                    simulation.Submit(FirstValid(simulation, (x, z) => new TerraformCommand(Player, TerraformMode.Level, x, z, x + 9, z + 7, x, z)));
                    break;
                case 50:
                    simulation.Submit(FirstValid(simulation, (x, z) => new ClearAreaCommand(Player, x, z, x + 14, z + 14)));
                    break;
                case 60:
                    simulation.Submit(new UndoConstructionCommand(Player));
                    break;
                case 120:
                    // Paused: applied without a tick, recorded at the current tick.
                    simulation.Submit(FirstValid(simulation, (x, z) => new TerraformCommand(Player, TerraformMode.Raise, x, z, x + 7, z + 7)));
                    simulation.ProcessPendingCommands();
                    simulation.Submit(FirstValid(simulation, (x, z) => new TerraformCommand(Player, TerraformMode.Lower, x + 2, z + 2, x + 6, z + 6)));
                    simulation.ProcessPendingCommands();
                    simulation.Submit(new TerraformCommand(Player, TerraformMode.Raise, 0, 0, 255, 255)); // Too large.
                    break;
                case 400:
                    simulation.Submit(new UndoConstructionCommand(Player)); // Expired.
                    break;
            }

            simulation.Tick(parallel);
            if (recorder is not null && simulation.CurrentTick % 200 == 0)
            {
                recorder.AddCheckpoint(simulation);
            }
        }
    }
}
