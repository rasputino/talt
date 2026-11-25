using BenchmarkDotNet.Attributes;
using Talt.Core.Construction;
using Talt.Core.Finance;
using Talt.Core.Maps;
using Talt.Core.Scenarios;
using Talt.Core.Simulation;
using Talt.Core.Stations;

namespace Talt.Benchmarks;

/// <summary>
/// Cost of the finance system at the reference scale (feature 011, SC-003): a 4096 × 4096 map with 5 000 seeded
/// stations (bays beside industries and town roads, with their roads), a loan and 50 000 registered running costs.
/// <see cref="DailyUpdate"/> is a day's running costs; <see cref="MonthlyUpdate"/> a month end (maintenance counted
/// from the stations and the incremental road counts, interest, company value, bankruptcy, ledger rotation);
/// <see cref="PayDelivery"/> prices and books one delivery reported by the stations.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 3, iterationCount: 10)]
public class FinanceBenchmarks
{
    private const int StationCount = 5_000;
    private static readonly MapSettings MapSettings = new(4096, 4096, Seed: 9);

    private FinanceSystem _finance = null!;
    private GameDate _day;
    private CargoDelivery _delivery;

    [GlobalSetup]
    public void Setup()
    {
        var simulation = GameSimulation.Create(
            new ScenarioSettings(Seed: 9, MapSizeTiles: 4096, VehicleCount: 0, Map: MapSettings, Stations: new StationSettings(50),
                Finance: new FinanceSettings(Inflation: true, DebugServicePays: true)),
            MapGenerator.Generate(MapSettings));
        simulation.Construction!.Player.Refund(1_000_000_000_000);
        foreach (var command in StationSeeder.Plan(simulation, StationCount))
        {
            simulation.Submit(command);
        }

        simulation.Submit(new ChangeLoanCommand(Company.PlayerId, LoanMode.BorrowMax));
        simulation.ProcessPendingCommands();
        _finance = simulation.Finance!;
        for (var i = 0; i < 50_000; i++)
        {
            _finance.RunningCosts.Register(Company.PlayerId, 2_000 + (i % 7_000), 15_000);
        }

        var stations = simulation.Stations!;
        _day = new GameDate(1950, 3, 2, new DateOnly(1950, 3, 2).DayNumber);
        _delivery = new CargoDelivery(Company.PlayerId, simulation.Content.CargoIndex("coal"), 25, stations.IdAt(0), stations.IdAt(stations.Count - 1),
            -1, _day.DayNumber - 6, _day.DayNumber, DeliveryConsumer.Industry, 0);
        Console.WriteLine($"// {stations.Count} stations, {simulation.Construction.Roads.RoadTileCount} road tiles, maintenance {_finance.YearlyMaintenance(Company.PlayerId)} a year");
    }

    /// <summary>One day: running costs of 50 000 registered vehicles (one per-company total).</summary>
    [Benchmark]
    public void DailyUpdate()
    {
        _finance.AdvanceDay(_day);
        _finance.Events.Clear();
    }

    /// <summary>One month end with 5 000 stations.</summary>
    [Benchmark]
    public void MonthlyUpdate()
    {
        _finance.AdvanceMonth(_day);
        _finance.Events.Clear();
    }

    /// <summary>Pays one delivery (distance, time factor, ledger, money event).</summary>
    [Benchmark]
    public long PayDelivery()
    {
        var paid = _finance.Pay(_delivery);
        _finance.Events.Clear();
        return paid;
    }
}
