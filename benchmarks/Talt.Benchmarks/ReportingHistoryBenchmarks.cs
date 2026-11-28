using BenchmarkDotNet.Attributes;
using Talt.Core.Maps;
using Talt.Core.Scenarios;
using Talt.Core.Simulation;
using Talt.Core.Stations;

namespace Talt.Benchmarks;

/// <summary>Monthly collection of all four cargo series for 32 working station groups.</summary>
[MemoryDiagnoser]
public class ReportingHistoryBenchmarks
{
    private readonly GameDate _boundary = new(1950, 2, 1, new DateOnly(1950, 2, 1).DayNumber);
    private StationRegistry _stations = null!;

    [GlobalSetup]
    public void Setup()
    {
        var map = new MapSettings(512, 512, Seed: 9);
        var game = GameSimulation.Create(new ScenarioSettings(Seed: 9, MapSizeTiles: 512, VehicleCount: 0, Map: map));
        game.Construction!.Player.Refund(100_000_000);
        foreach (var command in StationSeeder.Plan(game, 32))
        {
            game.Submit(command);
        }

        game.ProcessPendingCommands();
        _stations = game.Stations!;
        if (_stations.Count != 32)
        {
            throw new InvalidOperationException($"Expected 32 stations, found {_stations.Count}.");
        }
    }

    [Benchmark]
    public void CollectStationMonth() => _stations.AdvanceMonth(_boundary);
}
