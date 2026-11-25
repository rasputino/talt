using Talt.Core.Construction;
using Talt.Core.Finance;
using Talt.Core.Stations;
using Talt.Core.Tests.Stations;

namespace Talt.Core.Tests.Finance;

public sealed class PaymentTests
{
    [Fact]
    public void Formula_IsRateTimesAmountTimesDistance_ForFastDeliveries()
    {
        // 2 400 per 1 000 t per tile, 30 t, 40 tiles: 2 880.
        var quote = PaymentCalculator.Quote(2400, 20, 4, 35, units: 30, distance: 40, minDistance: 1, days: 5, PaymentCalculator.LevelOne);
        Assert.Equal(2880, quote.Amount);
        Assert.Equal(1000, quote.TimeFactorPerMille);
    }

    [Fact]
    public void Payment_IsLinearInAmountAndDistance_WithinOneUnit()
    {
        long Pay(int units, int distance) => PaymentCalculator.Quote(3333, 4, 60, 25, units, distance, 1, 2, PaymentCalculator.LevelOne).Amount;
        Assert.InRange(Pay(200, 37) - (2 * Pay(100, 37)), -1, 1);
        Assert.InRange(Pay(100, 74) - (2 * Pay(100, 37)), -1, 1);
    }

    [Fact]
    public void LateDeliveries_LoseValuePerDay_DownToTheMinimum()
    {
        Assert.Equal(1000, PaymentCalculator.TimeFactor(days: 4, freeDays: 4, decayPerDayPerMille: 60, minimumPercent: 25));
        Assert.Equal(880, PaymentCalculator.TimeFactor(6, 4, 60, 25));
        Assert.Equal(250, PaymentCalculator.TimeFactor(40, 4, 60, 25));
        var fast = PaymentCalculator.Quote(1000, 4, 60, 25, 1000, 10, 1, 0, PaymentCalculator.LevelOne).Amount;
        var slow = PaymentCalculator.Quote(1000, 4, 60, 25, 1000, 10, 1, 6, PaymentCalculator.LevelOne).Amount;
        Assert.Equal(10_000, fast);
        Assert.Equal(8_800, slow);
    }

    [Fact]
    public void ShortDistances_ArePaidAtTheMinimum_AndNothingPaysZero()
    {
        Assert.Equal(5, PaymentCalculator.Quote(1000, 4, 60, 25, 1000, 0, 5, 0, PaymentCalculator.LevelOne).Distance);
        Assert.Equal(0, PaymentCalculator.Quote(1000, 4, 60, 25, 0, 10, 1, 0, PaymentCalculator.LevelOne).Amount);
        Assert.Equal(0, PaymentCalculator.Quote(999, 4, 60, 25, 1, 1, 1, 0, PaymentCalculator.LevelOne).Amount); // Rounds down to 0.
    }

    [Fact]
    public void PaymentLevel_ScalesPayments()
    {
        var quote = PaymentCalculator.Quote(1000, 4, 60, 25, 1000, 10, 1, 0, 1_500_000);
        Assert.Equal(15_000, quote.Amount);
    }

    [Fact]
    public void Pay_UsesTheDistanceBetweenTheStations_AndBooksTheCargoIncome()
    {
        var kit = FinanceKit.Flat();
        Assert.True(kit.Road(5, 10, 50, 10).Succeeded);
        Assert.True(kit.Build("truck-bay", 6, 11).Succeeded);
        Assert.True(kit.Build("truck-bay", 46, 11).Succeeded);
        var from = kit.StationAt(6, 11);
        var to = kit.StationAt(46, 11);
        var coal = kit.Cargo("coal");
        var cash = kit.Player().Balance;
        kit.Finance().Events.Clear();

        var paid = kit.Finance().Pay(FinanceKit.Delivery(coal, 30, from, to, 100, 105));

        Assert.Equal(kit.Finance().Quote(coal, 30, 40, 5).Amount, paid);
        Assert.Equal(2880, paid);
        Assert.Equal(cash + paid, kit.Player().Balance);
        Assert.Equal(paid, kit.Ledger().Month(0, Ledger.IncomeSlot(coal)));
        Assert.True(kit.Finance().Events.TryDequeue(out var e));
        Assert.Equal(new MoneyEvent(Company.PlayerId, paid, 46, 11, MoneyEventKind.Income), e);
    }

    [Fact]
    public void RemovedOrigin_IsPaidFromWhereItStood()
    {
        var kit = FinanceKit.Flat();
        kit.Road(5, 10, 50, 10);
        kit.Build("truck-bay", 6, 11);
        kit.Build("truck-bay", 46, 11);
        var from = kit.StationAt(6, 11);
        var to = kit.StationAt(46, 11);
        Assert.True(kit.Remove(6, 11).Succeeded);
        Assert.True(kit.Stations.TryGetCentre(from, out var x, out var z));
        Assert.Equal((6, 11), (x, z));
        Assert.Equal(2880, kit.Finance().Pay(FinanceKit.Delivery(kit.Cargo("coal"), 30, from, to, 100, 105)));
    }

    [Fact]
    public void RealUnload_IsPaidThroughTheDeliveryListener()
    {
        var kit = StationKit.Generated();
        var (origin, _, _) = kit.ServeIndustry();
        var power = AcceptingStation(kit, "coal");
        var coal = kit.Cargo("coal");
        var hold = new CargoHold();
        hold.Add(new HeldCargo(coal, 40, origin, -1, -1, kit.Stations.Today - 3));
        var cash = kit.Player().Balance;

        var result = kit.Stations.Unload(power, hold, UnloadMode.Normal);

        Assert.True(result.Delivered > 0);
        kit.Stations.TryGetCentre(origin, out var ox, out var oz);
        kit.Stations.TryGetCentre(power, out var px, out var pz);
        var expected = kit.Finance().Quote(coal, result.Delivered, Math.Abs(px - ox) + Math.Abs(pz - oz), 3).Amount;
        Assert.True(expected > 0);
        Assert.Equal(cash + expected, kit.Player().Balance);
        Assert.Equal(expected, kit.Ledger().Year(0, Ledger.IncomeSlot(coal)));
        Assert.Equal(1, kit.Finance().Deliveries);
    }

    [Fact]
    public void UnloadAtTheOriginGroup_PaysNothing()
    {
        var kit = StationKit.Generated();
        var power = AcceptingStation(kit, "coal");
        var hold = new CargoHold();
        hold.Add(new HeldCargo(kit.Cargo("coal"), 10, power, -1, -1, kit.Stations.Today));
        var cash = kit.Player().Balance;
        kit.Stations.Unload(power, hold, UnloadMode.Normal);
        Assert.Equal(cash, kit.Player().Balance);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DebugService_IsPaidOnlyWhenAsked(bool pays)
    {
        var kit = FinanceKit.Flat(new FinanceSettings(DebugServicePays: pays));
        kit.Road(5, 10, 50, 10);
        kit.Build("truck-bay", 6, 11);
        var station = kit.StationAt(6, 11);
        var coal = kit.Cargo("coal");
        var debug = new CargoDelivery(Company.PlayerId, coal, 30, station, station, -1, 100, 104, DeliveryConsumer.DebugService, -1);
        var paid = kit.Finance().Pay(debug);
        Assert.Equal(pays ? kit.Finance().Quote(coal, 30, 40, 4).Amount : 0, paid);
    }

    [Fact]
    public void StationServicePays_ProducesIncome_InAGeneratedGame()
    {
        var kit = new StationKit(Talt.Core.Simulation.GameSimulation.Create(new Talt.Core.Scenarios.ScenarioSettings(
            Seed: 10, StartYear: 1950, MapSizeTiles: 256, VehicleCount: 0,
            Map: new Talt.Core.Maps.MapSettings(256, 256, Seed: 10, Roughness: 30, MaxHeight: 40, WaterAmount: 15, SeaLevel: 6),
            Stations: new StationSettings(60),
            Finance: new FinanceSettings(DebugServicePays: true))));
        kit.State.Player.Refund(100_000_000);
        kit.ServeIndustry();
        kit.Days(40);
        Assert.True(kit.Finance().TotalPaid > 0);
        Assert.True(kit.Ledger().YearIncome(0) >= kit.Finance().TotalPaid);
    }

    internal static int AcceptingStation(StationKit kit, string cargo)
    {
        var c = kit.Cargo(cargo);
        var industries = kit.Simulation.Industries;
        var plan = new ConstructionPlan();
        for (var slot = 0; slot < industries.Count; slot++)
        {
            var kind = kit.Simulation.Content.IndustryKind(industries.KindIndexAt(slot));
            if (!kind.AcceptsCargo(cargo) || !industries.FootprintBounds(slot, out _, out var minZ, out var maxX, out var maxZ))
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
}
