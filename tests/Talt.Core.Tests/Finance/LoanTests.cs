using System.Text.Json;
using Talt.Core.Commands;
using Talt.Core.Construction;
using Talt.Core.Finance;

namespace Talt.Core.Tests.Finance;

public sealed class LoanTests
{
    [Fact]
    public void NewGame_StartsWithTheStartingCash_AndNoLoan()
    {
        var kit = FinanceKit.Flat(balance: 500_000);
        Assert.Equal(500_000, kit.Player().Balance);
        Assert.Equal(0, kit.Player().Loan);
        var report = kit.Finance().Report(Company.PlayerId);
        Assert.Equal(500_000, report.MaxLoan);
        Assert.Equal(10_000, report.LoanStep);
    }

    [Fact]
    public void Borrow_AndRepay_MoveCashAndLoanByOneStep_WithoutLedgerEntries()
    {
        var kit = FinanceKit.Flat(balance: 500_000);
        Assert.True(kit.Loan(LoanMode.Borrow).Succeeded);
        Assert.Equal(510_000, kit.Player().Balance);
        Assert.Equal(10_000, kit.Player().Loan);
        Assert.True(kit.Loan(LoanMode.Repay).Succeeded);
        Assert.Equal(500_000, kit.Player().Balance);
        Assert.Equal(0, kit.Player().Loan);
        Assert.False(kit.Ledger().HasEntries);
    }

    [Fact]
    public void BorrowMax_StopsAtTheMaximum_AndMoreIsRefused()
    {
        var kit = FinanceKit.Flat(balance: 0);
        Assert.True(kit.Loan(LoanMode.BorrowMax).Succeeded);
        Assert.Equal(500_000, kit.Player().Loan);
        Assert.Equal(500_000, kit.Player().Balance);
        var refused = kit.Loan(LoanMode.Borrow);
        Assert.Equal(ConstructionReasons.MaxLoan, refused.ReasonCode);
        Assert.Equal(500_000, kit.Player().Loan);
    }

    [Fact]
    public void Repay_WithoutLoan_OrCash_IsRefused()
    {
        var kit = FinanceKit.Flat(balance: 5_000);
        Assert.Equal(ConstructionReasons.NoLoan, kit.Loan(LoanMode.Repay).ReasonCode);
        Assert.Equal(ConstructionReasons.NoLoan, kit.Loan(LoanMode.RepayMax).ReasonCode);
        kit.Loan(LoanMode.Borrow);
        kit.State.Player.Spend(14_000); // Leaves 1 000 of cash, 10 000 of loan.
        Assert.Equal(ConstructionReasons.NotEnoughCash, kit.Loan(LoanMode.Repay).ReasonCode);
        Assert.Equal(ConstructionReasons.NotEnoughCash, kit.Loan(LoanMode.RepayMax).ReasonCode);
    }

    [Fact]
    public void RepayMax_RepaysTheStepsTheCashCovers()
    {
        var kit = FinanceKit.Flat(balance: 0);
        kit.Loan(LoanMode.BorrowMax);
        kit.State.Player.Spend(465_000); // Cash 35 000.
        Assert.True(kit.Loan(LoanMode.RepayMax).Succeeded);
        Assert.Equal(470_000, kit.Player().Loan);
        Assert.Equal(5_000, kit.Player().Balance);
    }

    [Fact]
    public void Interest_IsChargedMonthly_AndExactOverAYear()
    {
        var kit = FinanceKit.Flat(balance: 0);
        kit.Loan(LoanMode.Borrow);
        kit.Loan(LoanMode.Borrow);
        kit.Loan(LoanMode.Borrow); // 30 000 at 5 % = 1 500 a year, 125 a month.
        kit.EndMonth();
        Assert.Equal(-125, kit.Ledger().Month(1, Ledger.Slot(LedgerCategory.LoanInterest)));
        kit.EndMonths(11);
        Assert.Equal(-1500, kit.Ledger().Year(1, Ledger.Slot(LedgerCategory.LoanInterest)) + kit.Ledger().Year(0, Ledger.Slot(LedgerCategory.LoanInterest)));
        Assert.Equal(30_000 - 1500, kit.Player().Balance);
    }

    [Fact]
    public void InterestWithRemainders_SumsExactlyOverTheYear()
    {
        var kit = FinanceKit.Flat(balance: 0);
        kit.Loan(LoanMode.Borrow); // 10 000 at 5 % = 500 a year: 41.67 a month.
        kit.EndMonths(12);
        long total = 0;
        for (var back = 1; back <= 12; back++)
        {
            total += kit.Ledger().Month(back, Ledger.Slot(LedgerCategory.LoanInterest));
        }

        Assert.Equal(-500, total);
    }

    [Fact]
    public void Command_RoundTripsThroughJson()
    {
        foreach (var mode in Enum.GetValues<LoanMode>())
        {
            var command = new ChangeLoanCommand(0, mode);
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                CommandSerializer.Write(writer, command);
            }

            using var document = JsonDocument.Parse(stream.ToArray());
            Assert.Equal(command, CommandSerializer.Read(document.RootElement));
        }
    }

    [Fact]
    public void MaplessGame_RefusesLoans()
    {
        var simulation = Talt.Core.Simulation.GameSimulation.Create(new Talt.Core.Scenarios.ScenarioSettings(VehicleCount: 0, MapSizeTiles: 64, RoadSpacing: 8));
        Assert.Null(simulation.Finance);
        Assert.Equal(ConstructionReasons.NoMap, new ChangeLoanCommand(0, LoanMode.Borrow).Validate(simulation).ReasonCode);
    }
}
