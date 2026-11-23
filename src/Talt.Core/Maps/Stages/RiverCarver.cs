namespace Talt.Core.Maps.Stages;

/// <summary>
/// Stage 4: rivers (research R5). Tiles that collect enough water from upstream become river tiles;
/// because every land tile drains towards the sea or a lake, a river can only end in water. Each
/// river tile gets a water surface that never rises downstream and never floats above a lower bank,
/// and its bed is carved one voxel below the water.
/// </summary>
internal static class RiverCarver
{
    private const TileTraits WaterFlags = TileTraits.Sea | TileTraits.Lake | TileTraits.River;

    public static void Run(MapBuilder b, Drainage drainage)
    {
        var parent = drainage.Parent;
        var order = drainage.Order;
        var count = drainage.OrderCount;
        var catchment = drainage.Scratch;
        var floor = new byte[b.TileCount];
        var flags = b.Flags;
        var height = b.Height;
        var water = b.WaterLevel;
        var width = b.Width;
        var depth = b.Depth;

        for (var k = 0; k < count; k++)
        {
            catchment[order[k]] = 1;
        }

        for (var k = count - 1; k >= 0; k--)
        {
            var c = order[k];
            var p = parent[c];
            if (p >= 0)
            {
                catchment[p] += catchment[c];
            }
        }

        var threshold = Threshold(b);
        var rivers = new List<int>();
        for (var k = 0; k < count; k++)
        {
            var c = order[k];
            if (catchment[c] >= threshold && (flags[c] & (byte)TileTraits.Lake) == 0)
            {
                flags[c] |= (byte)TileTraits.River;
                rivers.Add(c);
            }
        }

        // Downstream first: the water level a river must keep at least, set by the water body it ends in.
        foreach (var c in rivers)
        {
            var p = parent[c];
            if ((flags[p] & (byte)TileTraits.River) != 0)
            {
                floor[c] = floor[p];
            }
            else
            {
                floor[c] = water[p];
            }

            water[c] = height[c];
        }

        // Upstream first: lower the water to the lowest dry bank, never below the floor, and make sure
        // the tile downstream is not higher than this one.
        for (var r = rivers.Count - 1; r >= 0; r--)
        {
            var c = rivers[r];
            var level = (int)water[c];
            ForEachNeighbour(c, width, depth, n =>
            {
                if ((flags[n] & (byte)WaterFlags) == 0)
                {
                    level = Math.Min(level, height[n]);
                }
            });

            level = Math.Max(level, floor[c]);
            water[c] = (byte)level;
            var p = parent[c];
            if ((flags[p] & (byte)TileTraits.River) != 0)
            {
                water[p] = (byte)Math.Min(water[p], level);
            }
        }

        // Where a dry bank is still lower than the water (next to a lake outlet), raise the bank.
        foreach (var c in rivers)
        {
            var level = water[c];
            ForEachNeighbour(c, width, depth, n =>
            {
                if ((flags[n] & (byte)WaterFlags) == 0 && height[n] < level)
                {
                    height[n] = level;
                }
            });
        }

        var springs = 0;
        var hasUpstream = new bool[b.TileCount];
        foreach (var c in rivers)
        {
            var p = parent[c];
            if ((flags[p] & (byte)TileTraits.River) != 0)
            {
                hasUpstream[p] = true;
            }
        }

        foreach (var c in rivers)
        {
            height[c] = (byte)(water[c] - 1);
            if (!hasUpstream[c])
            {
                springs++;
            }
        }

        b.Statistics = b.Statistics with { RiverTiles = rivers.Count, Springs = springs };
    }

    /// <summary>Catchment (tiles) a tile needs to count as a river: grows with map size, shrinks with water amount.</summary>
    internal static int Threshold(MapBuilder b)
    {
        var area = (long)b.Width * b.Depth;
        var baseline = Math.Max(b.Tuning.RiverMinCatchment, (int)Math.Min(int.MaxValue / 4, area / b.Tuning.RiverAreaDivisor));
        return (int)Math.Min(int.MaxValue, (long)baseline * 25 / Math.Max(10, b.Settings.WaterAmount));
    }

    private static void ForEachNeighbour(int tile, int width, int depth, Action<int> action)
    {
        var x = tile % width;
        var z = tile / width;
        if (x > 0)
        {
            action(tile - 1);
        }

        if (x < width - 1)
        {
            action(tile + 1);
        }

        if (z > 0)
        {
            action(tile - width);
        }

        if (z < depth - 1)
        {
            action(tile + width);
        }
    }
}
