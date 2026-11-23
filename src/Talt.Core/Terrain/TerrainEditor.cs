using Talt.Core.Voxels;

namespace Talt.Core.Terrain;

/// <summary>
/// Debug terrain edits that raise or lower a single column by one voxel. They exist to exercise the
/// renderer's update path until real terraforming arrives (feature 006). Terrain is not simulation
/// state, so these edits do not affect determinism.
/// </summary>
/// <remarks>
/// Edits stay inside existing chunks (heights 1 to the top of the column's chunk), so they are safe
/// while the renderer is meshing (see <see cref="VoxelWorld"/>).
/// </remarks>
public static class TerrainEditor
{
    /// <summary>
    /// Raises the column at (x, z) by one voxel: the surface material moves up and the voxel it leaves
    /// becomes dirt (stone and sand stay as they are). Returns the changed voxel positions (empty when
    /// the column is outside the map or already at the top).
    /// </summary>
    public static IReadOnlyList<(int X, int Y, int Z)> Raise(VoxelWorld world, Heightmap heightmap, int x, int z)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(heightmap);

        if (!IsInside(world, heightmap, x, z, out var height) || world.GetChunk(ChunkCoord.FromVoxel(x, height, z)) is null)
        {
            return [];
        }

        var top = world.GetVoxel(x, height - 1, z);
        world.SetVoxel(x, height, z, top);
        var below = top is VoxelMaterial.Stone or VoxelMaterial.Sand or VoxelMaterial.Water ? top : VoxelMaterial.Dirt;
        world.SetVoxel(x, height - 1, z, below);
        heightmap.Refresh(world, x, z);
        return [(x, height, z), (x, height - 1, z)];
    }

    /// <summary>
    /// Lowers the column at (x, z) by one voxel, keeping its surface material; never below height 1.
    /// Returns the changed voxel positions (empty when nothing changed).
    /// </summary>
    public static IReadOnlyList<(int X, int Y, int Z)> Lower(VoxelWorld world, Heightmap heightmap, int x, int z)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(heightmap);

        if (!IsInside(world, heightmap, x, z, out var height) || height <= 1)
        {
            return [];
        }

        var top = world.GetVoxel(x, height - 1, z);
        world.SetVoxel(x, height - 1, z, VoxelMaterial.Air);
        world.SetVoxel(x, height - 2, z, top);
        heightmap.Refresh(world, x, z);
        return [(x, height - 1, z), (x, height - 2, z)];
    }

    private static bool IsInside(VoxelWorld world, Heightmap heightmap, int x, int z, out int height)
    {
        height = 0;
        if ((uint)x >= heightmap.Width || (uint)z >= heightmap.Depth || world.GetChunk(ChunkCoord.FromVoxel(x, 0, z)) is null)
        {
            return false;
        }

        height = world.GetSurfaceHeight(x, z);
        return height > 0;
    }
}
