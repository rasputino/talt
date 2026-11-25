using Talt.Core.Commands;
using Talt.Core.Construction;
using Talt.Core.Finance;
using Talt.Core.Scenarios;
using Talt.Core.Simulation;

namespace Talt.Core.Tests.Finance;

/// <summary>Feature 011 US7 (research R17): optional infinite money, one way only.</summary>
public sealed class InfiniteMoneyTests
{
    [Fact]
    public void NewGameOption_StartsWithInfiniteMoney()
    {
        var kit = FinanceKit.Flat(new FinanceSettings(InfiniteMoney: true));
        Assert.True(kit.Player().HasInfiniteMoney);
        Assert.Equal(0, kit.Player().InfiniteMoneySinceTick);
        Assert.True(kit.Finance().UsedInfiniteMoney(Company.PlayerId));
        Assert.True(kit.Finance().IsActive);
    }

    [Fact]
    public void Command_TurnsItOnOnce_AndNothingTurnsItOff()
    {
        var kit = FinanceKit.Flat();
        Assert.False(kit.Finance().UsedInfiniteMoney(Company.PlayerId));
        kit.Days(2);
        Assert.True(kit.Run(new EnableInfiniteMoneyCommand(Company.PlayerId)).Succeeded);
        Assert.Equal(2 * GameClock.TicksPerDay, kit.Player().InfiniteMoneySinceTick);
        Assert.Equal(ConstructionReasons.AlreadyInfinite, kit.Run(new EnableInfiniteMoneyCommand(Company.PlayerId)).ReasonCode);
        kit.EndMonths(14);
        Assert.True(kit.Player().HasInfiniteMoney);
        Assert.Equal(2 * GameClock.TicksPerDay, kit.Player().InfiniteMoneySinceTick);
    }

    [Fact]
    public void Construction_NeverFailsForMoney_CashStays_AndTheLedgerCounts()
    {
        var kit = FinanceKit.Flat(balance: 100);
        Assert.Equal(ConstructionReasons.NotEnoughMoney, kit.Road(5, 10, 50, 10).ReasonCode);
        kit.Run(new EnableInfiniteMoneyCommand(Company.PlayerId));

        Assert.True(kit.Road(5, 10, 50, 10).Succeeded);
        Assert.True(kit.Build("truck-bay", 6, 11).Succeeded);
        Assert.Equal(100, kit.Player().Balance);
        Assert.True(kit.YearSlot(LedgerCategory.Construction) < 0);
        kit.EndMonth();
        Assert.Equal(100, kit.Player().Balance);
        Assert.True(kit.Ledger().Month(1, Ledger.Slot(LedgerCategory.PropertyMaintenance)) < 0);
    }

    [Fact]
    public void Loans_AreRefused_AndBankruptcyNeverCounts()
    {
        var kit = FinanceKit.Flat(balance: 0);
        kit.Loan(LoanMode.BorrowMax);
        kit.State.Player.Spend(600_000);
        kit.EndMonths(2);
        Assert.Equal(2, kit.Player().BankruptMonths);
        kit.Run(new EnableInfiniteMoneyCommand(Company.PlayerId));

        Assert.Equal(ConstructionReasons.InfiniteMoney, kit.Loan(LoanMode.Borrow).ReasonCode);
        Assert.Equal(ConstructionReasons.InfiniteMoney, kit.Loan(LoanMode.RepayMax).ReasonCode);
        kit.EndMonths(10);
        Assert.Equal(0, kit.Player().BankruptMonths);
        Assert.False(kit.Player().IsGameOver);
    }

    [Fact]
    public void RecordedSessions_ReplayTheCommandAndTheSetting()
    {
        var settings = new ScenarioSettings(Seed: 4, StartYear: 1950, MapSizeTiles: 256, VehicleCount: 0,
            Map: new Talt.Core.Maps.MapSettings(256, 256, Seed: 4, Roughness: 30, MaxHeight: 40, WaterAmount: 15, SeaLevel: 6),
            Finance: new FinanceSettings(Sandbox: true));
        var simulation = GameSimulation.Create(settings);
        var recorder = new SessionRecorder(new RecordedSession(simulation.Settings));
        simulation.AttachRecorder(recorder);
        simulation.Submit(new EnableInfiniteMoneyCommand(Company.PlayerId));
        foreach (var command in Talt.Core.Stations.StationSeeder.Plan(simulation, 6))
        {
            simulation.Submit(command);
        }

        for (var i = 0; i < 1500; i++)
        {
            simulation.Tick();
        }

        recorder.AddCheckpoint(simulation);
        Assert.True(simulation.Construction!.Player.HasInfiniteMoney);
        Assert.Equal(500_000, simulation.Construction.Player.Balance); // Built with infinite money: cash unchanged.

        var json = recorder.Session.ToJson();
        var replay = SessionReplayer.Replay(RecordedSession.FromJson(json));
        Assert.True(replay.Succeeded, replay.FailureReason);
        Assert.Equal(simulation.ComputeFingerprint(), replay.Simulation.ComputeFingerprint());

        var withSetting = new RecordedSession(settings with { Finance = new FinanceSettings(InfiniteMoney: true) }, "test");
        Assert.Equal(new FinanceSettings(InfiniteMoney: true), RecordedSession.FromJson(withSetting.ToJson()).Settings.Finance);
    }

    [Fact]
    public void InfiniteMoney_IsPartOfTheFingerprint()
    {
        var a = FinanceKit.Flat();
        var b = FinanceKit.Flat();
        Assert.Equal(a.Simulation.ComputeFingerprint(), b.Simulation.ComputeFingerprint());
        b.Run(new EnableInfiniteMoneyCommand(Company.PlayerId));
        Assert.NotEqual(a.Simulation.ComputeFingerprint(), b.Simulation.ComputeFingerprint());
    }
}
