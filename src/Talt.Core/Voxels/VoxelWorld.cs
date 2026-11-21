namespace Talt.Core.Voxels;

/// <summary>
/// Sparse collection of chunks. Missing chunks are treated as air.
/// </summary>
public sealed class VoxelWorld
{
    private readonly Dictionary<ChunkCoord, Chunk> _chunks = [];

    public IEnumerable<Chunk> Chunks => _chunks.Values;

    public int ChunkCount => _chunks.Count;

    public Chunk? GetChunk(ChunkCoord coord) => _chunks.GetValueOrDefault(coord);

    public Chunk GetOrCreateChunk(ChunkCoord coord)
    {
        if (!_chunks.TryGetValue(coord, out var chunk))
        {
            chunk = new Chunk(coord);
            _chunks.Add(coord, chunk);
        }

        return chunk;
    }

    /// <summary>Gets the voxel at a world position. Positions in missing chunks are air.</summary>
    public VoxelMaterial GetVoxel(int x, int y, int z)
    {
        var chunk = GetChunk(ChunkCoord.FromVoxel(x, y, z));
        return chunk?.Get(x & Chunk.Mask, y & Chunk.Mask, z & Chunk.Mask) ?? VoxelMaterial.Air;
    }

    /// <summary>Sets the voxel at a world position, creating the chunk if needed.</summary>
    public void SetVoxel(int x, int y, int z, VoxelMaterial material)
    {
        var chunk = GetOrCreateChunk(ChunkCoord.FromVoxel(x, y, z));
        chunk.Set(x & Chunk.Mask, y & Chunk.Mask, z & Chunk.Mask, material);
    }
}
