using Talt.Core.Construction;
using Talt.Core.Determinism;
using Talt.Core.Network;
using Talt.Core.Roads;
using Talt.Core.Tests.Construction;
using Talt.Core.Tests.Roads;

namespace Talt.Core.Tests.Network;

/// <summary>
/// The incremental road network (research R5): connectivity, and after any sequence of actions it equals a
/// network built from scratch (FR-004); routes are shortest (FR-005) and unchanged by how it was built.
/// </summary>
public class RoadNetworkTests
{
    [Fact]
    public void StraightRoad_IsOneEdgeEachWay()
    {
        var kit = new RoadKit();
        kit.Road(5, 5, 25, 5);

        var edges = kit.State.Network.Canonical();

        Assert.Equal(2, kit.State.Network.NodeCount);
        Assert.Equal(2, edges.Count);
        Assert.All(edges, e => Assert.Equal(20 * Dir8.StraightLength, e.Length));
        Assert.All(edges, e => Assert.Equal(80, e.SpeedKmh));
    }

    [Fact]
    public void RingWithoutJunctions_HasNoNodes()
    {
        var kit = new RoadKit();
        kit.Road(10, 10, 20, 10);
        kit.Road(20, 10, 20, 20);
        kit.Road(20, 20, 10, 20);
        kit.Road(10, 20, 10, 10);

        Assert.Equal(0, kit.State.Network.NodeCount);
        kit.AssertNetworkMatchesRebuild();
    }

    [Fact]
    public void RandomActions_KeepTheNetworkEqualToAFullRebuild()
    {
        var map = new TestMap(128);
        for (var z = 0; z < 128; z++)
        {
            for (var x = 60; x < 70; x++)
            {
                map.Height(x, z, 10 + Math.Min(x - 59, 70 - x) / 2);
            }
        }

        for (var x = 20; x < 24; x++)
        {
            for (var z = 0; z < 128; z++)
            {
                map.Water(x, z, 7, 9);
            }
        }

        var kit = new RoadKit(map, startYear: 1990);
        var random = new DeterministicRandom(77);
        var router = new RoadRouter(kit.State.Network, useCache: true);
        var applied = 0;
        for (var step = 0; step < 160; step++)
        {
            var x0 = random.NextInt(128);
            var z0 = random.NextInt(128);
            var x1 = Math.Clamp(x0 + random.NextInt(61) - 30, 0, 127);
            var z1 = Math.Clamp(z0 + random.NextInt(61) - 30, 0, 127);
            var choice = random.NextInt(10);
            ConstructionCommand command = choice switch
            {
                < 5 => new BuildRoadCommand(Company.PlayerId, random.NextInt(2) == 0 ? "paved" : "fast", x0, z0, x1, z1),
                5 => new BuildMotorwayCommand(Company.PlayerId, x0, z0, x1, z1),
                6 => new SetOneWayCommand(Company.PlayerId, x0, z0),
                7 => new BuildBridgeCommand(Company.PlayerId, "paved", x0, z0, x1, z0),
                _ => new RemoveRoadCommand(Company.PlayerId, x0, z0, x1, z1),
            };
            if (kit.Run(command).Succeeded)
            {
                applied++;
            }

            if (step % 20 == 19 && random.NextInt(3) == 0)
            {
                kit.Run(new UndoConstructionCommand(Company.PlayerId));
            }

            kit.AssertNetworkMatchesRebuild();
        }

        Assert.True(applied > 40, $"only {applied} actions applied");
        AssertRoutesAreShortest(kit, router, new DeterministicRandom(5), 60);
    }

    [Fact]
    public void IncrementalUpdate_RebuildsOnlyNearbyRegions()
    {
        var kit = new RoadKit(new TestMap(512));
        for (var z = 8; z < 512; z += 16)
        {
            kit.Road(0, z, 511, z);
        }

        kit.Road(100, 8, 100, 200);

        Assert.InRange(kit.State.Network.LastRebuiltRegions, 1, 16);
    }

    /// <summary>Compares router lengths with a brute-force Dijkstra over tile links (exits respected).</summary>
    internal static void AssertRoutesAreShortest(RoadKit kit, RoadRouter router, DeterministicRandom random, int pairs)
    {
        var nodes = kit.State.Network.NodeTiles();
        if (nodes.Count < 2)
        {
            return;
        }

        var steps = new List<RouteStep>();
        for (var i = 0; i < pairs; i++)
        {
            var a = nodes[random.NextInt(nodes.Count)];
            var b = nodes[random.NextInt(nodes.Count)];
            var expected = TileDijkstra(kit.Roads, a, b);
            var found = router.TryFindRoute(a, b, steps, out var length);
            Assert.Equal(expected >= 0, found);
            if (found)
            {
                Assert.Equal(expected, length);
                Assert.Equal(length, steps.Sum(s => (long)s.Length));
                for (var s = 1; s < steps.Count; s++)
                {
                    Assert.Equal(steps[s - 1].ToTile, steps[s].FromTile);
                }
            }
        }
    }

    private static long TileDijkstra(RoadLayer layer, int from, int to)
    {
        if (from == to)
        {
            return 0;
        }

        var best = new Dictionary<int, long> { [from] = 0 };
        var queue = new PriorityQueue<int, long>();
        queue.Enqueue(from, 0);
        while (queue.TryDequeue(out var tile, out var cost))
        {
            if (cost > best[tile])
            {
                continue;
            }

            if (tile == to)
            {
                return cost;
            }

            var exits = layer.ExitsAt(tile);
            for (var d = 0; d < 8; d++)
            {
                if ((exits & (1 << d)) == 0)
                {
                    continue;
                }

                var next = layer.Follow(tile, d);
                var c = cost + layer.LinkLength(tile, d);
                if (!best.TryGetValue(next, out var old) || c < old)
                {
                    best[next] = c;
                    queue.Enqueue(next, c);
                }
            }
        }

        return -1;
    }
}
