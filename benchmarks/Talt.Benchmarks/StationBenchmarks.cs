using BenchmarkDotNet.Attributes;
using Talt.Core.Construction;
using Talt.Core.Maps;
using Talt.Core.Scenarios;
using Talt.Core.Simulation;
using Talt.Core.Stations;

namespace Talt.Benchmarks;

/// <summary>
/// Cost of stations at the reference scale (feature 010, SC-001, SC-002): a 4096 × 4096 map with 5 000 bays
/// beside industries and town roads (<see cref="StationSeeder"/>), every cargo served and the debug service
/// collecting 50 %. <see cref="DailyUpdate"/> is one day of stations: coverage check, industries handing their
/// output through the production sink, town passengers and mail, ratings and losses. The grouped game adds a
/// small warehouse beside every tenth station, so groups merge their stock.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 10)]
public class StationBenchmarks
{
    private const int StationCount = 5_000;
    private static readonly MapSettings MapSettings = new(4096, 4096, Seed: 9);

    private GameSimulation _plain = null!;
    private GameSimulation _grouped = null!;
    private StationRegistry _stations = null!;
    private StationRegistry _groupedStations = null!;
    private readonly CatchmentPreview _preview = new();
    private (int X, int Z)[] _previewTiles = [];
    private int _previewIndex;
    private GameDate _day;

    [GlobalSetup]
    public void Setup()
    {
        _plain = Create(warehouses: false);
        _grouped = Create(warehouses: true);
        _stations = _plain.Stations!;
        _groupedStations = _grouped.Stations!;
        _day = new GameDate(1950, 3, 2, new DateOnly(1950, 3, 2).DayNumber);
        _previewTiles = Enumerable.Range(0, 64).Select(i => ((i * 37) % 4000 + 40, (i * 53) % 4000 + 40)).ToArray();
        Console.WriteLine($"// plain: {_stations.Count} stations, {_stations.GroupCount} groups; grouped: {_groupedStations.Count} stations, {_groupedStations.GroupCount} groups");
    }

    /// <summary>One day of 5 000 served stations (each its own group).</summary>
    [Benchmark]
    public void DailyUpdate() => Day(_plain, _stations);

    /// <summary>One day of the same stations with about 500 warehouses grouping them.</summary>
    [Benchmark]
    public void DailyUpdateWithGroups() => Day(_grouped, _groupedStations);

    /// <summary>The monthly statistics rotation of 5 000 stations.</summary>
    [Benchmark]
    public void MonthlyUpdate() => _stations.AdvanceMonth(_day);

    /// <summary>One catchment preview of a bus bay (the tool recomputes it when the cursor moves to another tile).</summary>
    [Benchmark]
    public int CatchmentPreview()
    {
        var (x, z) = _previewTiles[_previewIndex++ & 63];
        _stations.PreviewCatchment(Company.PlayerId, warehouse: false, 0, x, z, _preview);
        return _preview.Tiles.Count;
    }

    private void Day(GameSimulation simulation, StationRegistry stations)
    {
        stations.BeginDay(_day);
        simulation.Industries.AdvanceDay(_day, stations);
        stations.AdvanceDay(_day, parallel: true);
    }

    private static GameSimulation Create(bool warehouses)
    {
        var simulation = GameSimulation.Create(
            new ScenarioSettings(Seed: 9, MapSizeTiles: 4096, VehicleCount: 0, Map: MapSettings, Stations: new StationSettings(50)),
            MapGenerator.Generate(MapSettings));
        var state = simulation.Construction!;
        state.Player.Refund(1_000_000_000_000);
        foreach (var command in StationSeeder.Plan(simulation, StationCount))
        {
            simulation.Submit(command);
        }

        simulation.ProcessPendingCommands();
        if (warehouses)
        {
            var stations = simulation.Stations!;
            var plan = new ConstructionPlan();
            var small = stations.Rules.FindWarehouse("small");
            var corners = new List<(int X, int Z)>();
            for (var slot = 0; slot < stations.Count; slot += 10)
            {
                var (x0, z0, _, _) = stations.BoundsAt(slot);
                foreach (var (dx, dz) in new[] { (3, 0), (-4, 0), (0, 3), (0, -4), (3, 3), (-4, -4) })
                {
                    StationPlanner.PlanWarehouse(state, Company.PlayerId, small, x0 + dx, z0 + dz, plan);
                    if (plan.IsValid)
                    {
                        corners.Add((x0 + dx, z0 + dz));
                        break;
                    }
                }
            }

            foreach (var (x, z) in corners)
            {
                simulation.Submit(new BuildWarehouseCommand(Company.PlayerId, "small", x, z));
            }

            simulation.ProcessPendingCommands();
        }

        // A month of play so coverage, served flags, ratings and waiting cargo reach a steady state.
        for (var i = 0; i < 30 * GameClock.TicksPerDay; i++)
        {
            simulation.Tick();
        }

        return simulation;
    }
}
