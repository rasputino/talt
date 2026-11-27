using Talt.Core.Construction;
using Talt.Core.Stations;
using Talt.Core.Tests.Stations;
using Talt.Core.Vehicles;

namespace Talt.Core.Tests.Demand;

/// <summary>User story 3: goods from a factory want specific towns; raw cargo still goes anywhere.</summary>
public sealed class GoodsDestinationTests
{
    private static (StationKit Kit, int Factory, int FactoryId, int[] Stops, int[] Towns) Setup()
    {
        for (var seed = 10; seed < 40; seed++)
        {
            var kit = StationKit.Generated(seed);
            var industries = kit.Simulation.Industries;
            var hasFactory = false;
            for (var slot = 0; slot < industries.Count && !hasFactory; slot++)
            {
                hasFactory = industries.KindIdAt(slot) == "factory";
            }

            var towns = kit.Simulation.Towns!;
            var accepting = Enumerable.Range(0, towns.Count).Where(t => towns.Accepts(t, food: false)).Take(2).ToArray();
            if (!hasFactory || accepting.Length < 2)
            {
                continue;
            }

            (int StationId, int IndustryId, int Cargo) factory;
            int[] stops;
            try
            {
                factory = kit.ServeIndustry("factory", "goods");
                stops = [kit.ServeTown(accepting[0], "truck-bay"), kit.ServeTown(accepting[1], "truck-bay")];
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            if (!stops.All(s => kit.Stations.Accepts(s, kit.Cargo("goods"))))
            {
                continue;
            }

            var d = new DemandKitLines(kit);
            var depot = d.DepotNear(accepting[0]);
            d.Line(depot, [factory.StationId, stops[0]], "goods");
            d.Line(depot, [factory.StationId, stops[1]], "goods");
            kit.Days(kit.Simulation.Router!.Rules.ApplyDelayDays + 1);
            return (kit, factory.StationId, factory.IndustryId, stops, accepting);
        }

        throw new InvalidOperationException("No map with a servable factory and two goods towns.");
    }

    [Fact]
    public void FactoryGoods_GetTownDestinations_AndOnlyRoutedOnesLeaveTheFactory()
    {
        var (kit, factory, factoryId, stops, towns) = Setup();
        var goods = kit.Cargo("goods");
        var stations = kit.Stations;
        stations.MarkServed(factory, goods);
        kit.Days(1);
        var unservedBefore = stations.DirectedUnserved;
        var taken = 0;
        for (var i = 0; i < 50; i++)
        {
            taken += stations.TakeProduction(factoryId, goods, 40);
        }

        Assert.True(taken > 0);
        Assert.True(taken < 2000, "goods for towns the lines do not reach stay at the factory");
        Assert.True(stations.DirectedUnserved > unservedBefore);
        var list = new List<DestinationAmount>();
        stations.DestinationWaiting(factory, goods, list);
        Assert.All(list, entry => Assert.Contains(entry.Town, towns));
        Assert.Equal(taken, list.Sum(e => e.Amount));
        foreach (var entry in list)
        {
            var stop = stops[Array.IndexOf(towns, entry.Town)];
            Assert.Equal(stations.GroupKeyOfStation(stop), entry.NextGroupKey);
        }
    }

    [Fact]
    public void GoodsForOneTown_AreNotDeliveredInTheOther()
    {
        var (kit, factory, factoryId, stops, towns) = Setup();
        var goods = kit.Cargo("goods");
        var stations = kit.Stations;
        stations.MarkServed(factory, goods);
        kit.Days(1);
        for (var i = 0; i < 50; i++)
        {
            stations.TakeProduction(factoryId, goods, 40);
        }

        var hold = new CargoHold();
        stations.Load(factory, goods, 10_000, hold, 50, nextStationId: stops[0]);
        var forFirst = hold.Destination(towns[0]);
        Assert.Equal(hold.Total, forFirst); // only goods routed to the first town board this truck
        var result = stations.Unload(stops[1], hold, UnloadMode.Normal, nextStationId: factory);
        Assert.Equal(0, result.Delivered);
        var delivered = stations.Unload(stops[0], hold, UnloadMode.Normal, nextStationId: factory);
        Assert.True(delivered.Delivered > 0 || forFirst == 0);
    }

    [Fact]
    public void Coal_IsStillDeliveredAnywhereItIsAccepted()
    {
        var kit = StationKit.Generated();
        var (station, _, cargo) = kit.ServeIndustry();
        Assert.False(kit.Simulation.Stations!.Demand!.IsDirected(cargo));
        kit.Stations.MarkServed(station, cargo);
        kit.Days(5);
        Assert.True(kit.Stations.WaitingAt(station, cargo) > 0);
        var list = new List<DestinationAmount>();
        kit.Stations.DestinationWaiting(station, cargo, list);
        Assert.Empty(list);
    }

    /// <summary>Depot and line helpers for a <see cref="StationKit"/> game.</summary>
    private sealed class DemandKitLines(StationKit kit)
    {
        private RoadVehicleSystem Vehicles => kit.Simulation.RoadVehicles!;

        public int DepotNear(int town)
        {
            var (cx, cz) = kit.Simulation.Towns!.CentreOf(town);
            var plan = new ConstructionPlan();
            for (var r = 1; r < 16; r++)
            {
                for (var z = cz - r; z <= cz + r; z++)
                {
                    for (var x = cx - r; x <= cx + r; x++)
                    {
                        DepotPlanner.PlanDepot(kit.State, Company.PlayerId, Vehicles.Rules, x, z, plan);
                        if (plan.IsValid && kit.Run(new BuildDepotCommand(Company.PlayerId, x, z)).Succeeded)
                        {
                            Vehicles.Depots.TryGetAt(kit.State.Map.Index(x, z), out var depot);
                            return depot;
                        }
                    }
                }
            }

            throw new InvalidOperationException("No depot fits near the town.");
        }

        public void Line(int depot, int[] stations, string cargo)
        {
            Assert.True(kit.Run(new BuyVehicleCommand(Company.PlayerId, depot, "truck-box", cargo)).Succeeded, kit.Last.ReasonCode);
            var id = Vehicles.LastCreatedId;
            Assert.True(kit.Run(new CreateLineCommand(Company.PlayerId, stations, id)).Succeeded, kit.Last.ReasonCode);
            kit.Run(new StartVehicleCommand(Company.PlayerId, id, Start: false));
        }
    }
}
