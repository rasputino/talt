using BenchmarkDotNet.Attributes;
using Talt.Core.Maps;
using Talt.Core.Scenarios;
using Talt.Core.Simulation;
using Talt.Core.Towns;

namespace Talt.Benchmarks;

/// <summary>
/// Cost of towns at the reference scale (feature 008, SC-002): a 4096 × 4096 map with all its towns
/// (about 500). <see cref="MonthlyUpdateServed"/> is the worst case: every town fully served, so every town
/// runs growth actions (buildings, replacements, streets with network updates) every month.
/// </summary>
/// <remarks>
/// One invocation per iteration: towns keep growing, so each measured month follows the previous one (about the
/// first year of play) instead of thousands of months of growth.
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 2, iterationCount: 10, invocationCount: 1)]
public class TownBenchmarks
{
    private static readonly MapSettings MapSettings = new(4096, 4096, Seed: 9);

    private MapData _map = null!;
    private GameSimulation _served = null!;
    private GameSimulation _unserved = null!;
    private GameDate _month;

    [GlobalSetup]
    public void Setup()
    {
        // Each game owns (and changes) its map's tile data, so every game gets its own copy.
        _served = Create(100, MapGenerator.Generate(MapSettings));
        _unserved = Create(0, MapGenerator.Generate(MapSettings));
        _month = new GameDate(1950, 2, 1, new DateOnly(1950, 2, 1).DayNumber);
        Console.WriteLine($"// {_served.Towns!.Count} towns, {_served.Towns.Buildings.Count} buildings");
    }

    /// <summary>One month of 500 served towns (statistics, ratings and growth).</summary>
    [Benchmark]
    public void MonthlyUpdateServed() => _served.Towns!.AdvanceMonth(_month, parallel: true);

    /// <summary>One month of 500 towns without service (statistics and ratings only).</summary>
    [Benchmark]
    public void MonthlyUpdateUnserved() => _unserved.Towns!.AdvanceMonth(_month, parallel: true);

    [IterationSetup(Target = nameof(CreateGameWithTowns))]
    public void FreshMap() => _map = MapGenerator.Generate(MapSettings);

    /// <summary>Creating a game on a generated map: industries, town streets, founding every town, the road network.</summary>
    [Benchmark]
    public TownRegistry CreateGameWithTowns() => Create(0, _map).Towns!;

    private static GameSimulation Create(int servicePercent, MapData map) => GameSimulation.Create(
        new ScenarioSettings(Seed: 9, MapSizeTiles: 4096, VehicleCount: 0, Map: MapSettings, Towns: new TownSettings(DebugServicePercent: servicePercent)),
        map);
}
