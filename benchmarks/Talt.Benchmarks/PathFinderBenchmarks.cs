using BenchmarkDotNet.Attributes;
using Talt.Core.Network;

namespace Talt.Benchmarks;

/// <summary>
/// A* searches between random junctions of a 257 × 257 road grid (66 049 nodes, ~220 000 edges),
/// the network of a 4096 × 4096 tile map with a junction every 16 tiles.
/// </summary>
[MemoryDiagnoser]
public class PathFinderBenchmarks
{
    private const int Queries = 100;

    private TransportNetwork _network = null!;
    private PathFinder _pathFinder = null!;
    private (int Start, int Goal)[] _queries = null!;
    private readonly List<int> _path = [];

    [GlobalSetup]
    public void Setup()
    {
        _network = GridNetworkGenerator.Generate(257, 257, spacing: 16, seed: 1);
        _pathFinder = new PathFinder(_network);
        var random = new Random(42);
        _queries = [.. Enumerable.Range(0, Queries).Select(_ => (random.Next(_network.NodeCount), random.Next(_network.NodeCount)))];
    }

    /// <summary>100 random searches across the whole map (the worst case: very long routes).</summary>
    [Benchmark(OperationsPerInvoke = Queries)]
    public int RandomLongRoutes()
    {
        var found = 0;
        foreach (var (start, goal) in _queries)
        {
            if (_pathFinder.TryFindPath(start, goal, _path))
            {
                found++;
            }
        }

        return found;
    }
}
