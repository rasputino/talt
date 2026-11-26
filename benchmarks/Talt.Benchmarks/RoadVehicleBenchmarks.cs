using BenchmarkDotNet.Attributes;
using Talt.Core.Maps;
using Talt.Core.Network;
using Talt.Core.Scenarios;
using Talt.Core.Simulation;
using Talt.Core.Vehicles;

namespace Talt.Benchmarks;

/// <summary>
/// Cost of road vehicles at the reference scale (feature 012, SC-001): a 4096 × 4096 map with a road every 16 tiles,
/// bus bays on a 40-tile lattice with a depot each, lines between neighbouring connected bays and 10 000, 50 000 or
/// 100 000 buses (<see cref="VehicleSeeder"/>, at most eight per line), driven for 2 400 ticks so they have left their
/// depots. Each invocation is one full <see cref="GameSimulation.Tick"/> (vehicles with lanes, junction reservations,
/// stops, loading, breakdowns, routes; plus every other system and the day events once every 40 ticks).
/// </summary>
[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 5, iterationCount: 15, invocationCount: 40)]
public class RoadVehicleBenchmarks
{
    private GameSimulation _simulation = null!;

    [Params(10_000, 50_000, 100_000)]
    public int Vehicles { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _simulation = Create(Vehicles);
        var vehicles = _simulation.RoadVehicles!;
        Console.WriteLine($"// {vehicles.Count} vehicles on {vehicles.Lines.Count} lines, {vehicles.Depots.Count} depots");
    }

    /// <summary>A seeded reference game after its vehicles left the depots (also used by tools and tests).</summary>
    internal static GameSimulation Create(int vehicles, int warmupTicks = 2400)
    {
        var map = MapSettings.TryParse("T2-4096x4096-s7-r50-h28-w25-l7", out var settings, out _) ? settings : null;
        var simulation = GameSimulation.Create(new ScenarioSettings(Seed: 7, VehicleCount: 0, MapSizeTiles: 4096, Map: map));
        simulation.Construction!.SeedDebugGrid(16);
        foreach (var command in VehicleSeeder.Plan(simulation, vehicles))
        {
            simulation.Submit(command);
        }

        for (var t = 0; t < warmupTicks; t++)
        {
            simulation.Tick(parallel: true);
        }

        return simulation;
    }

    /// <summary>One full simulation tick, the drive step on all cores (the game's setting).</summary>
    [Benchmark]
    public void Tick() => _simulation.Tick(parallel: true);

    /// <summary>One full simulation tick on one core (for the parallel speed-up).</summary>
    [Benchmark]
    public void TickSequential() => _simulation.Tick(parallel: false);
}

/// <summary>
/// Route queries for vehicles (feature 012, SC-006) on the same grid map: an uncached A* search between nodes at least
/// 1 000 tiles apart, and a search between neighbouring bays (the usual departure).
/// </summary>
[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 10)]
public class RouteBenchmarks
{
    private readonly List<RouteStep> _steps = [];
    private RoadRouter _router = null!;
    private (int From, int To)[] _long = [];
    private (int From, int To)[] _short = [];
    private int _index;

    [GlobalSetup]
    public void Setup()
    {
        var simulation = RoadVehicleBenchmarks.Create(5_000, warmupTicks: 0);
        simulation.Tick();
        var network = simulation.Construction!.Network;
        _router = new RoadRouter(network, useCache: false);
        var nodes = network.NodeTiles();
        var width = simulation.Construction.Map.Width;
        var random = new Random(5);
        var longPairs = new List<(int, int)>();
        var shortPairs = new List<(int, int)>();
        while (longPairs.Count < 64 || shortPairs.Count < 64)
        {
            var a = nodes[random.Next(nodes.Count)];
            var b = nodes[random.Next(nodes.Count)];
            var distance = Math.Abs((a % width) - (b % width)) + Math.Abs((a / width) - (b / width));
            if (distance >= 1000 && longPairs.Count < 64 && _router.TryFindRoute(a, b, _steps, out _))
            {
                longPairs.Add((a, b));
            }
            else if (distance is >= 30 and <= 60 && shortPairs.Count < 64 && _router.TryFindRoute(a, b, _steps, out _))
            {
                shortPairs.Add((a, b));
            }
        }

        _long = [.. longPairs];
        _short = [.. shortPairs];
    }

    [Benchmark]
    public long LongRoute()
    {
        var (from, to) = _long[_index++ & 63];
        _router.TryFindRoute(from, to, _steps, out var length);
        return length;
    }

    [Benchmark]
    public long DepartureRoute()
    {
        var (from, to) = _short[_index++ & 63];
        _router.TryFindRoute(from, to, _steps, out var length);
        return length;
    }
}
