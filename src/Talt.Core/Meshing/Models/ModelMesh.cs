using System.Runtime.InteropServices;

namespace Talt.Core.Meshing.Models;

/// <summary>
/// One vertex of a model mesh (12 bytes; data-model.md ModelVertex, read by <c>models.wgsl</c>). Positions
/// are in half-voxel units relative to the model's pivot (centre of the base), so odd-sized models stay
/// centred. Four vertices per quad, drawn with the index pattern 0-1-2 / 0-2-3.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 2)]
public readonly record struct ModelVertex(short X2, short Y2, short Z2, ushort FaceAo, uint Color)
{
    public const int SizeInBytes = 12;

    /// <summary>Face index 0–5 in <see cref="FaceDirections"/> order.</summary>
    public int FaceIndex => FaceAo & 7;

    /// <summary>Ambient occlusion level 0 (darkest) to 3.</summary>
    public int AmbientOcclusion => (FaceAo >> 3) & 3;

    /// <summary>Palette index 1–255.</summary>
    public int PaletteIndex => (int)(Color & 0xff);

    /// <summary>Palette row of the model (set by the client when the model is uploaded).</summary>
    public int PaletteRow => (int)(Color >> 8);

    /// <summary>The same vertex using another palette row.</summary>
    public ModelVertex WithPaletteRow(int row) => this with { Color = (Color & 0xff) | ((uint)row << 8) };
}

/// <summary>
/// The meshes of one model at each detail level (0 = full detail, then voxels merged 2×, 4× and 8×), built by
/// <see cref="ModelMesher"/>. Every level is a list of quads (4 vertices each).
/// </summary>
public sealed class ModelMesh
{
    /// <summary>Number of detail levels.</summary>
    public const int LevelCount = 4;

    public ModelMesh(ModelVertex[][] levels, int sizeX, int sizeY, int sizeZ)
    {
        ArgumentNullException.ThrowIfNull(levels);
        if (levels.Length != LevelCount)
        {
            throw new ArgumentException($"A model mesh has {LevelCount} levels.", nameof(levels));
        }

        Levels = levels;
        SizeX = sizeX;
        SizeY = sizeY;
        SizeZ = sizeZ;
    }

    /// <summary>Vertices per level (4 per quad).</summary>
    public IReadOnlyList<ModelVertex[]> Levels { get; }

    /// <summary>Size of the source model in voxels (game axes).</summary>
    public int SizeX { get; }

    public int SizeY { get; }

    public int SizeZ { get; }

    /// <summary>Quads at a level.</summary>
    public int QuadCount(int level) => Levels[level].Length / 4;
}
