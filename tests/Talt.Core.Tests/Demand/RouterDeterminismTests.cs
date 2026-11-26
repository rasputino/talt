using Talt.Core.Construction;
using Talt.Core.Simulation;
using Talt.Core.Tests.Vehicles;
using Talt.Core.Vehicles;
using Talt.Tests.Support;

namespace Talt.Core.Tests.Demand;

/// <summary>User story 5 and FR-011/FR-025: routes follow network changes at fixed ticks, whatever the threads do.</summary>
public sealed class RouterDeterminismTests
{
    [Fact]
    public void ANewDirectLine_IsUsedByNewCargo_WithinTheUpdatePeriod()
    {
        var d = DemandKit.ThreeTowns();
        d.Supply(d.A, 10);
        Assert.Equal(0, d.Waiting(d.A, d.TownC, d.C));
        d.Line([d.A, d.C], 2);
        var router = d.Kit.Simulation.Router!;
        var applied = router.AppliedCount;
        d.Kit.Days(1 + router.Rules.ApplyDelayDays);
        Assert.True(router.AppliedCount > applied);
        d.Kit.Days(10);
        Assert.True(d.Waiting(d.A, d.TownC, d.C) > 0, "new passengers for C take the direct line");
    }

    [Fact]
    public void ALineDeleted_ReroutesWaitingCargo_AndCargoWithoutRouteExpires()
    {
        var d = DemandKit.ThreeTowns();
        d.Supply(d.A, 20);
        var forC = d.Waiting(d.A, d.TownC, d.B);
        Assert.True(forC > 0);
        Assert.True(d.Kit.Run(new DeleteLineCommand(Company.PlayerId, d.LineBC)).Succeeded);

        // A job already running finishes first; the next one, started by the change, applies a delay later.
        d.Kit.Days((2 * d.Kit.Simulation.Router!.Rules.ApplyDelayDays) + 2);
        Assert.Equal(0, d.Waiting(d.A, d.TownC, d.B)); // re-routed: B no longer leads to C
        Assert.True(d.Waiting(d.A, d.TownC, -1) > 0, "waiting without a route");
        var expired = d.Kit.Stations.DirectedExpired;
        d.Kit.Days(d.Kit.Stations.Demand!.ExpiryDays + 1);
        Assert.True(d.Kit.Stations.DirectedExpired > expired, "cargo without a route expires");
        Assert.Equal(0, d.Waiting(d.A, d.TownC, -1));
    }

    [Fact]
    public void TheJobAppliesAtItsTick()
    {
        var d = DemandKit.ThreeTowns();
        var router = d.Kit.Simulation.Router!;
        d.Line([d.A, d.C], 1);

        // The job's main-thread part is built over four ticks, then the job starts with a fixed apply tick.
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(-1, router.PendingApplyTick);
            d.Kit.Simulation.Tick();
        }

        var applyAt = router.PendingApplyTick;
        Assert.True(applyAt > d.Kit.Simulation.CurrentTick);
        var applied = router.AppliedCount;
        while (d.Kit.Simulation.CurrentTick < applyAt)
        {
            Assert.Equal(applied, router.AppliedCount);
            d.Kit.Simulation.Tick();
        }

        d.Kit.Simulation.Tick();
        Assert.Equal(applied + 1, router.AppliedCount);
    }

    [Fact]
    public void ParallelSequentialAndBackgroundJobs_GiveTheSameFingerprints()
    {
        static (Talt.Core.Tests.Stations.StationKit Kit, RoadVehicleSystem Vehicles) Game()
        {
            var (kit, a, b, depot) = EarningTests.TownLine();
            var vehicles = kit.Simulation.RoadVehicles!;
            var buses = new int[3];
            for (var i = 0; i < buses.Length; i++)
            {
                Assert.True(kit.Run(new BuyVehicleCommand(Company.PlayerId, depot, "bus-townsman")).Succeeded);
                buses[i] = vehicles.LastCreatedId;
            }

            Assert.True(kit.Run(new CreateLineCommand(Company.PlayerId, [a, b], buses[0])).Succeeded);
            var line = vehicles.Lines.IdAt(0);
            Assert.True(kit.Run(new AssignLineCommand(Company.PlayerId, buses[1], line)).Succeeded);
            Assert.True(kit.Run(new AssignLineCommand(Company.PlayerId, buses[2], line)).Succeeded);
            return (kit, vehicles);
        }

        var parallel = Game();
        var sequential = Game();
        for (var t = 0; t < 4000; t++)
        {
            parallel.Kit.Simulation.Tick(parallel: true);
            sequential.Kit.Simulation.Tick(parallel: false);
            if (t % 200 == 199)
            {
                Assert.Equal(sequential.Kit.Simulation.ComputeFingerprint(), parallel.Kit.Simulation.ComputeFingerprint());
            }
        }

        Assert.True(parallel.Kit.Simulation.Router!.AppliedCount > 1);
        Assert.True(parallel.Kit.Stations.DirectedDelivered > 0, "own-town passengers are delivered at the other stop");
    }

    [Fact]
    public void Conservation_OverAYear_ToTheUnit()
    {
        var (kit, a, b, depot) = EarningTests.TownLine();
        var vehicles = kit.Simulation.RoadVehicles!;
        var buses = new int[3];
        for (var i = 0; i < buses.Length; i++)
        {
            Assert.True(kit.Run(new BuyVehicleCommand(Company.PlayerId, depot, "bus-townsman")).Succeeded);
            buses[i] = vehicles.LastCreatedId;
        }

        Assert.True(kit.Run(new CreateLineCommand(Company.PlayerId, [a, b], buses[0])).Succeeded);
        var line = vehicles.Lines.IdAt(0);
        Assert.True(kit.Run(new AssignLineCommand(Company.PlayerId, buses[1], line)).Succeeded);
        Assert.True(kit.Run(new AssignLineCommand(Company.PlayerId, buses[2], line)).Succeeded);
        var stations = kit.Stations;
        for (var day = 0; day < 365; day++)
        {
            kit.Days(1);
            long onBoard = 0;
            for (var slot = 0; slot < vehicles.Count; slot++)
            {
                var hold = vehicles.HoldAt(slot);
                for (var i = 0; i < hold.Count; i++)
                {
                    if (hold[i].Destination >= 0)
                    {
                        onBoard += hold[i].Amount;
                    }
                }
            }

            Assert.Equal(stations.DirectedGenerated, stations.DirectedDelivered + stations.DirectedWaiting + onBoard + stations.DirectedLost);
        }

        Assert.True(stations.DirectedGenerated > 0);
        Assert.True(stations.DirectedDelivered > 0);
    }

    [Fact]
    public void SteadyStateTicks_WithRouting_AllocateNothing()
    {
        var d = DemandKit.ThreeTowns();
        // Past the expiry time, waiting cargo (and so the packet pools) stops growing.
        d.Supply(d.A, d.Kit.Stations.Demand!.ExpiryDays + 10);
        var simulation = d.Kit.Simulation;

        // A whole route period (start, background job, apply, re-route slices) per window, sequential so the job runs on this thread.
        var period = simulation.Router!.Rules.UpdateIntervalDays * GameClock.TicksPerDay;
        AllocationAssert.NoSteadyStateAllocations(() => simulation.Tick(parallel: false), period, "ticks with destination routing");
    }
}
