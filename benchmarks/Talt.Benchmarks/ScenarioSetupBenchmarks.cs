using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using Talt.Core.Network;
using Talt.Core.Scenarios;
using Talt.Core.Simulation;

namespace Talt.Benchmarks;

/// <summary>
/// Setup of the reference scenario (4096 × 4096 map, 256 × 256 junctions, 50 000 vehicles):
/// building the road grid and the 5 000 synthetic lines, whose 10 000 long A* routes dominate
/// <see cref="GameSimulation.Create"/>.
/// </summary>
/// <remarks>Each operation takes seconds, so the job runs few iterations, one call each.</remarks>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Monitoring, launchCount: 1, warmupCount: 1, iterationCount: 3)]
public class ScenarioSetupBenchmarks
{
    private static readonly ScenarioSettings Reference = new(Seed: 1, MapSizeTiles: 4096, VehicleCount: 50_000);

    private TransportNetwork _network = null!;

    [Params(true, false)]
    public bool Parallel { get; set; }

    [GlobalSetup]
    public void Setup() => _network = ScenarioBuilder.Build(Reference with { VehicleCount = 1, LineCount = 1 }).Network;

    /// <summary>Road grid only (no lines).</summary>
    [Benchmark]
    public TransportNetwork GenerateNetwork()
    {
        var junctions = Reference.MapSizeTiles / Reference.RoadSpacing;
        var origin = Reference.RoadSpacing / 2;
        return GridNetworkGenerator.Generate(
            junctions, junctions, Reference.RoadSpacing, Reference.Seed, Reference.RoadRemovalChancePerMille, origin, origin);
    }

    /// <summary>5 000 lines (10 000 A* routes) and 50 000 vehicles on the reference network.</summary>
    [Benchmark]
    public VehicleFleet CreateSyntheticTraffic() =>
        SyntheticTraffic.Create(_network, Reference.EffectiveLineCount, Reference.VehicleCount, Reference.Seed, Parallel);
}
