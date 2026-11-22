using Talt.Core.Determinism;
using Talt.Core.Network;

namespace Talt.Core.Simulation;

/// <summary>
/// Fills a fleet with random lines and vehicles, for benchmarks and the technical prototype.
/// Deterministic for a given seed on every platform (uses <see cref="DeterministicRandom"/>).
/// </summary>
public static class SyntheticTraffic
{
    /// <summary>Slowest random cruise speed (4 tiles/s).</summary>
    public static readonly int MinCruiseSpeed = FixedMath.SpeedFromTilesPerSecond(4);

    /// <summary>Fastest random cruise speed (10 tiles/s).</summary>
    public static readonly int MaxCruiseSpeed = FixedMath.SpeedFromTilesPerSecond(10);

    /// <summary>
    /// Creates lines between random pairs of nodes and spreads <paramref name="vehicleCount"/>
    /// vehicles evenly along them.
    /// </summary>
    /// <returns>The fleet, ready to tick.</returns>
    public static VehicleFleet Create(TransportNetwork network, int lineCount, int vehicleCount, int seed = 1)
    {
        ArgumentNullException.ThrowIfNull(network);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(lineCount);
        ArgumentOutOfRangeException.ThrowIfNegative(vehicleCount);

        var fleet = new VehicleFleet(network);
        var pathFinder = new PathFinder(network);
        var random = new DeterministicRandom(unchecked((ulong)seed) ^ 0x5EED_7AFF_1C00_0001UL);
        var outbound = new List<int>();
        var inbound = new List<int>();
        var attempts = 0;

        while (fleet.LineCount < lineCount && attempts++ < lineCount * 20)
        {
            var a = random.NextInt(network.NodeCount);
            var b = random.NextInt(network.NodeCount);
            if (a == b
                || !pathFinder.TryFindPath(a, b, outbound)
                || !pathFinder.TryFindPath(b, a, inbound))
            {
                continue;
            }

            fleet.AddLine(outbound.ToArray(), inbound.ToArray());
        }

        if (fleet.LineCount == 0)
        {
            throw new InvalidOperationException("Could not create any line: the network is too disconnected.");
        }

        for (var v = 0; v < vehicleCount; v++)
        {
            var line = v % fleet.LineCount;
            var order = v / fleet.LineCount;
            var vehiclesOnLine = (vehicleCount - line + fleet.LineCount - 1) / fleet.LineCount;
            var startEdge = (int)((long)order * fleet.LineEdgeCount(line) / vehiclesOnLine);
            var maxSpeed = random.NextInt(MinCruiseSpeed, MaxCruiseSpeed + 1);
            var initialDwell = random.NextInt(FixedMath.SecondsToTicks(2) + 1);
            fleet.AddVehicle(line, maxSpeed, initialDwell, startEdge);
        }

        return fleet;
    }
}
