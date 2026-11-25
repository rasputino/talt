using Talt.Core.Construction;
using Talt.Core.Finance;
using Talt.Core.Roads;

namespace Talt.Core.Tests.Finance;

public sealed class InflationTests
{
    [Fact]
    public void WithoutInflation_PricesNeverChange()
    {
        var kit = FinanceKit.Flat();
        kit.EndMonths(30);
        Assert.Equal(PaymentCalculator.LevelOne, kit.Finance().CostLevel);
        Assert.Equal(PaymentCalculator.LevelOne, kit.Finance().PaymentLevel);
        Assert.Equal(500_000, kit.Finance().MaxLoan);
        Assert.False(kit.Finance().IsActive);
    }

    [Fact]
    public void Levels_GrowMonthly_ByTheYearlyRatesCompounded()
    {
        var kit = FinanceKit.Flat(new FinanceSettings(Inflation: true));
        kit.EndMonth();
        Assert.Equal(1_000_000 + (1_000_000L * 200 / 120_000), kit.Finance().CostLevel);
        Assert.Equal(1_000_000 + (1_000_000L * 150 / 120_000), kit.Finance().PaymentLevel);
        kit.EndMonths(11);
        Assert.InRange(kit.Finance().CostLevel, 1_020_100, 1_020_250); // ≈ 1.02018 after 12 months.
        Assert.Equal(kit.Finance().CostLevel, kit.State.CostLevel);
    }

    [Fact]
    public void Inflation_ScalesConstructionLoanAndPayments()
    {
        var kit = FinanceKit.Flat(new FinanceSettings(Inflation: true));
        kit.EndMonths(120);
        var level = kit.Finance().CostLevel;
        Assert.True(level > 1_200_000);

        var plan = new ConstructionPlan();
        var command = new BuildRoadCommand(Company.PlayerId, "paved", 5, 10, 14, 10);
        Assert.True(command.Preview(kit.State, new ConstructionPlanner(), plan).Succeeded);
        Assert.Equal(Money.MulDiv(10 * 400, level, PaymentCalculator.LevelOne), plan.Cost);
        Assert.True(kit.Finance().MaxLoan > 500_000);
        Assert.Equal(0, kit.Finance().MaxLoan % kit.Finance().LoanStep);
        Assert.True(kit.Finance().Quote(kit.Cargo("coal"), 30, 40, 1).Amount > 2880);
    }

    [Fact]
    public void Inflation_StopsAfterTheDataYears()
    {
        var kit = FinanceKit.Flat(new FinanceSettings(Inflation: true));
        var years = kit.Finance().Rules.InflationYears;
        for (var i = 0; i < years * 12; i++)
        {
            kit.Finance().AdvanceMonth(kit.Simulation.Date); // Direct month ends: 1 200 of them.
        }

        var level = kit.Finance().CostLevel;
        Assert.Equal(years * 12, kit.Finance().InflationMonths);
        kit.Finance().AdvanceMonth(kit.Simulation.Date);
        Assert.Equal(level, kit.Finance().CostLevel);
    }
}
