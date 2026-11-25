namespace Talt.ModelGen;

/// <summary>
/// Road vehicles (8 voxels per tile, front towards +Z): a car, a van, a bus and a truck. The body colour is a
/// reserved "paint" palette entry, so each vehicle shows its own colour.
/// </summary>
internal static class Vehicles
{
    private const int Tyre = 0x1C1C1E;
    private const int Chassis = 0x34363A;
    private const int Glass = 0x26303A;
    private const int HeadLight = 0xF4EEC8;
    private const int TailLight = 0xB0261E;
    private const int Bumper = 0x8E9196;

    public static void AddAll(ModelCatalog catalog)
    {
        Car(catalog);
        Van(catalog);
        Bus(catalog);
        Truck(catalog);
    }

    private static void Car(ModelCatalog catalog)
    {
        var c = new VoxelCanvas(4, 4, 7);
        var paint = c.Reserve(0xC8C8C8);
        Wheels(c, [1, 5]);
        c.Box(0, 1, 0, 4, 2, 7, paint);
        c.Box(0, 2, 1, 4, 3, 6, paint);
        c.Box(0, 3, 2, 4, 4, 5, paint);
        // Windows all round the cabin, pillars painted.
        var glass = c.Color(Glass);
        c.Paint(0, 2, 2, 1, 3, 5, glass);
        c.Paint(3, 2, 2, 4, 3, 5, glass);
        c.Paint(1, 2, 5, 3, 3, 6, glass);
        c.Paint(1, 2, 1, 3, 3, 2, glass);
        Lights(c, 6, 0, y: 1);
        catalog.Add("vehicle", "car.vox", c, paint: [paint]);
    }

    private static void Van(ModelCatalog catalog)
    {
        var c = new VoxelCanvas(4, 5, 7);
        var paint = c.Reserve(0xC8C8C8);
        Wheels(c, [1, 5]);
        c.Box(0, 1, 0, 4, 4, 6, paint);
        c.Box(0, 1, 6, 4, 3, 7, paint);
        c.Box(0, 4, 0, 4, 5, 5, c.Color(0xD8D8D2));
        var glass = c.Color(Glass);
        c.Paint(0, 3, 4, 4, 4, 6, glass);
        c.Paint(1, 3, 5, 3, 4, 6, glass);
        Lights(c, 6, 0, y: 1);
        catalog.Add("vehicle", "van.vox", c, paint: [paint]);
    }

    private static void Bus(ModelCatalog catalog)
    {
        var c = new VoxelCanvas(4, 6, 9);
        var paint = c.Reserve(0xC8C8C8);
        Wheels(c, [1, 7]);
        c.Box(0, 1, 0, 4, 5, 9, paint);
        c.Box(0, 5, 0, 4, 6, 9, c.Color(0xE4E4DE)); // Light roof.
        var glass = c.Color(Glass);
        for (var z = 0; z < 8; z++)
        {
            if (z % 3 != 2)
            {
                c.Paint(0, 3, z, 1, 5, z + 1, glass);
                c.Paint(3, 3, z, 4, 5, z + 1, glass);
            }
        }

        c.Paint(0, 2, 8, 4, 5, 9, glass); // Windscreen.
        c.Set(1, 4, 8, c.Color(0xF0C040)); // Destination sign.
        c.Set(2, 4, 8, c.Color(0xF0C040));
        Lights(c, 8, 0, y: 1);
        catalog.Add("vehicle", "bus.vox", c, paint: [paint]);
    }

    private static void Truck(ModelCatalog catalog)
    {
        var c = new VoxelCanvas(4, 6, 8);
        var paint = c.Reserve(0xC8C8C8);
        Wheels(c, [1, 2, 6]);
        c.Box(0, 1, 0, 4, 2, 8, c.Color(Chassis));
        c.Box(0, 2, 0, 4, 6, 5, c.Color(0xE6E2D8)); // Cargo box.
        c.Paint(0, 5, 0, 4, 6, 5, c.Color(0xCFCBC0));
        c.Box(0, 2, 6, 4, 5, 8, paint); // Cab.
        c.Box(0, 1, 5, 4, 2, 8, paint);
        var glass = c.Color(Glass);
        c.Paint(0, 3, 7, 4, 5, 8, glass);
        c.Paint(0, 3, 6, 1, 5, 7, glass);
        c.Paint(3, 3, 6, 4, 5, 7, glass);
        Lights(c, 7, 0, y: 1);
        catalog.Add("vehicle", "truck.vox", c, paint: [paint]);
    }

    private static void Wheels(VoxelCanvas c, int[] axles)
    {
        var tyre = c.Color(Tyre);
        c.Box(1, 0, 0, c.SizeX - 1, 1, c.SizeZ, c.Color(Chassis));
        foreach (var z in axles)
        {
            c.Set(0, 0, z, tyre);
            c.Set(c.SizeX - 1, 0, z, tyre);
        }
    }

    private static void Lights(VoxelCanvas c, int front, int back, int y)
    {
        c.Set(0, y, front, c.Color(HeadLight));
        c.Set(c.SizeX - 1, y, front, c.Color(HeadLight));
        c.Set(0, y, back, c.Color(TailLight));
        c.Set(c.SizeX - 1, y, back, c.Color(TailLight));
        for (var x = 1; x < c.SizeX - 1; x++)
        {
            c.Set(x, y, front, c.Color(Bumper));
        }
    }
}
