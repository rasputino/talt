using Talt.Core.Content;
using Talt.Core.Demand;

namespace Talt.Core.Tests.Demand;

/// <summary>User stories 4 and 6: what the windows and the overlay read.</summary>
public sealed class DemandQueryTests
{
    private static readonly int Passengers = GameContent.Default.CargoIndex("passengers");

    [Fact]
    public void MonthlyDemand_IsProductionTimesShare()
    {
        var world = new FakeDemandWorld().Town(0, 0, 1000).Town(10, 0, 1000);
        var rules = DestinationTablesTests.Rules();
        var tables = new DestinationTables(rules, world);
        var rule = rules.RuleOf(Passengers);
        Assert.Equal(500, tables.MonthlyDemand(rule, 0, 0, 1000)); // own town
        Assert.Equal(500, tables.MonthlyDemand(rule, 0, 1, 1000));
    }

    [Fact]
    public void TopUnservedPairs_AddBothDirections_AndDropServedDemand()
    {
        var world = new FakeDemandWorld().Town(0, 0, 1000).Town(10, 0, 1000).Town(500, 0, 1000);
        var rules = DestinationTablesTests.Rules();
        var tables = new DestinationTables(rules, world);
        var rule = rules.RuleOf(Passengers);
        var pairs = new List<DemandPair>();
        tables.TopUnservedPairs(rule, [1000, 1000, 1000], 10, pairs);
        Assert.Equal((0, 1), (pairs[0].TownA, pairs[0].TownB)); // the closest pair has the most demand
        var before = pairs[0].Unserved;

        // Serve 0 → 1 fully for a month: the pair's unserved demand falls.
        var index = tables.Destinations(rule, 0).IndexOf(1);
        tables.RecordMoved(rule, 0, index, tables.MonthlyDemand(rule, 0, index, 1000));
        tables.AdvanceMonth();
        tables.TopUnservedPairs(rule, [1000, 1000, 1000], 10, pairs);
        var pair = pairs.Single(p => p.TownA == 0 && p.TownB == 1);
        Assert.True(pair.Unserved < before);
        tables.TopUnservedPairs(rule, [1000, 1000, 1000], 1, pairs);
        Assert.Single(pairs);
    }
}
