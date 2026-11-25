namespace Talt.ModelGen;

/// <summary>Trees (broadleaf, conifer, birch; several variants each), bushes and rocks.</summary>
internal static class Nature
{
    public static void AddAll(ModelCatalog catalog)
    {
        for (var v = 0; v < 3; v++)
        {
            catalog.Add("tree:broadleaf", $"broadleaf-{(char)('a' + v)}.vox", Broadleaf(v));
            catalog.Add("tree:conifer", $"conifer-{(char)('a' + v)}.vox", Conifer(v));
        }

        for (var v = 0; v < 2; v++)
        {
            catalog.Add("tree:birch", $"birch-{(char)('a' + v)}.vox", Birch(v));
        }

        for (var v = 0; v < 3; v++)
        {
            catalog.Add("bush", $"bush-{(char)('a' + v)}.vox", Bush(v));
            catalog.Add("rock", $"rock-{(char)('a' + v)}.vox", Rock(v));
        }
    }

    private static VoxelCanvas Broadleaf(int variant)
    {
        var scale = 0.85f + (variant * 0.13f);
        var size = (int)MathF.Ceiling(9 * scale) | 1;
        var height = (int)MathF.Ceiling(28 * scale);
        var c = new VoxelCanvas(size, height, size);
        var centre = size / 2f;
        int[] leaves = [0x3E7A2C, 0x4C8E34, 0x5BA03C, 0x346A26];
        var crownY = height - (7.2f * scale);

        // The trunk reaches into the crown, so no gap shows under the leaves.
        Trunk(c, centre, (int)crownY, 0x5A3A1E, 0x4A2F18, 2);
        Blob(c, centre, crownY, centre, centre - 0.2f, 6.5f * scale, centre - 0.2f, leaves, variant * 11);
        Blob(c, centre - (1.5f * scale), crownY - (2.5f * scale), centre + (1f * scale), 3f * scale, 3.2f * scale, 3f * scale, leaves, (variant * 11) + 3);
        Blob(c, centre + (1.6f * scale), crownY - (1.8f * scale), centre - (1.2f * scale), 3f * scale, 3f * scale, 3f * scale, leaves, (variant * 11) + 5);
        return c;
    }

    private static VoxelCanvas Conifer(int variant)
    {
        var scale = 0.9f + (variant * 0.12f);
        var size = (int)MathF.Ceiling(8 * scale) | 1;
        var height = (int)MathF.Ceiling(34 * scale);
        var c = new VoxelCanvas(size, height, size);
        var centre = size / 2f;
        Trunk(c, centre, (int)(8 * scale), 0x4A3020, 0x3A2618, 2);
        int[] needles = [0x1F4A26, 0x285A2E, 0x1A3F20];
        var bottom = (int)(5 * scale);
        var tiers = 4;
        for (var y = bottom; y < height; y++)
        {
            var t = (y - bottom) / (float)(height - bottom);
            var tierT = (t * tiers) % 1f; // Each tier widens downward.
            var radius = ((1f - t) * (size / 2f)) * (0.55f + (0.45f * (1f - tierT))) + 0.4f;
            for (var z = 0; z < size; z++)
            {
                for (var x = 0; x < size; x++)
                {
                    var dx = x + 0.5f - centre;
                    var dz = z + 0.5f - centre;
                    if ((dx * dx) + (dz * dz) <= radius * radius)
                    {
                        var n = VoxelCanvas.Noise(x, y, z, 77 + variant);
                        var shade = VoxelCanvas.Shade(needles[(int)(n * needles.Length) % needles.Length], 0.85f + (0.25f * (MathF.Round(t * 4f) / 4f)));
                        c.Set(x, y, z, c.Color(shade));
                    }
                }
            }
        }

        return c;
    }

    private static VoxelCanvas Birch(int variant)
    {
        var scale = 0.9f + (variant * 0.15f);
        var size = 7;
        var height = (int)MathF.Ceiling(26 * scale);
        var c = new VoxelCanvas(size, height, size);
        var centre = size / 2f;
        var bark = c.Color(0xE6E2D6);
        var mark = c.Color(0x3A3632);
        var crownY = height - (6f * scale);
        for (var y = 0; y < (int)crownY; y++)
        {
            c.Set(3, y, 3, VoxelCanvas.Noise(3, y, 3, 9 + variant) < 0.22f ? mark : bark);
        }

        int[] leaves = [0x7FB040, 0x8EC24A, 0x6EA036];
        Blob(c, centre, crownY, centre, 3.2f, 6f * scale, 3.2f, leaves, 31 + variant);
        return c;
    }

    private static VoxelCanvas Bush(int variant)
    {
        var c = new VoxelCanvas(6, 4, 6);
        int[] leaves = variant switch
        {
            0 => [0x3F7A30, 0x4A8A36, 0x356A28],
            1 => [0x5A8A34, 0x6A9A3C, 0x4E7A2E],
            _ => [0x2F6A34, 0x3A7A3C, 0x2A5A2C],
        };
        Blob(c, 3f, 1.6f, 3f, 2.9f, 2.2f, 2.6f, leaves, 50 + variant);
        if (variant == 1)
        {
            // A few flowers.
            var flower = c.Color(0xE8D04A);
            for (var i = 0; i < 6; i++)
            {
                var x = (int)(VoxelCanvas.Noise(i, 0, 1, 5) * 6);
                var z = (int)(VoxelCanvas.Noise(i, 0, 2, 5) * 6);
                for (var y = 3; y >= 0; y--)
                {
                    if (c.Get(x, y, z) != 0)
                    {
                        c.Set(x, y, z, flower);
                        break;
                    }
                }
            }
        }

        return c;
    }

    private static VoxelCanvas Rock(int variant)
    {
        var (sx, sy, sz) = variant switch { 0 => (5, 3, 4), 1 => (7, 4, 6), _ => (4, 2, 4) };
        var c = new VoxelCanvas(sx, sy, sz);
        int[] stone = [0x8A8C90, 0x7A7C80, 0x9A9CA0, 0x6E7074];
        c.Ellipsoid(sx / 2f, 0.2f, sz / 2f, sx / 2f, sy * 1.05f, sz / 2f, (x, y, z) =>
        {
            var n = VoxelCanvas.Noise(x, y, z, 90 + variant);
            if (n < 0.12f && y > 0)
            {
                return 0; // Chipped edges.
            }

            return c.Color(y == sy - 1 && n > 0.6f ? 0x6A7A4A : stone[(int)(n * stone.Length) % stone.Length]); // Moss on top.
        });
        return c;
    }

    private static void Trunk(VoxelCanvas c, float centre, int height, int color, int dark, int width)
    {
        var x0 = (int)MathF.Round(centre - (width / 2f));
        for (var y = 0; y < height; y++)
        {
            for (var z = x0; z < x0 + width; z++)
            {
                for (var x = x0; x < x0 + width; x++)
                {
                    c.Set(x, y, z, c.Color(VoxelCanvas.Noise(x, y, z, 3) < 0.3f ? dark : color));
                }
            }
        }
    }

    /// <summary>A leafy blob: an ellipsoid with a ragged edge, darker underneath, colours mixed by noise.</summary>
    private static void Blob(VoxelCanvas c, float cx, float cy, float cz, float rx, float ry, float rz, int[] colors, int seed)
    {
        c.Ellipsoid(cx, cy, cz, rx, ry, rz, (x, y, z) =>
        {
            var dx = (x + 0.5f - cx) / rx;
            var dy = (y + 0.5f - cy) / ry;
            var dz = (z + 0.5f - cz) / rz;
            var edge = (dx * dx) + (dy * dy) + (dz * dz);
            var n = VoxelCanvas.Noise(x, y, z, seed);
            if (edge > 0.7f && n < 0.35f)
            {
                return 0; // Ragged outline.
            }

            var light = 0.82f + (0.3f * (MathF.Round(Math.Clamp((dy + 1f) / 2f, 0f, 1f) * 4f) / 4f)); // Few shades: small palette.
            return c.Color(VoxelCanvas.Shade(colors[(int)(n * colors.Length) % colors.Length], light));
        });
    }
}
