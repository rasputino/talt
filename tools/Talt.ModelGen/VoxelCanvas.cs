using Talt.Core.Meshing.Models;

namespace Talt.ModelGen;

/// <summary>
/// A model being drawn: a <see cref="VoxModel"/> (game axes, Y up, front towards +Z) with a palette that
/// grows as colours are used, plus small drawing helpers. Deterministic: the same calls give the same file.
/// </summary>
internal sealed class VoxelCanvas
{
    private readonly Dictionary<uint, byte> _colors = [];
    private byte _next = 1;

    public VoxelCanvas(int sizeX, int sizeY, int sizeZ)
    {
        Model = new VoxModel(sizeX, sizeY, sizeZ);
    }

    public VoxModel Model { get; }

    public int SizeX => Model.SizeX;

    public int SizeY => Model.SizeY;

    public int SizeZ => Model.SizeZ;

    /// <summary>Palette index of an RGB colour (0xRRGGBB), added on first use.</summary>
    public byte Color(int rgb)
    {
        var rgba = VoxModel.Rgba((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        if (_colors.TryGetValue(rgba, out var index))
        {
            return index;
        }

        if (_next == 0)
        {
            throw new InvalidOperationException("A model may use at most 255 colours.");
        }

        index = _next++;
        _colors[rgba] = index;
        Model.Palette[index] = rgba;
        return index;
    }

    /// <summary>Reserves a palette index for a colour role (paint or glass) so it is never shared.</summary>
    public byte Reserve(int rgb)
    {
        var index = _next++;
        Model.Palette[index] = VoxModel.Rgba((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return index;
    }

    public void Set(int x, int y, int z, byte color)
    {
        if (Model.Contains(x, y, z))
        {
            Model.Set(x, y, z, color);
        }
    }

    public byte Get(int x, int y, int z) => Model.Get(x, y, z);

    /// <summary>Fills the box [x0, x1) × [y0, y1) × [z0, z1) (clipped to the model).</summary>
    public void Box(int x0, int y0, int z0, int x1, int y1, int z1, byte color)
    {
        for (var z = Math.Max(0, z0); z < Math.Min(SizeZ, z1); z++)
        {
            for (var y = Math.Max(0, y0); y < Math.Min(SizeY, y1); y++)
            {
                for (var x = Math.Max(0, x0); x < Math.Min(SizeX, x1); x++)
                {
                    Model.Set(x, y, z, color);
                }
            }
        }
    }

    /// <summary>Clears the box [x0, x1) × [y0, y1) × [z0, z1).</summary>
    public void Clear(int x0, int y0, int z0, int x1, int y1, int z1) => Box(x0, y0, z0, x1, y1, z1, 0);

    /// <summary>Replaces filled voxels of the box with <paramref name="color"/> (paints a surface).</summary>
    public void Paint(int x0, int y0, int z0, int x1, int y1, int z1, byte color)
    {
        for (var z = Math.Max(0, z0); z < Math.Min(SizeZ, z1); z++)
        {
            for (var y = Math.Max(0, y0); y < Math.Min(SizeY, y1); y++)
            {
                for (var x = Math.Max(0, x0); x < Math.Min(SizeX, x1); x++)
                {
                    if (Model.Get(x, y, z) != 0)
                    {
                        Model.Set(x, y, z, color);
                    }
                }
            }
        }
    }

    /// <summary>A filled ellipsoid with centre (cx, cy, cz) and radii; <paramref name="pick"/> chooses each voxel's colour (0 skips it).</summary>
    public void Ellipsoid(float cx, float cy, float cz, float rx, float ry, float rz, Func<int, int, int, byte> pick)
    {
        for (var z = (int)MathF.Floor(cz - rz); z <= (int)MathF.Ceiling(cz + rz); z++)
        {
            for (var y = (int)MathF.Floor(cy - ry); y <= (int)MathF.Ceiling(cy + ry); y++)
            {
                for (var x = (int)MathF.Floor(cx - rx); x <= (int)MathF.Ceiling(cx + rx); x++)
                {
                    var dx = (x + 0.5f - cx) / rx;
                    var dy = (y + 0.5f - cy) / ry;
                    var dz = (z + 0.5f - cz) / rz;
                    if ((dx * dx) + (dy * dy) + (dz * dz) <= 1f)
                    {
                        var color = pick(x, y, z);
                        if (color != 0)
                        {
                            Set(x, y, z, color);
                        }
                    }
                }
            }
        }
    }

    /// <summary>A vertical cylinder with its base centre at (cx, y0, cz).</summary>
    public void Cylinder(float cx, int y0, float cz, float radius, int height, Func<int, int, int, byte> pick)
    {
        for (var y = y0; y < y0 + height; y++)
        {
            for (var z = (int)MathF.Floor(cz - radius); z <= (int)MathF.Ceiling(cz + radius); z++)
            {
                for (var x = (int)MathF.Floor(cx - radius); x <= (int)MathF.Ceiling(cx + radius); x++)
                {
                    var dx = x + 0.5f - cx;
                    var dz = z + 0.5f - cz;
                    if ((dx * dx) + (dz * dz) <= radius * radius)
                    {
                        var color = pick(x, y, z);
                        if (color != 0)
                        {
                            Set(x, y, z, color);
                        }
                    }
                }
            }
        }
    }

    /// <summary>Deterministic hash of a voxel and a seed, 0–1.</summary>
    public static float Noise(int x, int y, int z, int seed)
    {
        var h = unchecked((uint)seed * 0x9E3779B1u) ^ ((uint)x * 0x27D4EB2Du) ^ ((uint)y * 0x165667B1u) ^ ((uint)z * 0x85EBCA6Bu);
        h ^= h >> 15;
        h *= 0x2C1B3C6Du;
        h ^= h >> 12;
        h *= 0x297A2D39u;
        h ^= h >> 15;
        return (h & 0xFFFFFF) / (float)0x1000000;
    }

    /// <summary>Multiplies an RGB colour's channels by <paramref name="factor"/>.</summary>
    public static int Shade(int rgb, float factor)
    {
        static int Channel(int c, float f) => Math.Clamp((int)MathF.Round(c * f), 0, 255);
        return (Channel((rgb >> 16) & 0xFF, factor) << 16) | (Channel((rgb >> 8) & 0xFF, factor) << 8) | Channel(rgb & 0xFF, factor);
    }

    /// <summary>Mixes two RGB colours.</summary>
    public static int Mix(int a, int b, float t)
    {
        static int Channel(int x, int y, float t) => Math.Clamp((int)MathF.Round(x + ((y - x) * t)), 0, 255);
        return (Channel((a >> 16) & 0xFF, (b >> 16) & 0xFF, t) << 16) | (Channel((a >> 8) & 0xFF, (b >> 8) & 0xFF, t) << 8) | Channel(a & 0xFF, b & 0xFF, t);
    }
}
