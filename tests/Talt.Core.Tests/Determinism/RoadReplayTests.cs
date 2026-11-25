using Talt.Core.Commands;
using Talt.Core.Construction;
using Talt.Core.Determinism;
using Talt.Core.Maps;
using Talt.Core.Roads;
using Talt.Core.Scenarios;
using Talt.Core.Simulation;

namespace Talt.Core.Tests.Determinism;

/// <summary>
/// SC-006: a game with a map, town streets and every kind of road action records and replays to the same
/// state; parallel and sequential runs agree and the fingerprint is pinned so every CI platform must agree.
/// </summary>
/// <remarks>
/// Regenerate the golden only when road rules, seeding, construction, finance or map generation change on purpose. It
/// changed with feature 008: towns are founded on the seeded streets (buildings, extra streets, town state).
/// </remarks>
public class RoadReplayTests
{
    private const string ExpectedAtTick400 = "9F3321082557F362";

    private static readonly ScenarioSettings Settings = new(
        Seed: 21,
        StartYear: 1990,
        MapSizeTiles: 256,
        Map: new MapSettings(256, 256, Seed: 21, Roughness: 60, MaxHeight: 40, WaterAmount: 20, SeaLevel: 6));

    [Fact]
    public void TownStreets_AreSeeded_AsValidTownRoads()
    {
        var simulation = GameSimulation.Create(Settings);
        var state = simulation.Construction!;

        Assert.True(state.Roads.RoadTileCount > 0);
        Assert.Null(state.Roads.Validate());
        Assert.True(state.Network.NodeCount > 0);
        for (var tile = 0; tile < state.Roads.Width * state.Roads.Depth; tile++)
        {
            if (state.Roads.HasRoad(tile))
            {
                Assert.Equal(TileObjectKind.Road, state.Objects.KindAt(tile));
                Assert.Equal(TileOwnerKind.Town, state.Objects.OwnerAt(tile).Kind);
            }
        }

        Assert.Equal(simulation.ComputeFingerprint(), GameSimulation.Create(Settings).ComputeFingerprint());
    }

    [Fact]
    public void RecordedRoadSession_ReplaysToTheSameFingerprints()
    {
        var simulation = GameSimulation.Create(Settings);
        var recorder = new SessionRecorder(new RecordedSession(simulation.Settings));
        simulation.AttachRecorder(recorder);
        Run(simulation, recorder, parallel: true);

        var session = RecordedSession.FromJson(recorder.Session.ToJson());
        var replay = SessionReplayer.Replay(session);

        Assert.True(replay.Succeeded, replay.FailureReason);
        Assert.All(replay.Checkpoints, c => Assert.True(c.Matches));
        var log = string.Join("; ", session.Entries.Select(e => $"{e.Tick} {e.Command.TypeName} {e.Outcome} {e.Reason}"));
        foreach (var type in new[] { BuildRoadCommand.Type, RemoveRoadCommand.Type, SetOneWayCommand.Type, BuildMotorwayCommand.Type })
        {
            Assert.True(session.Entries.Any(e => e.Outcome == CommandOutcome.Applied && e.Command.TypeName == type), $"{type} not applied: {log}");
        }
    }

    [Fact]
    public void ParallelAndSequential_Match_AndEqualTheGolden()
    {
        var parallel = GameSimulation.Create(Settings);
        Run(parallel, null, parallel: true);
        var sequential = GameSimulation.Create(Settings);
        Run(sequential, null, parallel: false);

        Assert.Equal(parallel.ComputeFingerprint(), sequential.ComputeFingerprint());
        Assert.Equal(ExpectedAtTick400, StateHasher.Format(parallel.ComputeFingerprint()));
    }

    /// <summary>The first command of a deterministic search over the map that previews as valid, or null.</summary>
    private static ConstructionCommand? FirstValid(GameSimulation simulation, Func<int, int, ConstructionCommand> create)
    {
        var state = simulation.Construction!;
        var planner = new ConstructionPlanner();
        var plan = new ConstructionPlan();
        for (var z = 8; z < 240; z += 12)
        {
            for (var x = 8; x < 240; x += 12)
            {
                var command = create(x, z);
                if (command.Preview(state, planner, plan).Succeeded)
                {
                    return command;
                }
            }
        }

        return null;
    }

    private static void Submit(GameSimulation simulation, ConstructionCommand? command)
    {
        if (command is not null)
        {
            simulation.Submit(command);
        }
    }

    /// <summary>A fixed script with every road action (some processed while paused) and an undo.</summary>
    private static void Run(GameSimulation simulation, SessionRecorder? recorder, bool parallel)
    {
        const int Player = Company.PlayerId;
        (int X, int Z) road = (0, 0);
        for (var t = 0; t < 400; t++)
        {
            switch (t)
            {
                case 5:
                    var build = FirstValid(simulation, (x, z) => new BuildRoadCommand(Player, "fast", x, z, x + 14, z + 9));
                    if (build is BuildRoadCommand b)
                    {
                        road = (b.X0, b.Z0);
                    }

                    Submit(simulation, build);
                    break;
                case 20:
                    Submit(simulation, FirstValid(simulation, (x, z) => new SetOneWayCommand(Player, x, z)));
                    simulation.Submit(new SetOneWayCommand(Player, road.X + 1, road.Z));
                    break;
                case 40:
                    Submit(simulation, FirstValid(simulation, (x, z) => new BuildMotorwayCommand(Player, x, z, x + 16, z)));
                    break;
                case 60:
                    Submit(simulation, FirstValid(simulation, (x, z) => new BuildBridgeCommand(Player, "paved", x, z, x + 6, z)));
                    Submit(simulation, FirstValid(simulation, (x, z) => new BuildTunnelCommand(Player, "paved", x, z, -1)));
                    break;
                case 80:
                    simulation.Submit(new UndoConstructionCommand(Player));
                    break;
                case 120:
                    // Paused: applied without a tick.
                    simulation.Submit(new RemoveRoadCommand(Player, road.X, road.Z, road.X + 3, road.Z));
                    simulation.ProcessPendingCommands();
                    Submit(simulation, FirstValid(simulation, (x, z) => new BuildMotorwayConnectorCommand(Player, x, z, true)));
                    simulation.ProcessPendingCommands();
                    break;
                case 200:
                    Submit(simulation, FirstValid(simulation, (x, z) => new RemoveRoadCommand(Player, x, z, x + 4, z)));
                    break;
            }

            simulation.Tick(parallel);
            if (recorder is not null && simulation.CurrentTick % 100 == 0)
            {
                recorder.AddCheckpoint(simulation);
            }
        }
    }
}
