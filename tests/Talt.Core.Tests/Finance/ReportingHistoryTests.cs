using Talt.Core.Finance;
using Talt.Core.Simulation;
using Talt.Core.Stations;
using Talt.Core.Tests.Stations;
using Talt.Core.Towns;
using Talt.Tests.Support;

namespace Talt.Core.Tests.Finance;

public sealed class ReportingHistoryTests
{
    [Fact]
    public void CompletedMonthsKeepTheLatestTwelveDatesAndDistinguishMissingFromZero()
    {
        var history = new MonthlyHistory<int>(2);
        for (var i = 0; i < 13; i++)
        {
            var next = new DateOnly(1950, 2, 1).AddMonths(i);
            var values = history.Close(new GameDate(next.Year, next.Month, 1, next.DayNumber));
            values[0] = i;
            values[1] = 0;
        }

        Assert.Equal((1951, 1), history.Date(0));
        Assert.Equal((1950, 2), history.Date(11));
        Assert.True(history.TryGet(11, 0, out var first));
        Assert.Equal(1, first);
        Assert.True(history.TryGet(0, 1, out var empty));
        Assert.Equal(0, empty);
        Assert.False(history.TryGet(12, 1, out _));
        AllocationAssert.NoSteadyStateAllocations(
            () => history.Close(new GameDate(1951, 3, 1, 0))[1] = 10, 64, "12-month history rotation");
    }

    [Fact]
    public void StationAndTownCaptureTheSameMonthAsTheirGauges()
    {
        var kit = StationKit.Generated(stations: new StationSettings(80));
        var (id, _, cargo) = kit.ServeIndustry();
        kit.Days(31);
        var stations = kit.Stations;
        Assert.Equal((1950, 1), stations.HistoryDate(id, 0));
        Assert.True(stations.TryGetMonth(id, 0, cargo, out var month));
        var totals = stations.StatisticsOf(stations.GroupOf(id), cargo);
        Assert.Equal(new StationCargoMonth(stations.WaitingAt(id, cargo), totals.PickedUpLast, totals.DeliveredLast, stations.RatingAt(id, cargo)), month);
        var towns = kit.Simulation.Towns!;
        Assert.True(towns.TryGetTown(0, out var info));
        Assert.True(towns.History.TryGet(0, 0, out var townMonth));
        Assert.Equal(new TownMonth(info.PassengersGeneratedLastMonth, info.PassengersTransportedLastMonth,
            info.MailGeneratedLastMonth, info.MailTransportedLastMonth, towns.RatingOf(0, 0)), townMonth);
        AllocationAssert.NoSteadyStateAllocations(() => stations.AdvanceMonth(kit.Simulation.Date), 64, "station monthly snapshots");
    }

    [Fact]
    public void RemovingAStationPreservesTheOtherStationsHistoryAtItsNewSlot()
    {
        var kit = StationKit.Flat();
        Assert.True(kit.Road(5, 10, 50, 10).Succeeded);
        Assert.True(kit.Build("truck-bay", 6, 11).Succeeded);
        Assert.True(kit.Build("truck-bay", 30, 11).Succeeded);
        var first = kit.StationAt(6, 11);
        var second = kit.StationAt(30, 11);
        var cargo = kit.Cargo("coal");
        StationJoinAndGroupTests.Transfer(kit, second, cargo, 17);
        kit.Days(31);
        Assert.True(kit.Stations.TryGetMonth(second, 0, cargo, out var before));
        Assert.True(before.Waiting > 0);
        Assert.True(kit.Remove(6, 11).Succeeded);
        Assert.False(kit.Stations.TryGetMonth(first, 0, cargo, out _));
        Assert.True(kit.Stations.TryGetMonth(second, 0, cargo, out var after));
        Assert.Equal(before, after);
        Assert.True(kit.Build("truck-bay", 6, 11).Succeeded);
        Assert.False(kit.Stations.TryGetMonth(kit.StationAt(6, 11), 0, cargo, out _));
    }

    [Fact]
    public void CompanyPhysicalVolumesAndClosingValueShareFinancialPeriods()
    {
        var ledger = new Ledger(2, 1950, 12);
        ledger.AddDelivered(1, 7);
        ledger.SetPeriodValue(99);
        ledger.StartMonth(1951, 1);
        ledger.StartYear(1951);
        ledger.AddDelivered(1, 2);
        ledger.SetPeriodValue(-3);
        Assert.Equal(7, ledger.MonthDelivered(1, 1));
        Assert.Equal(7, ledger.YearDelivered(1, 1));
        Assert.Equal(2, ledger.YearDelivered(0, 1));
        Assert.Equal(99, ledger.MonthValue(1));
        Assert.Equal(-3, ledger.YearValue(0));
        Assert.Equal(0, ledger.MonthIncome(0));
        Assert.Equal(0, ledger.YearExpenses(0));
    }
}
