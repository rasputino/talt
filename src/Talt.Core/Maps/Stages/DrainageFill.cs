namespace Talt.Core.Maps.Stages;

/// <summary>
/// Where the water of every land tile goes: a forest of parent pointers rooted in the sea, plus the
/// order in which tiles were reached (parents always come before their children).
/// </summary>
internal sealed class Drainage
{
    public Drainage(int[] parent, int[] order, int orderCount, int[] scratch)
    {
        Parent = parent;
        Order = order;
        OrderCount = orderCount;
        Scratch = scratch;
    }

    /// <summary>Tile the water of each tile flows to; -1 for sea tiles.</summary>
    public int[] Parent { get; }

    /// <summary>Land tiles, downstream first.</summary>
    public int[] Order { get; }

    public int OrderCount { get; }

    /// <summary>Reusable integer array (one per tile) for later stages.</summary>
    public int[] Scratch { get; }
}

/// <summary>
/// Stage 3: lakes and drainage with the priority-flood algorithm (research R4). Flooding starts at
/// the sea and always continues with the lowest unvisited tile, so every land tile ends up with a
/// parent that is its lowest already-reached neighbour; following parents always leads to the sea.
/// Depressions are filled to their spill level: deep ones become lakes, shallow ones are levelled.
/// </summary>
internal static class DrainageFill
{
    /// <summary>Each voxel of height is split into this many sub-levels so flat ground still drains somewhere.</summary>
    private const int Sub = 16;

    private const int Unvisited = ushort.MaxValue;
    private const int Buckets = (128 * Sub) + Sub;

    public static Drainage Run(MapBuilder b)
    {
        var height = b.Height;
        var count = b.TileCount;
        var width = b.Width;
        var depth = b.Depth;
        var sea = (byte)b.SeaLevel;

        EnsureSea(b);

        var jitter = Jitter(b);

        // Level of a tile: height in sub-levels plus the smooth jitter, raised to the spill level of its basin.
        var level = new ushort[count];
        Array.Fill(level, (ushort)Unvisited);
        var parent = new int[count];
        var next = new int[count];
        var order = new int[count];
        var head = new int[Buckets];
        var tail = new int[Buckets];
        Array.Fill(head, -1);
        Array.Fill(tail, -1);

        // Every sea tile counts as reached; the ones touching land start the flood.
        for (var i = 0; i < count; i++)
        {
            if (height[i] < sea)
            {
                level[i] = (ushort)(sea * Sub);
                parent[i] = -1;
            }
        }

        for (var z = 0; z < depth; z++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = x + z * width;
                if (height[i] < sea && TouchesLand(height, sea, x, z, width, depth))
                {
                    Push(head, tail, next, i, sea * Sub);
                }
            }
        }

        var orderCount = 0;
        var current = sea * Sub;
        var popped = 0;
        while (true)
        {
            while (current < Buckets && head[current] < 0)
            {
                current++;
            }

            if (current >= Buckets)
            {
                break;
            }

            var c = head[current];
            head[current] = next[c];
            if (head[current] < 0)
            {
                tail[current] = -1;
            }

            if ((++popped & 0xFFFFF) == 0)
            {
                b.CancellationToken.ThrowIfCancellationRequested();
            }

            if (height[c] >= sea)
            {
                order[orderCount++] = c;
            }

            var levelOfC = level[c];
            var cx = c % width;
            var cz = c / width;
            if (cx > 0)
            {
                Visit(c - 1);
            }

            if (cx < width - 1)
            {
                Visit(c + 1);
            }

            if (cz > 0)
            {
                Visit(c - width);
            }

            if (cz < depth - 1)
            {
                Visit(c + width);
            }

            void Visit(int n)
            {
                if (level[n] != Unvisited)
                {
                    return;
                }

                var lv = Math.Max((height[n] * Sub) + jitter[n], (int)levelOfC);
                level[n] = (ushort)lv;
                parent[n] = c;
                Push(head, tail, next, n, lv);
            }
        }

        ApplyLakes(b, level);
        return new Drainage(parent, order, orderCount, next);
    }

    /// <summary>With no tile under the sea (extreme settings) the lowest tile becomes a one-tile sea so water has somewhere to go.</summary>
    private static void EnsureSea(MapBuilder b)
    {
        var height = b.Height;
        var sea = (byte)b.SeaLevel;
        var lowest = 0;
        for (var i = 0; i < height.Length; i++)
        {
            if (height[i] < sea)
            {
                return;
            }

            if (height[i] < height[lowest])
            {
                lowest = i;
            }
        }

        height[lowest] = (byte)(sea - 1);
    }

    /// <summary>Smooth noise in 0 to <see cref="Sub"/> - 1 that tilts flat ground, so rivers meander instead of running straight.</summary>
    private static byte[] Jitter(MapBuilder b)
    {
        var noise = b.Noise(10);
        var jitter = new byte[b.TileCount];
        b.ForBands((_, z0, z1) =>
        {
            for (var z = z0; z < z1; z++)
            {
                for (var x = 0; x < b.Width; x++)
                {
                    jitter[x + (z * b.Width)] = (byte)Math.Clamp((int)(noise.Fractal(x / 14f, z / 14f, 2) * Sub), 0, Sub - 1);
                }
            }
        });
        return jitter;
    }

    private static bool TouchesLand(byte[] height, byte sea, int x, int z, int width, int depth)
    {
        var i = x + z * width;
        return (x > 0 && height[i - 1] >= sea)
            || (x < width - 1 && height[i + 1] >= sea)
            || (z > 0 && height[i - width] >= sea)
            || (z < depth - 1 && height[i + width] >= sea);
    }

    private static void Push(int[] head, int[] tail, int[] next, int tile, int level)
    {
        next[tile] = -1;
        if (head[level] < 0)
        {
            head[level] = tile;
        }
        else
        {
            next[tail[level]] = tile;
        }

        tail[level] = tile;
    }

    /// <summary>Turns filled depressions into lakes (deep) or levelled ground (shallow) and marks the sea.</summary>
    private static void ApplyLakes(MapBuilder b, ushort[] level)
    {
        var minDepth = b.Tuning.LakeMinDepth;
        var sea = (byte)b.SeaLevel;
        b.ForBands((_, z0, z1) =>
        {
            for (var i = z0 * b.Width; i < z1 * b.Width; i++)
            {
                if (b.Height[i] < sea)
                {
                    b.Flags[i] |= (byte)TileTraits.Sea;
                    b.WaterLevel[i] = sea;
                    continue;
                }

                var spill = level[i] / Sub;
                var fill = spill - b.Height[i];
                if (fill >= minDepth)
                {
                    b.Flags[i] |= (byte)TileTraits.Lake;
                    b.WaterLevel[i] = (byte)spill;
                }
                else if (fill > 0)
                {
                    b.Height[i] = (byte)spill;
                }
            }
        });
    }
}
