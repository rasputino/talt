using Talt.Core.Content;
using Talt.Core.Industries;

namespace Talt.Core.Tests.Industries;

public class IndustryProductionTests
{
    [Fact]
    public void OneMonthProducesExactlyTheMonthlyRate()
    {
        var (content, fleet) = Producer(perMonth: 100, capacity: 10_000);
        var coal = content.CargoIndex("test-cargo");

        AdvanceDays(fleet, new DateOnly(1950, 1, 1), 31);

        Assert.Equal(100, fleet.AvailableAt(0, coal));
    }

    [Fact]
    public void LeapFebruaryStillProducesExactlyTheMonthlyRate()
    {
        var (content, fleet) = Producer(perMonth: 90, capacity: 10_000);
        var coal = content.CargoIndex("test-cargo");

        AdvanceDays(fleet, new DateOnly(1952, 2, 1), 29);

        Assert.Equal(90, fleet.AvailableAt(0, coal));
    }

    [Fact]
    public void ProductionStopsAtTheStorageCap()
    {
        var (content, fleet) = Producer(perMonth: 100, capacity: 40);
        var coal = content.CargoIndex("test-cargo");

        AdvanceDays(fleet, new DateOnly(1950, 1, 1), 40);

        Assert.Equal(40, fleet.AvailableAt(0, coal));
    }

    [Fact]
    public void ZeroProductionAddsNothing()
    {
        var (content, fleet) = Producer(perMonth: 0, capacity: 40);
        var coal = content.CargoIndex("test-cargo");

        AdvanceDays(fleet, new DateOnly(1950, 1, 1), 31);

        Assert.Equal(0, fleet.AvailableAt(0, coal));
    }

    [Fact]
    public void MonthlyStatisticsRotate()
    {
        var (content, fleet) = Consumer(economy: """{"driftPerMille": 0}""");
        var cargo = content.CargoIndex("test-cargo");
        var id = fleet.IdAt(0);

        var delivered = fleet.Deliver(id, cargo, 7);
        Assert.Equal(7, delivered.Accepted);
        fleet.AdvanceMonth(IndustryTestData.FirstDay(1950, 2));
        Assert.True(fleet.TryGetIndustry(id, out var info));
        var row = info.LastMonth.Single(entry => entry.CargoId == "test-cargo");
        Assert.Equal(7, row.Accepted);
        Assert.Equal(0, row.Shipped);

        fleet.AdvanceMonth(IndustryTestData.FirstDay(1950, 3));
        Assert.True(fleet.TryGetIndustry(id, out info));
        row = info.LastMonth.Single(entry => entry.CargoId == "test-cargo");
        Assert.Equal(0, row.Accepted);
    }

    [Fact]
    public void ShippedAmountsAreReportedForLastMonth()
    {
        var (content, fleet) = Producer(perMonth: 100, capacity: 1000);
        var cargo = content.CargoIndex("test-cargo");
        var id = fleet.IdAt(0);
        AdvanceDays(fleet, new DateOnly(1950, 1, 1), 31);

        Assert.Equal(3, fleet.Take(id, cargo, 3));
        fleet.AdvanceMonth(IndustryTestData.FirstDay(1950, 2));

        Assert.True(fleet.TryGetIndustry(id, out var info));
        Assert.Equal(3, info.LastMonth.Single(entry => entry.CargoId == "test-cargo").Shipped);
    }

    private static (GameContent Content, IndustryFleet Fleet) Producer(int perMonth, int capacity)
    {
        var industry = IndustryTestData.MinimalIndustry.Replace(
            "\"weight\": 1,",
            $"\"weight\": 1, \"production\": [{{ \"cargo\": \"test-cargo\", \"perMonth\": {perMonth} }}], \"stock\": [{{ \"cargo\": \"test-cargo\", \"capacity\": {capacity} }}],",
            StringComparison.Ordinal);
        var content = IndustryTestData.Content(IndustryTestData.CargoFile(IndustryTestData.RawCargo), IndustryTestData.IndustryFile(industry));
        return (content, IndustryTestData.Fleet(content));
    }

    private static (GameContent Content, IndustryFleet Fleet) Consumer(string economy)
    {
        var industry = IndustryTestData.MinimalIndustry.Replace(
            "\"weight\": 1,",
            "\"weight\": 1, \"accepts\": [\"test-cargo\"],",
            StringComparison.Ordinal);
        var content = IndustryTestData.Content(
            IndustryTestData.CargoFile(IndustryTestData.RawCargo),
            IndustryTestData.IndustryFile(industry, economy));
        return (content, IndustryTestData.Fleet(content));
    }

    private static void AdvanceDays(IndustryFleet fleet, DateOnly first, int days)
    {
        for (var i = 0; i < days; i++)
        {
            fleet.AdvanceDay(IndustryTestData.DayAfter(first.AddDays(i)));
        }
    }
}
