using Talt.Core.Content;

namespace Talt.ModelGen;

/// <summary>
/// Industries from their data: a gravel yard over the footprint, each data box turned into a building with a
/// plinth, a roof edge and windows, plus details per kind (head frame, spoil heap, crop rows, trees, pump,
/// striped chimney, storage tanks, sawtooth roof, log piles). Pivot = footprint centre.
/// </summary>
internal static class Industries
{
    private const int Tile = 8;

    public static void Add(ModelCatalog catalog, IndustryKindDefinition kind)
    {
        var (canvas, glass) = Build(kind);
        catalog.Add($"industry:{kind.Id}", $"industry-{kind.Id}.vox", canvas, glow: [glass]);
    }

    private static (VoxelCanvas Canvas, int Glass) Build(IndustryKindDefinition kind)
    {
        var sizeX = kind.FootprintWidth * Tile;
        var sizeZ = kind.FootprintDepth * Tile;
        var top = 0;
        foreach (var box in kind.Model)
        {
            top = Math.Max(top, box.Y + box.Height);
        }

        var c = new VoxelCanvas(sizeX, (top * Tile) + 10, sizeZ);
        var glass = c.Reserve(0x2E3A48);
        var id = kind.Id;
        var yard = c.Color(id is "forest" or "farm" ? 0x5E8A3A : 0x8E887C);
        c.Box(0, 0, 0, sizeX, 1, sizeZ, yard);
        for (var z = 0; z < sizeZ; z++)
        {
            for (var x = 0; x < sizeX; x++)
            {
                if (VoxelCanvas.Noise(x, 0, z, 17) < 0.25f)
                {
                    c.Set(x, 0, z, c.Color(VoxelCanvas.Shade(id is "forest" or "farm" ? 0x5E8A3A : 0x8E887C, 0.88f)));
                }
            }
        }

        for (var i = 0; i < kind.Model.Count; i++)
        {
            var box = kind.Model[i];
            var (bx0, bz0) = BoxMin(kind, box);
            var bx1 = bx0 + (box.Width * Tile);
            var bz1 = bz0 + (box.Depth * Tile);
            var by0 = Math.Max(1, box.Y * Tile);
            var by1 = (box.Y + box.Height) * Tile;
            var rgb = (int)box.Color;
            var ground = box.Y == 0 && box.Height == 1 && box.Width * box.Depth >= kind.FootprintWidth * kind.FootprintDepth / 2;
            if (ground)
            {
                // A low base slab: keep it as the yard, slightly raised.
                c.Box(bx0, 0, bz0, bx1, 2, bz1, c.Color(rgb));
                continue;
            }

            var tall = box.Width == 1 && box.Depth == 1 && box.Height >= 3;
            if (tall)
            {
                Chimney(c, id, bx0, by0, bz0, by1, rgb);
                continue;
            }

            // A building: inset by one voxel, plinth, darker roof edge, windows.
            c.Box(bx0 + 1, by0, bz0 + 1, bx1 - 1, by1, bz1 - 1, c.Color(rgb));
            c.Paint(bx0 + 1, by0, bz0 + 1, bx1 - 1, by0 + 1, bz1 - 1, c.Color(VoxelCanvas.Shade(rgb, 0.7f)));
            c.Paint(bx0 + 1, by1 - 1, bz0 + 1, bx1 - 1, by1, bz1 - 1, c.Color(VoxelCanvas.Shade(rgb, 0.8f)));
            if (by1 - by0 >= 6 && id is not ("farm" or "forest"))
            {
                for (var y = by0 + 3; y + 2 <= by1 - 2; y += 5)
                {
                    for (var x = bx0 + 2; x + 2 <= bx1 - 2; x += 4)
                    {
                        c.Paint(x, y, bz0 + 1, x + 2, y + 2, bz0 + 2, glass);
                        c.Paint(x, y, bz1 - 2, x + 2, y + 2, bz1 - 1, glass);
                    }

                    for (var z = bz0 + 2; z + 2 <= bz1 - 2; z += 4)
                    {
                        c.Paint(bx0 + 1, y, z, bx0 + 2, y + 2, z + 2, glass);
                        c.Paint(bx1 - 2, y, z, bx1 - 1, y + 2, z + 2, glass);
                    }
                }
            }

            if (id is "factory" or "food-plant" && box.Y > 0)
            {
                Sawtooth(c, bx0 + 1, by1, bz0 + 1, bx1 - 1, bz1 - 1, glass);
            }
        }

        Details(c, kind);
        return (c, glass);
    }

    /// <summary>Lowest voxel corner of a data box (boxes are centred on the footprint centre).</summary>
    private static (int X, int Z) BoxMin(IndustryKindDefinition kind, ModelBox box)
    {
        var centreX = box.X + (box.Width % 2 == 0 ? 0f : 0.5f) - (kind.FootprintWidth % 2 == 0 ? 0f : 0.5f);
        var centreZ = box.Z + (box.Depth % 2 == 0 ? 0f : 0.5f) - (kind.FootprintDepth % 2 == 0 ? 0f : 0.5f);
        var x = (int)MathF.Round((kind.FootprintWidth * Tile / 2f) + (centreX * Tile) - (box.Width * Tile / 2f));
        var z = (int)MathF.Round((kind.FootprintDepth * Tile / 2f) + (centreZ * Tile) - (box.Depth * Tile / 2f));
        return (x, z);
    }

    private static void Chimney(VoxelCanvas c, string id, int x0, int y0, int z0, int y1, int rgb)
    {
        var cx = x0 + 4f;
        var cz = z0 + 4f;
        if (id is "coal-mine" or "iron-mine")
        {
            // Head frame: four legs, cross braces and a wheel on top.
            var steel = c.Color(0x4A4E54);
            foreach (var (lx, lz) in new[] { (1, 1), (6, 1), (1, 6), (6, 6) })
            {
                c.Box(x0 + lx, 1, z0 + lz, x0 + lx + 1, y1, z0 + lz + 1, steel);
            }

            for (var y = 4; y < y1; y += 5)
            {
                c.Box(x0 + 1, y, z0 + 1, x0 + 7, y + 1, z0 + 2, steel);
                c.Box(x0 + 1, y, z0 + 6, x0 + 7, y + 1, z0 + 7, steel);
            }

            c.Box(x0 + 2, y1, z0 + 3, x0 + 6, y1 + 3, z0 + 5, c.Color(0x8A3A2A));
            return;
        }

        if (id == "oil-well")
        {
            return; // The pump is drawn in Details.
        }

        // Round chimney, red and white bands near the top for power stations.
        var red = c.Color(0xB03A2E);
        var white = c.Color(0xE6E2DA);
        var body = c.Color(rgb);
        c.Cylinder(cx, 1, cz, 2.6f, y1 + 4, (_, y, _) =>
        {
            if (id == "power-station" && y > y1 - 4)
            {
                return ((y / 2) % 2 == 0) ? red : white;
            }

            return body;
        });
        c.Cylinder(cx, y1 + 3, cz, 1.4f, 2, (_, _, _) => 0);
        c.Clear((int)cx - 1, y1 + 3, (int)cz - 1, (int)cx + 1, y1 + 5, (int)cz + 1);
    }

    private static void Sawtooth(VoxelCanvas c, int x0, int y, int z0, int x1, int z1, byte glass)
    {
        var roof = c.Color(0x6A6458);
        for (var z = z0; z < z1; z++)
        {
            var t = (z - z0) % 4;
            for (var h = 0; h <= t; h++)
            {
                c.Box(x0, y + h, z, x1, y + h + 1, z + 1, h == t && t == 3 ? glass : roof);
            }
        }
    }

    private static void Details(VoxelCanvas c, IndustryKindDefinition kind)
    {
        var sx = c.SizeX;
        var sz = c.SizeZ;
        switch (kind.Id)
        {
            case "coal-mine":
                Heap(c, sx * 0.72f, sz * 0.7f, 5.5f, 4f, [0x1E1E20, 0x2A2A2E, 0x141416], 1);
                break;
            case "iron-mine":
                Heap(c, sx * 0.28f, sz * 0.72f, 5f, 3.5f, [0x8C5A40, 0x7A4A34, 0x9A6A50], 2);
                break;
            case "forest":
                for (var i = 0; i < 9; i++)
                {
                    var x = 3 + (int)(VoxelCanvas.Noise(i, 1, 0, 4) * (sx - 6));
                    var z = 3 + (int)(VoxelCanvas.Noise(i, 2, 0, 4) * (sz - 6));
                    c.Box(x, 1, z, x + 1, 5, z + 1, c.Color(0x4A3020));
                    c.Ellipsoid(x + 0.5f, 8f, z + 0.5f, 2.6f, 4.5f, 2.6f, (vx, vy, vz) => c.Color(VoxelCanvas.Noise(vx, vy, vz, 8) < 0.5f ? 0x2F5A28 : 0x3A6A30));
                }

                // Stacked logs.
                c.Box(1, 1, sz - 3, 7, 3, sz - 1, c.Color(0x9A7048));
                break;
            case "farm":
                for (var x = 1; x < sx / 2; x++)
                {
                    for (var z = 1; z < sz - 1; z++)
                    {
                        c.Set(x, 1, z, c.Color(z % 2 == 0 ? 0xC9B85A : 0xA89840)); // Crop rows.
                    }
                }

                break;
            case "oil-well":
                {
                    var steel = c.Color(0x2A2A2A);
                    var cx = sx / 2;
                    var cz = sz / 2;
                    c.Box(cx - 1, 2, cz - 1, cx + 1, 9, cz, steel); // Post.
                    c.Box(cx - 5, 9, cz - 1, cx + 5, 10, cz, c.Color(0x3A3A3A)); // Beam.
                    c.Box(cx + 4, 6, cz - 1, cx + 6, 10, cz, c.Color(0xC8A040)); // Horse head.
                    c.Cylinder(sx * 0.25f, 2, sz * 0.75f, 2.5f, 5, (_, _, _) => c.Color(0x8A8A82)); // Small tank.
                    break;
                }

            case "refinery":
                c.Cylinder(sx * 0.25f, 2, sz * 0.25f, 3.2f, 9, (_, y, _) => c.Color(y % 4 == 0 ? 0xB8B8B0 : 0xD8D8D0));
                c.Cylinder(sx * 0.75f, 2, sz * 0.75f, 3f, 8, (_, y, _) => c.Color(y % 4 == 0 ? 0xB8B8B0 : 0xD8D8D0));
                c.Box(sx / 4, 6, sz / 2, (3 * sx) / 4, 7, (sz / 2) + 1, c.Color(0x7A7A74)); // Pipe.
                break;
            case "sawmill":
                for (var i = 0; i < 3; i++)
                {
                    c.Box(2, 1 + i, 1 + i, sx - 2, 2 + i, 4 - i, c.Color(i % 2 == 0 ? 0x9A7048 : 0x8A6038));
                }

                break;
            case "steel-mill":
                c.Cylinder(sx * 0.25f, 2, sz * 0.25f, 3f, 14, (_, y, _) => c.Color(y > 11 ? 0x3E444A : 0x5A3A2E));
                break;
            default:
                break;
        }
    }

    private static void Heap(VoxelCanvas c, float cx, float cz, float radius, float height, int[] colors, int seed) =>
        c.Ellipsoid(cx, 1f, cz, radius, height, radius, (x, y, z) => c.Color(colors[(int)(VoxelCanvas.Noise(x, y, z, seed) * colors.Length) % colors.Length]));
}
