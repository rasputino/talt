using Talt.Core.Network;

namespace Talt.Core.Simulation;

/// <summary>
/// Fills a fleet with random lines and vehicles, for benchmarks and the technical prototype.
/// </summary>
public static class SyntheticTraffic
{
    /// <summary>
    /// Creates lines between random pairs of nodes and spreads <paramref name="vehicleCount"/>
    /// vehicles evenly along them.
    /// </summary>
    /// <returns>The fleet, ready to tick.</returns>
    public static VehicleFleet Create(TransportNetwork network, int lineCount, int vehicleCount, int seed = 1)
    {
        ArgumentNullException.ThrowIfNull(network);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(lineCount);

        var fleet = new VehicleFleet(network);
        var pathFinder = new PathFinder(network);
        var random = new Random(seed);
        var outbound = new List<int>();
        var inbound = new List<int>();
        var attempts = 0;

        while (fleet.LineCount < lineCount && attempts++ < lineCount * 20)
        {
            var a = random.Next(network.NodeCount);
            var b = random.Next(network.NodeCount);
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
            var maxSpeed = 4f + random.NextSingle() * 6f; // 4..10 tiles/s
            fleet.AddVehicle(line, maxSpeed, initialDwell: random.NextSingle() * 2f, startEdge);
        }

        return fleet;
    }
}
