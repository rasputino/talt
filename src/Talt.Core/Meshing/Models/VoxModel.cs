namespace Talt.Core.Meshing.Models;

/// <summary>
/// A small voxel model (feature 018, data-model.md VoxModel): a dense grid of palette indices in game
/// axes (Y up) and a 256-entry RGBA palette. Index 0 is empty; indices 1–255 are colours.
/// </summary>
public sealed class VoxModel
{
    /// <summary>Largest size of a model along any axis (the file format's limit).</summary>
    public const int MaxSize = 256;

    private readonly byte[] _voxels;
    private readonly uint[] _palette;

    /// <summary>Creates an empty model; every voxel is 0 and the palette is black.</summary>
    public VoxModel(int sizeX, int sizeY, int sizeZ)
    {
        if (sizeX is < 1 or > MaxSize || sizeY is < 1 or > MaxSize || sizeZ is < 1 or > MaxSize)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeX), $"Model sizes must be 1–{MaxSize} (got {sizeX} × {sizeY} × {sizeZ}).");
        }

        SizeX = sizeX;
        SizeY = sizeY;
        SizeZ = sizeZ;
        _voxels = new byte[sizeX * sizeY * sizeZ];
        _palette = new uint[256];
    }

    public int SizeX { get; }

    public int SizeY { get; }

    public int SizeZ { get; }

    /// <summary>Palette index per cell, <c>x + SizeX * (y + SizeY * z)</c>; 0 is empty.</summary>
    public ReadOnlySpan<byte> Voxels => _voxels;

    /// <summary>RGBA per palette index (R in the low byte, A in the high byte); entry 0 is unused.</summary>
    public Span<uint> Palette => _palette;

    /// <summary>Number of filled voxels.</summary>
    public int Count
    {
        get
        {
            var count = 0;
            foreach (var v in _voxels)
            {
                count += v != 0 ? 1 : 0;
            }

            return count;
        }
    }

    public bool Contains(int x, int y, int z) => (uint)x < (uint)SizeX && (uint)y < (uint)SizeY && (uint)z < (uint)SizeZ;

    /// <summary>Palette index at a cell, or 0 outside the model.</summary>
    public byte Get(int x, int y, int z) => Contains(x, y, z) ? _voxels[Index(x, y, z)] : (byte)0;

    public void Set(int x, int y, int z, byte color)
    {
        if (!Contains(x, y, z))
        {
            throw new ArgumentOutOfRangeException(nameof(x), $"({x}, {y}, {z}) is outside the {SizeX} × {SizeY} × {SizeZ} model.");
        }

        _voxels[Index(x, y, z)] = color;
    }

    /// <summary>Packs a colour as stored in <see cref="Palette"/>.</summary>
    public static uint Rgba(byte r, byte g, byte b, byte a = 255) => r | ((uint)g << 8) | ((uint)b << 16) | ((uint)a << 24);

    /// <summary>Whether two models have the same sizes, voxels and palette.</summary>
    public bool ContentEquals(VoxModel other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return SizeX == other.SizeX && SizeY == other.SizeY && SizeZ == other.SizeZ
            && _voxels.AsSpan().SequenceEqual(other._voxels) && _palette.AsSpan(1).SequenceEqual(other._palette.AsSpan(1));
    }

    private int Index(int x, int y, int z) => x + (SizeX * (y + (SizeY * z)));
}
