using Talt.Core.Maps;

namespace Talt.Client.Audio;

/// <summary>
/// Coarse environment grid for one game (data-model.md AmbientMap). Built once from
/// <see cref="MapData"/>; sampling the camera target is O(1) and allocation-free.
/// </summary>
internal sealed class AmbientMap
{
    public const int MaximumCells = 128;

    private readonly byte[] _cells;
    private readonly float _tilesPerCellX;
    private readonly float _tilesPerCellZ;

    public AmbientMap(MapData map)
    {
        ArgumentNullException.ThrowIfNull(map);
        Width = Math.Min(MaximumCells, map.Width);
        Depth = Math.Min(MaximumCells, map.Depth);
        _tilesPerCellX = map.Width / (float)Width;
        _tilesPerCellZ = map.Depth / (float)Depth;
        _cells = new byte[Width * Depth];

        var towns = new List<Site>();
        foreach (var site in map.TownSites)
        {
            towns.Add(site);
        }

        for (var cellZ = 0; cellZ < Depth; cellZ++)
        {
            var z0 = (int)(cellZ * _tilesPerCellZ);
            var z1 = Math.Max(z0 + 1, (int)((cellZ + 1) * _tilesPerCellZ));
            for (var cellX = 0; cellX < Width; cellX++)
            {
                var x0 = (int)(cellX * _tilesPerCellX);
                var x1 = Math.Max(x0 + 1, (int)((cellX + 1) * _tilesPerCellX));
                var water = 0;
                var samples = 0;
                var stepZ = Math.Max(1, (z1 - z0) / 4);
                var stepX = Math.Max(1, (x1 - x0) / 4);
                for (var z = z0; z < z1; z += stepZ)
                {
                    for (var x = x0; x < x1; x += stepX)
                    {
                        samples++;
                        if (map.WaterLevelAt(Math.Min(x, map.Width - 1), Math.Min(z, map.Depth - 1)) > 0)
                        {
                            water++;
                        }
                    }
                }

                var environment = water * 100 >= samples * 35
                    ? AudioEnvironment.Water
                    : IsTown(towns, (x0 + x1) / 2, (z0 + z1) / 2)
                        ? AudioEnvironment.Town
                        : AudioEnvironment.Country;
                _cells[cellX + cellZ * Width] = (byte)environment;
            }
        }
    }

    public int Width { get; }

    public int Depth { get; }

    /// <summary>Average tile size of a cell, for tests and diagnostics.</summary>
    public float TilesPerCell => (_tilesPerCellX + _tilesPerCellZ) * 0.5f;

    /// <summary>Environment of the cell containing a tile position; positions outside use the edge.</summary>
    public AudioEnvironment EnvironmentAt(float x, float z)
    {
        var cellX = Math.Clamp((int)(x / _tilesPerCellX), 0, Width - 1);
        var cellZ = Math.Clamp((int)(z / _tilesPerCellZ), 0, Depth - 1);
        return (AudioEnvironment)_cells[cellX + cellZ * Width];
    }

    private static bool IsTown(List<Site> towns, int x, int z)
    {
        for (var i = 0; i < towns.Count; i++)
        {
            var site = towns[i];
            var radius = site.FreeRadius + 6;
            if (Math.Abs(x - site.X) <= radius && Math.Abs(z - site.Z) <= radius)
            {
                return true;
            }
        }

        return false;
    }
}
