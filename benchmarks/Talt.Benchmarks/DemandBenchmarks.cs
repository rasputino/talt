using BenchmarkDotNet.Attributes;
using Talt.Core.Demand;
using Talt.Core.Simulation;

namespace Talt.Benchmarks;

/// <summary>
/// Cost of destination demand and routing (feature 013, SC-001) on feature 012's reference game: a 4096 × 4096 map with
/// its towns, bus bays on a 40-tile lattice, lines between neighbouring bays and 50 000 buses, run for 40 days so routes
/// were applied and passengers with destinations wait and ride. <see cref="BuildProblems"/> is the main-thread part of
/// a route job (lines collected, graph, serving towns, flows); <see cref="Solve"/> is the worker part (assignment);
/// <see cref="TablesDay"/> is one day of destination table refreshes; <see cref="Tick"/> is a full simulation tick with
/// everything (a route job starts every 160 ticks and runs in the background).
/// </summary>
[MemoryDiagnoser]
[SimpleJob(launchCount: 1, warmupCount: 2, iterationCount: 8)]
public class DemandBenchmarks
{
    private GameSimulation _simulation = null!;
    private RoutingProblem _synthetic = null!;
    private RouteAssignment _assignment = null!;
    private readonly RouteTable _syntheticTable = new();

    [Params(50_000)]
    public int Vehicles { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _simulation = RoadVehicleBenchmarks.Create(Vehicles, warmupTicks: 40 * GameClock.TicksPerDay);
        var router = _simulation.Router!;
        // Worst tick over two route periods (a job's staged build, start, apply and re-route slices).
        double worstRouting = 0, worstOther = 0, worstDay = 0;
        var stopwatch = new System.Diagnostics.Stopwatch();
        for (var t = 0; t < 4 * router.Rules.UpdateIntervalDays * GameClock.TicksPerDay; t++)
        {
            var routing = router.WorkDue(_simulation.CurrentTick);
            var day = (_simulation.CurrentTick + 1) % GameClock.TicksPerDay == 0;
            stopwatch.Restart();
            _simulation.Tick(parallel: true);
            var ms = stopwatch.Elapsed.TotalMilliseconds;
            if (routing)
            {
                worstRouting = Math.Max(worstRouting, ms);
            }
            else if (day)
            {
                worstDay = Math.Max(worstDay, ms);
            }
            else
            {
                worstOther = Math.Max(worstOther, ms);
            }
        }

        var c = System.Globalization.CultureInfo.InvariantCulture;
        Console.WriteLine($"// worst ticks over four route periods: routing {worstRouting.ToString("F2", c)} ms, day boundary {worstDay.ToString("F2", c)} ms, other {worstOther.ToString("F2", c)} ms");
        router.BuildProblemsNow();
        _synthetic = Synthetic();
        _assignment = new RouteAssignment(router.Rules);
        _assignment.Solve(_synthetic, _syntheticTable);
        Console.WriteLine($"// synthetic: {_synthetic.NodeCount} nodes, {_synthetic.EdgeCount} edges, {_assignment.LastDestinations} destinations, {_syntheticTable.Count} route entries");
        var (nodes, edges) = router.ProblemSize();
        Console.WriteLine($"// {_simulation.Stations!.Count} stations, {nodes} graph nodes, {edges} edges, {router.AppliedCount} route sets applied, "
            + $"{router.LastDestinations} destinations, {_simulation.Stations.DirectedWaiting} passengers waiting with destinations, "
            + $"{_simulation.Stations.DirectedDelivered} delivered, {_simulation.Towns!.Count} towns");
    }

    /// <summary>Main-thread part of a route job.</summary>
    [Benchmark]
    public int BuildProblems() => _simulation.Router!.BuildProblemsNow();

    /// <summary>Worker part of a route job (also builds, so the input is fresh).</summary>
    [Benchmark]
    public void Solve()
    {
        _simulation.Router!.BuildProblemsNow();
        _simulation.Router.SolveNow();
    }

    /// <summary>
    /// The worker part on a dense network: 4 900 groups on a 70 × 70 lattice 20 tiles apart joined to their four
    /// neighbours, 544 towns each served by one group in every 3 × 3 block, and every group producing passengers with
    /// the 33 nearest towns as destinations (half its own town).
    /// </summary>
    [Benchmark]
    public void SolveSynthetic() => _assignment.Solve(_synthetic, _syntheticTable, parallel: true);

    /// <summary><see cref="SolveSynthetic"/> on one thread.</summary>
    [Benchmark]
    public void SolveSyntheticSequential() => _assignment.Solve(_synthetic, _syntheticTable);

    private static RoutingProblem Synthetic()
    {
        const int side = 70;
        const int spacing = 20;
        var p = new RoutingProblem();
        p.Reset(0, 0);
        for (var z = 0; z < side; z++)
        {
            for (var x = 0; x < side; x++)
            {
                p.AddNode(1 + (z * side) + x, x * spacing, z * spacing);
            }
        }

        for (var z = 0; z < side; z++)
        {
            for (var x = 0; x < side; x++)
            {
                var n = (z * side) + x;
                if (x + 1 < side)
                {
                    p.AddEdge(n, n + 1, 300, 2000);
                    p.AddEdge(n + 1, n, 300, 2000);
                }

                if (z + 1 < side)
                {
                    p.AddEdge(n, n + side, 300, 2000);
                    p.AddEdge(n + side, n, 300, 2000);
                }
            }
        }

        // Town of a node: its 3 × 3 block; the block's centre node serves it.
        static int TownOf(int x, int z) => ((z / 3) * ((70 + 2) / 3)) + (x / 3);
        var blocks = (side + 2) / 3;
        for (var bz = 0; bz < blocks; bz++)
        {
            for (var bx = 0; bx < blocks; bx++)
            {
                var cx = Math.Min(side - 1, (bx * 3) + 1);
                var cz = Math.Min(side - 1, (bz * 3) + 1);
                p.AddServing((bz * blocks) + bx, (cz * side) + cx);
            }
        }

        var dest = new int[33];
        var share = new int[33];
        for (var bz = 0; bz < blocks; bz++)
        {
            for (var bx = 0; bx < blocks; bx++)
            {
                var town = (bz * blocks) + bx;
                var candidates = new List<(int Town, int Distance)>();
                for (var oz = 0; oz < blocks; oz++)
                {
                    for (var ox = 0; ox < blocks; ox++)
                    {
                        if (ox != bx || oz != bz)
                        {
                            candidates.Add(((oz * blocks) + ox, Math.Abs(ox - bx) + Math.Abs(oz - bz)));
                        }
                    }
                }

                candidates.Sort((a, b) => a.Distance != b.Distance ? a.Distance.CompareTo(b.Distance) : a.Town.CompareTo(b.Town));
                dest[0] = town;
                share[0] = 500_000;
                for (var i = 1; i < 33; i++)
                {
                    dest[i] = candidates[i - 1].Town;
                    share[i] = 500_000 / 32;
                }

                p.AddTableRow(town, dest, share);
            }
        }

        for (var z = 0; z < side; z++)
        {
            for (var x = 0; x < side; x++)
            {
                p.AddFlow((z * side) + x, TownOf(x, z), 300);
            }
        }

        p.Seal();
        return p;
    }

    /// <summary>One day of destination table refreshes.</summary>
    [Benchmark]
    public void TablesDay() => _simulation.Destinations!.AdvanceDay();

    /// <summary>One full simulation tick (parallel), routing included.</summary>
    [Benchmark]
    public void Tick() => _simulation.Tick(parallel: true);
}
