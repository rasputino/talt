using Talt.Core.Towns;

namespace Talt.ModelGen;

/// <summary>
/// Town buildings from their data (footprint, storeys, wall and roof colours, roof and window style): a stone
/// plinth, walls with framed windows (glass is a "glow" entry, lit at night), a door on the front (+Z), corner
/// pilasters, and a stepped gable roof with chimney or a flat roof with parapet and rooftop details. Two
/// variants per type differ in roof shade, door position and details.
/// </summary>
internal static class Buildings
{
    /// <summary>Model voxels per storey (the simulation's storey is 0.6 tiles; 5 voxels is 0.625).</summary>
    public const int StoreyVoxels = 5;

    private const int Tile = 8;

    public static void Add(ModelCatalog catalog, BuildingType type)
    {
        for (var variant = 0; variant < 2; variant++)
        {
            var (canvas, glass) = Build(type, variant);
            catalog.Add($"town-building:{type.Id}", $"building-{type.Id}-{(char)('a' + variant)}.vox", canvas, glow: [glass]);
        }
    }

    private static (VoxelCanvas Canvas, int Glass) Build(BuildingType type, int variant)
    {
        var sizeX = type.Width * Tile;
        var sizeZ = type.Depth * Tile;
        var wallHeight = type.Height * StoreyVoxels;
        var pitched = type.Model.Roof == RoofStyle.Pitched;
        var roofHeight = pitched ? (Math.Min(sizeX, sizeZ) / 2) + 1 : 3;
        var c = new VoxelCanvas(sizeX, wallHeight + roofHeight + 3, sizeZ);
        var glass = c.Reserve(0x2E3A48);

        var wallRgb = type.Model.WallColor;
        var roofRgb = VoxelCanvas.Shade(type.Model.RoofColor, variant == 0 ? 1f : 0.88f);
        var wall = c.Color(wallRgb);
        var wallDark = c.Color(VoxelCanvas.Shade(wallRgb, 0.86f));
        var trim = c.Color(VoxelCanvas.Mix(wallRgb, 0xFFFFFF, 0.45f));
        var plinth = c.Color(VoxelCanvas.Mix(VoxelCanvas.Shade(wallRgb, 0.6f), 0x6A6660, 0.5f));
        var door = c.Color(0x5A3A22);

        // Walls inset by one voxel, so neighbours read as separate buildings.
        const int x0 = 1, z0 = 1;
        var x1 = sizeX - 1;
        var z1 = sizeZ - 1;
        c.Box(x0, 0, z0, x1, wallHeight, z1, wall);
        c.Paint(x0, 0, z0, x1, 1, z1, plinth);
        foreach (var (px, pz) in new[] { (x0, z0), (x1 - 1, z0), (x0, z1 - 1), (x1 - 1, z1 - 1) })
        {
            c.Paint(px, 1, pz, px + 1, wallHeight, pz + 1, wallDark); // Corner pilasters.
        }

        // Storey bands on taller buildings.
        if (type.Height >= 3)
        {
            for (var s = 1; s < type.Height; s++)
            {
                c.Paint(x0, s * StoreyVoxels, z0, x1, (s * StoreyVoxels) + 1, z1, wallDark);
            }
        }

        if (type.Model.Windows != WindowStyle.None)
        {
            var grid = type.Model.Windows == WindowStyle.Grid;
            for (var s = 0; s < type.Height; s++)
            {
                var y = (s * StoreyVoxels) + 2;
                Windows(c, x0, x1, z0, z1, y, grid, glass, trim, skipDoor: s == 0, sizeX);
            }
        }
        else
        {
            // A tall arched window per side (chapel).
            for (var y = 3; y < wallHeight - 2; y++)
            {
                c.Paint(sizeX / 2 - 1, y, z0, (sizeX / 2) + 1, y + 1, z0 + 1, glass);
                c.Paint(x0, y, (sizeZ / 2) - 1, x0 + 1, y + 1, (sizeZ / 2) + 1, glass);
                c.Paint(x1 - 1, y, (sizeZ / 2) - 1, x1, y + 1, (sizeZ / 2) + 1, glass);
            }
        }

        // Door on the front face, with a step.
        var doorX = variant == 0 ? (sizeX / 2) - 1 : Math.Max(x0 + 1, (sizeX / 2) - 3);
        c.Paint(doorX, 1, z1 - 1, doorX + 2, 4, z1, door);
        if (type.Category is BuildingCategory.Shop)
        {
            // Shop window and awning.
            c.Paint(x0 + 1, 1, z1 - 1, doorX, 4, z1, glass);
            c.Paint(doorX + 2, 1, z1 - 1, x1 - 1, 4, z1, glass);
            c.Box(x0, 4, z1, x1, 5, z1 + 1, c.Color(variant == 0 ? 0xB8403A : 0x3A6A9A));
        }

        if (pitched)
        {
            PitchedRoof(c, type, wallHeight, roofRgb, variant);
        }
        else
        {
            FlatRoof(c, type, wallHeight, roofRgb, variant, x0, z0, x1, z1);
        }

        if (type.Id == "bell-chapel")
        {
            // A small bell tower over the front.
            var tx = (sizeX / 2) - 1;
            c.Box(tx, wallHeight, z1 - 3, tx + 2, wallHeight + roofHeight + 3, z1 - 1, wallDark);
            c.Set(tx, wallHeight + roofHeight + 1, z1 - 2, c.Color(0xC8A040));
        }

        return (c, glass);
    }

    private static void Windows(VoxelCanvas c, int x0, int x1, int z0, int z1, int y, bool grid, byte glass, byte trim, bool skipDoor, int sizeX)
    {
        var step = grid ? 3 : 4;
        // Front and back faces (along X).
        for (var x = x0 + 1; x + 2 <= x1 - 1; x += step)
        {
            foreach (var z in new[] { z0, z1 - 1 })
            {
                if (skipDoor && z == z1 - 1 && Math.Abs(x + 1 - (sizeX / 2)) <= 2)
                {
                    continue;
                }

                c.Paint(x, y, z, x + 2, y + 2, z + 1, glass);
                c.Paint(x, y - 1, z, x + 2, y, z + 1, trim); // Sill.
            }
        }

        // Side faces (along Z).
        for (var z = z0 + 1; z + 2 <= z1 - 1; z += step)
        {
            foreach (var x in new[] { x0, x1 - 1 })
            {
                c.Paint(x, y, z, x + 1, y + 2, z + 2, glass);
                c.Paint(x, y - 1, z, x + 1, y, z + 2, trim);
            }
        }
    }

    private static void PitchedRoof(VoxelCanvas c, BuildingType type, int wallHeight, int roofRgb, int variant)
    {
        var sizeX = c.SizeX;
        var sizeZ = c.SizeZ;
        var alongX = type.Width >= type.Depth; // Ridge along the longer side.
        var span = alongX ? sizeZ : sizeX;
        var roof = c.Color(roofRgb);
        var roofDark = c.Color(VoxelCanvas.Shade(roofRgb, 0.82f));
        var gable = c.Color(type.Model.WallColor);
        for (var step = 0; step < span / 2; step++)
        {
            var y = wallHeight + step;
            var row = step % 2 == 0 ? roof : roofDark; // Tile courses.
            if (alongX)
            {
                c.Box(0, y, step, sizeX, y + 1, sizeZ - step, row);
                c.Box(1, y, step + 1, sizeX - 1, y + 1, sizeZ - step - 1, gable); // Gable ends inside the eaves.
                c.Box(2, y, step + 1, sizeX - 2, y + 1, sizeZ - step - 1, row);
            }
            else
            {
                c.Box(step, y, 0, sizeX - step, y + 1, sizeZ, row);
                c.Box(step + 1, y, 1, sizeX - step - 1, y + 1, sizeZ - 1, gable);
                c.Box(step + 1, y, 2, sizeX - step - 1, y + 1, sizeZ - 2, row);
            }
        }

        if (type.Category is BuildingCategory.House)
        {
            // Brick chimney through the roof.
            var cx = variant == 0 ? sizeX - 3 : 2;
            var cz = alongX ? (sizeZ / 2) - 2 : 2;
            c.Box(cx, wallHeight, cz, cx + 1, wallHeight + (span / 2) + 2, cz + 1, c.Color(0x8A4A36));
            c.Set(cx, wallHeight + (span / 2) + 2, cz, c.Color(0x3A3A3A));
        }
    }

    private static void FlatRoof(VoxelCanvas c, BuildingType type, int wallHeight, int roofRgb, int variant, int x0, int z0, int x1, int z1)
    {
        var roof = c.Color(roofRgb);
        var parapet = c.Color(VoxelCanvas.Shade(type.Model.WallColor, 0.8f));
        c.Box(x0, wallHeight, z0, x1, wallHeight + 1, z1, parapet);
        c.Box(x0 + 1, wallHeight, z0 + 1, x1 - 1, wallHeight + 1, z1 - 1, roof);
        var metal = c.Color(0x9AA0A4);
        if (type.Height >= 4)
        {
            // Stair and plant room.
            var w = Math.Max(2, (x1 - x0) / 3);
            var sx = variant == 0 ? x0 + 1 : x1 - 1 - w;
            c.Box(sx, wallHeight + 1, z0 + 1, sx + w, wallHeight + 3, z0 + 1 + w, c.Color(VoxelCanvas.Shade(roofRgb, 1.15f)));
            c.Box(x1 - 3, wallHeight + 1, z1 - 3, x1 - 2, wallHeight + 2, z1 - 2, metal); // Vents.
        }
        else
        {
            c.Set(x0 + 2, wallHeight + 1, z1 - 3, metal);
        }
    }
}
