using Talt.Core.Construction;
using Talt.Core.Finance;
using Talt.Core.Vehicles;

namespace Talt.Core.Tests.Vehicles;

public sealed class DrivingTests
{
    [Fact]
    public void BoughtBus_LeavesTheDepot_AndShuttlesBetweenTheStops()
    {
        var kit = VehicleKit.Flat();
        var (west, east, depot) = kit.Corridor();
        var bus = kit.Buy(depot);
        Assert.Equal(RoadVehicleStatus.InDepot, kit.Vehicles.StatusAt(kit.Slot(bus)));
        kit.Line([west, east], bus);

        var visitedWest = false;
        var visitedEast = false;
        for (var day = 0; day < 60 && !(visitedWest && visitedEast); day++)
        {
            kit.Days(1);
            var slot = kit.Slot(bus);
            if (kit.Vehicles.StatusAt(slot) == RoadVehicleStatus.Loading)
            {
                visitedWest |= kit.Vehicles.TargetIdAt(slot) == west;
                visitedEast |= kit.Vehicles.TargetIdAt(slot) == east;
            }
        }

        Assert.True(visitedWest && visitedEast, "the bus should stop at both ends");
        Assert.True(kit.Vehicles.TotalDistance > 40 * 65_536);
    }

    [Fact]
    public void Purchase_ChargesThePrice_AndRegistersTheRunningCost()
    {
        var kit = VehicleKit.Flat();
        var (_, _, depot) = kit.Corridor();
        var before = kit.State.Player.Balance;
        kit.Buy(depot);
        var price = kit.Vehicles.Rules.Models[kit.Vehicles.Rules.FindModel("bus-townsman")].Price;
        Assert.Equal(before - price, kit.State.Player.Balance);
        Assert.Equal(-price, kit.State.Player.Ledger!.Month(0, Ledger.Slot(LedgerCategory.VehiclePurchase)));
        Assert.Equal(1, kit.Simulation.Finance!.RunningCosts.ActiveCount);
    }

    [Fact]
    public void Purchase_IsRefused_WithoutMoney()
    {
        var kit = VehicleKit.Flat(balance: 30_000);
        var (_, _, depot) = kit.Corridor();
        var result = kit.Run(new BuyVehicleCommand(Company.PlayerId, depot, "bus-voltline"));
        Assert.False(result.Succeeded);
        Assert.Equal(ConstructionReasons.NotEnoughMoney, result.ReasonCode);
        Assert.Equal(0, kit.Vehicles.Count);
    }

    [Fact]
    public void ManyBuses_OnOneLine_QueueWithoutOverlapping()
    {
        var kit = VehicleKit.Flat();
        var (west, east, depot) = kit.Corridor();
        var buses = new int[8];
        for (var i = 0; i < buses.Length; i++)
        {
            buses[i] = kit.Buy(depot);
        }

        kit.Line([west, east], buses);
        kit.Days(80); // CheckInvariants after every tick: no overlap, lanes sorted.
        Assert.True(kit.Vehicles.TotalDistance > 8 * 80 * 65_536);
    }

    [Fact]
    public void ParallelAndSequentialTicks_GiveTheSameFingerprints()
    {
        static ulong[] Run(bool parallel)
        {
            var kit = VehicleKit.Flat();
            var (west, east, depot) = kit.Corridor();
            var buses = new int[30];
            for (var i = 0; i < buses.Length; i++)
            {
                buses[i] = kit.Buy(depot);
            }

            kit.Line([west, east], buses);
            var prints = new ulong[20];
            for (var i = 0; i < prints.Length; i++)
            {
                kit.Ticks(200, parallel);
                prints[i] = kit.Simulation.ComputeFingerprint();
            }

            return prints;
        }

        Assert.Equal(Run(parallel: false), Run(parallel: true));
    }

    [Fact]
    public void StoppingDistance_IsTheExactSumOfBrakingSteps()
    {
        for (var v = 0; v < 5000; v += 37)
        {
            long sum = 0;
            for (var s = v - 409; s > 0; s -= 409)
            {
                sum += s;
            }

            Assert.Equal(sum, RoadVehicleSystem.StopDistance(v, 409));
        }
    }
}
