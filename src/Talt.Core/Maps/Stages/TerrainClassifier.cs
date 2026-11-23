namespace Talt.Core.Maps.Stages;

/// <summary>
/// Stage 5: surface classification (research R6): water, beach, rock, snow and grass, the cliff and
/// coast flags, and which tiles can be built on. Runs in parallel over bands of rows.
/// </summary>
internal static class TerrainClassifier
{
    private const TileTraits WaterFlags = TileTraits.Sea | TileTraits.Lake | TileTraits.River;
    private const float PatchScale = 30f;

    public static void Run(MapBuilder b)
    {
        var width = b.Width;
        var depth = b.Depth;
        var height = b.Height;
        var flags = b.Flags;

        // Pass 1: cliffs (a land tile next to land more than one voxel away) and water columns.
        b.ForBands((_, z0, z1) =>
        {
            for (var z = z0; z < z1; z++)
            {
                for (var x = 0; x < width; x++)
                {
                    var i = x + z * width;
                    if ((flags[i] & (byte)WaterFlags) != 0)
                    {
                        b.Terrain[i] = (byte)TerrainType.Water;
                        continue;
                    }

                    var h = height[i];
                    if (IsSteep(flags, height, h, x > 0, i - 1) || IsSteep(flags, height, h, x < width - 1, i + 1)
                        || IsSteep(flags, height, h, z > 0, i - width) || IsSteep(flags, height, h, z < depth - 1, i + width))
                    {
                        flags[i] |= (byte)TileTraits.Cliff;
                    }
                }
            }
        });

        // Pass 2: is there sea or lake within the beach width, along each row?
        var beach = b.Tuning.BeachWidth;
        var nearRow = new bool[b.TileCount];
        b.ForBands((_, z0, z1) =>
        {
            for (var z = z0; z < z1; z++)
            {
                for (var x = 0; x < width; x++)
                {
                    var found = false;
                    for (var dx = -beach; dx <= beach && !found; dx++)
                    {
                        var nx = x + dx;
                        found = (uint)nx < (uint)width && (flags[nx + z * width] & (byte)(TileTraits.Sea | TileTraits.Lake)) != 0;
                    }

                    nearRow[x + z * width] = found;
                }
            }
        });

        // Pass 3: combine rows, then pick the terrain type of every land tile.
        var rock = b.Noise(7);
        var wobble = b.Noise(8);
        var sea = b.SeaLevel;
        var range = b.Settings.MaxHeight - sea;
        var rockLine = sea + range * b.Tuning.RockLinePercent / 100;
        var snowLine = Math.Max(sea + b.Tuning.MinSnowAboveSea, sea + range * b.Tuning.SnowLinePercent / 100);

        b.ForBands((_, z0, z1) =>
        {
            for (var z = z0; z < z1; z++)
            {
                for (var x = 0; x < width; x++)
                {
                    var i = x + z * width;
                    if ((flags[i] & (byte)WaterFlags) != 0)
                    {
                        continue;
                    }

                    var coast = false;
                    for (var dz = -beach; dz <= beach && !coast; dz++)
                    {
                        var nz = z + dz;
                        coast = (uint)nz < (uint)depth && nearRow[x + nz * width];
                    }

                    var h = height[i];
                    var cliff = (flags[i] & (byte)TileTraits.Cliff) != 0;
                    var snowHere = snowLine + (int)((wobble.Fractal(x / PatchScale, z / PatchScale, 2) - 0.5f) * 4f);
                    TerrainType type;
                    if (h >= snowHere)
                    {
                        type = TerrainType.Snow;
                    }
                    else if (cliff || (h >= rockLine && rock.Fractal(x / PatchScale, z / PatchScale, 2) > 0.62f))
                    {
                        type = TerrainType.Rock;
                    }
                    else if (coast && h <= sea + 1)
                    {
                        type = TerrainType.Beach;
                    }
                    else
                    {
                        type = TerrainType.Grass;
                    }

                    b.Terrain[i] = (byte)type;
                    if (coast)
                    {
                        flags[i] |= (byte)TileTraits.Coast;
                    }

                    if (!cliff && type != TerrainType.Rock)
                    {
                        flags[i] |= (byte)TileTraits.Buildable;
                    }
                }
            }
        });

        CountWater(b);
    }

    private static bool IsSteep(byte[] flags, byte[] height, int h, bool inside, int neighbour)
    {
        return inside && (flags[neighbour] & (byte)WaterFlags) == 0 && Math.Abs(height[neighbour] - h) > 1;
    }

    private static void CountWater(MapBuilder b)
    {
        int sea = 0, lake = 0;
        foreach (var f in b.Flags)
        {
            if ((f & (byte)TileTraits.Sea) != 0)
            {
                sea++;
            }
            else if ((f & (byte)TileTraits.Lake) != 0)
            {
                lake++;
            }
        }

        b.Statistics = b.Statistics with { SeaTiles = sea, LakeTiles = lake };
    }
}
