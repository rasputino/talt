using Talt.Core.Content;
using Talt.Core.Industries;

namespace Talt.Core.Tests.Industries;

public class IndustryDeliveryTests
{
    [Fact]
    public void SawmillConvertsWholeBatchesImmediately()
    {
        var (content, fleet) = Sawmill();
        var wood = content.CargoIndex("test-cargo");
        var lumber = content.CargoIndex("lumber");
        var id = fleet.IdAt(0);

        var result = fleet.Deliver(id, wood, 4);

        Assert.Equal(DeliveryOutcome.Accepted, result.Outcome);
        Assert.Equal(4, result.Accepted);
        Assert.Equal("lumber", result.ConvertedCargoId);
        Assert.Equal(3, result.ConvertedAmount);
        Assert.Equal(3, fleet.AvailableAt(0, lumber));
        Assert.Equal(0, fleet.PendingInputAt(0, wood));
    }

    [Fact]
    public void PartialBatchesWaitForTheNextDelivery()
    {
        var (content, fleet) = Sawmill();
        var wood = content.CargoIndex("test-cargo");
        var lumber = content.CargoIndex("lumber");
        var id = fleet.IdAt(0);

        fleet.Deliver(id, wood, 5);
        Assert.Equal(1, fleet.PendingInputAt(0, wood));
        Assert.Equal(3, fleet.AvailableAt(0, lumber));

        fleet.Deliver(id, wood, 3);
        Assert.Equal(0, fleet.PendingInputAt(0, wood));
        Assert.Equal(6, fleet.AvailableAt(0, lumber));
    }

    [Fact]
    public void CargoTheIndustryDoesNotAcceptIsRefused()
    {
        var (content, fleet) = Sawmill();
        var lumber = content.CargoIndex("lumber");

        var result = fleet.Deliver(fleet.IdAt(0), lumber, 10);

        Assert.Equal(DeliveryOutcome.Refused, result.Outcome);
        Assert.Equal(0, result.Accepted);
        Assert.Equal(10, result.Refused);
    }

    [Fact]
    public void DeliveryIsRefusedWhenTheInputStoreIsFull()
    {
        var (content, fleet) = Sawmill(woodCapacity: 4);
        var wood = content.CargoIndex("test-cargo");
        var id = fleet.IdAt(0);

        var result = fleet.Deliver(id, wood, 8);

        Assert.Equal(DeliveryOutcome.PartiallyAccepted, result.Outcome);
        Assert.Equal(4, result.Accepted);
        Assert.Equal(4, result.Refused);
    }

    [Fact]
    public void FullOutputStorePostponesProcessing()
    {
        var (content, fleet) = Sawmill(lumberCapacity: 3);
        var wood = content.CargoIndex("test-cargo");
        var lumber = content.CargoIndex("lumber");
        var id = fleet.IdAt(0);

        fleet.Deliver(id, wood, 4);
        var second = fleet.Deliver(id, wood, 4);

        Assert.Equal(3, fleet.AvailableAt(0, lumber));
        Assert.Equal(4, fleet.PendingInputAt(0, wood));
        Assert.Equal(0, second.ConvertedAmount);
    }

    [Fact]
    public void AcceptedCargoWithoutARecipeIsConsumed()
    {
        var industry = IndustryTestData.MinimalIndustry.Replace(
            "\"weight\": 1,",
            "\"weight\": 1, \"accepts\": [\"test-cargo\"],",
            StringComparison.Ordinal);
        var content = IndustryTestData.Content(
            IndustryTestData.CargoFile(IndustryTestData.RawCargo),
            IndustryTestData.IndustryFile(industry));
        var fleet = IndustryTestData.Fleet(content);
        var cargo = content.CargoIndex("test-cargo");

        var result = fleet.Deliver(fleet.IdAt(0), cargo, 10);

        Assert.Equal(DeliveryOutcome.Accepted, result.Outcome);
        Assert.Equal(10, result.Consumed);
        Assert.Equal(0, fleet.AvailableAt(0, cargo));
        Assert.Equal(10, result.Accepted);
    }

    [Fact]
    public void TakeNeverRemovesMoreThanIsAvailable()
    {
        var industry = IndustryTestData.MinimalIndustry.Replace(
            "\"weight\": 1,",
            "\"weight\": 1, \"production\": [{ \"cargo\": \"test-cargo\", \"perMonth\": 100 }], \"stock\": [{ \"cargo\": \"test-cargo\", \"capacity\": 1000 }],",
            StringComparison.Ordinal);
        var content = IndustryTestData.Content(
            IndustryTestData.CargoFile(IndustryTestData.RawCargo),
            IndustryTestData.IndustryFile(industry));
        var fleet = IndustryTestData.Fleet(content);
        var cargo = content.CargoIndex("test-cargo");

        for (var day = 0; day < 31; day++)
        {
            fleet.AdvanceDay(IndustryTestData.DayAfter(new DateOnly(1950, 1, 1).AddDays(day)));
        }

        Assert.Equal(100, fleet.AvailableAt(0, cargo));
        Assert.Equal(100, fleet.Take(fleet.IdAt(0), cargo, 100));
        Assert.Equal(0, fleet.Take(fleet.IdAt(0), cargo, 100));
        Assert.Equal(0, fleet.AvailableAt(0, cargo));
    }

    private static (GameContent Content, IndustryFleet Fleet) Sawmill(int woodCapacity = 400, int lumberCapacity = 600)
    {
        var lumberCargo = IndustryTestData.RawCargo.Replace("\"id\": \"test-cargo\"", "\"id\": \"lumber\"", StringComparison.Ordinal);
        var industry = IndustryTestData.MinimalIndustry.Replace(
            "\"weight\": 1,",
            $"\"weight\": 1, \"accepts\": [\"test-cargo\"], \"recipes\": [{{ \"input\": \"test-cargo\", \"inputAmount\": 4, \"output\": \"lumber\", \"outputAmount\": 3 }}], \"stock\": [{{ \"cargo\": \"test-cargo\", \"capacity\": {woodCapacity} }}, {{ \"cargo\": \"lumber\", \"capacity\": {lumberCapacity} }}],",
            StringComparison.Ordinal);
        var content = IndustryTestData.Content(
            IndustryTestData.CargoFile($"{IndustryTestData.RawCargo},{lumberCargo}"),
            IndustryTestData.IndustryFile(industry));
        return (content, IndustryTestData.Fleet(content));
    }
}
