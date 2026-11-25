using Talt.Core.Construction;
using Talt.Core.Finance;
using Talt.Core.Tests.Stations;

namespace Talt.Core.Tests.Finance;

/// <summary>SC-002: the ledger always equals the change of cash, loan movements apart; company value follows its formula.</summary>
public sealed class LedgerCashTests
{
    [Fact]
    public void OverYears_YearTotalsEqualTheChangeOfCash()
    {
        var kit = new StationKit(Talt.Core.Simulation.GameSimulation.Create(new Talt.Core.Scenarios.ScenarioSettings(
            Seed: 10, StartYear: 1950, MapSizeTiles: 256, VehicleCount: 0,
            Map: new Talt.Core.Maps.MapSettings(256, 256, Seed: 10, Roughness: 30, MaxHeight: 40, WaterAmount: 15, SeaLevel: 6),
            Stations: new Talt.Core.Stations.StationSettings(60),
            Finance: new FinanceSettings(DebugServicePays: true, Inflation: true))));
        var startCash = kit.Player().Balance;
        kit.ServeIndustry();
        kit.ServeTown();
        kit.Loan(LoanMode.Borrow);
        kit.Loan(LoanMode.Borrow);
        kit.Finance().RunningCosts.Register(Company.PlayerId, 7_777, 1_000);

        kit.EndMonths(30);
        kit.Loan(LoanMode.Repay);

        long ledger = 0;
        for (var back = 0; back < kit.Ledger().YearCount; back++)
        {
            ledger += kit.Ledger().YearTotal(back);
        }

        Assert.Equal(kit.Player().Balance - startCash - kit.Player().Loan, ledger);
        Assert.True(kit.Ledger().YearIncome(1) > 0);
        Assert.True(kit.Ledger().YearExpenses(1) < 0);
    }

    [Fact]
    public void CompanyValue_IsCashMinusLoanPlusProperty()
    {
        var kit = FinanceKit.Flat();
        kit.Road(5, 10, 50, 10);
        kit.Build("truck-bay", 6, 11);
        kit.Finance().RunningCosts.Register(Company.PlayerId, 0, 7_000);
        kit.Loan(LoanMode.Borrow);
        kit.EndMonth();

        var paved = kit.State.RoadRules.FindType("paved");
        long build = 0;
        for (var t = 0; t < kit.State.RoadRules.Types.Count; t++)
        {
            build += kit.State.OwnedRoadTiles(Company.PlayerId, t) * kit.State.RoadRules.Types[t].BuildCost;
        }

        build += kit.Stations.Rules.Pieces[kit.Stations.Rules.FindPiece("truck-bay")].Cost;
        Assert.True(kit.State.OwnedRoadTiles(Company.PlayerId, paved) > 0);
        var expected = kit.Player().Balance - kit.Player().Loan + (build / 2) + 7_000;
        Assert.Equal(expected, kit.Player().Value);
        Assert.Equal(expected, kit.Finance().Report(Company.PlayerId).Value);
    }
}
