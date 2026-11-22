namespace Talt.Core.Voxels;

/// <summary>
/// Position of a region in region units. A region is a cube of <see cref="ChunksPerSide"/>³ chunks
/// (<see cref="Size"/>³ voxels): the unit the renderer draws, culls and simplifies at once.
/// </summary>
public readonly record struct RegionCoord(int X, int Y, int Z)
{
    public const int ChunksPerSideBits = 2;
    public const int ChunksPerSide = 1 << ChunksPerSideBits; // 4
    public const int Size = ChunksPerSide * Chunk.Size;       // 128 voxels

    /// <summary>World position (in voxels) of the region's minimum corner.</summary>
    public (int X, int Y, int Z) Origin => (X * Size, Y * Size, Z * Size);

    /// <summary>Region that contains the given chunk.</summary>
    public static RegionCoord FromChunk(ChunkCoord chunk) =>
        new(chunk.X >> ChunksPerSideBits, chunk.Y >> ChunksPerSideBits, chunk.Z >> ChunksPerSideBits);

    /// <summary>Region that contains the given world voxel position.</summary>
    public static RegionCoord FromVoxel(int x, int y, int z) => FromChunk(ChunkCoord.FromVoxel(x, y, z));

    /// <summary>Position of a chunk inside the region, 0 to <see cref="ChunksPerSide"/> − 1 per axis.</summary>
    public static (int X, int Y, int Z) LocalChunk(ChunkCoord chunk) =>
        (chunk.X & (ChunksPerSide - 1), chunk.Y & (ChunksPerSide - 1), chunk.Z & (ChunksPerSide - 1));

    /// <summary>The coordinates of every chunk inside the region (whether or not it exists).</summary>
    public IEnumerable<ChunkCoord> Chunks()
    {
        for (var y = 0; y < ChunksPerSide; y++)
        {
            for (var z = 0; z < ChunksPerSide; z++)
            {
                for (var x = 0; x < ChunksPerSide; x++)
                {
                    yield return new ChunkCoord(X * ChunksPerSide + x, Y * ChunksPerSide + y, Z * ChunksPerSide + z);
                }
            }
        }
    }
}
