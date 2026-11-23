using BenchmarkDotNet.Attributes;
using Talt.Core.Maps;
using Talt.Core.Meshing;
using Talt.Core.Network;
using Talt.Core.Terrain;
using Talt.Core.Voxels;

namespace Talt.Benchmarks;

/// <summary>
/// Meshing cost per chunk (feature 003, research R10): the reference naive mesher against the greedy
/// mesher, and simplified region meshes. The world is the prototype's terrain with a road grid
/// (16-tile spacing) on a 256 × 256 map; every chunk of the first region is meshed per operation.
/// </summary>
[MemoryDiagnoser]
public class ChunkMeshingBenchmarks
{
    private const int ChunksPerOperation = 16; // One region of the single-layer map.

    private VoxelWorld _world = null!;
    private Chunk[] _chunks = null!;

    [Params(1, 2, 3)]
    public int Lod { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _world = WorldBuilder.Build(MapGenerator.Generate(new MapSettings(256, 256, Seed: 2026)));
        var network = GridNetworkGenerator.Generate(16, 16, spacingTiles: 16, seed: 2026, originTileX: 8, originTileZ: 8);
        NetworkTerrainPainter.PaintRoads(_world, network);
        _chunks = [.. new RegionCoord(0, 0, 0).Chunks().Select(_world.GetChunk).OfType<Chunk>()];
    }

    /// <summary>Reference: one quad per face, 36-byte vertices and 32-bit indices (feature 001 renderer).</summary>
    [Benchmark(Baseline = true, OperationsPerInvoke = ChunksPerOperation)]
    public int NaiveReferencePerChunk()
    {
        var faces = 0;
        foreach (var chunk in _chunks)
        {
            faces += ChunkMesher.Build(_world, chunk).FaceCount;
        }

        return faces;
    }

    /// <summary>Greedy mesher with packed 8-byte vertices (what the game draws at full detail).</summary>
    [Benchmark(OperationsPerInvoke = ChunksPerOperation)]
    public int GreedyPerChunk()
    {
        var quads = 0;
        foreach (var chunk in _chunks)
        {
            quads += GreedyMesher.BuildChunk(_world, chunk.Coord).QuadCount;
        }

        return quads;
    }

    /// <summary>One simplified region mesh (16 chunks), reported per chunk for comparison.</summary>
    [Benchmark(OperationsPerInvoke = ChunksPerOperation)]
    public int RegionLodPerChunk() => GreedyMesher.BuildRegionLod(_world, new RegionCoord(0, 0, 0), Lod).QuadCount;
}
