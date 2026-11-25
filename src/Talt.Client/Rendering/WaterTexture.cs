using Talt.Core.Maps;
using Talt.Core.Voxels;

namespace Talt.Client.Rendering;

/// <summary>
/// Per-tile water facts for the water shader (feature 018, data-model.md WaterTexture): channel R is the
/// distance to the nearest land tile (0 on land, 0–4 tiles mapped to 0–1 on water), channel G the water
/// depth (0–8 voxels mapped to 0–1) and channel B the surface material of that nearest land tile (a
/// <see cref="VoxelMaterial"/> value), so the shallow edge of the water takes the colour of the shore;
/// channel A is 0 on rivers and 255 elsewhere (foam lines only on sea and lakes). One texel per tile up to 4096 tiles per side, one per 2 or 4 tiles
/// above. Distances use a two-pass chamfer transform (orthogonal step 1, diagonal √2), so a full 4096 map
/// builds in well under a second; edits rebuild a rectangle with a 4-tile margin.
/// </summary>
internal sealed class WaterTexture
{
    public const int MaxSide = 4096;
    public const float MaxDistance = 4f;
    public const float MaxDepth = 8f;

    /// <summary>RGBA8: distance, depth, land material, unused.</summary>
    public const int BytesPerTexel = 4;

    private readonly MapData _map;
    private readonly float[] _distance;
    private readonly byte[] _material;

    public WaterTexture(MapData map)
    {
        _map = map ?? throw new ArgumentNullException(nameof(map));
        Stride = 1;
        while (Math.Max(map.Width, map.Depth) / Stride > MaxSide)
        {
            Stride *= 2;
        }

        Width = (map.Width + Stride - 1) / Stride;
        Depth = (map.Depth + Stride - 1) / Stride;
        Texels = new byte[Width * Depth * BytesPerTexel];
        _distance = new float[Width * Depth];
        _material = new byte[Width * Depth];
        Rebuild(0, 0, Width, Depth);
    }

    /// <summary>Tiles per texel along each side.</summary>
    public int Stride { get; }

    public int Width { get; }

    public int Depth { get; }

    /// <summary>RGBA8 texels, row by row.</summary>
    public byte[] Texels { get; }

    /// <summary>Distance channel of a texel (0–1).</summary>
    public float DistanceAt(int x, int z) => Texels[(x + (z * Width)) * BytesPerTexel] / 255f;

    /// <summary>Depth channel of a texel (0–1).</summary>
    public float DepthAt(int x, int z) => Texels[((x + (z * Width)) * BytesPerTexel) + 1] / 255f;

    /// <summary>Surface material of the land tile nearest to a texel (its own on land).</summary>
    public VoxelMaterial LandMaterialAt(int x, int z) => (VoxelMaterial)Texels[((x + (z * Width)) * BytesPerTexel) + 2];

    /// <summary>
    /// Rebuilds the texels covering tiles [x0, x1) × [z0, z1) (plus the margin distances depend on); returns
    /// the texel rectangle that changed.
    /// </summary>
    public (int X, int Z, int Width, int Depth) RebuildTiles(int x0, int z0, int x1, int z1)
    {
        var margin = (int)MaxDistance + 1;
        var tx0 = Math.Max(0, (x0 / Stride) - margin);
        var tz0 = Math.Max(0, (z0 / Stride) - margin);
        var tx1 = Math.Min(Width, ((x1 + Stride - 1) / Stride) + margin);
        var tz1 = Math.Min(Depth, ((z1 + Stride - 1) / Stride) + margin);
        Rebuild(tx0, tz0, tx1, tz1);
        return (tx0, tz0, tx1 - tx0, tz1 - tz0);
    }

    private void Rebuild(int x0, int z0, int x1, int z1)
    {
        // Seed: 0 on land; far on water. A margin outside the rectangle supplies the land around it.
        var margin = (int)MaxDistance + 1;
        int ex0 = Math.Max(0, x0 - margin), ez0 = Math.Max(0, z0 - margin);
        int ex1 = Math.Min(Width, x1 + margin), ez1 = Math.Min(Depth, z1 + margin);
        for (var z = ez0; z < ez1; z++)
        {
            for (var x = ex0; x < ex1; x++)
            {
                var i = x + (z * Width);
                _distance[i] = IsWater(x, z) ? MaxDistance : 0f;
                _material[i] = (byte)LandMaterial(x, z);
            }
        }

        const float Diagonal = 1.41421356f;
        for (var z = ez0; z < ez1; z++)
        {
            for (var x = ex0; x < ex1; x++)
            {
                var i = x + (z * Width);
                var d = _distance[i];
                if (d == 0f)
                {
                    continue;
                }

                if (x > ex0)
                {
                    d = Relax(i, i - 1, 1f, d);
                }

                if (z > ez0)
                {
                    d = Relax(i, i - Width, 1f, d);
                    if (x > ex0)
                    {
                        d = Relax(i, i - Width - 1, Diagonal, d);
                    }

                    if (x < ex1 - 1)
                    {
                        d = Relax(i, i - Width + 1, Diagonal, d);
                    }
                }

                _distance[i] = d;
            }
        }

        for (var z = ez1 - 1; z >= ez0; z--)
        {
            for (var x = ex1 - 1; x >= ex0; x--)
            {
                var i = x + (z * Width);
                var d = _distance[i];
                if (d == 0f)
                {
                    continue;
                }

                if (x < ex1 - 1)
                {
                    d = Relax(i, i + 1, 1f, d);
                }

                if (z < ez1 - 1)
                {
                    d = Relax(i, i + Width, 1f, d);
                    if (x < ex1 - 1)
                    {
                        d = Relax(i, i + Width + 1, Diagonal, d);
                    }

                    if (x > ex0)
                    {
                        d = Relax(i, i + Width - 1, Diagonal, d);
                    }
                }

                _distance[i] = d;
            }
        }

        for (var z = z0; z < z1; z++)
        {
            for (var x = x0; x < x1; x++)
            {
                var i = x + (z * Width);
                var tile = _map.Index(Math.Min(_map.Width - 1, x * Stride), Math.Min(_map.Depth - 1, z * Stride));
                var depth = Math.Max(0, _map.WaterLevels[tile] - _map.Heights[tile]);
                var t = i * BytesPerTexel;
                Texels[t] = (byte)MathF.Round(MathF.Min(_distance[i] * Stride, MaxDistance) / MaxDistance * 255f);
                Texels[t + 1] = (byte)MathF.Round(MathF.Min(depth, MaxDepth) / MaxDepth * 255f);
                Texels[t + 2] = _material[i];
                // A: 255 on sea and lakes, 0 on rivers (narrow rivers get no foam line, which would run down their middle).
                Texels[t + 3] = (byte)((_map.Flags[tile] & (byte)TileTraits.River) != 0 ? 0 : 255);
            }
        }
    }

    /// <summary>The distance through <paramref name="from"/> when it is shorter; then also takes its land material.</summary>
    private float Relax(int i, int from, float step, float current)
    {
        var candidate = _distance[from] + step;
        if (candidate < current)
        {
            _material[i] = _material[from];
            return candidate;
        }

        return current;
    }

    /// <summary>Surface material of a land texel's tile (what the terrain mesh shows on top).</summary>
    private VoxelMaterial LandMaterial(int x, int z)
    {
        var tx = Math.Min(_map.Width - 1, x * Stride);
        var tz = Math.Min(_map.Depth - 1, z * Stride);
        return _map.TerrainAt(tx, tz) switch
        {
            TerrainType.Beach => VoxelMaterial.Sand,
            TerrainType.Rock => VoxelMaterial.Stone,
            TerrainType.Snow => VoxelMaterial.Snow,
            TerrainType.Water => VoxelMaterial.Sand, // Water tiles never seed; only their neighbours' value counts.
            _ => VoxelMaterial.Grass,
        };
    }

    private bool IsWater(int x, int z)
    {
        var tx = Math.Min(_map.Width - 1, x * Stride);
        var tz = Math.Min(_map.Depth - 1, z * Stride);
        return _map.IsWater(_map.Index(tx, tz));
    }
}
