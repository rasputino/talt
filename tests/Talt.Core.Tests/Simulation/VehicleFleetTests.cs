using System.Numerics;
using Talt.Core.Determinism;
using Talt.Core.Network;
using Talt.Core.Simulation;

namespace Talt.Core.Tests.Simulation;

public class VehicleFleetTests
{
    private static readonly int Tile = FixedMath.UnitsPerTile;

    /// <summary>Straight line A — B — C, 10 tiles per segment, two-way.</summary>
    internal static (TransportNetwork Network, int[] Outbound, int[] Inbound) StraightLine()
    {
        var builder = new TransportNetworkBuilder();
        var a = builder.AddNodeAtTile(0, 0);
        var b = builder.AddNodeAtTile(10, 0);
        var c = builder.AddNodeAtTile(20, 0);
        var ab = builder.AddEdge(a, b);
        var bc = builder.AddEdge(b, c);
        var cb = builder.AddEdge(c, b);
        var ba = builder.AddEdge(b, a);
        return (builder.Build(), [ab, bc], [cb, ba]);
    }

    [Fact]
    public void Vehicle_TravelsToTheOtherStopAndWaits()
    {
        var (network, outbound, inbound) = StraightLine();
        var fleet = new VehicleFleet(network);
        var line = fleet.AddLine(outbound, inbound);
        var id = fleet.AddVehicle(line, FixedMath.SpeedFromTilesPerSecond(5));
        var slot = fleet.SlotOf(id);

        for (var t = 0; t < 2000 && !fleet.IsAtStop(slot); t++)
        {
            fleet.Tick();
        }

        Assert.True(fleet.IsAtStop(slot));
        Assert.Equal(new Vector2(20, 0), fleet.GetPose(slot).Position);
        Assert.Equal(new Vector2(-1, 0), fleet.GetPose(slot).Direction); // Ready to go back.
        Assert.Equal(20L * Tile, fleet.TotalDistance); // Exact: integer units.
    }

    [Fact]
    public void Vehicle_WaitsExactlyTheDwellTime()
    {
        var (network, outbound, inbound) = StraightLine();
        var fleet = new VehicleFleet(network);
        var id = fleet.AddVehicle(fleet.AddLine(outbound, inbound), FixedMath.SpeedFromTilesPerSecond(8));
        var slot = fleet.SlotOf(id);

        while (!fleet.IsAtStop(slot))
        {
            fleet.Tick();
        }

        var waited = 0;
        while (fleet.IsAtStop(slot))
        {
            fleet.Tick();
            waited++;
        }

        Assert.Equal(VehicleFleet.DwellTicks, waited);
        Assert.Equal(60, VehicleFleet.DwellTicks); // 3 s × 20 ticks.
    }

    [Fact]
    public void Vehicle_NeverExceedsMaxSpeed()
    {
        var (network, outbound, inbound) = StraightLine();
        var fleet = new VehicleFleet(network);
        var max = FixedMath.SpeedFromTilesPerSecond(3);
        var slot = fleet.SlotOf(fleet.AddVehicle(fleet.AddLine(outbound, inbound), max));

        for (var t = 0; t < 1000; t++)
        {
            fleet.Tick();
            Assert.InRange(fleet.GetSpeed(slot), 0, max);
        }
    }

    [Fact]
    public void Vehicle_ReturnsToFirstStopAfterAFullLoop()
    {
        var (network, outbound, inbound) = StraightLine();
        var fleet = new VehicleFleet(network);
        var slot = fleet.SlotOf(fleet.AddVehicle(fleet.AddLine(outbound, inbound), FixedMath.SpeedFromTilesPerSecond(8)));
        var stopsReached = 0;
        var wasAtStop = false;

        for (var t = 0; t < 5000 && stopsReached < 2; t++)
        {
            fleet.Tick();
            if (fleet.IsAtStop(slot) && !wasAtStop)
            {
                stopsReached++;
            }

            wasAtStop = fleet.IsAtStop(slot);
        }

        Assert.Equal(2, stopsReached);
        Assert.Equal(Vector2.Zero, fleet.GetPose(slot).Position);
        Assert.Equal(40L * Tile, fleet.TotalDistance);
    }

    [Fact]
    public void ArrivalTick_MatchesTheFloatPrototypeWithinOneTick()
    {
        // The integer model must keep the float prototype's behaviour (FR-021): same constants
        // (1.5 / 2.5 tiles/s², 5 tiles/s, 20 Hz), same arrival tick within rounding.
        var (network, outbound, inbound) = StraightLine();
        var fleet = new VehicleFleet(network);
        var slot = fleet.SlotOf(fleet.AddVehicle(fleet.AddLine(outbound, inbound), FixedMath.SpeedFromTilesPerSecond(5)));

        var ticks = 0;
        while (!fleet.IsAtStop(slot))
        {
            fleet.Tick();
            ticks++;
        }

        Assert.InRange(ticks, ExpectedFloatArrivalTicks() - 1, ExpectedFloatArrivalTicks() + 1);
    }

    [Fact]
    public void AddAndRemove_KeepSlotsDenseAndIdsStable()
    {
        var (network, outbound, inbound) = StraightLine();
        var fleet = new VehicleFleet(network);
        var line = fleet.AddLine(outbound, inbound);
        var speed = FixedMath.SpeedFromTilesPerSecond(5);
        var a = fleet.AddVehicle(line, speed);
        var b = fleet.AddVehicle(line, speed);
        var c = fleet.AddVehicle(line, speed);

        Assert.True(fleet.RemoveVehicle(a));

        Assert.Equal(2, fleet.VehicleCount);
        Assert.False(fleet.ContainsVehicle(a));
        Assert.Equal(0, fleet.SlotOf(c)); // Last vehicle moved into the free slot.
        Assert.Equal(1, fleet.SlotOf(b));
        Assert.False(fleet.RemoveVehicle(a));
        Assert.Equal(c + 1, fleet.AddVehicle(line, speed)); // Ids are never reused.
    }

    [Fact]
    public void AddVehicle_ValidatesArguments()
    {
        var (network, outbound, inbound) = StraightLine();
        var fleet = new VehicleFleet(network);
        var line = fleet.AddLine(outbound, inbound);

        Assert.Throws<ArgumentOutOfRangeException>(() => fleet.AddVehicle(line + 1, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => fleet.AddVehicle(line, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => fleet.AddVehicle(line, VehicleFleet.MaxAllowedSpeed + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => fleet.AddVehicle(line, 100, startEdge: 4));
    }

    [Fact]
    public void DisconnectedLine_IsRejected()
    {
        var (network, outbound, _) = StraightLine();
        var fleet = new VehicleFleet(network);

        Assert.Throws<ArgumentException>(() => fleet.AddLine(outbound, outbound));
    }

    [Fact]
    public void ParallelAndSequentialTicks_GiveIdenticalState()
    {
        var network = GridNetworkGenerator.Generate(40, 40, spacingTiles: 8, seed: 5);
        var parallel = SyntheticTraffic.Create(network, lineCount: 200, vehicleCount: 20_000, seed: 7);
        var sequential = SyntheticTraffic.Create(network, lineCount: 200, vehicleCount: 20_000, seed: 7);

        for (var t = 0; t < 400; t++)
        {
            parallel.Tick(parallel: true);
            sequential.Tick(parallel: false);
        }

        Assert.Equal(Fingerprint(sequential), Fingerprint(parallel));
        Assert.Equal(sequential.TotalDistance, parallel.TotalDistance);
    }

    [Theory]
    [InlineData(150)] // Reference removal chance: a few pairs are unreachable.
    [InlineData(600)] // Very disconnected: many attempts fail, so several batches are needed.
    public void ParallelAndSequentialCreation_GiveIdenticalFleets(int removalChancePerMille)
    {
        var network = GridNetworkGenerator.Generate(40, 40, spacingTiles: 8, seed: 5, removalChancePerMille);
        var parallel = SyntheticTraffic.Create(network, lineCount: 300, vehicleCount: 5_000, seed: 7, parallel: true);
        var sequential = SyntheticTraffic.Create(network, lineCount: 300, vehicleCount: 5_000, seed: 7, parallel: false);

        Assert.Equal(sequential.LineCount, parallel.LineCount);
        Assert.Equal(Fingerprint(sequential), Fingerprint(parallel));
    }

    [Fact]
    public void Tick_DoesNotAllocatePerVehicle()
    {
        var network = GridNetworkGenerator.Generate(40, 40, spacingTiles: 8, seed: 5);
        var fleet = SyntheticTraffic.Create(network, lineCount: 100, vehicleCount: 50_000, seed: 3);
        fleet.Tick(parallel: false); // Warm up.

        var before = GC.GetAllocatedBytesForCurrentThread();
        fleet.Tick(parallel: false);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }

    private static ulong Fingerprint(VehicleFleet fleet)
    {
        var hasher = new StateHasher();
        hasher.Append(fleet);
        return hasher.GetFingerprint();
    }

    /// <summary>Replays the old float model for the straight-line scenario.</summary>
    private static int ExpectedFloatArrivalTicks()
    {
        const float dt = 0.05f, acceleration = 1.5f, deceleration = 2.5f, creep = 0.5f, max = 5f;
        float distance = 0, speed = 0;
        var ticks = 0;
        while (true)
        {
            ticks++;
            var remaining = 20f - distance;
            speed = speed * speed / (2f * deceleration) >= remaining
                ? MathF.Max(speed - deceleration * dt, creep)
                : MathF.Min(speed + acceleration * dt, max);
            if (speed * dt >= remaining)
            {
                return ticks;
            }

            distance += speed * dt;
        }
    }
}
