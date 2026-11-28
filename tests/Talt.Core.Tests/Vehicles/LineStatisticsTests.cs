using Talt.Core.Construction;
using Talt.Core.Simulation;
using Talt.Core.Stations;
using Talt.Core.Vehicles;
using Talt.Tests.Support;

namespace Talt.Core.Tests.Vehicles;

public sealed class LineStatisticsTests
{
    [Fact]
    public void DistanceIsCapacityWeightedAndYearClosesOnce()
    {
        var lines = new LineRegistry();
        var id = lines.Create(0, [1, 2]);
        Assert.Null(lines.CurrentYear(id).LoadPercent);
        lines.RecordDistance(id, 9, 20, 20);
        lines.RecordDistance(id, 1, 0, 20);
        lines.RecordBoarding(id, 20, passengers: true);
        lines.RecordBoarding(id, 5, passengers: false);
        lines.RecordProfit(id, 123);
        var actual = lines.CurrentYear(id);
        Assert.Equal(90, actual.LoadPercent);
        Assert.Equal(new LineYearStatistics(123, 20, 5, 180, 200), actual);
        lines.CloseYear();
        Assert.Equal(actual, lines.PreviousYear(id));
        Assert.Equal(default, lines.CurrentYear(id));
        Assert.True(lines.HasPreviousYear(id));
    }

    [Fact]
    public void OccupancyFollowsMergingPartialUnloadingAndClearingTheHold()
    {
        var hold = new CargoHold();
        var first = new HeldCargo(0, 7, 1, -1, 2, 3);
        hold.Add(first);
        hold.Add(first with { Amount = 2 });
        hold.Add(first with { Origin = 4, Amount = 5 });
        Assert.Equal(14, hold.Total);
        hold.SetAmount(0, 4);
        Assert.Equal(9, hold.Total);
        hold.SetPart(0, 0, 0);
        Assert.Equal(5, hold.Total);
        hold.Clear();
        Assert.Equal(0, hold.Total);
    }

    [Fact]
    public void FirstStopClocksAreIndependentAndCanBeDisabled()
    {
        var lines = new LineRegistry();
        var id = lines.Create(0, [10, 20]);
        lines.SetIntervalDays(id, 3);
        Assert.True(lines.TryDispatch(id, 10, 0, rail: false));
        Assert.True(lines.TryDispatch(id, 10, 0, rail: true));
        Assert.True(lines.TryDispatch(id, 20, 1, rail: true));
        Assert.False(lines.TryDispatch(id, 10, 119, rail: true));
        Assert.True(lines.TryDispatch(id, 10, 120, rail: true));
        Assert.True(lines.TryDispatch(id, 10, 120, rail: false));
        lines.SetIntervalDays(id, 0);
        Assert.True(lines.TryDispatch(id, 10, 121, rail: false));
        lines.SetIntervalDays(id, 1);
        Assert.True(lines.TryDispatch(id, 10, 121, rail: false));
    }

    [Fact]
    public void BusesLeaveTheFirstStopAtTheRequestedSpacing()
    {
        var kit = VehicleKit.Flat();
        var (west, east, depot) = kit.Corridor();
        var first = kit.Buy(depot);
        var second = kit.Buy(depot);
        var line = kit.Line([west, east], first, second);
        Assert.True(kit.Run(new SetLineIntervalCommand(Company.PlayerId, line, 30)).Succeeded);
        var firstTick = -1L;
        var secondTick = -1L;
        for (var tick = 0; tick < 5_000 && (firstTick < 0 || secondTick < 0); tick++)
        {
            kit.Simulation.Tick(false);
            if (firstTick < 0 && kit.Vehicles.StopIndexAt(kit.Slot(first)) == 1)
            {
                firstTick = kit.Simulation.CurrentTick;
            }

            if (secondTick < 0 && kit.Vehicles.StopIndexAt(kit.Slot(second)) == 1)
            {
                secondTick = kit.Simulation.CurrentTick;
            }
        }

        Assert.True(firstTick >= 0 && secondTick >= 0);
        Assert.True(Math.Abs(firstTick - secondTick) >= 30 * GameClock.TicksPerDay);
    }

    [Fact]
    public void OpenDistanceMatchesEveryMovementAcrossStopsAndYearEnd()
    {
        var (kit, a, b, depot) = EarningTests.TownLine();
        var fleet = kit.Simulation.RoadVehicles!;
        Assert.True(kit.Run(new BuyVehicleCommand(Company.PlayerId, depot, "bus-townsman")).Succeeded);
        Assert.True(kit.Run(new CreateLineCommand(Company.PlayerId, [a, b], fleet.IdAt(0))).Succeeded);
        var line = fleet.LineAt(0);
        long occupied = 0, offered = 0;
        var firstYear = kit.Simulation.Date.Year;
        while (kit.Simulation.Date.Year == firstYear)
        {
            var before = fleet.TotalDistance;
            var serving = fleet.TargetKindAt(0) == VehicleTargetKind.Stop;
            var load = fleet.HoldAt(0).Total;
            kit.Simulation.Tick(false);
            var distance = serving ? fleet.TotalDistance - before : 0;
            occupied += distance * load;
            offered += distance * fleet.ModelAt(0).Capacity;
            if (kit.Simulation.Date.Year == firstYear)
            {
                var live = fleet.Lines.CurrentYear(line);
                Assert.Equal(occupied, live.OccupiedDistance);
                Assert.Equal(offered, live.CapacityDistance);
                Assert.Equal(live, fleet.Lines.CurrentYear(line));
            }
        }

        Assert.True(occupied > 0);
        Assert.Equal(occupied, fleet.Lines.PreviousYear(line).OccupiedDistance);
        Assert.Equal(offered, fleet.Lines.PreviousYear(line).CapacityDistance);
        Assert.Equal(default, fleet.Lines.CurrentYear(line));
    }

    [Fact]
    public void SellingAnotherVehicleKeepsTheSwappedVehiclesOpenLeg()
    {
        var kit = VehicleKit.Flat();
        var (west, east, depot) = kit.Corridor();
        var sold = kit.Buy(depot);
        var moving = kit.Buy(depot);
        var line = kit.Line([west, east], moving);
        kit.Days(1);
        var before = kit.Vehicles.Lines.CurrentYear(line);
        Assert.True(before.CapacityDistance > 0);
        Assert.True(kit.Run(new SellVehicleCommand(Company.PlayerId, sold)).Succeeded);
        Assert.Equal(0, kit.Slot(moving));
        Assert.Equal(before, kit.Vehicles.Lines.CurrentYear(line));
        kit.Days(1);
        Assert.True(kit.Vehicles.Lines.CurrentYear(line).CapacityDistance > before.CapacityDistance);
    }

    [Fact]
    public void OperatingCostsAndDistanceStayWithOriginalLine()
    {
        var kit = VehicleKit.Flat();
        var (west, east, depot) = kit.Corridor();
        var bus = kit.Buy(depot);
        var first = kit.Line([west, east], bus);
        var second = kit.Line([west, east]);
        kit.Days(10);
        var lines = kit.Vehicles.Lines;
        var previous = lines.CurrentYear(first);
        Assert.True(previous.CapacityDistance > 0);
        Assert.Equal(kit.Vehicles.ProfitThisYearAt(kit.Slot(bus)), previous.Profit);
        Assert.True(kit.Run(new AssignLineCommand(Company.PlayerId, bus, second)).Succeeded);
        kit.Days(10);
        Assert.Equal(previous, lines.CurrentYear(first));
        Assert.Equal(kit.Vehicles.ProfitThisYearAt(kit.Slot(bus)), previous.Profit + lines.CurrentYear(second).Profit);
        Assert.Equal(ConstructionReasons.OwnedByOther, kit.Run(new SetLineIntervalCommand(Company.PlayerId + 1, second, 3)).ReasonCode);
        Assert.Equal(ConstructionReasons.NoLine, kit.Run(new SetLineIntervalCommand(Company.PlayerId, -1, 3)).ReasonCode);
        Assert.False(kit.Run(new SetLineIntervalCommand(Company.PlayerId, second, -1)).Succeeded);
        Assert.True(kit.Run(new SetLineIntervalCommand(Company.PlayerId, second, 5)).Succeeded);
        Assert.Equal(5, lines.IntervalDays(second));
    }

    [Fact]
    public void DrivingOnWarmedLanesDoesNotAllocate()
    {
        var kit = VehicleKit.Flat();
        var (west, east, depot) = kit.Corridor();
        kit.Line([west, east], kit.Buy(depot));
        kit.Days(40);
        var distance = kit.Vehicles.TotalDistance;
        AllocationAssert.NoSteadyStateAllocations(() => kit.Simulation.Tick(parallel: false), 40, "road tick on warmed lanes");
        Assert.True(kit.Vehicles.TotalDistance > distance);
    }

    [Fact]
    public void PostingMovementAndDispatchingDoNotAllocate()
    {
        var lines = new LineRegistry();
        var id = lines.Create(0, [1, 2]);
        lines.SetIntervalDays(id, 1);
        long tick = 0;
        AllocationAssert.NoSteadyStateAllocations(() =>
        {
            lines.RecordDistance(id, 1000, 4, 10);
            lines.RecordBoarding(id, 1, passengers: true);
            lines.TryDispatch(id, 1, tick++, rail: false);
        }, 200, "line statistics and dispatch");
    }
}
