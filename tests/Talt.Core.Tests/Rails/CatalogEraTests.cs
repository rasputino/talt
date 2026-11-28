using Talt.Core.Rails;
using Talt.Core.Vehicles;

namespace Talt.Core.Tests.Rails;

public sealed class CatalogEraTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DepotRulesReplaceMatchingWagonsInPlace(bool defer)
    {
        var kit = new RailKit(startYear: 1985);
        kit.Path((5, 20), (50, 20));
        var depot = kit.Depot(4, 20, Core.Roads.Dir8.E);
        var train = kit.Trains.Find(kit.Train(depot, wagons: 2, wagon: "wagon-hopper"))!;
        var front = train.Parts[0];
        var original = train.Parts[1];
        var company = kit.State.Player;
        var before = company.Balance;
        kit.Ok(new SetReplacementCommand(company.Id, true, "wagon-hopper", "wagon-deepwell", defer, false));
        kit.Trains.ReplaceInDepot(train);
        Assert.Same(front, train.Parts[0]);
        Assert.Same(original, train.Parts[1]);
        Assert.All(train.Parts.Skip(1), part => Assert.Equal(defer ? "wagon-hopper" : "wagon-deepwell", kit.Trains.ModelOf(part).Id));
        Assert.Equal(defer, before == company.Balance);
        Talt.Tests.Support.AllocationAssert.NoSteadyStateAllocations(() => kit.Trains.ReplaceInDepot(train), 100, "depot replacement evaluation");
    }

    [Fact]
    public void RetiredLocomotiveNotifiesOnceAndNewRulePreservesFreight()
    {
        var kit = new RailKit(startYear: 1985);
        kit.Path((5, 20), (50, 20));
        var depot = kit.Depot(4, 20, Core.Roads.Dir8.E);
        var train = kit.Trains.Find(kit.Train(depot, "loco-ironhorse", 1, "wagon-hopper"))!;
        var power = train.Parts[0];
        var hopper = train.Parts[1];
        hopper.Hold.Add(new(hopper.Cargo, 10, 1, 2, 3, 4, 5));
        power.PurchaseDay -= 40 * 365;
        kit.Ok(new SetReplacementCommand(0, true, "loco-ironhorse", "", false, true));
        kit.State.Year = 1991;
        kit.Trains.ReplaceInDepot(train);
        kit.Trains.ReplaceInDepot(train);
        Assert.True(kit.Trains.Notices.TryDequeue(out var notice));
        Assert.Equal(VehicleNoticeKind.RenewalDue, notice.Kind);
        Assert.False(kit.Trains.Notices.TryDequeue(out _));
        Assert.True(power.RenewalNotified);

        kit.Ok(new SetReplacementCommand(0, true, "loco-ironhorse", "loco-ridgeforge", true, true));
        kit.Trains.ReplaceInDepot(train);
        Assert.Equal("loco-ridgeforge", kit.Trains.ModelOf(power).Id);
        Assert.Equal(kit.Simulation.Date.DayNumber, power.PurchaseDay);
        Assert.False(power.RenewalNotified);
        Assert.Same(hopper, train.Parts[1]);
        Assert.Equal(10, hopper.Hold.Total);
    }

    [Fact]
    public void InsufficientFundsLeaveEveryWagonAndTheLedgerUnchanged()
    {
        var kit = new RailKit(startYear: 1985);
        kit.Path((5, 20), (50, 20));
        var depot = kit.Depot(4, 20, Core.Roads.Dir8.E);
        var train = kit.Trains.Find(kit.Train(depot, wagons: 2, wagon: "wagon-hopper"))!;
        var company = kit.State.Player;
        company.Spend(company.Balance);
        kit.Ok(new SetReplacementCommand(0, true, "wagon-hopper", "wagon-deepwell", false, false));
        kit.Trains.ReplaceInDepot(train);
        Assert.All(train.Parts.Skip(1), p => Assert.Equal("wagon-hopper", kit.Trains.ModelOf(p).Id));
        Assert.Equal(0, company.Balance);
    }

    [Fact]
    public void RosterIncludesTwelvePoweredModelsAndSeparateWagonGenerations()
    {
        var rail = RailRules.Default.Models;
        Assert.True(rail.Count(m => m.Kind == RollingStockKind.Locomotive) >= 12);
        Assert.True(rail.Count(m => m.Kind == RollingStockKind.Wagon && m.Carries(rail[RailRules.Default.FindModel("wagon-hopper")].DefaultCargo)) >= 2);
        Assert.True(VehicleRules.Default.Models.Count >= 8);
        foreach (var model in rail)
        {
            Assert.True(model.AvailableIn(model.FirstYear));
            Assert.True(model.AvailableIn(model.LastYear));
            Assert.False(model.AvailableIn(model.LastYear + 1));
        }
    }
}
