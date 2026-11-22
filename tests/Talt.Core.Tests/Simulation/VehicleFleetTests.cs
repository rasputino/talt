using System.Numerics;
using Talt.Core.Network;
using Talt.Core.Simulation;

namespace Talt.Core.Tests.Simulation;

public class VehicleFleetTests
{
    /// <summary>Straight line A — B — C, 10 tiles per segment, two-way.</summary>
    private static (TransportNetwork Network, int[] Outbound, int[] Inbound) StraightLine()
    {
        var builder = new TransportNetworkBuilder();
        var a = builder.AddNode(new Vector2(0, 0));
        var b = builder.AddNode(new Vector2(10, 0));
        var c = builder.AddNode(new Vector2(20, 0));
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
        var vehicle = fleet.AddVehicle(line, maxSpeed: 5f);

        // Run until the vehicle reaches the far stop (20 tiles away).
        for (var t = 0; t < 2000 && !fleet.IsAtStop(vehicle); t++)
        {
            fleet.Tick(0.05f);
        }

        Assert.True(fleet.IsAtStop(vehicle));
        Assert.Equal(new Vector2(20, 0), fleet.GetPose(vehicle).Position);
        Assert.Equal(new Vector2(-1, 0), fleet.GetPose(vehicle).Direction); // Ready to go back.
        Assert.Equal(20.0, fleet.TotalDistance, 3);
    }

    [Fact]
    public void Vehicle_NeverExceedsMaxSpeed()
    {
        var (network, outbound, inbound) = StraightLine();
        var fleet = new VehicleFleet(network);
        var vehicle = fleet.AddVehicle(fleet.AddLine(outbound, inbound), maxSpeed: 3f);

        for (var t = 0; t < 500; t++)
        {
            fleet.Tick(0.05f);
            Assert.InRange(fleet.GetSpeed(vehicle), 0f, 3f);
        }
    }

    [Fact]
    public void Vehicle_ReturnsToFirstStopAfterAFullLoop()
    {
        var (network, outbound, inbound) = StraightLine();
        var fleet = new VehicleFleet(network);
        var vehicle = fleet.AddVehicle(fleet.AddLine(outbound, inbound), maxSpeed: 8f);
        var stopsReached = 0;
        var wasAtStop = false;

        for (var t = 0; t < 5000 && stopsReached < 2; t++)
        {
            fleet.Tick(0.05f);
            if (fleet.IsAtStop(vehicle) && !wasAtStop)
            {
                stopsReached++;
            }

            wasAtStop = fleet.IsAtStop(vehicle);
        }

        Assert.Equal(2, stopsReached);
        Assert.Equal(Vector2.Zero, fleet.GetPose(vehicle).Position);
        Assert.Equal(40.0, fleet.TotalDistance, 3);
    }

    [Fact]
    public void DisconnectedLine_IsRejected()
    {
        var (network, outbound, _) = StraightLine();
        var fleet = new VehicleFleet(network);

        Assert.Throws<ArgumentException>(() => fleet.AddLine(outbound, outbound));
    }

    [Fact]
    public void ParallelAndSequentialTicks_GiveIdenticalResults()
    {
        var network = GridNetworkGenerator.Generate(40, 40, spacing: 8, seed: 5);
        var parallel = SyntheticTraffic.Create(network, lineCount: 200, vehicleCount: 20_000, seed: 7);
        var sequential = SyntheticTraffic.Create(network, lineCount: 200, vehicleCount: 20_000, seed: 7);

        for (var t = 0; t < 200; t++)
        {
            parallel.Tick(0.05f, parallel: true);
            sequential.Tick(0.05f, parallel: false);
        }

        for (var v = 0; v < parallel.VehicleCount; v++)
        {
            Assert.Equal(sequential.GetPose(v), parallel.GetPose(v));
        }
    }

    [Fact]
    public void Tick_DoesNotAllocatePerVehicle()
    {
        var network = GridNetworkGenerator.Generate(40, 40, spacing: 8, seed: 5);
        var fleet = SyntheticTraffic.Create(network, lineCount: 100, vehicleCount: 50_000, seed: 3);
        fleet.Tick(0.05f, parallel: false); // Warm up.

        var before = GC.GetAllocatedBytesForCurrentThread();
        fleet.Tick(0.05f, parallel: false);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }
}
