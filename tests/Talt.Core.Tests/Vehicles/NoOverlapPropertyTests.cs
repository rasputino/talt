using Talt.Core.Vehicles;

namespace Talt.Core.Tests.Vehicles;

/// <summary>
/// SC-002: randomized road grids with bays, a depot and many buses on random lines; <see cref="RoadVehicleSystem.CheckInvariants"/>
/// runs after every tick (no two vehicles closer than their length and the gap in a lane, every vehicle on a road link).
/// </summary>
public sealed class NoOverlapPropertyTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void RandomGrids_NeverOverlap(int seed)
    {
        var random = new Random(seed);
        var kit = VehicleKit.Flat(64);
        var spacing = random.Next(6, 11);
        for (var line = 8; line <= 56; line += spacing)
        {
            kit.Road(8, line, 56, line);
            kit.Road(line, 8, line, 56);
        }

        var stations = new List<int>();
        for (var tries = 0; tries < 200 && stations.Count < 10; tries++)
        {
            var x = random.Next(6, 58);
            var z = random.Next(6, 58);
            if (kit.State.Roads.HasRoad(kit.State.Map.Index(x, z)))
            {
                continue;
            }

            var result = kit.Run(new Talt.Core.Stations.BuildStationCommand(0, "bus-bay", x, z));
            if (result.Succeeded && kit.Stations.TryGetStationAt(x, z, out var id))
            {
                stations.Add(id);
            }
        }

        var depot = -1;
        for (var tries = 0; tries < 200 && depot < 0; tries++)
        {
            var x = random.Next(6, 58);
            var z = random.Next(6, 58);
            if (kit.Run(new BuildDepotCommand(0, x, z)).Succeeded)
            {
                kit.Vehicles.Depots.TryGetAt(kit.State.Map.Index(x, z), out depot);
            }
        }

        Assert.True(stations.Count >= 4 && depot >= 0);
        var buses = new List<int>();
        for (var i = 0; i < 40; i++)
        {
            buses.Add(kit.Buy(depot, i % 3 == 0 ? "bus-voltline" : "bus-townsman"));
        }

        for (var l = 0; l < 5; l++)
        {
            var stops = new[] { stations[random.Next(stations.Count)], stations[random.Next(stations.Count)], stations[random.Next(stations.Count)] };
            kit.Line(stops, buses.GetRange(l * 8, 8).ToArray());
        }

        kit.Days(70); // 2 800 ticks × 40 vehicles = 112 000 vehicle-ticks, checked after every tick.
        Assert.True(kit.Vehicles.TotalDistance > 0);
    }
}
