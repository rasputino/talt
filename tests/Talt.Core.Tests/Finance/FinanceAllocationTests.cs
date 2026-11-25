using Talt.Core.Construction;
using Talt.Core.Finance;
using Talt.Core.Stations;
using Talt.Core.Tests.Stations;
using Talt.Tests.Support;

namespace Talt.Core.Tests.Finance;

/// <summary>Constitution II, SC-003: the daily and monthly finance updates and paying a delivery allocate nothing.</summary>
public sealed class FinanceAllocationTests
{
    [Fact]
    public void DailyMonthlyAndPayments_AllocateNothing_InSteadyState()
    {
        var kit = StationKit.Generated(seed: 12, stations: new StationSettings(50));
        foreach (var command in StationSeeder.Plan(kit.Simulation, 40))
        {
            kit.Simulation.Submit(command);
        }

        kit.Simulation.ProcessPendingCommands();
        kit.Loan(LoanMode.Borrow);
        kit.Finance().RunningCosts.Register(Company.PlayerId, 5_000, 1_000);
        var finance = kit.Finance();
        var date = kit.Simulation.Date;
        var station = kit.Stations.IdAt(0);
        var other = kit.Stations.IdAt(1);
        var delivery = FinanceKit.Delivery(kit.Cargo("coal"), 20, station, other, date.DayNumber - 3, date.DayNumber);

        AllocationAssert.NoSteadyStateAllocations(
            () =>
            {
                finance.Pay(delivery);
                finance.AdvanceDay(date);
                finance.AdvanceMonth(date);
                finance.Events.Clear();
            },
            iterationsPerRound: 50,
            "50 daily and monthly finance updates and payments");
    }
}
