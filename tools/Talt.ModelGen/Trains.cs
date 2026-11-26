namespace Talt.ModelGen;

/// <summary>
/// Feature 015: original locomotives and wagons of rail.json (8 voxels per tile, front towards +Z), keyed
/// <c>train:&lt;model id&gt;</c>, three voxels wide like the track and as long as their simulated length. The body colour is
/// a reserved "paint" entry, so each model shows the colour of its data file entry.
/// </summary>
internal static class Trains
{
    private const int Wheel = 0x1C1C1E;
    private const int Frame = 0x34363A;
    private const int Glass = 0x26303A;
    private const int HeadLight = 0xF4EEC8;
    private const int Roof = 0x55575C;

    public static void AddAll(ModelCatalog catalog)
    {
        // Ironhorse: a steam engine, boiler in front, cab at the back, a chimney.
        var steam = Body(8, 5, out var paint);
        var black = steam.Color(0x22262A);
        steam.Box(0, 2, 2, 3, 4, 8, black);
        steam.Box(0, 2, 0, 3, 5, 2, paint);
        steam.Paint(0, 3, 0, 3, 4, 2, steam.Color(Glass));
        steam.Box(1, 4, 6, 2, 5, 7, black);
        steam.Set(1, 3, 7, steam.Color(HeadLight));
        catalog.Add("train:loco-ironhorse", "train-loco-ironhorse.vox", steam, paint: [paint]);

        // Meridian: a hood diesel with cabs at both ends.
        var diesel = Body(8, 4, out paint);
        diesel.Box(0, 2, 0, 3, 4, 8, paint);
        diesel.Box(0, 3, 1, 3, 4, 7, diesel.Color(Roof));
        diesel.Paint(0, 3, 0, 3, 4, 1, diesel.Color(Glass));
        diesel.Paint(0, 3, 7, 3, 4, 8, diesel.Color(Glass));
        diesel.Set(1, 2, 7, diesel.Color(HeadLight));
        catalog.Add("train:loco-meridian", "train-loco-meridian.vox", diesel, paint: [paint]);

        // Aurora: a streamlined electric with a pantograph.
        var electric = Body(8, 5, out paint);
        electric.Box(0, 2, 0, 3, 4, 8, paint);
        electric.Paint(0, 3, 7, 3, 4, 8, electric.Color(Glass));
        electric.Paint(0, 3, 0, 3, 4, 1, electric.Color(Glass));
        var pole = electric.Color(0x3A3D42);
        electric.Set(1, 4, 3, pole);
        electric.Set(1, 4, 4, pole);
        electric.Set(1, 2, 7, electric.Color(HeadLight));
        catalog.Add("train:loco-aurora", "train-loco-aurora.vox", electric, paint: [paint]);

        // Swift: a railcar with a window band.
        var railcar = Coach(7, out paint);
        railcar.Paint(0, 2, 6, 3, 4, 7, railcar.Color(Glass));
        railcar.Set(1, 2, 6, railcar.Color(HeadLight));
        catalog.Add("train:railcar-swift", "train-railcar-swift.vox", railcar, paint: [paint]);

        catalog.Add("train:wagon-coach", "train-wagon-coach.vox", Coach(6, out paint), paint: [paint]);

        var mail = Body(6, 4, out paint);
        mail.Box(0, 2, 0, 3, 4, 6, paint);
        mail.Box(0, 3, 0, 3, 4, 6, mail.Color(Roof));
        catalog.Add("train:wagon-mailvan", "train-wagon-mailvan.vox", mail, paint: [paint]);

        var hopper = Body(5, 4, out paint);
        hopper.Box(0, 2, 0, 3, 4, 5, paint);
        hopper.Clear(1, 3, 1, 2, 4, 4);
        hopper.Box(1, 3, 1, 2, 4, 4, hopper.Color(0x2A2420)); // The load.
        catalog.Add("train:wagon-hopper", "train-wagon-hopper.vox", hopper, paint: [paint]);

        var tank = Body(5, 4, out paint);
        tank.Box(0, 2, 0, 3, 3, 5, paint);
        tank.Box(1, 3, 0, 2, 4, 5, paint);
        catalog.Add("train:wagon-tank", "train-wagon-tank.vox", tank, paint: [paint]);

        var boxcar = Body(6, 4, out paint);
        boxcar.Box(0, 2, 0, 3, 4, 6, paint);
        boxcar.Paint(0, 2, 2, 3, 3, 4, boxcar.Color(0x3E2A18)); // Sliding door.
        catalog.Add("train:wagon-boxcar", "train-wagon-boxcar.vox", boxcar, paint: [paint]);
    }

    /// <summary>A coach: a painted box with windows along both sides and a grey roof.</summary>
    private static VoxelCanvas Coach(int length, out byte paint)
    {
        var c = Body(length, 4, out paint);
        c.Box(0, 2, 0, 3, 4, length, paint);
        c.Box(0, 3, 0, 3, 4, length, c.Color(Roof));
        var glass = c.Color(Glass);
        for (var z = 1; z < length - 1; z++)
        {
            if (z % 2 == 1)
            {
                c.Paint(0, 2, z, 1, 3, z + 1, glass);
                c.Paint(2, 2, z, 3, 3, z + 1, glass);
            }
        }

        return c;
    }

    /// <summary>A frame on two bogies (wheels at both ends) and a reserved paint colour.</summary>
    private static VoxelCanvas Body(int length, int height, out byte paint)
    {
        var c = new VoxelCanvas(3, height, length);
        paint = c.Reserve(0xC8C8C8);
        var wheel = c.Color(Wheel);
        c.Box(0, 1, 0, 3, 2, length, c.Color(Frame));
        foreach (var z in (ReadOnlySpan<int>)[0, length - 1])
        {
            c.Set(0, 0, z, wheel);
            c.Set(2, 0, z, wheel);
        }

        return c;
    }
}
