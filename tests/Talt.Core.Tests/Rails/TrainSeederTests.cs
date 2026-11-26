using Talt.Core.Rails;
using Talt.Core.Tests.Construction;
using Talt.Tests.Support;

namespace Talt.Core.Tests.Rails;

public sealed class TrainSeederTests
{
    private static RailKit Seeded(int trains, int size = 256)
    {
        var kit = new RailKit(new TestMap(size));
        foreach (var command in TrainSeeder.Plan(kit.Simulation, trains))
        {
            kit.Simulation.Submit(command);
        }

        kit.Simulation.ProcessPendingCommands();
        return kit;
    }

    [Fact]
    public void SeededLoopsRunSafelyAndDeliverTheirStops()
    {
        var kit = Seeded(48);
        Assert.Equal(48, kit.Trains.Count);
        var loading = 0;
        kit.Ticks(4000, each: () =>
        {
            for (var slot = 0; slot < kit.Trains.Count; slot++)
            {
                loading += kit.Trains.TrainAt(slot).Mode == TrainMode.Loading ? 1 : 0;
            }
        });

        var running = 0;
        for (var slot = 0; slot < kit.Trains.Count; slot++)
        {
            running += kit.Trains.TrainAt(slot).Mode != TrainMode.InDepot ? 1 : 0;
        }

        Assert.Equal(48, running);
        Assert.True(loading > 0);
    }

    [Fact]
    public void TheTrainTickAllocatesNothingInSteadyState()
    {
        var kit = Seeded(24);
        kit.Ticks(3000, parallel: false);
        var tick = kit.Simulation.CurrentTick;
        AllocationAssert.NoSteadyStateAllocations(() => kit.Trains.Tick(tick++, parallel: false), 400, "400 train ticks with 24 trains");
        AllocationAssert.NoSteadyStateAllocations(() => kit.Simulation.Tick(parallel: false), 400, "400 ticks with 24 trains");
    }
}
