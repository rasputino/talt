using System.Collections.Concurrent;

namespace Talt.Core.Demand;

/// <summary>
/// The route assignment (feature 013, research R5): a pure function from a <see cref="RoutingProblem"/> to a
/// <see cref="RouteTable"/>, safe to run on a worker thread. For every destination town with demand and a serving
/// node, a multi-source Dijkstra over the reversed graph from the town's serving nodes keeps the two best labels with
/// distinct sources at each node (so cargo at a serving node can still be sent to another serving node of the same
/// town). Each node's next hop is its out-edge minimising edge cost + the neighbour's label. With two iterations the
/// estimated monthly flows are loaded on the first pass's routes, overloaded edges get dearer, and the second pass's
/// choice takes half of the cargo where it differs (method of successive averages). Labels are compared as
/// (cost, source) pairs, so results do not depend on the priority queue's order.
/// </summary>
/// <remarks>
/// Destinations are independent, so they may be searched on several threads (<c>parallel</c>): each thread has its own
/// <see cref="Searcher"/>, edge loads are summed (order does not matter) and the output is sorted at the end, so the
/// table is identical for any number of threads.
/// </remarks>
public sealed class RouteAssignment
{
    private const int SourceBits = 20;
    private const long SourceMask = (1L << SourceBits) - 1;
    private const int MaxCongestionFactorPerMille = 4000;

    private readonly DemandRules _rules;
    private readonly ConcurrentBag<Searcher> _pool = [];
    private readonly List<Searcher> _used = [];
    private readonly Searcher _serial;
    private int _generation;

    // Shared, read-only during the searches.
    private RoutingProblem _p = null!;
    private int[] _revStart = new int[16];
    private int[] _fill = new int[16];
    private int[] _revFrom = new int[16];
    private int[] _revEdge = new int[16];
    private int[] _cost = new int[16];
    private long[] _load = new long[16];
    private long _limit;

    // Demand entries (town << 32 | node) with units, sorted.
    private long[] _demKey = new long[16];
    private int[] _demUnits = new int[16];
    private int _demCount;

    // Destinations and, per destination, the first pass's (node, hop) pairs.
    private int[] _destTown = new int[16];
    private int[] _destDemStart = new int[16];
    private int[] _destDemEnd = new int[16];
    private int[] _destR1Start = new int[16];
    private int[] _destR1End = new int[16];
    private int[] _r1Node = new int[16];
    private int[] _r1Hop = new int[16];
    private int _destCount;

    // Final entries (group key << 32 | town) → hop1, hop2.
    private long[] _outKey = new long[16];
    private int[] _outIndex = new int[16];
    private int[] _outHop1 = new int[16];
    private int[] _outHop2 = new int[16];
    private int _outCount;

    public RouteAssignment(DemandRules rules)
    {
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _serial = new Searcher(this);
    }

    /// <summary>Destinations routed by the last <see cref="Solve"/> (statistics).</summary>
    public int LastDestinations => _destCount;

    /// <summary>Computes the routes of <paramref name="problem"/> into <paramref name="output"/>.</summary>
    /// <param name="parallel">Search destinations on several threads (same result; allocates a little bookkeeping).</param>
    public void Solve(RoutingProblem problem, RouteTable output, bool parallel = false)
    {
        ArgumentNullException.ThrowIfNull(problem);
        ArgumentNullException.ThrowIfNull(output);
        var n = problem.NodeCount;
        if (n >= 1 << SourceBits)
        {
            throw new InvalidOperationException("Too many station groups for one company and cargo.");
        }

        output.Reset(problem.Company, problem.Cargo);
        for (var i = 0; i < n; i++)
        {
            output.AddNode(problem.Key[i]);
        }

        _p = problem;
        _generation++;
        Prepare(problem);
        BuildDemand(problem);
        BuildDestinations(problem);
        if (_destCount == 0)
        {
            return;
        }

        // Iteration 1: free-flow costs; each searcher keeps its (destination, node, hop) results and edge loads.
        var loads = _rules.Iterations > 1;
        RunAll(parallel, pass: 1, loads);
        GatherFirstPass(problem);

        _outCount = 0;
        if (_rules.Iterations > 1)
        {
            Congest(problem);
            RunAll(parallel, pass: 2, loads: false);
            foreach (var s in _used)
            {
                s.CopyOutput(this);
            }
        }
        else
        {
            for (var d = 0; d < _destCount; d++)
            {
                for (var i = _destR1Start[d]; i < _destR1End[d]; i++)
                {
                    AddOutput(problem.Key[_r1Node[i]], _destTown[d], problem.Key[_r1Hop[i]], -1);
                }
            }
        }

        foreach (var s in _used)
        {
            if (!ReferenceEquals(s, _serial))
            {
                _pool.Add(s);
            }
        }

        _used.Clear();
        for (var i = 0; i < _outCount; i++)
        {
            _outIndex[i] = i;
        }

        Array.Sort(_outKey, _outIndex, 0, _outCount);
        for (var i = 0; i < _outCount; i++)
        {
            var key = _outKey[i];
            var j = _outIndex[i];
            output.Add((int)(key >> 32), (int)key, _outHop1[j], _outHop2[j], 500);
        }
    }

    /// <summary>Runs one pass over every destination, on this thread or on several.</summary>
    private void RunAll(bool parallel, int pass, bool loads)
    {
        if (!parallel || _destCount < 8)
        {
            var s = Use(_serial);
            for (var d = 0; d < _destCount; d++)
            {
                s.Run(d, pass, loads);
            }

            return;
        }

        RunParallel(pass, loads);
    }

    /// <summary>The parallel pass (its own method: the lambdas' closure would otherwise be allocated on every call).</summary>
    private void RunParallel(int pass, bool loads)
    {
        var locals = new ConcurrentBag<Searcher>();
        Parallel.For(
            0,
            _destCount,
            () =>
            {
                var s = _pool.TryTake(out var pooled) ? pooled : new Searcher(this);
                s.Prepare(_generation, _p.NodeCount, _p.EdgeCount);
                return s;
            },
            (d, _, s) =>
            {
                s.Run(d, pass, loads);
                return s;
            },
            s => locals.Add(s));
        foreach (var s in locals)
        {
            if (!_used.Contains(s))
            {
                _used.Add(s);
            }
        }
    }

    private Searcher Use(Searcher s)
    {
        s.Prepare(_generation, _p.NodeCount, _p.EdgeCount);
        if (!_used.Contains(s))
        {
            _used.Add(s);
        }

        return s;
    }

    /// <summary>Collects the first pass's results by destination and sums the searchers' edge loads.</summary>
    private void GatherFirstPass(RoutingProblem p)
    {
        Array.Clear(_load, 0, p.EdgeCount);
        var total = 0;
        foreach (var s in _used)
        {
            total += s.R1Count;
            for (var e = 0; e < p.EdgeCount; e++)
            {
                _load[e] += s.Load[e];
            }
        }

        Ensure(ref _r1Node, total);
        Ensure(ref _r1Hop, total);
        Array.Clear(_destR1Start, 0, _destCount);
        foreach (var s in _used)
        {
            for (var i = 0; i < s.R1Count; i++)
            {
                _destR1Start[s.R1Dest[i]]++;
            }
        }

        var start = 0;
        for (var d = 0; d < _destCount; d++)
        {
            var count = _destR1Start[d];
            _destR1Start[d] = start;
            _destR1End[d] = start;
            start += count;
        }

        // Within a destination the order is the searcher's (deterministic per destination: one searcher ran it).
        foreach (var s in _used)
        {
            for (var i = 0; i < s.R1Count; i++)
            {
                var at = _destR1End[s.R1Dest[i]]++;
                _r1Node[at] = s.R1Node[i];
                _r1Hop[at] = s.R1Hop[i];
            }
        }
    }

    private void Prepare(RoutingProblem p)
    {
        var n = p.NodeCount;
        var m = p.EdgeCount;
        Ensure(ref _revStart, n + 1);
        Ensure(ref _revFrom, m);
        Ensure(ref _revEdge, m);
        Ensure(ref _cost, m);
        Ensure(ref _load, m);
        Ensure(ref _fill, n);

        // Reverse CSR.
        Array.Clear(_revStart, 0, n + 1);
        for (var e = 0; e < m; e++)
        {
            _revStart[p.EdgeTo[e] + 1]++;
        }

        for (var i = 0; i < n; i++)
        {
            _revStart[i + 1] += _revStart[i];
        }

        var fill = _fill;
        Array.Clear(fill, 0, n);
        for (var from = 0; from < n; from++)
        {
            for (var e = p.EdgeStart[from]; e < p.EdgeStart[from + 1]; e++)
            {
                var to = p.EdgeTo[e];
                var at = _revStart[to] + fill[to]++;
                _revFrom[at] = from;
                _revEdge[at] = e;
            }
        }

        for (var e = 0; e < m; e++)
        {
            _cost[e] = p.EdgeTicks[e] + _rules.DwellTicks + (p.EdgeCapacity[e] == 0 ? _rules.ZeroCapacityPenaltyTicks : 0);
        }

        _limit = ((long)_rules.MaxRouteDays * Simulation.GameClock.TicksPerDay * (MaxCongestionFactorPerMille / 1000)) + _rules.ZeroCapacityPenaltyTicks;
    }

    /// <summary>Demand entries per (destination town, origin node): flows times table shares.</summary>
    private void BuildDemand(RoutingProblem p)
    {
        _demCount = 0;
        for (var f = 0; f < p.FlowCount; f++)
        {
            if (!p.Rows.TryGetValue(p.FlowRow[f], out var range))
            {
                continue;
            }

            for (var k = 0; k < range.Count; k++)
            {
                var share = p.TableShare[range.Start + k];
                if (share <= 0)
                {
                    continue;
                }

                Ensure(ref _demKey, _demCount + 1);
                Ensure(ref _demUnits, _demCount + 1);
                _demKey[_demCount] = ((long)p.TableDest[range.Start + k] << 32) | (uint)p.FlowNode[f];
                _demUnits[_demCount] = (int)((long)p.FlowUnits[f] * share / Apportion.ShareScale);
                _demCount++;
            }
        }

        Array.Sort(_demKey, _demUnits, 0, _demCount);

        // Merge duplicates.
        var w = 0;
        for (var i = 0; i < _demCount; i++)
        {
            if (w > 0 && _demKey[w - 1] == _demKey[i])
            {
                _demUnits[w - 1] += _demUnits[i];
                continue;
            }

            _demKey[w] = _demKey[i];
            _demUnits[w] = _demUnits[i];
            w++;
        }

        _demCount = w;
    }

    /// <summary>Destination towns that have demand and at least one serving node, in ascending order.</summary>
    private void BuildDestinations(RoutingProblem p)
    {
        _destCount = 0;
        var s = 0;
        var i = 0;
        while (i < _demCount)
        {
            var town = (int)(_demKey[i] >> 32);
            var end = i;
            while (end < _demCount && (int)(_demKey[end] >> 32) == town)
            {
                end++;
            }

            while (s < p.ServingCount && p.ServingTown[s] < town)
            {
                s++;
            }

            if (s < p.ServingCount && p.ServingTown[s] == town)
            {
                Ensure(ref _destTown, _destCount + 1);
                Ensure(ref _destDemStart, _destCount + 1);
                Ensure(ref _destDemEnd, _destCount + 1);
                Ensure(ref _destR1Start, _destCount + 1);
                Ensure(ref _destR1End, _destCount + 1);
                _destTown[_destCount] = town;
                _destDemStart[_destCount] = i;
                _destDemEnd[_destCount] = end;
                _destCount++;
            }

            i = end;
        }
    }

    /// <summary>Second-pass costs: edges loaded beyond their capacity get dearer.</summary>
    private void Congest(RoutingProblem p)
    {
        for (var e = 0; e < p.EdgeCount; e++)
        {
            var capacity = p.EdgeCapacity[e];
            if (capacity <= 0 || _load[e] <= capacity)
            {
                continue;
            }

            var excessPerMille = (_load[e] - capacity) * 1000 / capacity;
            var factor = Math.Min(MaxCongestionFactorPerMille, 1000 + (excessPerMille * _rules.CongestionPenaltyPerMille / 1000));
            _cost[e] = (int)Math.Min(int.MaxValue, (long)_cost[e] * factor / 1000);
        }
    }

    private void AddOutput(int groupKey, int town, int hop1, int hop2)
    {
        Ensure(ref _outKey, _outCount + 1);
        Ensure(ref _outIndex, _outCount + 1);
        Ensure(ref _outHop1, _outCount + 1);
        Ensure(ref _outHop2, _outCount + 1);
        _outKey[_outCount] = ((long)groupKey << 32) | (uint)town;
        _outHop1[_outCount] = hop1;
        _outHop2[_outCount] = hop2;
        _outCount++;
    }

    private static int LowerBound(int[] array, int count, int value)
    {
        int lo = 0, hi = count;
        while (lo < hi)
        {
            var mid = (lo + hi) >>> 1;
            if (array[mid] < value)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }

    private static void Ensure<T>(ref T[] array, int needed)
    {
        if (needed > array.Length)
        {
            Array.Resize(ref array, Math.Max(needed, array.Length * 2));
        }
    }

    /// <summary>One thread's search state: labels, hops and its share of the results.</summary>
    private sealed class Searcher(RouteAssignment owner)
    {
        private readonly PriorityQueue<int, long> _queue = new();
        private long[] _l1 = [];
        private long[] _l2 = [];
        private byte[] _settled = [];
        private int[] _settledFirst = [];
        private int[] _servingStamp = [];
        private int[] _hopNode = [];
        private int[] _hopEdge = [];
        private int[] _touched = [];
        private int _touchedCount;
        private int _stamp;
        private int _generation = -1;

        // Pass 2 output, in this searcher's order.
        private long[] _outKey = new long[16];
        private int[] _outHop1 = new int[16];
        private int[] _outHop2 = new int[16];
        private int _outCount;

        public long[] Load { get; private set; } = [];

        public int[] R1Dest { get; private set; } = new int[16];

        public int[] R1Node { get; private set; } = new int[16];

        public int[] R1Hop { get; private set; } = new int[16];

        public int R1Count { get; private set; }

        /// <summary>Sizes and clears the arrays for a new problem (once per Solve).</summary>
        public void Prepare(int generation, int nodes, int edges)
        {
            if (_generation == generation)
            {
                return;
            }

            _generation = generation;
            if (_l1.Length < nodes)
            {
                var size = Math.Max(nodes, _l1.Length * 2);
                _l1 = new long[size];
                _l2 = new long[size];
                _settled = new byte[size];
                _settledFirst = new int[size];
                _servingStamp = new int[size];
                _hopNode = new int[size];
                _hopEdge = new int[size];
                _touched = new int[size];
            }

            if (Load.Length < edges)
            {
                Load = new long[Math.Max(edges, Load.Length * 2)];
            }

            Array.Fill(_l1, long.MaxValue, 0, nodes);
            Array.Fill(_l2, long.MaxValue, 0, nodes);
            Array.Clear(_settled, 0, nodes);
            Array.Fill(_hopNode, -1, 0, nodes);
            Array.Clear(_servingStamp, 0, nodes);
            Array.Clear(Load, 0, edges);
            _stamp = 0;
            _touchedCount = 0;
            R1Count = 0;
            _outCount = 0;
        }

        /// <summary>Searches one destination and records the pass's results.</summary>
        public void Run(int d, int pass, bool loads)
        {
            var o = owner;
            RunDestination(d);
            if (pass == 1)
            {
                for (var t = 0; t < _touchedCount; t++)
                {
                    var g = _touched[t];
                    if (_hopNode[g] >= 0)
                    {
                        if (R1Count == R1Dest.Length)
                        {
                            R1Dest = Grow(R1Dest);
                            R1Node = Grow(R1Node);
                            R1Hop = Grow(R1Hop);
                        }

                        R1Dest[R1Count] = d;
                        R1Node[R1Count] = g;
                        R1Hop[R1Count] = _hopNode[g];
                        R1Count++;
                    }
                }

                if (loads)
                {
                    LoadFlows(d);
                }
            }
            else
            {
                var key = o._p.Key;
                for (var i = o._destR1Start[d]; i < o._destR1End[d]; i++)
                {
                    var g = o._r1Node[i];
                    if (_outCount == _outKey.Length)
                    {
                        Array.Resize(ref _outKey, _outCount * 2);
                        Array.Resize(ref _outHop1, _outCount * 2);
                        Array.Resize(ref _outHop2, _outCount * 2);
                    }

                    _outKey[_outCount] = ((long)key[g] << 32) | (uint)o._destTown[d];
                    _outHop1[_outCount] = key[o._r1Hop[i]];
                    _outHop2[_outCount] = _hopNode[g] >= 0 ? key[_hopNode[g]] : -1;
                    _outCount++;
                }
            }

            ClearTouched();
        }

        /// <summary>Appends this searcher's pass 2 entries to the owner's output.</summary>
        public void CopyOutput(RouteAssignment o)
        {
            for (var i = 0; i < _outCount; i++)
            {
                Ensure(ref o._outKey, o._outCount + 1, ref o._outIndex, ref o._outHop1, ref o._outHop2);
                o._outKey[o._outCount] = _outKey[i];
                o._outHop1[o._outCount] = _outHop1[i];
                o._outHop2[o._outCount] = _outHop2[i];
                o._outCount++;
            }

            _outCount = 0;
        }

        private static int[] Grow(int[] array)
        {
            var copy = new int[array.Length * 2];
            array.CopyTo(copy, 0);
            return copy;
        }

        private void RunDestination(int d)
        {
            var o = owner;
            var p = o._p;
            var town = o._destTown[d];
            _stamp++;

            // Search box: origins and serving nodes, widened by the margin.
            int x0 = int.MaxValue, z0 = int.MaxValue, x1 = int.MinValue, z1 = int.MinValue;
            for (var i = o._destDemStart[d]; i < o._destDemEnd[d]; i++)
            {
                var node = (int)o._demKey[i];
                x0 = Math.Min(x0, p.X[node]);
                z0 = Math.Min(z0, p.Z[node]);
                x1 = Math.Max(x1, p.X[node]);
                z1 = Math.Max(z1, p.Z[node]);
            }

            var first = LowerBound(p.ServingTown, p.ServingCount, town);
            var last = first;
            while (last < p.ServingCount && p.ServingTown[last] == town)
            {
                var node = p.ServingNode[last++];
                x0 = Math.Min(x0, p.X[node]);
                z0 = Math.Min(z0, p.Z[node]);
                x1 = Math.Max(x1, p.X[node]);
                z1 = Math.Max(z1, p.Z[node]);
                _servingStamp[node] = _stamp;
            }

            var margin = o._rules.PruneMarginTiles;
            x0 -= margin;
            z0 -= margin;
            x1 += margin;
            z1 += margin;
            var limit = o._limit;
            var cost = o._cost;
            var revStart = o._revStart;
            var revFrom = o._revFrom;
            var revEdge = o._revEdge;

            _queue.Clear();
            for (var i = first; i < last; i++)
            {
                var node = p.ServingNode[i];
                Relax(node, node);
            }

            while (_queue.TryDequeue(out var v, out var c))
            {
                var source = (int)(c & SourceMask);
                if ((_l1[v] != c && _l2[v] != c) || _settled[v] == 2 || (_settled[v] == 1 && _settledFirst[v] == source))
                {
                    continue;
                }

                if (_settled[v]++ == 0)
                {
                    _settledFirst[v] = source;
                }

                var dist = c >> SourceBits;
                for (var r = revStart[v]; r < revStart[v + 1]; r++)
                {
                    var u = revFrom[r];
                    if (p.X[u] < x0 || p.X[u] > x1 || p.Z[u] < z0 || p.Z[u] > z1)
                    {
                        continue;
                    }

                    var nd = dist + cost[revEdge[r]];
                    if (nd <= limit)
                    {
                        Relax(u, (nd << SourceBits) | (uint)source);
                    }
                }
            }

            // Next hops.
            for (var t = 0; t < _touchedCount; t++)
            {
                var g = _touched[t];
                var serving = _servingStamp[g] == _stamp;
                var best = long.MaxValue;
                var bestHop = -1;
                var bestEdge = -1;
                for (var e = p.EdgeStart[g]; e < p.EdgeStart[g + 1]; e++)
                {
                    var h = p.EdgeTo[e];
                    var label = _l1[h];
                    if (label != long.MaxValue && serving && (label & SourceMask) == g)
                    {
                        label = _l2[h];
                    }

                    if (label == long.MaxValue)
                    {
                        continue;
                    }

                    var total = cost[e] + (label >> SourceBits);
                    if (total < best || (total == best && h < bestHop))
                    {
                        best = total;
                        bestHop = h;
                        bestEdge = e;
                    }
                }

                _hopNode[g] = bestHop;
                _hopEdge[g] = bestEdge;
            }
        }

        private void Relax(int u, long label)
        {
            var source = (int)(label & SourceMask);
            if (_settled[u] == 2 || (_settled[u] == 1 && _settledFirst[u] == source))
            {
                return;
            }

            if (_l1[u] == long.MaxValue && _l2[u] == long.MaxValue)
            {
                _touched[_touchedCount++] = u;
            }

            if (_l1[u] != long.MaxValue && (_l1[u] & SourceMask) == source)
            {
                if (label < _l1[u])
                {
                    _l1[u] = label;
                    _queue.Enqueue(u, label);
                }

                return;
            }

            if (_l2[u] != long.MaxValue && (_l2[u] & SourceMask) == source)
            {
                if (label < _l2[u])
                {
                    _l2[u] = label;
                    if (_l2[u] < _l1[u])
                    {
                        (_l1[u], _l2[u]) = (_l2[u], _l1[u]);
                    }

                    _queue.Enqueue(u, label);
                }

                return;
            }

            if (label < _l1[u])
            {
                _l2[u] = _l1[u];
                _l1[u] = label;
                _queue.Enqueue(u, label);
            }
            else if (label < _l2[u])
            {
                _l2[u] = label;
                _queue.Enqueue(u, label);
            }
        }

        /// <summary>Adds the destination's estimated flows along the first pass's hops (to this searcher's loads).</summary>
        private void LoadFlows(int d)
        {
            var o = owner;
            var n = o._p.NodeCount;
            for (var i = o._destDemStart[d]; i < o._destDemEnd[d]; i++)
            {
                var units = o._demUnits[i];
                if (units <= 0)
                {
                    continue;
                }

                var node = (int)o._demKey[i];
                for (var steps = 0; steps < n; steps++)
                {
                    if (steps > 0 && _servingStamp[node] == _stamp)
                    {
                        break;
                    }

                    var hop = _hopNode[node];
                    if (hop < 0)
                    {
                        break;
                    }

                    Load[_hopEdge[node]] += units;
                    node = hop;
                }
            }
        }

        private void ClearTouched()
        {
            for (var t = 0; t < _touchedCount; t++)
            {
                var u = _touched[t];
                _l1[u] = long.MaxValue;
                _l2[u] = long.MaxValue;
                _settled[u] = 0;
                _hopNode[u] = -1;
            }

            _touchedCount = 0;
        }
    }

    private static void Ensure(ref long[] keys, int needed, ref int[] index, ref int[] hop1, ref int[] hop2)
    {
        Ensure(ref keys, needed);
        Ensure(ref index, needed);
        Ensure(ref hop1, needed);
        Ensure(ref hop2, needed);
    }
}
