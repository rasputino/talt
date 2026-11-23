using System.Collections.Concurrent;

namespace Talt.Core.Voxels;

/// <summary>
/// Sparse collection of chunks. Missing chunks are treated as air.
/// </summary>
/// <remarks>
/// Threading: the renderer's mesh workers read voxels while the main thread may change them. A mesh
/// built during a write may be stale and is rebuilt. The chunk dictionary is concurrent (feature 006,
/// research R6), so the main thread may also create chunks — for example when terraforming raises a
/// column into the chunk above — while workers read; only one thread may write at a time.
/// </remarks>
public sealed class VoxelWorld
{
    private readonly ConcurrentDictionary<ChunkCoord, Chunk> _chunks = new();
    private int _topChunkY = -1;

    /// <summary>All chunks, sorted by coordinate (Y, then Z, then X) so enumeration order is stable.</summary>
    public IEnumerable<Chunk> Chunks => _chunks.Values.OrderBy(c => c.Coord.Y).ThenBy(c => c.Coord.Z).ThenBy(c => c.Coord.X);

    public int ChunkCount => _chunks.Count;

    public Chunk? GetChunk(ChunkCoord coord) => _chunks.GetValueOrDefault(coord);

    public Chunk GetOrCreateChunk(ChunkCoord coord)
    {
        if (!_chunks.TryGetValue(coord, out var chunk))
        {
            chunk = new Chunk(coord);
            _chunks[coord] = chunk;
            _topChunkY = Math.Max(_topChunkY, coord.Y);
        }

        return chunk;
    }

    /// <summary>Gets the voxel at a world position. Positions in missing chunks are air.</summary>
    public VoxelMaterial GetVoxel(int x, int y, int z)
    {
        var chunk = GetChunk(ChunkCoord.FromVoxel(x, y, z));
        return chunk?.Get(x & Chunk.Mask, y & Chunk.Mask, z & Chunk.Mask) ?? VoxelMaterial.Air;
    }

    /// <summary>Highest vertical chunk index that exists (-1 for an empty world).</summary>
    public int TopChunkY => _topChunkY;

    /// <summary>
    /// Height of the first air voxel above the highest solid voxel in a column, searching down from
    /// <paramref name="maxY"/> (default: the top of the highest chunk). Returns 0 when the column is empty.
    /// </summary>
    public int GetSurfaceHeight(int x, int z, int maxY = int.MaxValue)
    {
        var start = Math.Min(maxY, ((_topChunkY + 1) * Chunk.Size) - 1);
        for (var y = start; y >= 0; y--)
        {
            if (GetVoxel(x, y, z).IsSolid())
            {
                return y + 1;
            }
        }

        return 0;
    }

    /// <summary>Sets the voxel at a world position, creating the chunk if needed.</summary>
    public void SetVoxel(int x, int y, int z, VoxelMaterial material)
    {
        var chunk = GetOrCreateChunk(ChunkCoord.FromVoxel(x, y, z));
        chunk.Set(x & Chunk.Mask, y & Chunk.Mask, z & Chunk.Mask, material);
    }
}
