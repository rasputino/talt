using Talt.Core.Content;
using Talt.Core.Demand;

namespace Talt.Core.Tests.Demand;

public sealed class DestinationTablesTests
{
    private static readonly int Passengers = GameContent.Default.CargoIndex("passengers");
    private static readonly int Goods = GameContent.Default.CargoIndex("goods");

    internal static DemandRules Rules(int passengerPopulationExponent = 1, int maxDestinations = 32, int range = 1500)
    {
        var json = DemandRules.ReadResource()
            .Replace("\"id\": \"passengers\", \"source\": \"buildings\", \"ownTownPerMille\": 500, \"populationExponent\": 1",
                $"\"id\": \"passengers\", \"source\": \"buildings\", \"ownTownPerMille\": 500, \"populationExponent\": {passengerPopulationExponent}", StringComparison.Ordinal)
            .Replace("\"maxDestinations\": 32", $"\"maxDestinations\": {maxDestinations}", StringComparison.Ordinal)
            .Replace("\"rangeTiles\": 1500", $"\"rangeTiles\": {range}", StringComparison.Ordinal);
        return DemandRules.FromJson(json, GameContent.Default);
    }

    [Fact]
    public void DevelopersExample_DistanceOnly_SplitsTheOtherHalfByInverseDistance()
    {
        // Alicante, Barcelona, Cádiz and Donostia at 10, 8, 5 and 2 tiles: 5.4 / 6.8 / 10.8 / 27 % (clarification Q1).
        var world = new FakeDemandWorld().Town(0, 0, 1000).Town(10, 0, 500).Town(0, 8, 500).Town(5, 0, 500).Town(0, 2, 500);
        var rules = Rules(passengerPopulationExponent: 0);
        var tables = new DestinationTables(rules, world);
        var rule = rules.RuleOf(Passengers);
        var dest = tables.Destinations(rule, 0).ToArray();
        var shares = tables.Shares(rule, 0).ToArray();
        Assert.Equal([0, 4, 3, 2, 1], dest); // own town first, then by weight
        Assert.Equal(500_000, shares[0]);
        Assert.InRange(shares[1], 270_270 - 2, 270_270 + 2);
        Assert.InRange(shares[2], 108_108 - 2, 108_108 + 2);
        Assert.InRange(shares[3], 67_567 - 2, 67_567 + 2);
        Assert.InRange(shares[4], 54_054 - 2, 54_054 + 2);
        Assert.Equal(Apportion.ShareScale, shares.Sum());
    }

    [Fact]
    public void BiggerTowns_AttractMore_AtTheSameDistance()
    {
        var world = new FakeDemandWorld().Town(0, 0, 1000).Town(100, 0, 4000).Town(0, 100, 2000);
        var rules = Rules();
        var tables = new DestinationTables(rules, world);
        var rule = rules.RuleOf(Passengers);
        var shares = tables.Shares(rule, 0);
        Assert.Equal(1, tables.Destinations(rule, 0)[1]);
        Assert.InRange(shares[1], (2 * shares[2]) - 2, (2 * shares[2]) + 2);
    }

    [Fact]
    public void TopN_AndRange_LimitTheDestinations()
    {
        var world = new FakeDemandWorld().Town(0, 0, 1000);
        for (var i = 1; i <= 10; i++)
        {
            world.Town(i * 10, 0, 1000);
        }

        world.Town(5000, 0, 100_000); // out of range
        var rules = Rules(maxDestinations: 4, range: 1000);
        var tables = new DestinationTables(rules, world);
        var rule = rules.RuleOf(Passengers);
        Assert.Equal([0, 1, 2, 3, 4], tables.Destinations(rule, 0).ToArray());
    }

    [Fact]
    public void ATownAlone_KeepsAllItsCargo()
    {
        var world = new FakeDemandWorld().Town(0, 0, 1000);
        var tables = new DestinationTables(Rules(), world);
        var rule = Rules().RuleOf(Passengers);
        Assert.Equal([0], tables.Destinations(rule, 0).ToArray());
        Assert.Equal([Apportion.ShareScale], tables.Shares(rule, 0).ToArray());
    }

    [Fact]
    public void Goods_GoOnlyToTownsThatAcceptThem_FromTheIndustry()
    {
        var world = new FakeDemandWorld().Town(0, 0, 1000, acceptsGoods: false).Town(200, 0, 1000).Town(400, 0, 1000)
            .Industry(77, 10, 0, Goods);
        var rules = Rules();
        var tables = new DestinationTables(rules, world);
        var rule = rules.RuleOf(Goods);
        var row = tables.RowOf(rule, 77);
        Assert.Equal(0, row);
        Assert.Equal([1, 2], tables.Destinations(rule, row).ToArray());
        Assert.Equal(Apportion.ShareScale, tables.Shares(rule, row).ToArray().Sum());
    }

    [Fact]
    public void DailySlices_RefreshEveryTownWithinAMonth()
    {
        var world = new FakeDemandWorld();
        for (var i = 0; i < 70; i++)
        {
            world.Town(i * 20, 0, 1000);
        }

        var rules = Rules();
        var tables = new DestinationTables(rules, world);
        var rule = rules.RuleOf(Passengers);
        for (var i = 0; i < 70; i++)
        {
            world.SetPopulation(i, i == 69 ? 50_000 : 1000);
        }

        for (var day = 0; day < 30; day++)
        {
            tables.AdvanceDay();
        }

        for (var town = 0; town < 69; town++)
        {
            var dest = tables.Destinations(rule, town);
            if ((town * 20) + 1500 >= 69 * 20)
            {
                Assert.Contains(69, dest.ToArray());
            }
        }
    }

    [Fact]
    public void Split_HandsOutExactly_AndRecordsStatistics()
    {
        var world = new FakeDemandWorld().Town(0, 0, 1000).Town(10, 0, 1000).Town(20, 0, 1000);
        var rules = Rules();
        var tables = new DestinationTables(rules, world);
        var rule = rules.RuleOf(Passengers);
        Span<int> units = stackalloc int[tables.Width];
        var total = new int[3];
        for (var i = 0; i < 100; i++)
        {
            var n = tables.Split(rule, 0, 3, units);
            var sum = 0;
            for (var k = 0; k < n; k++)
            {
                sum += units[k];
                total[k] += units[k];
            }

            Assert.Equal(3, sum);
        }

        Assert.InRange(total[0], 149, 151);
        tables.RecordMoved(rule, 0, 1, 5);
        tables.RecordUnserved(rule, 0, 2, 7);
        tables.AdvanceMonth();
        Assert.Equal((5, 0, 0, 0), tables.Statistics(rule, 0, 1));
        Assert.Equal((0, 7, 0, 0), tables.Statistics(rule, 0, 2));
    }
}
