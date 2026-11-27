using Talt.Core.Demand;

namespace Talt.Core.Tests.Demand;

public sealed class RouteAssignmentTests
{
    private static RouteTable Solve(RoutingProblem problem, int iterations = 2)
    {
        var rules = iterations == 2 ? DemandRules.Default : DemandRulesWithOneIteration();
        var table = new RouteTable();
        new RouteAssignment(rules).Solve(problem, table);
        return table;
    }

    private static DemandRules DemandRulesWithOneIteration() => DemandRules.FromJson(
        DemandRules.ReadResource().Replace("\"iterations\": 2", "\"iterations\": 1", StringComparison.Ordinal), Talt.Core.Content.GameContent.Default);

    /// <summary>Nodes with keys 1..n on a line, 10 tiles apart.</summary>
    private static RoutingProblem Problem(int nodes)
    {
        var p = new RoutingProblem();
        p.Reset(1, 0);
        for (var i = 1; i <= nodes; i++)
        {
            p.AddNode(i, i * 10, 0);
        }

        return p;
    }

    private static void Both(RoutingProblem p, int a, int b, int ticks = 100, int capacity = 1000)
    {
        p.AddEdge(p.NodeOf(a), p.NodeOf(b), ticks, capacity);
        p.AddEdge(p.NodeOf(b), p.NodeOf(a), ticks, capacity);
    }

    private static void Wants(RoutingProblem p, int node, int row, int units, params (int Town, int Share)[] table)
    {
        p.AddFlow(p.NodeOf(node), row, units);
        p.AddTableRow(row, table.Select(t => t.Town).ToArray(), table.Select(t => t.Share).ToArray());
    }

    private static (int Hop1, int Hop2) Route(RouteTable table, int group, int town)
    {
        var i = table.Find(group, town);
        return i < 0 ? (-1, -1) : (table.Hop1At(i), table.Hop2At(i));
    }

    [Fact]
    public void Chain_RoutesTowardsTheServingGroup()
    {
        var p = Problem(3);
        Both(p, 1, 2);
        Both(p, 2, 3);
        p.AddServing(30, p.NodeOf(3));
        Wants(p, 1, 0, 100, (30, 1_000_000));
        p.Seal();
        var table = Solve(p);
        Assert.Equal((2, -1), Route(table, 1, 30));
        Assert.Equal((3, -1), Route(table, 2, 30));
        Assert.Equal((-1, -1), Route(table, 3, 30)); // the only serving group: nowhere else to go
        Assert.True(table.HasNode(2));
        Assert.False(table.HasNode(9));
    }

    [Fact]
    public void OwnTownCargo_LeavesItsGroupForAnotherServingGroup()
    {
        var p = Problem(3);
        Both(p, 1, 2);
        Both(p, 2, 3);
        p.AddServing(10, p.NodeOf(1));
        p.AddServing(10, p.NodeOf(3));
        Wants(p, 1, 10, 100, (10, 1_000_000));
        p.Seal();
        var table = Solve(p);
        Assert.Equal((2, -1), Route(table, 1, 10)); // away from 1, towards 3
        Assert.Equal(1, Route(table, 2, 10).Hop1); // nearest serving group from 2: 1 (lower key on a tie)
        Assert.Equal((2, -1), Route(table, 3, 10));
    }

    [Fact]
    public void SaturatedDirectLink_SplitsWithTheDetourOnTheSecondPass()
    {
        var p = Problem(3); // 1 = A, 2 = B (serves town 5), 3 = C (detour)
        p.AddEdge(p.NodeOf(1), p.NodeOf(2), 100, 10);
        p.AddEdge(p.NodeOf(1), p.NodeOf(3), 60, 10_000);
        p.AddEdge(p.NodeOf(3), p.NodeOf(2), 60, 10_000);
        p.AddServing(5, p.NodeOf(2));
        Wants(p, 1, 0, 1000, (5, 1_000_000));
        p.Seal();
        Assert.Equal((2, 3), Route(Solve(p), 1, 5));
        Assert.Equal((2, -1), Route(Solve(p, iterations: 1), 1, 5));
        var table = Solve(p);
        Assert.Equal(500, table.Share2At(table.Find(1, 5)));
    }

    [Fact]
    public void UnreachableTowns_HaveNoRoute()
    {
        var p = Problem(4);
        Both(p, 1, 2);
        Both(p, 3, 4);
        p.AddServing(40, p.NodeOf(4));
        Wants(p, 1, 0, 100, (40, 1_000_000));
        p.Seal();
        var table = Solve(p);
        Assert.Equal(-1, table.Find(1, 40));
        Assert.Equal(-1, table.Find(2, 40));
    }

    [Fact]
    public void ZeroCapacityLinks_AreUsedOnlyWithoutAnAlternative()
    {
        var p = Problem(3);
        p.AddEdge(p.NodeOf(1), p.NodeOf(2), 10, 0); // a line with no vehicles
        p.AddEdge(p.NodeOf(1), p.NodeOf(3), 500, 100);
        p.AddEdge(p.NodeOf(3), p.NodeOf(2), 500, 100);
        p.AddServing(7, p.NodeOf(2));
        Wants(p, 1, 0, 1, (7, 1_000_000));
        p.Seal();
        Assert.Equal(3, Route(Solve(p, iterations: 1), 1, 7).Hop1);

        var q = Problem(2);
        q.AddEdge(q.NodeOf(1), q.NodeOf(2), 10, 0);
        q.AddServing(7, q.NodeOf(2));
        Wants(q, 1, 0, 1, (7, 1_000_000));
        q.Seal();
        Assert.Equal(2, Route(Solve(q), 1, 7).Hop1);
    }

    [Fact]
    public void FollowingHops_AlwaysEndsAtAServingGroup()
    {
        // A ring of 12 groups with chords; towns served at a few of them.
        var p = Problem(12);
        for (var i = 1; i <= 12; i++)
        {
            Both(p, i, (i % 12) + 1, 50 + (i * 7 % 30));
        }

        Both(p, 1, 7, 400);
        Both(p, 3, 10, 300);
        int[] serving = [2, 6, 11];
        foreach (var s in serving)
        {
            p.AddServing(100 + s, p.NodeOf(s));
        }

        for (var i = 1; i <= 12; i++)
        {
            Wants(p, i, i, 50, (102, 300_000), (106, 300_000), (111, 400_000));
        }

        p.Seal();
        var table = Solve(p);
        foreach (var s in serving)
        {
            var town = 100 + s;
            for (var start = 1; start <= 12; start++)
            {
                if (start == s)
                {
                    continue;
                }

                var node = start;
                var steps = 0;
                while (node != s)
                {
                    var (hop, _) = Route(table, node, town);
                    Assert.NotEqual(-1, hop);
                    node = hop;
                    Assert.True(++steps <= 12, "route loops");
                }
            }
        }
    }

    [Fact]
    public void SameProblem_SameTable()
    {
        static RoutingProblem Build()
        {
            var p = Problem(6);
            Both(p, 1, 2);
            Both(p, 2, 3, 50);
            Both(p, 1, 4, 70);
            Both(p, 4, 3, 70);
            Both(p, 3, 5);
            Both(p, 5, 6);
            p.AddServing(9, p.NodeOf(6));
            p.AddServing(9, p.NodeOf(3));
            Wants(p, 1, 0, 300, (9, 1_000_000));
            p.Seal();
            return p;
        }

        var a = Solve(Build());
        var b = Solve(Build());
        Assert.Equal(Hash(a), Hash(b));
    }

    [Fact]
    public void ParallelSolve_EqualsSequentialSolve()
    {
        // A 12 × 12 lattice, a town served at every third node, every node producing for 20 towns.
        var p = Problem(144);
        for (var z = 0; z < 12; z++)
        {
            for (var x = 0; x < 12; x++)
            {
                var key = 1 + (z * 12) + x;
                if (x < 11)
                {
                    Both(p, key, key + 1, 40 + ((x * 7 + z) % 30), 50);
                }

                if (z < 11)
                {
                    Both(p, key, key + 12, 40 + ((z * 5 + x) % 30), 50);
                }

                if (key % 3 == 0)
                {
                    p.AddServing(key / 3, p.NodeOf(key));
                }
            }
        }

        for (var key = 1; key <= 144; key++)
        {
            var towns = Enumerable.Range(0, 20).Select(i => (((key / 3) + i) % 48) + 1).Distinct().ToArray();
            Wants(p, key, key, 400, towns.Select(t => (t, 1_000_000 / towns.Length)).ToArray());
        }

        p.Seal();
        var sequential = new RouteTable();
        var parallel = new RouteTable();
        new RouteAssignment(DemandRules.Default).Solve(p, sequential, parallel: false);
        var assignment = new RouteAssignment(DemandRules.Default);
        assignment.Solve(p, parallel, parallel: true);
        Assert.True(assignment.LastDestinations >= 8);
        Assert.True(sequential.Count > 1000);
        Assert.Equal(Hash(sequential), Hash(parallel));
        assignment.Solve(p, parallel, parallel: true); // pooled searchers reused
        Assert.Equal(Hash(sequential), Hash(parallel));
    }

    private static ulong Hash(RouteTable table)
    {
        var h = new Talt.Core.Determinism.StateHasher();
        h.Append(table);
        return h.GetFingerprint();
    }
}
