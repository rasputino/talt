using Talt.Core.Commands;
using Talt.Core.Construction;
using Talt.Core.Finance;
using Talt.Core.Tests.Stations;

namespace Talt.Core.Tests.Finance;

public sealed class BankruptcyTests
{
    /// <summary>A company at the maximum loan with negative cash.</summary>
    private static StationKit Bankrupt(FinanceSettings? settings = null)
    {
        var kit = FinanceKit.Flat(settings, balance: 0);
        kit.Loan(LoanMode.BorrowMax);
        kit.State.Player.Spend(600_000); // Outside commands: cash −100 000.
        kit.Finance().Events.Clear();
        return kit;
    }

    private static List<MoneyEventKind> Drain(StationKit kit)
    {
        var kinds = new List<MoneyEventKind>();
        while (kit.Finance().Events.TryDequeue(out var e))
        {
            kinds.Add(e.Kind);
        }

        return kinds;
    }

    [Fact]
    public void Streak_WarnsAtThree_AndEndsTheGameAtSix()
    {
        var kit = Bankrupt();
        kit.EndMonths(2);
        Assert.Equal(2, kit.Player().BankruptMonths);
        Assert.Empty(Drain(kit));
        kit.EndMonth();
        Assert.Equal([MoneyEventKind.BankruptcyWarning], Drain(kit));
        kit.EndMonths(2);
        Assert.False(kit.Player().IsGameOver);
        kit.EndMonth();
        Assert.True(kit.Player().IsGameOver);
        Assert.Contains(MoneyEventKind.GameOver, Drain(kit));
        Assert.True(kit.Finance().IsGameOver(Company.PlayerId));
    }

    [Fact]
    public void AfterGameOver_EveryCommandIsRefused()
    {
        var kit = Bankrupt();
        kit.EndMonths(6);
        Assert.Equal(ConstructionReasons.GameOver, kit.Loan(LoanMode.Repay).ReasonCode);
        Assert.Equal(ConstructionReasons.GameOver, kit.Road(5, 10, 8, 10).ReasonCode);
    }

    [Fact]
    public void PositiveCashOrALoanBelowTheMaximum_ResetsTheStreak()
    {
        var kit = Bankrupt();
        kit.EndMonths(2);
        kit.State.Player.Refund(200_000); // Cash positive at the next month end.
        kit.EndMonth();
        Assert.Equal(0, kit.Player().BankruptMonths);

        kit.State.Player.Spend(200_000);
        kit.EndMonth();
        Assert.Equal(1, kit.Player().BankruptMonths);
        kit.State.Player.Refund(310_000); // Cash positive.
        Assert.True(kit.Loan(LoanMode.Repay).Succeeded); // Loan below the maximum...
        kit.State.Player.Spend(400_000); // ...and cash negative again.
        Assert.True(kit.Player().Balance < 0);
        kit.EndMonth();
        Assert.Equal(0, kit.Player().BankruptMonths);
    }

    [Fact]
    public void Sandbox_NeverEnds()
    {
        var kit = Bankrupt(new FinanceSettings(Sandbox: true));
        kit.EndMonths(24);
        Assert.False(kit.Player().IsGameOver);
        Assert.Equal(24, kit.Player().BankruptMonths);
        Assert.Empty(Drain(kit));
        Assert.True(kit.Loan(LoanMode.Repay).ReasonCode is ConstructionReasons.NotEnoughCash);
    }

    [Fact]
    public void FinanceSettings_RoundTripThroughRecordedSessions()
    {
        var settings = new Talt.Core.Scenarios.ScenarioSettings(Map: new Talt.Core.Maps.MapSettings(64, 64, 1), MapSizeTiles: 64, VehicleCount: 0,
            Finance: new FinanceSettings(Sandbox: true, Inflation: true, DebugServicePays: true));
        var session = new RecordedSession(settings, "test");
        Assert.Equal(settings.Finance, RecordedSession.FromJson(session.ToJson()).Settings.Finance);

        var plain = new RecordedSession(settings with { Finance = null }, "test");
        Assert.DoesNotContain("\"finance\"", plain.ToJson(), StringComparison.Ordinal);
        Assert.Null(RecordedSession.FromJson(plain.ToJson()).Settings.Finance);
    }
}
