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
        RoadVehicleModels(catalog);
    }

    /// <summary>
    /// Feature 012: compact models of the starter vehicles (vehicles.json), four voxels (half a tile) long like their
    /// simulated length, three wide to fit one lane, keyed <c>vehicle:&lt;model id&gt;</c>.
    /// </summary>
    private static void RoadVehicleModels(ModelCatalog catalog)
    {
        // Townsman bus: a box with a window band and a light roof.
        var bus = new VoxelCanvas(3, 4, 4);
        var paint = bus.Reserve(0xC8C8C8);
        SmallWheels(bus);
        bus.Box(0, 1, 0, 3, 3, 4, paint);
        bus.Box(0, 3, 0, 3, 4, 4, bus.Color(0xE4E4DE));
        bus.Paint(0, 2, 0, 1, 3, 4, bus.Color(Glass));
        bus.Paint(2, 2, 0, 3, 3, 4, bus.Color(Glass));
        bus.Paint(0, 2, 3, 3, 3, 4, bus.Color(Glass));
        bus.Set(1, 1, 3, bus.Color(HeadLight));
        catalog.Add("vehicle:bus-townsman", "road-bus.vox", bus, paint: [paint]);

        // Voltline electric bus: the same body with the trolley poles on the roof.
        var volt = new VoxelCanvas(3, 6, 4);
        paint = volt.Reserve(0xC8C8C8);
        SmallWheels(volt);
        volt.Box(0, 1, 0, 3, 3, 4, paint);
        volt.Box(0, 3, 0, 3, 4, 4, volt.Color(0xDCE3EA));
        volt.Paint(0, 2, 0, 1, 3, 4, volt.Color(Glass));
        volt.Paint(2, 2, 0, 3, 3, 4, volt.Color(Glass));
        volt.Paint(0, 2, 3, 3, 3, 4, volt.Color(Glass));
        var pole = volt.Color(0x3A3D42);
        volt.Set(0, 4, 1, pole);
        volt.Set(2, 4, 1, pole);
        volt.Set(0, 5, 0, pole);
        volt.Set(2, 5, 0, pole);
        volt.Set(1, 1, 3, volt.Color(HeadLight));
        catalog.Add("vehicle:bus-voltline", "road-bus-electric.vox", volt, paint: [paint]);

        // Tipper: a painted cab at the front and an open steel bin.
        var tipper = new VoxelCanvas(3, 4, 4);
        paint = tipper.Reserve(0xC8C8C8);
        SmallWheels(tipper);
        tipper.Box(0, 1, 3, 3, 3, 4, paint);
        tipper.Paint(0, 2, 3, 3, 3, 4, tipper.Color(Glass));
        var steel = tipper.Color(0x6E7277);
        tipper.Box(0, 1, 0, 3, 3, 3, steel);
        tipper.Clear(1, 2, 0, 2, 3, 3);
        tipper.Box(1, 2, 0, 2, 3, 3, tipper.Color(0x5A4632)); // The load (earth-coloured) inside the bin.
        catalog.Add("vehicle:truck-tipper", "road-tipper.vox", tipper, paint: [paint]);

        // Tanker: cab and a pale round-topped tank.
        var tanker = new VoxelCanvas(3, 4, 4);
        paint = tanker.Reserve(0xC8C8C8);
        SmallWheels(tanker);
        tanker.Box(0, 1, 3, 3, 3, 4, paint);
        tanker.Paint(0, 2, 3, 3, 3, 4, tanker.Color(Glass));
        var tank = tanker.Color(0xD8DCE0);
        tanker.Box(0, 1, 0, 3, 3, 3, tank);
        tanker.Box(1, 3, 0, 2, 4, 3, tank);
        catalog.Add("vehicle:truck-tanker", "road-tanker.vox", tanker, paint: [paint]);

        // Box truck: cab and a tall cargo box.
        var box = new VoxelCanvas(3, 4, 4);
        paint = box.Reserve(0xC8C8C8);
        SmallWheels(box);
        box.Box(0, 1, 3, 3, 3, 4, paint);
        box.Paint(0, 2, 3, 3, 3, 4, box.Color(Glass));
        box.Box(0, 1, 0, 3, 4, 3, box.Color(0xE6E2D8));
        catalog.Add("vehicle:truck-box", "road-box.vox", box, paint: [paint]);
    }

    private static void SmallWheels(VoxelCanvas c)
    {
        var tyre = c.Color(Tyre);
        c.Box(1, 0, 0, c.SizeX - 1, 1, c.SizeZ, c.Color(Chassis));
        c.Set(0, 0, 0, tyre);
        c.Set(c.SizeX - 1, 0, 0, tyre);
        c.Set(0, 0, c.SizeZ - 1, tyre);
        c.Set(c.SizeX - 1, 0, c.SizeZ - 1, tyre);
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
