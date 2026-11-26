using Talt.Core.Vehicles;

namespace Talt.Core.Tests.Vehicles;

public sealed class JunctionTests
{
    /// <summary>Counts, per vehicle, how many times it started loading at a stop.</summary>
    private static Dictionary<int, int> CountStops(VehicleKit kit, int[] vehicles, int days)
    {
        var stops = vehicles.ToDictionary(v => v, _ => 0);
        var loading = vehicles.ToDictionary(v => v, _ => false);
        for (var t = 0; t < days * 40; t++)
        {
            kit.Ticks(1);
            foreach (var v in vehicles)
            {
                var now = kit.Vehicles.StatusAt(kit.Slot(v)) == RoadVehicleStatus.Loading;
                if (now && !loading[v])
                {
                    stops[v]++;
                }

                loading[v] = now;
            }
        }

        return stops;
    }

    [Fact]
    public void Crossing_FedFromEverySide_KeepsEveryVehicleMovingForAMonth()
    {
        var kit = VehicleKit.Flat();
        kit.Road(10, 30, 50, 30);
        kit.Road(30, 10, 30, 50);
        var west = kit.Stop("bus-bay", 9, 30);
        var east = kit.Stop("bus-bay", 51, 30);
        var north = kit.Stop("bus-bay", 30, 9);
        var south = kit.Stop("bus-bay", 30, 51);
        var depot = kit.Depot(20, 31);
        var buses = new int[16];
        for (var i = 0; i < buses.Length; i++)
        {
            buses[i] = kit.Buy(depot);
        }

        kit.Line([west, east], buses[..8]);
        kit.Line([north, south], buses[8..]);
        var stops = CountStops(kit, buses, 100);
        Assert.All(buses, bus => Assert.True(stops[bus] >= 3, $"bus {bus} stopped only {stops[bus]} times"));
    }

    [Fact]
    public void TJunction_WithTurningTraffic_DoesNotDeadlock()
    {
        var kit = VehicleKit.Flat();
        kit.Road(10, 30, 50, 30);
        kit.Road(30, 30, 30, 50);
        var west = kit.Stop("bus-bay", 9, 30);
        var east = kit.Stop("bus-bay", 51, 30);
        var south = kit.Stop("bus-bay", 30, 51);
        var depot = kit.Depot(40, 31);
        var buses = new int[15];
        for (var i = 0; i < buses.Length; i++)
        {
            buses[i] = kit.Buy(depot);
        }

        kit.Line([west, south], buses[..5]);
        kit.Line([east, south], buses[5..10]);
        kit.Line([west, east], buses[10..]);
        var stops = CountStops(kit, buses, 100);
        Assert.All(buses, bus => Assert.True(stops[bus] >= 3, $"bus {bus} stopped only {stops[bus]} times"));
    }

    [Fact]
    public void OnlyOneVehicle_HoldsAJunction_AtATime()
    {
        var kit = VehicleKit.Flat();
        kit.Road(10, 30, 50, 30);
        kit.Road(30, 10, 30, 50);
        var west = kit.Stop("bus-bay", 9, 30);
        var east = kit.Stop("bus-bay", 51, 30);
        var north = kit.Stop("bus-bay", 30, 9);
        var south = kit.Stop("bus-bay", 30, 51);
        var depot = kit.Depot(20, 31);
        var buses = new int[12];
        for (var i = 0; i < buses.Length; i++)
        {
            buses[i] = kit.Buy(depot);
        }

        kit.Line([west, east], buses[..6]);
        kit.Line([north, south], buses[6..]);
        var crossing = kit.State.Map.Index(30, 30);
        var everHeld = false;
        for (var t = 0; t < 20 * 40; t++)
        {
            kit.Ticks(1);
            var holders = 0;
            for (var slot = 0; slot < kit.Vehicles.Count; slot++)
            {
                if (kit.Vehicles.StateAt(slot).HeldJunction == crossing)
                {
                    holders++;
                }
            }

            Assert.True(holders <= 1);
            everHeld |= holders == 1;
        }

        Assert.True(everHeld);
    }
}
