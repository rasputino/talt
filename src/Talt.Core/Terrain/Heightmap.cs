using Talt.Core.Maps;
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

    /// <summary>Builds the heightmap straight from generated tile data (water surface where there is water).</summary>
    public static Heightmap FromMapData(MapData map)
    {
        ArgumentNullException.ThrowIfNull(map);

        var heights = new int[map.Width * map.Depth];
        var ground = map.Heights;
        var water = map.WaterLevels;
        for (var i = 0; i < heights.Length; i++)
        {
            heights[i] = Math.Max(ground[i], water[i]);
        }

        return new Heightmap(map.Width, map.Depth, heights);
    }

    /// <summary>Recomputes one column from the world after it changed. Columns outside the map are ignored.</summary>
    public void Refresh(VoxelWorld world, int x, int z)
    {
        ArgumentNullException.ThrowIfNull(world);
        if ((uint)x < Width && (uint)z < Depth)
        {
            _heights[x + z * Width] = world.GetSurfaceHeight(x, z);
        }
    }

    /// <summary>Surface height at a column. Positions outside the map use the nearest edge column.</summary>
    public int Get(int x, int z) =>
        _heights[Math.Clamp(x, 0, Width - 1) + Math.Clamp(z, 0, Depth - 1) * Width];

    /// <summary>Surface height at a ground position in tiles.</summary>
    public int Get(float x, float z) => Get((int)MathF.Floor(x), (int)MathF.Floor(z));
}
