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

    /// <summary>
    /// Every chunk whose mesh can change when the voxel at a world position changes: faces and
    /// ambient occlusion read voxels up to one voxel across a chunk border, so this is the voxel's
    /// chunk plus every chunk within one voxel of it (1, 2, 4 or 8 chunks).
    /// </summary>
    public static IReadOnlyList<ChunkCoord> TouchedBy(int x, int y, int z)
    {
        var result = new List<ChunkCoord>(8);
        for (var dy = -1; dy <= 1; dy++)
        {
            for (var dz = -1; dz <= 1; dz++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    var coord = FromVoxel(x + dx, y + dy, z + dz);
                    if (!result.Contains(coord))
                    {
                        result.Add(coord);
                    }
                }
            }
        }

        return result;
    }

    private static int FloorDiv(int value) => value >> Chunk.SizeBits;
}
