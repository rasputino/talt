using Talt.Core.Simulation;
using Talt.Core.Stations;
using Talt.Tests.Support;

namespace Talt.Core.Tests.Stations;

/// <summary>Constitution II: the daily and monthly station updates allocate nothing in steady state.</summary>
public sealed class StationAllocationTests
{
    [Fact]
    public void DailyUpdate_AllocatesNothing_InSteadyState()
    {
        var kit = StationKit.Generated(seed: 12, stations: new StationSettings(50));
        foreach (var command in StationSeeder.Plan(kit.Simulation, 40))
        {
            kit.Simulation.Submit(command);
        }

        kit.Simulation.ProcessPendingCommands();
        kit.Days(40, parallel: false);
        var stations = kit.Stations;
        var day = kit.Simulation.Date;

        AllocationAssert.NoSteadyStateAllocations(
            () =>
            {
                stations.BeginDay(day);
                kit.Simulation.Industries.AdvanceDay(day, stations);
                stations.AdvanceDay(day, parallel: false);
                stations.AdvanceMonth(day);
            },
            iterationsPerRound: 20,
            "20 daily and monthly station updates");
    }
}
