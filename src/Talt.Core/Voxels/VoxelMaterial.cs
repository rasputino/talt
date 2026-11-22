namespace Talt.Core.Voxels;

/// <summary>
/// Material of a single voxel. Stored as one byte per voxel.
/// </summary>
public enum VoxelMaterial : byte
{
    Air = 0,
    Grass,
    Dirt,
    Stone,
    Sand,
    Water,
    Snow,
    Road,
}

public static class VoxelMaterialExtensions
{
    /// <summary>Whether the voxel blocks vision, so faces next to it are hidden.</summary>
    public static bool IsSolid(this VoxelMaterial material) => material != VoxelMaterial.Air;
}
