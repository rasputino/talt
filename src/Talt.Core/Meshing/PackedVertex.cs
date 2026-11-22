using System.Runtime.InteropServices;
using Talt.Core.Voxels;

namespace Talt.Core.Meshing;

/// <summary>
/// Compact 8-byte vertex produced by <see cref="GreedyMesher"/>. Layout matches the GPU vertex
/// buffer (<c>vec2&lt;u32&gt;</c> in <c>voxel.wgsl</c>).
/// </summary>
/// <remarks>
/// <para><see cref="Word0"/>: x (bits 0–7) | y (8–15) | z (16–23) | face direction (24–26). Positions
/// are in voxels relative to the region origin, 0 to <see cref="MaxCoordinate"/>.</para>
/// <para><see cref="Word1"/>: material (bits 0–7) | ambient occlusion level 0–3 (bits 8–9).</para>
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct PackedVertex(uint Word0, uint Word1)
{
    public const int SizeInBytes = 8;

    /// <summary>Largest coordinate a vertex can hold (the far edge of a region).</summary>
    public const int MaxCoordinate = RegionCoord.Size;

    public int X => (int)(Word0 & 0xff);

    public int Y => (int)((Word0 >> 8) & 0xff);

    public int Z => (int)((Word0 >> 16) & 0xff);

    /// <summary>Face direction index, see <see cref="FaceDirections"/>.</summary>
    public int FaceIndex => (int)((Word0 >> 24) & 0x7);

    public VoxelMaterial Material => (VoxelMaterial)(Word1 & 0xff);

    /// <summary>Ambient occlusion level, 0 (darkest) to 3 (unoccluded).</summary>
    public int AmbientOcclusion => (int)((Word1 >> 8) & 0x3);

    public static PackedVertex Create(int x, int y, int z, int faceIndex, VoxelMaterial material, int ambientOcclusion)
    {
        if ((uint)x > MaxCoordinate || (uint)y > MaxCoordinate || (uint)z > MaxCoordinate)
        {
            throw new ArgumentOutOfRangeException(nameof(x), $"Position ({x}, {y}, {z}) is outside a region.");
        }

        if ((uint)faceIndex >= FaceDirections.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(faceIndex));
        }

        if (material == VoxelMaterial.Air)
        {
            throw new ArgumentException("Air has no faces.", nameof(material));
        }

        if ((uint)ambientOcclusion > 3)
        {
            throw new ArgumentOutOfRangeException(nameof(ambientOcclusion));
        }

        return new PackedVertex(
            (uint)x | (uint)y << 8 | (uint)z << 16 | (uint)faceIndex << 24,
            (uint)material | (uint)ambientOcclusion << 8);
    }
}
