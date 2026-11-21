namespace Talt.Core.Voxels;

/// <summary>
/// Position of a chunk in chunk units. Y is the vertical axis.
/// </summary>
public readonly record struct ChunkCoord(int X, int Y, int Z)
{
    /// <summary>World position (in voxels) of the chunk's minimum corner.</summary>
    public (int X, int Y, int Z) Origin => (X * Chunk.Size, Y * Chunk.Size, Z * Chunk.Size);

    /// <summary>Chunk that contains the given world voxel position.</summary>
    public static ChunkCoord FromVoxel(int x, int y, int z) =>
        new(FloorDiv(x), FloorDiv(y), FloorDiv(z));

    private static int FloorDiv(int value) => value >> Chunk.SizeBits;
}
