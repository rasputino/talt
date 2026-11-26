using Talt.Core.Construction;
using Talt.Core.Finance;
using Talt.Core.Scenarios;
using Talt.Core.Simulation;
using Talt.Core.Maps;
using Talt.Core.Tests.Stations;
using Talt.Core.Vehicles;

namespace Talt.Core.Tests.Vehicles;

public sealed class EarningTests
{
    internal static (StationKit Kit, int A, int B, int Depot) TownLine(int seed = 10)
    {
        var kit = StationKit.Generated(seed);
        var a = kit.ServeTown(0);
        var b = kit.ServeTown(0);
        var vehicles = kit.Simulation.RoadVehicles!;
        var (cx, cz) = kit.Simulation.Towns!.CentreOf(0);
        var plan = new ConstructionPlan();
        for (var r = 1; r < 16; r++)
        {
            for (var z = cz - r; z <= cz + r; z++)
            {
                for (var x = cx - r; x <= cx + r; x++)
                {
                    DepotPlanner.PlanDepot(kit.State, Company.PlayerId, vehicles.Rules, x, z, plan);
                    if (plan.IsValid && kit.Run(new BuildDepotCommand(Company.PlayerId, x, z)).Succeeded)
                    {
                        vehicles.Depots.TryGetAt(kit.State.Map.Index(x, z), out var depot);
                        return (kit, a, b, depot);
                    }
                }
            }
        }

        throw new InvalidOperationException("No depot fits near the town.");
    }

    [Fact]
    public void BusesOnATownLine_CarryPassengers_AndEarnMoney()
    {
        var (kit, a, b, depot) = TownLine();
        var vehicles = kit.Simulation.RoadVehicles!;
        var buses = new int[3];
        for (var i = 0; i < buses.Length; i++)
        {
            Assert.True(kit.Run(new BuyVehicleCommand(Company.PlayerId, depot, "bus-townsman")).Succeeded);
            buses[i] = vehicles.LastCreatedId;
        }

        Assert.True(kit.Run(new CreateLineCommand(Company.PlayerId, [a, b], buses[0])).Succeeded, kit.Last.ReasonCode);
        var line = vehicles.Lines.IdAt(0);
        Assert.True(kit.Run(new AssignLineCommand(Company.PlayerId, buses[1], line)).Succeeded);
        Assert.True(kit.Run(new AssignLineCommand(Company.PlayerId, buses[2], line)).Succeeded);
        kit.Simulation.Finance!.Events.Clear();
        var sawVehicleIncome = false;
        for (var day = 0; day < 90; day++)
        {
            kit.Days(1);
            Assert.Null(vehicles.CheckInvariants());
            while (kit.Simulation.Finance.Events.TryDequeue(out var e))
            {
                sawVehicleIncome |= e.Kind == MoneyEventKind.Income && Array.IndexOf(buses, e.VehicleId) >= 0;
            }
        }

        Assert.True(vehicles.Deliveries > 0, "buses should deliver passengers");
        Assert.True(kit.Simulation.Finance.TotalPaid > 0);
        Assert.True(sawVehicleIncome, "income events should name the bus");
        long profit = 0;
        foreach (var bus in buses)
        {
            profit += vehicles.ProfitThisYearAt(vehicles.SlotOf(bus));
        }

        Assert.NotEqual(0, profit);
    }

    [Fact]
    public void TownLineGame_IsTheSame_InParallelAndSequentially()
    {
        static ulong Run(bool parallel)
        {
            var (kit, a, b, depot) = TownLine(11);
            for (var i = 0; i < 4; i++)
            {
                kit.Run(new BuyVehicleCommand(Company.PlayerId, depot, "bus-townsman"));
            }

            var vehicles = kit.Simulation.RoadVehicles!;
            kit.Run(new CreateLineCommand(Company.PlayerId, [a, b], vehicles.IdAt(0)));
            for (var s = 1; s < vehicles.Count; s++)
            {
                kit.Run(new AssignLineCommand(Company.PlayerId, vehicles.IdAt(s), vehicles.Lines.IdAt(0)));
            }

            kit.Days(60, parallel);
            return kit.Simulation.ComputeFingerprint();
        }

        Assert.Equal(Run(false), Run(true));
    }
}
