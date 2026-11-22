using System.Numerics;
using Talt.Core.Voxels;

namespace Talt.Core.Meshing;

/// <summary>
/// Base colors (linear RGB) for each voxel material.
/// </summary>
public static class MaterialPalette
{
    public static Vector3 BaseColor(VoxelMaterial material) => material switch
    {
        VoxelMaterial.Grass => Srgb(0x6a, 0xb0, 0x4c),
        VoxelMaterial.Dirt => Srgb(0x8a, 0x62, 0x3f),
        VoxelMaterial.Stone => Srgb(0x8d, 0x8f, 0x94),
        VoxelMaterial.Sand => Srgb(0xe0, 0xcf, 0x8c),
        VoxelMaterial.Water => Srgb(0x3d, 0x8e, 0xc9),
        VoxelMaterial.Snow => Srgb(0xf2, 0xf5, 0xf8),
        VoxelMaterial.Road => Srgb(0x5a, 0x5c, 0x62),
        _ => Vector3.One,
    };

    private static Vector3 Srgb(byte r, byte g, byte b) => new(ToLinear(r), ToLinear(g), ToLinear(b));

    private static float ToLinear(byte channel)
    {
        var c = channel / 255f;
        return c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
    }
}
