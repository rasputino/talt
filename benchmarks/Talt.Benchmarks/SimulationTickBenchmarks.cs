using BenchmarkDotNet.Attributes;
using Talt.Core.Scenarios;
using Talt.Core.Simulation;

namespace Talt.Benchmarks;

/// <summary>
/// Cost of one full <see cref="GameSimulation.Tick"/> (commands, vehicles, calendar events) on a
/// 4096 × 4096 map. The 50 000-vehicle case is the reference load of feature 001: it must stay well
/// below 2.5 ms so fast forward reaches at least ×20 within a frame (SC-003).
/// </summary>
[MemoryDiagnoser]
public class SimulationTickBenchmarks
{
    private GameSimulation _simulation = null!;

    [Params(10_000, 50_000, 100_000, 1_000_000)]
    public int Vehicles { get; set; }

    [Params(true, false)]
    public bool Parallel { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _simulation = GameSimulation.Create(new ScenarioSettings(
            Seed: 1, MapSizeTiles: 4096, RoadSpacing: 16, VehicleCount: Vehicles, LineCount: 2_000));

        // Let vehicles spread out along their lines before measuring.
        for (var i = 0; i < 2_000; i++)
        {
            _simulation.Tick();
        }
    }

    [Benchmark]
    public void Tick() => _simulation.Tick(Parallel);
}
