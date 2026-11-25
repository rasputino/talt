using Talt.Core.Simulation;
using Talt.Core.Stations;
using Xunit.Abstractions;

namespace Talt.Core.Tests.Stations;

/// <summary>Constitution II: the daily and monthly station updates allocate nothing in steady state.</summary>
public sealed class StationAllocationTests(ITestOutputHelper output)
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
        for (var warm = 0; warm < 20; warm++)
        {
            Phase(kit, stations, day);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        stations.BeginDay(day);
        var a = GC.GetAllocatedBytesForCurrentThread();
        kit.Simulation.Industries.AdvanceDay(day, stations);
        var b = GC.GetAllocatedBytesForCurrentThread();
        stations.AdvanceDay(day, parallel: false);
        var c = GC.GetAllocatedBytesForCurrentThread();
        stations.AdvanceMonth(day);
        var d = GC.GetAllocatedBytesForCurrentThread();
        output.WriteLine($"begin {a - before} industries {b - a} advance {c - b} month {d - c}");
        Assert.Equal(0, d - before);
    }

    private static void Phase(StationKit kit, StationRegistry stations, GameDate day)
    {
        stations.BeginDay(day);
        kit.Simulation.Industries.AdvanceDay(day, stations);
        stations.AdvanceDay(day, parallel: false);
    }
}
