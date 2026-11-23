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
    /// <remarks>
    /// Each attempt draws a random pair and keeps it as a line when both directions are reachable.
    /// Attempts run in batches no larger than the number of lines still missing, so no batch draws
    /// a pair the sequential loop would not have drawn. Routes of a batch are searched in parallel,
    /// then accepted in attempt order: the result does not depend on <paramref name="parallel"/>.
    /// </remarks>
    /// <param name="parallel">Search the routes of each batch on all CPU cores. Results are identical either way.</param>
    /// <returns>The fleet, ready to tick.</returns>
    public static VehicleFleet Create(TransportNetwork network, int lineCount, int vehicleCount, int seed = 1, bool parallel = true)
    {
        ArgumentNullException.ThrowIfNull(network);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(lineCount);
        ArgumentOutOfRangeException.ThrowIfNegative(vehicleCount);

        var fleet = new VehicleFleet(network);
        var random = new DeterministicRandom(unchecked((ulong)seed) ^ 0x5EED_7AFF_1C00_0001UL);
        var maxAttempts = (long)lineCount * 20;
        var attempts = 0L;

        while (fleet.LineCount < lineCount && attempts < maxAttempts)
        {
            var batchSize = (int)Math.Min(lineCount - fleet.LineCount, maxAttempts - attempts);
            attempts += batchSize;

            var pairs = new (int A, int B)[batchSize];
            for (var i = 0; i < batchSize; i++)
            {
                var a = random.NextInt(network.NodeCount);
                var b = random.NextInt(network.NodeCount);
                pairs[i] = (a, b);
            }

            var routes = FindRoutes(network, pairs, parallel);
            foreach (var route in routes)
            {
                if (route.Outbound is not null)
                {
                    fleet.AddLine(route.Outbound, route.Inbound!);
                }
            }
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

    /// <summary>Searches both directions of every pair; a pair with no route gets nulls.</summary>
    private static (int[]? Outbound, int[]? Inbound)[] FindRoutes(TransportNetwork network, (int A, int B)[] pairs, bool parallel)
    {
        var routes = new (int[]? Outbound, int[]? Inbound)[pairs.Length];

        if (!parallel || pairs.Length < 2)
        {
            var search = new RouteSearch(network);
            for (var i = 0; i < pairs.Length; i++)
            {
                routes[i] = search.Find(pairs[i].A, pairs[i].B);
            }

            return routes;
        }

        // One path finder per worker. Workers take the next pair from a shared counter and write
        // each route to its own slot, so the result does not depend on scheduling.
        var next = -1;
        var workers = Math.Min(Environment.ProcessorCount, pairs.Length);
        Parallel.For(0, workers, _ =>
        {
            var search = new RouteSearch(network);
            for (var i = Interlocked.Increment(ref next); i < pairs.Length; i = Interlocked.Increment(ref next))
            {
                routes[i] = search.Find(pairs[i].A, pairs[i].B);
            }
        });

        return routes;
    }

    private sealed class RouteSearch(TransportNetwork network)
    {
        private readonly PathFinder _pathFinder = new(network);
        private readonly List<int> _outbound = [];
        private readonly List<int> _inbound = [];

        public (int[]? Outbound, int[]? Inbound) Find(int a, int b)
        {
            if (a == b
                || !_pathFinder.TryFindPath(a, b, _outbound)
                || !_pathFinder.TryFindPath(b, a, _inbound))
            {
                return (null, null);
            }

            return (_outbound.ToArray(), _inbound.ToArray());
        }
    }
}
