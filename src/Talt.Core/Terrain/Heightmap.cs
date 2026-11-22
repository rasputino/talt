using Talt.Core.Voxels;

namespace Talt.Core.Terrain;

/// <summary>
/// Surface height of every column of the map, for fast lookups (camera, vehicles).
/// </summary>
public sealed class Heightmap
{
    private readonly int[] _heights;

    private Heightmap(int width, int depth, int[] heights)
    {
        Width = width;
        Depth = depth;
        _heights = heights;
    }

    public int Width { get; }

    public int Depth { get; }

    public static Heightmap FromWorld(VoxelWorld world, int width, int depth)
    {
        ArgumentNullException.ThrowIfNull(world);

        var heights = new int[width * depth];
        Parallel.For(0, depth, z =>
        {
            for (var x = 0; x < width; x++)
            {
                heights[x + z * width] = world.GetSurfaceHeight(x, z);
            }
        });

        return new Heightmap(width, depth, heights);
    }

    /// <summary>Surface height at a column. Positions outside the map use the nearest edge column.</summary>
    public int Get(int x, int z) =>
        _heights[Math.Clamp(x, 0, Width - 1) + Math.Clamp(z, 0, Depth - 1) * Width];

    /// <summary>Surface height at a ground position in tiles.</summary>
    public int Get(float x, float z) => Get((int)MathF.Floor(x), (int)MathF.Floor(z));
}
