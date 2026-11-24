using Talt.Core.Commands;
using Talt.Core.Construction;
using Talt.Core.Network;
using Talt.Core.Roads;
using Talt.Core.Scenarios;
using Talt.Core.Simulation;
using Talt.Core.Tests.Construction;

namespace Talt.Core.Tests.Roads;

/// <summary>Helpers for road tests: a game on a hand-made map, running commands at once, routes.</summary>
internal sealed class RoadKit
{
    private readonly RoadRouter _router;

    public RoadKit(TestMap? map = null, int startYear = 1950, long balance = 100_000_000)
    {
        map ??= new TestMap();
        var data = map.Build();
        var settings = new ScenarioSettings(Seed: 3, StartYear: startYear, MapSizeTiles: Math.Min(64, data.Width), RoadSpacing: 16, RoadRemovalChancePerMille: 0, VehicleCount: 0, LineCount: 1, Map: data.Settings);
        Simulation = GameSimulation.Create(settings, data, rules: TestMap.Rules with { StartingBalance = balance });
        Simulation.CommandProcessed += processed => Last = processed.Result;
        _router = new RoadRouter(State.Network, useCache: false);
    }

    public GameSimulation Simulation { get; }

    public ConstructionState State => Simulation.Construction!;

    public RoadLayer Roads => State.Roads;

    public CommandResult Last { get; private set; }

    public int Tile(int x, int z) => State.Map.Index(x, z);

    public RoadPiece Piece(int x, int z) => Roads.PieceAt(Tile(x, z));

    /// <summary>Applies a command now (as while paused) and returns its result.</summary>
    public CommandResult Run(ISimulationCommand command)
    {
        Simulation.Submit(command);
        Simulation.ProcessPendingCommands();
        Assert.Null(Roads.Validate());
        return Last;
    }

    public CommandResult Road(int x0, int z0, int x1, int z1, string type = "paved") =>
        Run(new BuildRoadCommand(Company.PlayerId, type, x0, z0, x1, z1));

    /// <summary>Plans a command into a fresh plan without applying it.</summary>
    public (CommandResult Result, ConstructionPlan Plan) Preview(ConstructionCommand command)
    {
        var plan = new ConstructionPlan();
        var result = command.Preview(State, new ConstructionPlanner(), plan);
        return (result, plan);
    }

    /// <summary>Shortest route length between two node tiles, or -1.</summary>
    public long Route(int x0, int z0, int x1, int z1)
    {
        var steps = new List<RouteStep>();
        return _router.TryFindRoute(Tile(x0, z0), Tile(x1, z1), steps, out var length) ? length : -1;
    }

    /// <summary>The network must equal one built from scratch from the same tiles (FR-004).</summary>
    public void AssertNetworkMatchesRebuild()
    {
        var fresh = RoadNetwork.Build(Roads);
        Span<int> speeds = stackalloc int[State.RoadRules.Types.Count];
        for (var i = 0; i < speeds.Length; i++)
        {
            speeds[i] = State.RoadRules.Types[i].SpeedKmh;
        }

        fresh.SetTypeSpeeds(speeds);
        fresh.BuildAll();
        Assert.Equal(fresh.NodeTiles(), State.Network.NodeTiles());
        Assert.Equal(fresh.Canonical(), State.Network.Canonical());
    }
}
