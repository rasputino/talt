using BenchmarkDotNet.Attributes;
using Talt.Core.Content;
using Talt.Core.Determinism;
using Talt.Core.Industries;
using Talt.Core.Maps;
using Talt.Core.Simulation;

namespace Talt.Benchmarks;

/// <summary>
/// Cost of the industry economy at the reference scale: a 4096 × 4096 map with about 1 000
/// industries (feature 009, SC-001/SC-002). The map is generated once; each iteration gets a fresh
/// fleet so caps and closures do not distort the measurement.
/// </summary>
[MemoryDiagnoser]
public class IndustryBenchmarks
{
    private GameContent _content = null!;
    private MapData _map = null!;
    private IndustryFleet _fleet = null!;
    private GameDate _day;
    private GameDate _month;

    [GlobalSetup]
    public void Setup()
    {
        _content = GameContent.Default;
        _map = MapGenerator.Generate(new MapSettings(4096, 4096, Seed: 9));
        _day = new GameDate(1950, 1, 2, new DateOnly(1950, 1, 2).DayNumber);
        _month = new GameDate(1950, 2, 1, new DateOnly(1950, 2, 1).DayNumber);
        _fleet = IndustryFleet.Create(_content, _map, 1950, new DeterministicRandom(9), initialOccupancyPerMille: 1000);
    }

    [IterationSetup(Target = nameof(AdvanceDay))]
    public void SetupDay() => _fleet = IndustryFleet.Create(_content, _map, 1950, new DeterministicRandom(9), initialOccupancyPerMille: 1000);

    [IterationSetup(Target = nameof(AdvanceMonth))]
    public void SetupMonth() => _fleet = IndustryFleet.Create(_content, _map, 1950, new DeterministicRandom(9), initialOccupancyPerMille: 1000);

    [Benchmark]
    public void AdvanceDay() => _fleet.AdvanceDay(_day);

    [Benchmark]
    public void AdvanceMonth() => _fleet.AdvanceMonth(_month);

    [Benchmark]
    public IndustryFleet PlaceOnMap() =>
        IndustryFleet.Create(_content, _map, 1950, new DeterministicRandom(9), initialOccupancyPerMille: 1000);
}
