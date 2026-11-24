using BenchmarkDotNet.Attributes;
using Talt.Client.Rendering;
using Talt.Core.Construction;
using Talt.Core.Maps;
using Talt.Core.Network;
using Talt.Core.Roads;

namespace Talt.Benchmarks;

/// <summary>
/// Feature 007 budgets (research R11): route queries across a fully connected 4096 × 4096 road network
/// (a road every 16 tiles in both directions: 65 536 junctions), the incremental network update after a
/// 100-tile road, a full rebuild, and the road tool's preview of a 200-tile drag on a hilly map.
/// </summary>
[MemoryDiagnoser]
public class RoadNetworkBenchmarks
{
    private const int Queries = 256;

    private ConstructionState _grid = null!;
    private RoadRouter _router = null!;
    private RoadRouter _cachedRouter = null!;
    private (int From, int To)[] _queries = null!;
    private readonly List<RouteStep> _steps = [];
    private readonly ConstructionPlan _build = new();
    private readonly ConstructionPlan _remove = new();
    private long _tick;

    private ConstructionState _hilly = null!;
    private readonly ConstructionPlanner _planner = new();
    private readonly ConstructionPlan _preview = new();
    private (int X, int Z) _dragStart;
    private (int X, int Z) _dragEnd;

    [GlobalSetup]
    public void Setup()
    {
        _grid = Flat(4096);
        _grid.SeedDebugGrid(16);
        _router = new RoadRouter(_grid.Network, useCache: false);
        _cachedRouter = new RoadRouter(_grid.Network, useCache: true);

        // Random far-apart junction pairs (at least a quarter of the map apart).
        var random = new Random(42);
        var nodes = _grid.Network.NodeTiles();
        var width = _grid.Map.Width;
        var pairs = new List<(int, int)>();
        while (pairs.Count < Queries)
        {
            var a = nodes[random.Next(nodes.Count)];
            var b = nodes[random.Next(nodes.Count)];
            if (Math.Abs((a % width) - (b % width)) + Math.Abs((a / width) - (b / width)) >= 1024)
            {
                pairs.Add((a, b));
            }
        }

        _queries = [.. pairs];
        foreach (var (from, to) in _queries)
        {
            _cachedRouter.TryFindRoute(from, to, _steps, out _);
        }

        // A 100-tile road between two grid lines, crossing six of them, and its removal.
        var planner = new ConstructionPlanner();
        planner.Roads.PlanRoad(_grid, Company.PlayerId, _grid.RoadRules.FindType("paved"), (1000, 1004), (1099, 1004), _build);
        Require(_build);
        _grid.Commit(_build, _grid.Player, _tick++);
        planner.Roads.PlanRemoval(_grid, Company.PlayerId, (1000, 1004), (1099, 1004), _remove);
        Require(_remove);
        _grid.Commit(_remove, _grid.Player, _tick++);

        // A 200-tile drag on a hilly generated map whose preview is valid.
        _hilly = new ConstructionState(ConstructionRules.Default with { StartingBalance = long.MaxValue / 4 },
            MapGenerator.Generate(new MapSettings(1024, 1024, Seed: 11, Roughness: 70, MaxHeight: 48, WaterAmount: 10)));
        var type = _hilly.RoadRules.FindType("paved");
        for (var z = 100; z < 900 && _dragEnd == default; z += 37)
        {
            for (var x = 100; x < 700 && _dragEnd == default; x += 41)
            {
                _planner.Roads.PlanRoad(_hilly, Company.PlayerId, type, (x, z), (x + 160, z + 120), _preview);
                if (_preview.IsValid)
                {
                    (_dragStart, _dragEnd) = ((x, z), (x + 160, z + 120));
                }
            }
        }

        if (_dragEnd == default)
        {
            throw new InvalidOperationException("No valid 200-tile drag found on the hilly map.");
        }
    }

    /// <summary>SC-002: average of 256 whole-map route searches (no cache).</summary>
    [Benchmark(OperationsPerInvoke = Queries)]
    public int LongRouteQuery()
    {
        var found = 0;
        foreach (var (from, to) in _queries)
        {
            if (_router.TryFindRoute(from, to, _steps, out _))
            {
                found++;
            }
        }

        return found;
    }

    /// <summary>The same queries served by the route cache.</summary>
    [Benchmark(OperationsPerInvoke = Queries)]
    public int CachedRouteQuery()
    {
        var found = 0;
        foreach (var (from, to) in _queries)
        {
            if (_cachedRouter.TryFindRoute(from, to, _steps, out _))
            {
                found++;
            }
        }

        return found;
    }

    /// <summary>SC-003: building and removing a 100-tile road (tiles, undo record and incremental network update), per action.</summary>
    [Benchmark(OperationsPerInvoke = 2)]
    public int IncrementalUpdate()
    {
        _grid.Commit(_build, _grid.Player, _tick++);
        _grid.Commit(_remove, _grid.Player, _tick++);
        return _grid.Network.LastRebuiltRegions;
    }

    /// <summary>Building the whole 4096 network from scratch (game creation), for comparison.</summary>
    [Benchmark]
    public int FullBuild() => RoadNetwork.Build(_grid.Roads).NodeCount;

    /// <summary>SC-004: the road tool's preview of a 200-tile drag on a hilly map.</summary>
    [Benchmark]
    public bool PlanRoad200()
    {
        _planner.Roads.PlanRoad(_hilly, Company.PlayerId, 1, _dragStart, _dragEnd, _preview);
        return _preview.IsValid;
    }

    internal static ConstructionState Flat(int size)
    {
        var count = size * size;
        var heights = new byte[count];
        Array.Fill(heights, (byte)10);
        var flags = new byte[count];
        Array.Fill(flags, (byte)TileTraits.Buildable);
        var map = new MapData(new MapSettings(size, size, 1), heights, new byte[count], new byte[count], flags, new byte[count], [], default);
        return new ConstructionState(ConstructionRules.Default with { StartingBalance = long.MaxValue / 4, MaxTilesPerAction = 1_000_000 }, map);
    }

    private static void Require(ConstructionPlan plan)
    {
        if (!plan.IsValid)
        {
            throw new InvalidOperationException($"Benchmark plan refused: {plan.Reason}");
        }
    }
}

/// <summary>Road meshing (research R10): a dense block in full detail and a 128 × 128 area simplified.</summary>
[MemoryDiagnoser]
public class RoadMeshBenchmarks
{
    private RoadMesher _mesher = null!;

    [GlobalSetup]
    public void Setup()
    {
        var state = RoadNetworkBenchmarks.Flat(1024);
        state.SeedDebugGrid(8);
        _mesher = new RoadMesher(state.Roads, state.Map, state.RoadRules);
        _mesher.BuildDetail(256, 256, 288, 288);
    }

    /// <summary>A 32 × 32-tile block with a road every 8 tiles (about 250 road tiles).</summary>
    [Benchmark]
    public int DetailBlock()
    {
        _mesher.BuildDetail(256, 256, 288, 288);
        return _mesher.TriangleCount;
    }

    /// <summary>A 128 × 128-tile area simplified (the far version).</summary>
    [Benchmark]
    public int SimplifiedArea()
    {
        _mesher.BuildSimplified(256, 256, 384, 384);
        return _mesher.TriangleCount;
    }
}
