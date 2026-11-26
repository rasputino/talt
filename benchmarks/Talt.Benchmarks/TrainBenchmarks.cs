using BenchmarkDotNet.Attributes;
using Talt.Core.Construction;
using Talt.Core.Maps;
using Talt.Core.Rails;
using Talt.Core.Scenarios;
using Talt.Core.Simulation;

namespace Talt.Benchmarks;

/// <summary>
/// Cost of trains at the reference scale (feature 015, SC-001): a flat 4096 × 4096 map with clockwise racetrack loops
/// (<see cref="TrainSeeder"/>: two stations, one-way path signals every five tiles, eight trains of a locomotive and two
/// coaches per loop) and 1 000 or 5 000 trains, run for 2 400 ticks so they have left their depots. Each invocation is one
/// full <see cref="GameSimulation.Tick"/> (train move phase, reservations at signals, stops, loading, routes; plus every
/// other system and the day events once every 40 ticks).
/// </summary>
[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 5, iterationCount: 15, invocationCount: 40)]
public class TrainBenchmarks
{
    private GameSimulation _simulation = null!;

    [Params(1_000, 5_000)]
    public int Trains { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _simulation = Create(Trains);
        Console.WriteLine($"// {_simulation.Trains!.Count} trains, {_simulation.Construction!.Rails.RailTileCount} track tiles");
    }

    internal static GameSimulation Flat(int size)
    {
        var count = size * size;
        var heights = new byte[count];
        Array.Fill(heights, (byte)10);
        var flags = new byte[count];
        Array.Fill(flags, (byte)TileTraits.Buildable);
        var map = new MapData(new MapSettings(size, size, 1), heights, new byte[count], new byte[count], flags, new byte[count], [], default);
        var settings = new ScenarioSettings(Seed: 7, VehicleCount: 0, MapSizeTiles: 64, Map: map.Settings);
        return GameSimulation.Create(settings, map, rules: ConstructionRules.Default with { StartingBalance = long.MaxValue / 4, MaxTilesPerAction = 1_000_000 });
    }

    internal static GameSimulation Create(int trains, int warmupTicks = 2400)
    {
        var simulation = Flat(4096);
        foreach (var command in TrainSeeder.Plan(simulation, trains))
        {
            simulation.Submit(command);
        }

        for (var t = 0; t < warmupTicks; t++)
        {
            simulation.Tick(parallel: true);
        }

        return simulation;
    }

    /// <summary>One full simulation tick, the move phase on all cores (the game's setting).</summary>
    [Benchmark]
    public void Tick() => _simulation.Tick(parallel: true);

    /// <summary>One full simulation tick on one core (for the parallel speed-up).</summary>
    [Benchmark]
    public void TickSequential() => _simulation.Tick(parallel: false);
}

/// <summary>
/// Train route search (feature 015, SC-005): a double-track ladder 1 000 tiles long with crossovers every 50 tiles; an
/// uncached search from one end to a station at the other.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 10)]
public class RailRouteBenchmarks
{
    private readonly RailRoute _route = new();
    private RailRouter _router = null!;
    private int _start;
    private int _station;

    [GlobalSetup]
    public void Setup()
    {
        var simulation = TrainBenchmarks.Flat(1536);
        var player = Company.PlayerId;
        void Run(Talt.Core.Commands.ISimulationCommand command)
        {
            simulation.Submit(command);
            simulation.ProcessPendingCommands();
        }

        for (var x = 10; x < 1010; x += 100)
        {
            Run(new BuildRailCommand(player, "normal", x, 100, x + 100, 100));
            Run(new BuildRailCommand(player, "normal", x, 104, x + 100, 104));
        }

        for (var x = 40; x < 1000; x += 50)
        {
            Run(new BuildRailCommand(player, "normal", x, 100, x + 4, 104));
        }

        Run(new BuildRailStationCommand(player, 1005, 100, 0, 1, 5));
        var state = simulation.Construction!;
        _station = state.Stations!.TryGetStationAt(1005, 100, out var id) ? id : throw new InvalidOperationException("No station.");
        _start = state.Map.Index(10, 100);
        _router = new RailRouter(state);
        if (!Search())
        {
            throw new InvalidOperationException("No route.");
        }

        Console.WriteLine($"// {_route.Count} steps, {_router.LastExpanded} states expanded");
    }

    [Benchmark]
    public bool Route1000() => Search();

    private bool Search() =>
        _router.FindRoute(_start, Talt.Core.Roads.Dir8.E, RouteGoalKind.Station, _station, Company.PlayerId, false, null, 0, 0, 1_000_000, _route, out _, out _);
}
