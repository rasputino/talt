using BenchmarkDotNet.Attributes;
using Talt.Core.Maps;
using Talt.Core.Voxels;

namespace Talt.Benchmarks;

/// <summary>
/// Map generation time per map size (feature 004): the generator on its own and building the voxel
/// world from its result. Budget: a 4096 x 4096 map in under 20 seconds in total.
/// </summary>
[SimpleJob(warmupCount: 1, iterationCount: 3)]
[MemoryDiagnoser]
public class MapGenerationBenchmarks
{
    private MapSettings _settings = null!;
    private MapData _map = null!;

    [Params(256, 1024, 2048, 4096)]
    public int Size { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _settings = new MapSettings(Size, Size, Seed: 2026);
        _map = MapGenerator.Generate(_settings);
    }

    [Benchmark(Baseline = true)]
    public MapData Generate() => MapGenerator.Generate(_settings);

    [Benchmark]
    public VoxelWorld BuildWorld() => WorldBuilder.Build(_map);
}
