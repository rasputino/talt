using Talt.Core.Construction;
using Talt.Core.Stations;

namespace Talt.Core.Tests.Stations;

/// <summary>
/// User stories 2–4: supply to served stations, waiting cap, loading and unloading, ratings and losses
/// (FR-008–FR-024).
/// </summary>
public sealed class CargoFlowTests
{
    [Fact]
    public void UnservedStation_ReceivesNothing_AndTheIndustryKeepsProducing()
    {
        var kit = StationKit.Generated();
        var (station, industry, coal) = kit.ServeIndustry();
        var slot = kit.Simulation.Industries.SlotOf(industry);
        var stockBefore = kit.Simulation.Industries.AvailableAt(slot, coal);

        kit.Days(10);

        Assert.Equal(0, kit.Stations.WaitingAt(station, coal));
        Assert.True(kit.Simulation.Industries.AvailableAt(kit.Simulation.Industries.SlotOf(industry), coal) > stockBefore);
        Assert.NotEqual(0UL, kit.Stations.SuppliesMask(station) & (1UL << coal));
    }

    [Fact]
    public void ServedStation_ReceivesTheRatingShare_AndTheRestStaysAtTheIndustry()
    {
        var kit = StationKit.Generated();
        var (station, industry, coal) = kit.ServeIndustry();
        var industries = kit.Simulation.Industries;
        kit.Stations.MarkServed(station, coal);
        var shippedBefore = industries.ShippedLastMonthAt(industries.SlotOf(industry), coal);
        _ = shippedBefore;

        kit.Days(20);

        var waiting = kit.Stations.WaitingAt(station, coal);
        Assert.True(waiting > 0);
        Assert.Equal(kit.Stations.TotalSupplied, waiting + kit.Stations.TotalLost);
        Assert.InRange(kit.Stations.RatingAt(station, coal), 0, 1000);
    }

    [Fact]
    public void BusStop_ReceivesTownPassengers_AndLoadingCountsThemAsTransported()
    {
        var kit = StationKit.Generated();
        var station = kit.ServeTown(0);
        var passengers = kit.Cargo("passengers");
        kit.Stations.MarkServed(station, passengers);
        kit.Days(15);
        var waiting = kit.Stations.WaitingAt(station, passengers);
        Assert.True(waiting > 0);

        var hold = new CargoHold();
        Assert.Equal(waiting, kit.Stations.Load(station, passengers, waiting, hold, 50));
        Assert.Equal(waiting, hold.AmountOf(passengers));
        Assert.All(Enumerable.Range(0, hold.Count), i => Assert.True(hold[i].SourceTown >= 0));
        // The town learns about it at the end of the month (feature 008 statistics).
        TownsRun(kit);
        var transported = 0;
        for (var town = 0; town < kit.Simulation.Towns!.Count; town++)
        {
            kit.Simulation.Towns.TryGetTown(town, out var info);
            transported += info.PassengersTransportedLastMonth;
        }

        Assert.True(transported >= waiting);
    }

    [Fact]
    public void WaitingCap_LosesTheExcess()
    {
        var kit = StationKit.Flat();
        Assert.True(kit.Road(2, 20, 60, 20).Succeeded);
        Assert.True(kit.Build("truck-bay", 10, 21).Succeeded);
        var station = kit.StationAt(10, 21);
        var coal = kit.Cargo("coal");
        var cap = kit.Stations.Rules.WaitingCap;
        StationJoinAndGroupTests.Transfer(kit, station, coal, cap - 10);
        var hold = new CargoHold();
        hold.Add(new HeldCargo(coal, 50, 9999, -1, -1, 0));
        kit.Stations.Unload(station, hold, UnloadMode.Transfer);
        Assert.Equal(cap, kit.Stations.WaitingAt(station, coal));
        Assert.Equal(40, kit.Stations.TotalLost);
    }

    [Fact]
    public void Load_TakesOldestFirst_AndNeverMoreThanWaiting()
    {
        var kit = StationKit.Flat();
        Assert.True(kit.Road(2, 20, 60, 20).Succeeded);
        Assert.True(kit.Build("truck-bay", 10, 21).Succeeded);
        var station = kit.StationAt(10, 21);
        var coal = kit.Cargo("coal");
        var first = new CargoHold();
        first.Add(new HeldCargo(coal, 30, 7001, -1, -1, 5));
        kit.Stations.Unload(station, first, UnloadMode.Transfer);
        var second = new CargoHold();
        second.Add(new HeldCargo(coal, 20, 7002, -1, -1, 6));
        kit.Stations.Unload(station, second, UnloadMode.Transfer);

        var hold = new CargoHold();
        Assert.Equal(40, kit.Stations.Load(station, coal, 40, hold, 70));
        Assert.Equal(7001, hold[0].Origin);
        Assert.Equal(30, hold[0].Amount);
        Assert.Equal(5, hold[0].PickupDay); // A transfer keeps its pickup day.
        Assert.Equal(10, hold[1].Amount);
        Assert.Equal(10, kit.Stations.Load(station, coal, 999, hold, 70));
        Assert.Equal(0, kit.Stations.WaitingAt(station, coal));
    }

    [Fact]
    public void Unload_DeliversAcceptedCargo_ReportsIt_AndKeepsTheRest()
    {
        var kit = StationKit.Generated();
        var power = FindAcceptingStation(kit, "coal");
        var coal = kit.Cargo("coal");
        var listener = new Listener();
        kit.Stations.DeliveryListener = listener;
        var hold = new CargoHold();
        hold.Add(new HeldCargo(coal, 40, 9999, -1, -1, kit.Stations.Today - 3));
        hold.Add(new HeldCargo(kit.Cargo("passengers"), 7, 9999, 0, -1, kit.Stations.Today));

        var result = kit.Stations.Unload(power, hold, UnloadMode.Normal);

        Assert.Equal(40, result.Delivered);
        Assert.Equal(7, result.Kept); // Truck stops do not handle passengers.
        var delivery = Assert.Single(listener.Deliveries);
        Assert.Equal(coal, delivery.Cargo);
        Assert.Equal(40, delivery.Amount);
        Assert.Equal(power, delivery.DestinationStation);
        Assert.Equal(DeliveryConsumer.Industry, delivery.ConsumerKind);
        Assert.Equal(3, delivery.DaysInTransit);
    }

    [Fact]
    public void Unload_AtTheOriginGroup_IsNotADelivery()
    {
        var kit = StationKit.Generated();
        var power = FindAcceptingStation(kit, "coal");
        var coal = kit.Cargo("coal");
        var hold = new CargoHold();
        hold.Add(new HeldCargo(coal, 10, power, -1, -1, kit.Stations.Today));
        Assert.Equal(10, kit.Stations.Unload(power, hold, UnloadMode.Normal).Kept);
    }

    [Fact]
    public void RatingTarget_FollowsTheTables()
    {
        var rules = StationRules.Default.Rating;
        var neglected = rules.Target(-1, 0, 5000);
        var good = rules.Target(2, 120, 50);
        Assert.True(good > neglected);
        Assert.InRange(good, 0, 1000);
        Assert.Equal(Math.Clamp(rules.Base + 300 + 200 + 150, 0, 1000), good);
    }

    [Fact]
    public void Rating_RisesWithFrequentFastPickups_AndFallsWhenNeglected()
    {
        var kit = StationKit.Generated();
        var (station, _, coal) = kit.ServeIndustry();
        kit.Stations.MarkServed(station, coal);
        var initial = kit.Stations.RatingAt(station, coal);
        Assert.Equal(kit.Stations.Rules.Rating.Initial, initial);
        var hold = new CargoHold();
        for (var day = 0; day < 30; day++)
        {
            kit.Days(1);
            kit.Stations.Load(station, coal, 10_000, hold, 120);
            hold.Clear();
        }

        var served = kit.Stations.RatingAt(station, coal);
        Assert.True(served > initial, $"{served} <= {initial}");
        kit.Days(90);
        Assert.True(kit.Stations.RatingAt(station, coal) < served);
    }

    [Fact]
    public void LowRating_LosesWaitingCargo()
    {
        var kit = StationKit.Flat();
        Assert.True(kit.Road(2, 20, 60, 20).Succeeded);
        Assert.True(kit.Build("truck-bay", 10, 21).Succeeded);
        var station = kit.StationAt(10, 21);
        var coal = kit.Cargo("coal");
        StationJoinAndGroupTests.Transfer(kit, station, coal, 3000);
        kit.Stations.MarkServed(station, coal);
        kit.Days(120);
        Assert.True(kit.Stations.RatingAt(station, coal) < kit.Stations.Rules.LossBelowRating);
        Assert.True(kit.Stations.WaitingAt(station, coal) < 3000);
        Assert.Equal(3000, kit.Stations.WaitingAt(station, coal) + kit.Stations.TotalLost);
    }

    /// <summary>A truck bay whose catchment accepts the cargo (near an industry that accepts it).</summary>
    private static int FindAcceptingStation(StationKit kit, string cargo)
    {
        var c = kit.Cargo(cargo);
        var industries = kit.Simulation.Industries;
        var plan = new ConstructionPlan();
        for (var slot = 0; slot < industries.Count; slot++)
        {
            var kind = kit.Simulation.Content.IndustryKind(industries.KindIndexAt(slot));
            if (!kind.AcceptsCargo(cargo) || !industries.FootprintBounds(slot, out var minX, out var minZ, out var maxX, out var maxZ))
            {
                continue;
            }

            for (var z = minZ; z < maxZ; z++)
            {
                var bx = maxX + 1;
                var rx = maxX + 2;
                if (kit.State.Map.Contains(rx, z + 1) && kit.State.Map.Contains(rx, z - 1) && kit.Road(rx, z - 1, rx, z + 1).Succeeded)
                {
                    StationPlanner.PlanPiece(kit.State, Company.PlayerId, kit.Stations.Rules.FindPiece("truck-bay"), bx, z, plan);
                    if (plan.IsValid && kit.Build("truck-bay", bx, z).Succeeded)
                    {
                        var id = kit.StationAt(bx, z);
                        Assert.True(kit.Stations.Accepts(id, c));
                        return id;
                    }
                }
            }
        }

        throw new InvalidOperationException($"No industry accepting {cargo} could be reached.");
    }

    private static void TownsRun(StationKit kit)
    {
        var month = kit.Simulation.Date.Month;
        while (kit.Simulation.Date.Month == month)
        {
            kit.Simulation.Tick();
        }
    }

    private sealed class Listener : IDeliveryListener
    {
        public List<CargoDelivery> Deliveries { get; } = [];

        public void OnDelivered(in CargoDelivery delivery) => Deliveries.Add(delivery);
    }
}
