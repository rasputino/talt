namespace Talt.Core.Demand;

/// <summary>
/// The immutable input of one route assignment (feature 013, data-model.md LinkGraphSnapshot): for one company and
/// one destination cargo, the station groups on its lines (nodes, by group key, ascending), the connections between
/// consecutive stops (edges with travel ticks and monthly capacity), the towns each node serves, the estimated monthly
/// production at each node per source table and a copy of those destination tables. Built on the main thread by
/// <see cref="CargoRouter"/> (or by tests) and then only read by the job.
/// </summary>
public sealed class RoutingProblem
{
    private readonly List<(int From, int To, int Ticks, int Capacity)> _rawEdges = [];
    private long[] _pairs = new long[16];

    public int Company { get; set; }

    public int Cargo { get; set; }

    // Nodes (sorted by key).
    public int NodeCount { get; private set; }

    private int[] _key = new int[16];

    public int[] Key => _key;

    private int[] _x = new int[16];

    public int[] X => _x;

    private int[] _z = new int[16];

    public int[] Z => _z;

    // Edges, CSR by source node; parallel edges merged (capacity added, ticks minimum).
    private int[] _edgeStart = new int[17];

    public int[] EdgeStart => _edgeStart;

    public int EdgeCount { get; private set; }

    private int[] _edgeTo = new int[16];

    public int[] EdgeTo => _edgeTo;

    private int[] _edgeTicks = new int[16];

    public int[] EdgeTicks => _edgeTicks;

    private int[] _edgeCapacity = new int[16];

    public int[] EdgeCapacity => _edgeCapacity;

    // Serving (town, node), sorted by town then node.
    public int ServingCount { get; private set; }

    private int[] _servingTown = new int[16];

    public int[] ServingTown => _servingTown;

    private int[] _servingNode = new int[16];

    public int[] ServingNode => _servingNode;

    // Flows (node, table row, units per month).
    public int FlowCount { get; private set; }

    private int[] _flowNode = new int[16];

    public int[] FlowNode => _flowNode;

    private int[] _flowRow = new int[16];

    public int[] FlowRow => _flowRow;

    private int[] _flowUnits = new int[16];

    public int[] FlowUnits => _flowUnits;

    // Copied destination table rows: row id → entries.
    public Dictionary<int, (int Start, int Count)> Rows { get; } = [];

    public int TableEntryCount { get; private set; }

    private int[] _tableDest = new int[16];

    public int[] TableDest => _tableDest;

    private int[] _tableShare = new int[16];

    public int[] TableShare => _tableShare;

    /// <summary>Clears everything for a new build.</summary>
    public void Reset(int company, int cargo)
    {
        Company = company;
        Cargo = cargo;
        NodeCount = 0;
        EdgeCount = 0;
        ServingCount = 0;
        FlowCount = 0;
        TableEntryCount = 0;
        Rows.Clear();
        _rawEdges.Clear();
    }

    /// <summary>Adds a node; keys must be added in ascending order.</summary>
    public int AddNode(int groupKey, int x, int z)
    {
        if (NodeCount > 0 && groupKey <= _key[NodeCount - 1])
        {
            throw new ArgumentException("Nodes must be added in ascending key order.", nameof(groupKey));
        }

        Grow(ref _key, ref _x, ref _z, NodeCount + 1);
        _key[NodeCount] = groupKey;
        _x[NodeCount] = x;
        _z[NodeCount] = z;
        return NodeCount++;
    }

    /// <summary>The node of a group key, or −1.</summary>
    public int NodeOf(int groupKey)
    {
        var i = Array.BinarySearch(_key, 0, NodeCount, groupKey);
        return i >= 0 ? i : -1;
    }

    /// <summary>Adds a connection (before <see cref="Seal"/>).</summary>
    public void AddEdge(int from, int to, int ticks, int capacity)
    {
        if (from != to)
        {
            _rawEdges.Add((from, to, Math.Max(1, ticks), Math.Max(0, capacity)));
        }
    }

    /// <summary>Records that a node serves a town (before <see cref="Seal"/>).</summary>
    public void AddServing(int town, int node)
    {
        Grow(ref _servingTown, ref _servingNode, ServingCount + 1);
        _servingTown[ServingCount] = town;
        _servingNode[ServingCount] = node;
        ServingCount++;
    }

    /// <summary>Records a node's monthly production of the cargo from one source table row.</summary>
    public void AddFlow(int node, int row, int unitsPerMonth)
    {
        if (unitsPerMonth <= 0)
        {
            return;
        }

        var a = _flowNode;
        var b = _flowRow;
        Grow(ref a, ref b, FlowCount + 1);
        _flowNode = a;
        _flowRow = b;
        if (_flowUnits.Length < _flowNode.Length)
        {
            var c = _flowUnits;
            Array.Resize(ref c, _flowNode.Length);
            _flowUnits = c;
        }

        _flowNode[FlowCount] = node;
        _flowRow[FlowCount] = row;
        _flowUnits[FlowCount] = unitsPerMonth;
        FlowCount++;
    }

    /// <summary>Copies a destination table row (once per row).</summary>
    public void AddTableRow(int row, ReadOnlySpan<int> destinations, ReadOnlySpan<int> shares)
    {
        if (Rows.ContainsKey(row))
        {
            return;
        }

        var dest = _tableDest;
        var share = _tableShare;
        Grow(ref dest, ref share, TableEntryCount + destinations.Length);
        _tableDest = dest;
        _tableShare = share;
        destinations.CopyTo(_tableDest.AsSpan(TableEntryCount));
        shares.CopyTo(_tableShare.AsSpan(TableEntryCount));
        Rows.Add(row, (TableEntryCount, destinations.Length));
        TableEntryCount += destinations.Length;
    }

    /// <summary>Builds the edge arrays and sorts the serving pairs; call once after adding everything.</summary>
    public void Seal()
    {
        _rawEdges.Sort(EdgeOrder.Comparison);
        if (_edgeStart.Length < NodeCount + 1)
        {
            _edgeStart = new int[Math.Max(NodeCount + 1, _edgeStart.Length * 2)];
        }

        if (_edgeTo.Length < _rawEdges.Count)
        {
            var size = Math.Max(_rawEdges.Count, _edgeTo.Length * 2);
            _edgeTo = new int[size];
            _edgeTicks = new int[size];
            _edgeCapacity = new int[size];
        }

        EdgeCount = 0;
        var e = 0;
        for (var node = 0; node < NodeCount; node++)
        {
            _edgeStart[node] = EdgeCount;
            while (e < _rawEdges.Count && _rawEdges[e].From == node)
            {
                var (_, to, ticks, capacity) = _rawEdges[e++];
                if (EdgeCount > _edgeStart[node] && _edgeTo[EdgeCount - 1] == to)
                {
                    _edgeTicks[EdgeCount - 1] = Math.Min(_edgeTicks[EdgeCount - 1], ticks);
                    _edgeCapacity[EdgeCount - 1] = (int)Math.Min(int.MaxValue, (long)_edgeCapacity[EdgeCount - 1] + capacity);
                    continue;
                }

                _edgeTo[EdgeCount] = to;
                _edgeTicks[EdgeCount] = ticks;
                _edgeCapacity[EdgeCount] = capacity;
                EdgeCount++;
            }
        }

        _edgeStart[NodeCount] = EdgeCount;

        // Serving pairs by town, then node, without duplicates.
        if (_pairs.Length < ServingCount)
        {
            _pairs = new long[Math.Max(ServingCount, _pairs.Length * 2)];
        }

        var pairs = _pairs;
        for (var i = 0; i < ServingCount; i++)
        {
            pairs[i] = ((long)_servingTown[i] << 32) | (uint)_servingNode[i];
        }

        Array.Sort(pairs, 0, ServingCount);
        var n = 0;
        var servingPairs = ServingCount;
        for (var i = 0; i < servingPairs; i++)
        {
            if (n > 0 && pairs[i] == (((long)_servingTown[n - 1] << 32) | (uint)_servingNode[n - 1]))
            {
                continue;
            }

            _servingTown[n] = (int)(pairs[i] >> 32);
            _servingNode[n] = (int)pairs[i];
            n++;
        }

        ServingCount = n;
    }

    private static void Grow(ref int[] a, ref int[] b, int needed)
    {
        if (needed <= a.Length)
        {
            return;
        }

        var size = Math.Max(needed, a.Length * 2);
        Array.Resize(ref a, size);
        Array.Resize(ref b, size);
    }

    private static void Grow(ref int[] a, ref int[] b, ref int[] c, int needed)
    {
        if (needed <= a.Length)
        {
            return;
        }

        var size = Math.Max(needed, a.Length * 2);
        Array.Resize(ref a, size);
        Array.Resize(ref b, size);
        Array.Resize(ref c, size);
    }

    private sealed class EdgeOrder : IComparer<(int From, int To, int Ticks, int Capacity)>
    {
        public static readonly EdgeOrder Instance = new();

        /// <summary>Cached delegate: sorting with an <see cref="IComparer{T}"/> would allocate one per call.</summary>
        public static readonly Comparison<(int From, int To, int Ticks, int Capacity)> Comparison = Instance.Compare;

        public int Compare((int From, int To, int Ticks, int Capacity) a, (int From, int To, int Ticks, int Capacity) b) =>
            a.From != b.From ? a.From.CompareTo(b.From) : a.To != b.To ? a.To.CompareTo(b.To) : a.Ticks.CompareTo(b.Ticks);
    }
}
