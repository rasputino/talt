using Talt.Core.Construction;
using Talt.Core.Finance;
using Talt.Core.Scenarios;
using Talt.Core.Simulation;
using Talt.Core.Stations;
using Talt.Core.Tests.Construction;
using Talt.Core.Tests.Stations;

namespace Talt.Core.Tests.Finance;

/// <summary>Finance test helpers on top of <see cref="StationKit"/>: games with finance options, month and year ends.</summary>
internal static class FinanceKit
{
    /// <summary>A flat hand-made map (no towns, no industries) with the given options and starting cash.</summary>
    public static StationKit Flat(FinanceSettings? finance = null, long balance = 1_000_000, int startYear = 1950)
    {
        var data = new TestMap().Build();
        var settings = new ScenarioSettings(Seed: 3, StartYear: startYear, MapSizeTiles: 64, RoadSpacing: 16, RoadRemovalChancePerMille: 0,
            VehicleCount: 0, LineCount: 1, Map: data.Settings, Finance: finance);
        return new StationKit(GameSimulation.Create(settings, data, rules: TestMap.Rules with { StartingBalance = balance }));
    }

    public static FinanceSystem Finance(this StationKit kit) => kit.Simulation.Finance!;

    public static Company Player(this StationKit kit) => kit.State.Player;

    public static Ledger Ledger(this StationKit kit) => kit.State.Player.Ledger!;

    /// <summary>Ticks until the month changes (the month-end handlers have run).</summary>
    public static void EndMonth(this StationKit kit)
    {
        var month = kit.Simulation.Date.Month;
        while (kit.Simulation.Date.Month == month)
        {
            kit.Simulation.Tick();
        }
    }

    public static void EndMonths(this StationKit kit, int months)
    {
        for (var i = 0; i < months; i++)
        {
            kit.EndMonth();
        }
    }

    public static Talt.Core.Commands.CommandResult Loan(this StationKit kit, LoanMode mode) => kit.Run(new ChangeLoanCommand(Company.PlayerId, mode));

    /// <summary>Sum of every slot of the current year.</summary>
    public static long YearSlot(this StationKit kit, LedgerCategory category) => kit.Ledger().Year(0, Talt.Core.Finance.Ledger.Slot(category));

    /// <summary>A delivery report as feature 010 makes it.</summary>
    public static CargoDelivery Delivery(int cargo, int amount, int origin, int destination, int pickupDay, int deliveryDay) =>
        new(Company.PlayerId, cargo, amount, origin, destination, -1, pickupDay, deliveryDay, DeliveryConsumer.Industry, 0);
}
