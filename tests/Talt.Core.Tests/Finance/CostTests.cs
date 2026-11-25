using Talt.Core.Construction;
using Talt.Core.Finance;
using Talt.Core.Roads;
using Talt.Core.Tests.Stations;

namespace Talt.Core.Tests.Finance;

public sealed class CostTests
{
    private static int[] ScanOwnedRoads(ConstructionState state, int companyId)
    {
        var counts = new int[state.RoadRules.Types.Count];
        for (var tile = 0; tile < state.Map.Width * state.Map.Depth; tile++)
        {
            if (state.Objects.OwnerAt(tile).IsCompany(companyId) && state.Roads.HasRoad(tile))
            {
                counts[state.Roads.TypeAt(tile)]++;
            }
        }

        return counts;
    }

    private static void AssertCountsMatchScan(ConstructionState state)
    {
        var scan = ScanOwnedRoads(state, Company.PlayerId);
        for (var type = 0; type < scan.Length; type++)
        {
            Assert.Equal(scan[type], state.OwnedRoadTiles(Company.PlayerId, type));
        }
    }

    [Fact]
    public void OwnedRoadCounts_FollowBuildingRemovingAndUndo()
    {
        var kit = FinanceKit.Flat();
        var paved = kit.State.RoadRules.FindType("paved");
        Assert.True(kit.Road(5, 10, 50, 10).Succeeded);
        Assert.Equal(46, kit.State.OwnedRoadTiles(Company.PlayerId, paved));
        AssertCountsMatchScan(kit.State);

        Assert.True(kit.Road(20, 5, 20, 30, "dirt").Succeeded);
        AssertCountsMatchScan(kit.State);
        Assert.True(kit.Run(new UndoConstructionCommand(Company.PlayerId)).Succeeded);
        AssertCountsMatchScan(kit.State);
        Assert.Equal(46, kit.State.OwnedRoadTiles(Company.PlayerId, paved));

        Assert.True(kit.Run(new RemoveRoadCommand(Company.PlayerId, 30, 10, 50, 10)).Succeeded);
        AssertCountsMatchScan(kit.State);
        Assert.True(kit.Build("bus-bay", 10, 11).Succeeded);
        AssertCountsMatchScan(kit.State);
    }

    [Fact]
    public void TownRoads_CostNothing_AndCountsStayRightAsTownsGrow()
    {
        var kit = StationKit.Generated(extraMoney: false);
        Assert.Equal(0, kit.Finance().YearlyMaintenance(Company.PlayerId));
        kit.EndMonths(3);
        AssertCountsMatchScan(kit.State);
        Assert.Equal(0, kit.YearSlot(LedgerCategory.PropertyMaintenance));
    }

    [Fact]
    public void Maintenance_IsATwelfthEachMonth_AndExactOverAYear()
    {
        var kit = FinanceKit.Flat();
        kit.Road(5, 10, 50, 10); // 46 paved tiles × 60.
        kit.Build("truck-bay", 6, 11); // 400 (the bay tile has a road piece of its own type too).
        var yearly = kit.Finance().YearlyMaintenance(Company.PlayerId);
        var roads = kit.Finance().Rules.RoadMaintenance;
        long expected = 400;
        for (var t = 0; t < roads.Count; t++)
        {
            expected += kit.State.OwnedRoadTiles(Company.PlayerId, t) * roads[t];
        }

        Assert.Equal(expected, yearly);
        kit.EndMonth();
        Assert.Equal(-(yearly / 12), kit.Ledger().Month(1, Ledger.Slot(LedgerCategory.PropertyMaintenance)));
        kit.EndMonths(11);
        Assert.Equal(-yearly, kit.Ledger().Year(1, Ledger.Slot(LedgerCategory.PropertyMaintenance)));
    }

    [Fact]
    public void Warehouses_CostTheirMaintenance()
    {
        var kit = FinanceKit.Flat();
        kit.Road(5, 10, 50, 10);
        Assert.True(kit.Warehouse("small", 30, 11).Succeeded);
        var withWarehouse = kit.Finance().YearlyMaintenance(Company.PlayerId);
        Assert.True(kit.Remove(30, 11).Succeeded);
        Assert.Equal(1800, withWarehouse - kit.Finance().YearlyMaintenance(Company.PlayerId));
    }

    [Fact]
    public void RunningCosts_AreChargedDaily_AndExactOverAYear()
    {
        var kit = FinanceKit.Flat();
        var a = kit.Finance().RunningCosts.Register(Company.PlayerId, 3650, 20_000);
        kit.Finance().RunningCosts.Register(Company.PlayerId, 1000, 5_000);
        Assert.Equal(4650, kit.Finance().RunningCosts.YearlyTotal(Company.PlayerId));
        kit.Days(1);
        Assert.Equal(-12, kit.Ledger().Month(0, Ledger.Slot(LedgerCategory.RunningCosts))); // (3650 + 1000) / 365 = 12.7.
        kit.EndMonths(12);
        Assert.Equal(-4650, kit.Ledger().Year(1, Ledger.Slot(LedgerCategory.RunningCosts)));

        kit.Finance().RunningCosts.Unregister(a);
        Assert.Equal(1000, kit.Finance().RunningCosts.YearlyTotal(Company.PlayerId));
        Assert.Throws<ArgumentOutOfRangeException>(() => kit.Finance().RunningCosts.Unregister(a));
        Assert.Equal(a, kit.Finance().RunningCosts.Register(Company.PlayerId, 10, 0)); // Lowest free handle reused.
    }

    [Fact]
    public void Costs_MayTakeCashBelowZero_ButConstructionStillNeedsCash()
    {
        var kit = FinanceKit.Flat(balance: 100);
        kit.Finance().RunningCosts.Register(Company.PlayerId, 36_500, 0);
        kit.Days(10);
        Assert.True(kit.Player().Balance < 0);
        Assert.Equal(ConstructionReasons.NotEnoughMoney, kit.Road(5, 10, 8, 10).ReasonCode);
    }

    [Fact]
    public void UndoRefund_IsBookedAsConstruction()
    {
        var kit = FinanceKit.Flat();
        kit.Road(5, 10, 50, 10);
        var spent = kit.YearSlot(LedgerCategory.Construction);
        Assert.True(spent < 0);
        kit.Run(new UndoConstructionCommand(Company.PlayerId));
        Assert.Equal(0, kit.YearSlot(LedgerCategory.Construction));
        Assert.Equal(1_000_000, kit.Player().Balance);
    }
}
