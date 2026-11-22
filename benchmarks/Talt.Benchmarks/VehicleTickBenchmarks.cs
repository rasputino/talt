using BenchmarkDotNet.Attributes;
using Talt.Core.Network;
using Talt.Core.Simulation;

namespace Talt.Benchmarks;

/// <summary>
/// Cost of one simulation tick for many vehicles on a 4096 × 4096 tile map
/// (a 257 × 257 road grid with 16 tiles between junctions).
/// </summary>
[MemoryDiagnoser]
public class VehicleTickBenchmarks
{
    private VehicleFleet _fleet = null!;

    [Params(10_000, 100_000, 1_000_000)]
    public int Vehicles { get; set; }

    [Params(true, false)]
    public bool Parallel { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var network = GridNetworkGenerator.Generate(257, 257, spacing: 16, seed: 1);
        _fleet = SyntheticTraffic.Create(network, lineCount: 2_000, vehicleCount: Vehicles, seed: 1);

        // Let vehicles spread out along their lines before measuring.
        for (var i = 0; i < 2_000; i++)
        {
            _fleet.Tick(0.05f);
        }
    }

    [Benchmark]
    public void Tick() => _fleet.Tick(0.05f, Parallel);
}
