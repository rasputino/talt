using Talt.Core.Determinism;
using Talt.Core.Simulation;
using Talt.Core.Stations;

namespace Talt.Core.Demand;

/// <summary>
/// Keeps the routes of destination cargo up to date (feature 013, research R3–R8, data-model.md CargoRouter
/// schedule). Every <see cref="DemandRules.UpdateIntervalDays"/> — or as soon as lines, vehicles or stations change —
/// it builds a <see cref="RoutingProblem"/> per company and destination cargo from the registered
/// <see cref="ILinkSource"/>s and the stations, and computes the routes: on a worker thread when the tick is parallel,
/// inline when it is sequential. The result is applied at exactly start tick + <see cref="DemandRules.ApplyDelayDays"/>
/// (the main thread waits for the worker if needed), so the simulation never depends on thread timing.
/// </summary>
public sealed partial class CargoRouter : IStateHashable, IThreadPoolWorkItem
{
    private readonly DemandRules _rules;
    private readonly StationRegistry _stations;
    private readonly DestinationTables _tables;
    private readonly List<ILinkSource> _sources = [];
    private readonly LinkCollector _collector = new();
    private readonly RouteAssignment _assignment;
    private readonly object _jobLock = new();
    private bool _jobRunning;
    private readonly List<RoutingProblem> _problemPool = [];
    private readonly List<RouteTable> _nextPool = [];
    private readonly List<int> _stopGroups = [];
    private int[] _stopKey = new int[256];
    private int[] _stopX = new int[256];
    private int[] _stopZ = new int[256];
    private readonly List<int> _keys = [];
    private List<RouteTable> _current = [];
    private List<RouteTable> _spare = [];
    private int _problemCount;

    // Schedule (hashed once started).
    private long _nextStartTick;
    private long _pendingApplyTick = -1;
    private int _startedVersion = int.MinValue;
    private bool _everStarted;
    private int _buildStage;
    private bool _parallelJob;
    private int _applied;

    public CargoRouter(DemandRules rules, StationRegistry stations, DestinationTables tables)
    {
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _stations = stations ?? throw new ArgumentNullException(nameof(stations));
        _tables = tables ?? throw new ArgumentNullException(nameof(tables));
        _assignment = new RouteAssignment(rules);
        _nextStartTick = 1;
    }

    public DemandRules Rules => _rules;

    public DestinationTables Tables => _tables;

    /// <summary>Bumped at every applied route set (windows refresh). Not hashed.</summary>
    public int Version { get; private set; }

    /// <summary>Route sets applied so far.</summary>
    public int AppliedCount => _applied;

    /// <summary>True once a route job was started (from then on the router is part of the fingerprint).</summary>
    public bool EverStarted => _everStarted;

    /// <summary>The tick the pending job applies at, or −1.</summary>
    public long PendingApplyTick => _pendingApplyTick;

    /// <summary>Destinations routed by the last job (statistics).</summary>
    public int LastDestinations { get; private set; }

    /// <summary>Registers a transport mode's lines (road vehicles; rail in feature 015).</summary>
    public void AddSource(ILinkSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _sources.Add(source);
    }

    /// <summary>The current routes of a company and cargo, or null.</summary>
    public RouteTable? TableFor(int company, int cargo)
    {
        foreach (var table in _current)
        {
            if (table.Company == company && table.Cargo == cargo)
            {
                return table;
            }
        }

        return null;
    }

    /// <summary>Next groups from a group towards a town (window queries).</summary>
    public bool TryNextHop(int company, int cargo, int groupKey, int town, out int hop1, out int hop2, out int share2PerMille)
    {
        var table = TableFor(company, cargo);
        var i = table?.Find(groupKey, town) ?? -1;
        hop1 = i >= 0 ? table!.Hop1At(i) : -1;
        hop2 = i >= 0 ? table!.Hop2At(i) : -1;
        share2PerMille = i >= 0 ? table!.Share2At(i) : 0;
        return i >= 0;
    }

    /// <summary>Called after every tick's vehicle step: applies a finished job at its tick, starts new ones, re-routes a slice of waiting cargo.</summary>
    public void Tick(long tick, bool parallel)
    {
        if (_pendingApplyTick >= 0 && tick >= _pendingApplyTick)
        {
            Apply();
        }

        if (_buildStage > 0)
        {
            ContinueBuild(tick, parallel);
        }
        else if (_pendingApplyTick < 0)
        {
            var version = SourceVersion();
            if (tick >= _nextStartTick || version != _startedVersion)
            {
                BeginBuild(tick, version);
            }
        }

        _stations.RerouteSlice(Math.Max(64, _stations.BucketCount / Math.Max(1, _rules.ApplyDelayDays * GameClock.TicksPerDay)));
    }

    /// <summary>Benchmarks: builds the routing problems from the current lines (main-thread part of a job start).</summary>
    internal int BuildProblemsNow()
    {
        WaitForJob();
        Collect();
        for (var i = 0; i < _problemCount; i++)
        {
            BuildGraph(_problemPool[i]);
            AddDemand(_problemPool[i]);
            _problemPool[i].Seal();
        }

        return _problemCount;
    }

    /// <summary>Benchmarks: runs the assignment of the problems last built (the worker part of a job).</summary>
    internal void SolveNow() => Compute();

    /// <summary>Benchmarks: true when the router does more than a re-route slice at <paramref name="tick"/>.</summary>
    internal bool WorkDue(long tick) => _buildStage > 0 || (_pendingApplyTick >= 0 && tick >= _pendingApplyTick) || (_pendingApplyTick < 0 && tick >= _nextStartTick);

    /// <summary>Statistics of the problems last built: nodes and edges summed.</summary>
    internal (int Nodes, int Edges) ProblemSize()
    {
        int nodes = 0, edges = 0;
        for (var i = 0; i < _problemCount; i++)
        {
            nodes += _problemPool[i].NodeCount;
            edges += _problemPool[i].EdgeCount;
        }

        return (nodes, edges);
    }

    /// <summary>Waits for a running job (used before disposing or by tests).</summary>
    public void WaitForJob()
    {
        lock (_jobLock)
        {
            while (_jobRunning)
            {
                Monitor.Wait(_jobLock);
            }
        }
    }

    public void AppendTo(StateHasher hasher)
    {
        ArgumentNullException.ThrowIfNull(hasher);
        if (!_everStarted)
        {
            return; // No line ever carried destination cargo: earlier games keep their fingerprints (FR-026).
        }

        hasher.Append(_rules.Fingerprint);
        hasher.Append(_nextStartTick);
        hasher.Append(_pendingApplyTick);
        hasher.Append(_buildStage);
        hasher.Append(_applied);
        hasher.Append(_current.Count);
        foreach (var table in _current)
        {
            hasher.Append(table);
        }

        hasher.Append(_tables);
    }

    void IThreadPoolWorkItem.Execute()
    {
        try
        {
            Compute();
        }
        finally
        {
            lock (_jobLock)
            {
                _jobRunning = false;
                Monitor.PulseAll(_jobLock);
            }
        }
    }

    private int SourceVersion()
    {
        var version = _stations.Version;
        foreach (var source in _sources)
        {
            version = unchecked((version * 31) + source.LinkVersion);
        }

        return version;
    }

    /// <summary>
    /// First stage of a job start: collects the lines and prepares one problem per (company, cargo). The problems are
    /// built over the next ticks (<see cref="ContinueBuild"/>) so no tick pays the whole main-thread cost; every stage
    /// runs at a fixed tick, so the result is still deterministic.
    /// </summary>
    private void BeginBuild(long tick, int version)
    {
        _startedVersion = version;
        _nextStartTick = tick + ((long)_rules.UpdateIntervalDays * GameClock.TicksPerDay);
        Collect();
        if (_problemCount == 0 && !_everStarted)
        {
            return; // Nothing to route yet.
        }

        _everStarted = true;
        _buildStage = 1;
    }

    /// <summary>Later stages: graph (nodes, edges), then served towns and flows, then sealing and the job's start.</summary>
    private void ContinueBuild(long tick, bool parallel)
    {
        switch (_buildStage)
        {
            case 1:
                for (var i = 0; i < _problemCount; i++)
                {
                    BuildGraph(_problemPool[i]);
                }

                _buildStage = 2;
                break;
            case 2:
                for (var i = 0; i < _problemCount; i++)
                {
                    AddDemand(_problemPool[i]);
                }

                _buildStage = 3;
                break;
            default:
                for (var i = 0; i < _problemCount; i++)
                {
                    _problemPool[i].Seal();
                }

                _buildStage = 0;
                Launch(tick, parallel);
                break;
        }
    }

    /// <summary>Starts (or runs) the job on the sealed problems; it applies at a fixed later tick.</summary>
    private void Launch(long tick, bool parallel)
    {
        _pendingApplyTick = tick + ((long)_rules.ApplyDelayDays * GameClock.TicksPerDay);
        lock (_jobLock)
        {
            _jobRunning = true;
        }

        _parallelJob = parallel;
        if (parallel)
        {
            ThreadPool.UnsafeQueueUserWorkItem(this, preferLocal: false);
        }
        else
        {
            ((IThreadPoolWorkItem)this).Execute();
        }
    }

    /// <summary>The job: a pure function of the problems (worker thread or inline).</summary>
    private void Compute()
    {
        var destinations = 0;
        for (var i = 0; i < _problemCount; i++)
        {
            while (_nextPool.Count <= i)
            {
                _nextPool.Add(new RouteTable());
            }

            _assignment.Solve(_problemPool[i], _nextPool[i], _parallelJob);
            destinations += _assignment.LastDestinations;
        }

        LastDestinations = destinations;
    }

    /// <summary>Swaps the job's tables in at the apply tick and starts re-routing waiting cargo.</summary>
    private void Apply()
    {
        WaitForJob();
        _pendingApplyTick = -1;
        var fresh = _spare;
        fresh.Clear();
        for (var i = 0; i < _problemCount; i++)
        {
            var table = _nextPool[i];
            // Split carries start again at zero (copying them would cost a search per entry on this thread).
            fresh.Add(table);
        }

        // The old tables become the pool of the next job.
        _nextPool.RemoveRange(0, _problemCount);
        _nextPool.AddRange(_current);
        _current.Clear();
        _spare = _current;
        _current = fresh;
        _applied++;
        Version++;
        _stations.BeginReroute();
    }

    /// <summary>Collects every source's lines and prepares one problem per (company, destination cargo), in ascending order.</summary>
    private void Collect()
    {
        _collector.Clear();
        foreach (var source in _sources)
        {
            source.CollectLinks(_collector);
        }

        _problemCount = 0;
        _keys.Clear();
        for (var line = 0; line < _collector.LineCount; line++)
        {
            if (!_rules.IsDirected(_collector.CargoOf(line)))
            {
                continue;
            }

            var key = (_collector.CompanyOf(line) << 8) | _collector.CargoOf(line);
            if (!_keys.Contains(key))
            {
                _keys.Add(key);
            }
        }

        _keys.Sort();

        // Every stop's group key and centre, looked up once (research R3; most of a job start's main-thread time).
        if (_stopKey.Length < _collector.StopCount)
        {
            var size = Math.Max(_collector.StopCount, _stopKey.Length * 2);
            _stopKey = new int[size];
            _stopX = new int[size];
            _stopZ = new int[size];
        }

        for (var s = 0; s < _collector.StopCount; s++)
        {
            var station = _collector.StationOf(s);
            _stopKey[s] = _stations.GroupKeyOfStation(station);
            if (!_stations.TryGetCentre(station, out _stopX[s], out _stopZ[s]))
            {
                _stopKey[s] = -1;
            }
        }

        foreach (var key in _keys)
        {
            if (_problemPool.Count <= _problemCount)
            {
                _problemPool.Add(new RoutingProblem());
            }

            _problemPool[_problemCount++].Reset(key >> 8, key & 0xFF);
        }
    }

    /// <summary>Nodes and edges of a problem from the collected lines.</summary>
    private void BuildGraph(RoutingProblem problem)
    {
        var company = problem.Company;
        var cargo = problem.Cargo;

        // Nodes: every group visited by the company's lines of this cargo.
        _stopGroups.Clear();
        for (var line = 0; line < _collector.LineCount; line++)
        {
            if (_collector.CompanyOf(line) != company || _collector.CargoOf(line) != cargo)
            {
                continue;
            }

            var first = _collector.FirstStopOf(line);
            for (var s = first; s < first + _collector.StopCountOf(line); s++)
            {
                var key = _stopKey[s];
                if (key >= 0)
                {
                    _stopGroups.Add(key);
                }
            }
        }

        _stopGroups.Sort();
        var previous = -1;
        foreach (var key in _stopGroups)
        {
            if (key != previous && _stations.TryGetCentre(key, out var x, out var z))
            {
                problem.AddNode(key, x, z);
                previous = key;
            }
        }

        // Edges: from each stop where cargo boards to the next stop (of another group) where it can leave.
        for (var line = 0; line < _collector.LineCount; line++)
        {
            if (_collector.CompanyOf(line) == company && _collector.CargoOf(line) == cargo)
            {
                AddLineEdges(problem, line);
            }
        }

    }

    /// <summary>The towns each node serves and the estimated production at each node.</summary>
    private void AddDemand(RoutingProblem problem)
    {
        for (var node = 0; node < problem.NodeCount; node++)
        {
            _stations.AddServing(problem.Key[node], problem.Cargo, problem, node);
        }

        _stations.AddFlows(problem.Cargo, problem);
    }

    private void AddLineEdges(RoutingProblem problem, int line)
    {
        var first = _collector.FirstStopOf(line);
        var count = _collector.StopCountOf(line);
        var tptm = _collector.TicksPerTileMilliOf(line);

        // Round trip: every leg's distance plus a dwell per stop.
        long roundTicks = 0;
        for (var i = 0; i < count; i++)
        {
            roundTicks += (LegTiles(first + i, first + ((i + 1) % count)) * (long)tptm / 1000) + _rules.DwellTicks;
        }

        var ticksPerMonth = 30L * GameClock.TicksPerDay;
        var capacity = (int)Math.Min(int.MaxValue, _collector.UnitsPerRoundOf(line) * ticksPerMonth / Math.Max(1, roundTicks));
        for (var i = 0; i < count; i++)
        {
            var from = first + i;
            if (!_collector.Loads(from))
            {
                continue;
            }

            var fromNode = _stopKey[from] < 0 ? -1 : problem.NodeOf(_stopKey[from]);
            if (fromNode < 0)
            {
                continue;
            }

            long tiles = 0;
            for (var k = 1; k < count; k++)
            {
                var a = first + ((i + k - 1) % count);
                var b = first + ((i + k) % count);
                tiles += LegTiles(a, b);
                var toNode = _stopKey[b] < 0 ? -1 : problem.NodeOf(_stopKey[b]);
                if (toNode < 0 || toNode == fromNode || !_collector.Unloads(b))
                {
                    continue;
                }

                problem.AddEdge(fromNode, toNode, (int)Math.Min(int.MaxValue / 2, tiles * tptm / 1000), capacity);
                break;
            }
        }
    }

    private int LegTiles(int stopA, int stopB) =>
        _stopKey[stopA] < 0 || _stopKey[stopB] < 0 ? 0 : Math.Abs(_stopX[stopA] - _stopX[stopB]) + Math.Abs(_stopZ[stopA] - _stopZ[stopB]);
}
