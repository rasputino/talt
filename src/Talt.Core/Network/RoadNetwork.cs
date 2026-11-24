using Talt.Core.Roads;

namespace Talt.Core.Network;

/// <summary>One edge of <see cref="RoadNetwork"/> in tile terms, for tests and tools (research R5).</summary>
public readonly record struct RoadEdgeInfo(int FromTile, int FirstDirection, int ToTile, int Length, int SpeedKmh);

/// <summary>
/// The routing graph of the roads (research R5, data-model.md RoadNetwork), derived from a
/// <see cref="RoadLayer"/> and updated incrementally.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nodes</b> are road tiles where something changes: not exactly two links (junctions, dead ends,
/// isolated pieces), a linked neighbour of another road type, or a motorway connector. <b>Edges</b> are the
/// chains of two-link tiles between nodes, one per allowed travel direction, with their length in units,
/// the slowest speed limit along them (km/h, 0 = none) and the first direction (so the tiles can be
/// retraced). A node-less loop (a ring with no junction) has no nodes and therefore no routes. Everything is
/// a pure function of the tile data, so an incrementally updated network equals one built from scratch
/// (<see cref="Canonical"/>).
/// </para>
/// <para>
/// Node ids are stable (free list) while a node exists. The out-edges of the nodes of each 64 × 64-tile
/// region live in flat arrays owned by the region (CSR). A change rebuilds only the regions whose nodes'
/// edges may have changed: the regions of the changed tiles and of the nodes at the ends of every chain
/// through a changed tile or its linked neighbours, walked before and after the change.
/// </para>
/// </remarks>
public sealed class RoadNetwork
{
    public const int RegionBits = 6;
    public const int RegionSize = 1 << RegionBits;

    private readonly RoadLayer _layer;
    private readonly int _regionsX;
    private readonly Region?[] _regions;
    private readonly Dictionary<int, int> _nodeOfTile = [];
    private readonly bool[] _dirty;
    private readonly List<int> _dirtyRegions = [];
    private readonly List<int> _scratchNodes = [];

    private int[] _nodeTile = new int[256];
    private int[] _nodeRegion = new int[256];
    private int[] _nodeFirst = new int[256];
    private int[] _nodeCount = new int[256];
    private int[] _free = new int[64];
    private int _freeCount;
    private int _nodeHigh;

    public RoadNetwork(RoadLayer layer)
    {
        _layer = layer ?? throw new ArgumentNullException(nameof(layer));
        _regionsX = (layer.Width + RegionSize - 1) >> RegionBits;
        var regionsZ = (layer.Depth + RegionSize - 1) >> RegionBits;
        _regions = new Region?[_regionsX * regionsZ];
        _dirty = new bool[_regions.Length];
    }

    public RoadLayer Layer => _layer;

    /// <summary>Incremented by every update (route caches compare it).</summary>
    public int Version { get; private set; }

    /// <summary>Number of nodes.</summary>
    public int NodeCount { get; private set; }

    /// <summary>Upper bound of node ids (arrays indexed by node need this size).</summary>
    public int NodeCapacity => _nodeHigh;

    /// <summary>Number of edges.</summary>
    public int EdgeCount { get; private set; }

    /// <summary>Regions rebuilt by the last update (benchmarks, tests).</summary>
    public int LastRebuiltRegions { get; private set; }

    /// <summary>Builds the whole graph from the layer (game creation, tests).</summary>
    public static RoadNetwork Build(RoadLayer layer)
    {
        var network = new RoadNetwork(layer);
        network.BuildAll();
        return network;
    }

    /// <summary>Rebuilds every region that has or had roads.</summary>
    public void BuildAll()
    {
        var width = _layer.Width;
        var types = _layer.Types;
        for (var tile = 0; tile < types.Length; tile++)
        {
            if (types[tile] != 0)
            {
                MarkDirty(RegionOf(tile % width, tile / width));
            }
        }

        for (var r = 0; r < _regions.Length; r++)
        {
            if (_regions[r] is { NodeCount: > 0 })
            {
                MarkDirty(r);
            }
        }

        RebuildDirty();
    }

    public bool TryGetNode(int tile, out int node) => _nodeOfTile.TryGetValue(tile, out node);

    public int NodeTile(int node) => _nodeTile[node];

    public int OutEdgeCount(int node) => _nodeCount[node];

    /// <summary>Target nodes of the out-edges of a node.</summary>
    public ReadOnlySpan<int> OutTargets(int node) => _regions[_nodeRegion[node]]!.EdgeTo.AsSpan(_nodeFirst[node], _nodeCount[node]);

    /// <summary>Lengths (units) of the out-edges of a node, in the order of <see cref="OutTargets"/>.</summary>
    public ReadOnlySpan<int> OutLengths(int node) => _regions[_nodeRegion[node]]!.EdgeLength.AsSpan(_nodeFirst[node], _nodeCount[node]);

    /// <summary>Speed limits (km/h, 0 = none) of the out-edges of a node.</summary>
    public ReadOnlySpan<int> OutSpeeds(int node) => _regions[_nodeRegion[node]]!.EdgeSpeed.AsSpan(_nodeFirst[node], _nodeCount[node]);

    /// <summary>First directions of the out-edges of a node.</summary>
    public ReadOnlySpan<byte> OutFirstDirections(int node) => _regions[_nodeRegion[node]]!.EdgeFirstDir.AsSpan(_nodeFirst[node], _nodeCount[node]);

    /// <summary>
    /// Call before road pieces change, with the tiles about to change: marks the regions whose edges run
    /// through them (walked on the old data).
    /// </summary>
    public void BeforeChange(ReadOnlySpan<int> tiles) => MarkAround(tiles);

    /// <summary>Call after the pieces changed, with the same tiles: marks again on the new data and rebuilds.</summary>
    public void AfterChange(ReadOnlySpan<int> tiles)
    {
        MarkAround(tiles);
        RebuildDirty();
    }

    /// <summary>Every edge in tile terms, sorted: equal for equal road layers however they were built.</summary>
    public List<RoadEdgeInfo> Canonical()
    {
        var result = new List<RoadEdgeInfo>(EdgeCount);
        for (var node = 0; node < _nodeHigh; node++)
        {
            if (_nodeTile[node] < 0)
            {
                continue;
            }

            var region = _regions[_nodeRegion[node]]!;
            for (var e = _nodeFirst[node]; e < _nodeFirst[node] + _nodeCount[node]; e++)
            {
                result.Add(new RoadEdgeInfo(_nodeTile[node], region.EdgeFirstDir[e], _nodeTile[region.EdgeTo[e]], region.EdgeLength[e], region.EdgeSpeed[e]));
            }
        }

        result.Sort((a, b) => a.FromTile != b.FromTile ? a.FromTile.CompareTo(b.FromTile)
            : a.FirstDirection != b.FirstDirection ? a.FirstDirection.CompareTo(b.FirstDirection)
            : a.ToTile.CompareTo(b.ToTile));
        return result;
    }

    /// <summary>Node tiles, sorted (tests).</summary>
    public List<int> NodeTiles()
    {
        var result = new List<int>(NodeCount);
        for (var node = 0; node < _nodeHigh; node++)
        {
            if (_nodeTile[node] >= 0)
            {
                result.Add(_nodeTile[node]);
            }
        }

        result.Sort();
        return result;
    }

    /// <summary>True when the tile is a node of the graph (a pure function of the tile data).</summary>
    public bool IsNodeTile(int tile)
    {
        var layer = _layer;
        if (!layer.HasRoad(tile))
        {
            return false;
        }

        var links = layer.LinksAt(tile);
        if (Dir8.Count(links) != 2 || (layer.FlagsAt(tile) & RoadFeatures.Connector) != 0)
        {
            return true;
        }

        var type = layer.TypeAt(tile);
        for (var d = 0; d < 8; d++)
        {
            if ((links & (1 << d)) != 0 && layer.TypeAt(layer.Follow(tile, d)) != type)
            {
                return true;
            }
        }

        return false;
    }

    private int RegionOf(int x, int z) => (x >> RegionBits) + ((z >> RegionBits) * _regionsX);

    private int RegionOfTile(int tile) => RegionOf(tile % _layer.Width, tile / _layer.Width);

    private void MarkDirty(int region)
    {
        if (!_dirty[region])
        {
            _dirty[region] = true;
            _dirtyRegions.Add(region);
        }
    }

    private void MarkAround(ReadOnlySpan<int> tiles)
    {
        var layer = _layer;
        foreach (var tile in tiles)
        {
            MarkDirty(RegionOfTile(tile));
            Walk(tile);
            var links = layer.LinksAt(tile);
            for (var d = 0; d < 8; d++)
            {
                if ((links & (1 << d)) != 0)
                {
                    var next = layer.Follow(tile, d);
                    MarkDirty(RegionOfTile(next));
                    Walk(next);
                }
            }
        }
    }

    /// <summary>Marks the regions of the nodes at the ends of every chain through a tile.</summary>
    private void Walk(int tile)
    {
        var layer = _layer;
        if (!layer.HasRoad(tile))
        {
            return;
        }

        var links = layer.LinksAt(tile);
        if (IsNodeTile(tile))
        {
            MarkDirty(RegionOfTile(tile));
            for (var d = 0; d < 8; d++)
            {
                if ((links & (1 << d)) != 0)
                {
                    MarkDirty(RegionOfTile(ChainEnd(tile, d, out _, out _, out _)));
                }
            }

            return;
        }

        for (var d = 0; d < 8; d++)
        {
            if ((links & (1 << d)) != 0)
            {
                var end = ChainEnd(tile, d, out _, out _, out var looped);
                if (!looped)
                {
                    MarkDirty(RegionOfTile(end));
                }
            }
        }
    }

    /// <summary>
    /// Follows the chain from <paramref name="tile"/> leaving towards <paramref name="direction"/> until a node.
    /// </summary>
    /// <param name="length">Total length in units.</param>
    /// <param name="exitsAll">True when travel is allowed along the whole chain in this direction.</param>
    /// <param name="looped">True when the chain came back to its start without a node (a ring).</param>
    /// <returns>The node tile reached (or the start tile for a ring).</returns>
    private int ChainEnd(int tile, int direction, out long length, out bool exitsAll, out bool looped)
    {
        var layer = _layer;
        length = 0;
        exitsAll = (layer.ExitsAt(tile) & (1 << direction)) != 0;
        looped = false;
        var current = tile;
        var dir = direction;
        var guard = layer.RoadTileCount + 2;
        while (guard-- > 0)
        {
            length += layer.LinkLength(current, dir);
            var next = layer.Follow(current, dir);
            if (next == tile && !IsNodeTile(tile))
            {
                looped = true;
                return tile;
            }

            if (IsNodeTile(next))
            {
                return next;
            }

            var arrival = Dir8.Opposite(dir);
            var other = layer.LinksAt(next) & ~(1 << arrival);
            dir = System.Numerics.BitOperations.TrailingZeroCount(other);
            exitsAll &= (layer.ExitsAt(next) & (1 << dir)) != 0;
            current = next;
        }

        looped = true;
        return tile;
    }

    private void RebuildDirty()
    {
        if (_dirtyRegions.Count == 0)
        {
            Version++;
            LastRebuiltRegions = 0;
            return;
        }

        // Pass 1: node sets of all dirty regions (ids of surviving nodes are kept).
        foreach (var r in _dirtyRegions)
        {
            RebuildNodes(r);
        }

        // Pass 2: out-edges of the nodes of the dirty regions; every target is registered by now.
        foreach (var r in _dirtyRegions)
        {
            RebuildEdges(r);
            _dirty[r] = false;
        }

        LastRebuiltRegions = _dirtyRegions.Count;
        _dirtyRegions.Clear();
        Version++;
    }

    private void RebuildNodes(int r)
    {
        var region = _regions[r];
        var width = _layer.Width;
        var x0 = (r % _regionsX) << RegionBits;
        var z0 = (r / _regionsX) << RegionBits;
        var x1 = Math.Min(x0 + RegionSize, width);
        var z1 = Math.Min(z0 + RegionSize, _layer.Depth);

        _scratchNodes.Clear();
        for (var z = z0; z < z1; z++)
        {
            for (var x = x0; x < x1; x++)
            {
                var tile = x + (z * width);
                if (IsNodeTile(tile))
                {
                    _scratchNodes.Add(tile);
                }
            }
        }

        // Free the nodes that disappeared.
        if (region is not null)
        {
            for (var i = 0; i < region.NodeCount; i++)
            {
                var node = region.Nodes[i];
                if (_scratchNodes.BinarySearch(_nodeTile[node]) < 0)
                {
                    EdgeCount -= _nodeCount[node];
                    _nodeOfTile.Remove(_nodeTile[node]);
                    _nodeTile[node] = -1;
                    _nodeCount[node] = 0;
                    Free(node);
                    NodeCount--;
                }
            }
        }

        if (_scratchNodes.Count == 0)
        {
            if (region is not null)
            {
                region.NodeCount = 0;
                region.EdgeCount = 0;
            }

            return;
        }

        region ??= _regions[r] = new Region();
        region.EnsureNodes(_scratchNodes.Count);
        region.NodeCount = _scratchNodes.Count;
        for (var i = 0; i < _scratchNodes.Count; i++)
        {
            var tile = _scratchNodes[i];
            if (!_nodeOfTile.TryGetValue(tile, out var node))
            {
                node = Allocate();
                _nodeTile[node] = tile;
                _nodeRegion[node] = r;
                _nodeCount[node] = 0;
                _nodeOfTile.Add(tile, node);
                NodeCount++;
            }

            region.Nodes[i] = node;
        }
    }

    private void RebuildEdges(int r)
    {
        var region = _regions[r];
        if (region is null)
        {
            return;
        }

        var layer = _layer;
        var count = 0;
        for (var i = 0; i < region.NodeCount; i++)
        {
            var node = region.Nodes[i];
            EdgeCount -= _nodeCount[node];
            var tile = _nodeTile[node];
            var links = layer.LinksAt(tile);
            _nodeFirst[node] = count;
            var speedHere = Speed(layer.TypeAt(tile));
            for (var d = 0; d < 8; d++)
            {
                if ((links & (1 << d)) == 0)
                {
                    continue;
                }

                var end = ChainEnd(tile, d, out var length, out var exitsAll, out _);
                if (!exitsAll)
                {
                    continue;
                }

                region.EnsureEdges(count + 1);
                region.EdgeTo[count] = _nodeOfTile[end];
                region.EdgeLength[count] = (int)Math.Min(length, int.MaxValue);
                region.EdgeSpeed[count] = SlowestSpeed(speedHere, tile, d);
                region.EdgeFirstDir[count] = (byte)d;
                count++;
            }

            _nodeCount[node] = count - _nodeFirst[node];
            EdgeCount += _nodeCount[node];
        }

        region.EdgeCount = count;
    }

    /// <summary>The lowest speed limit along a chain (its interior tiles share one type), 0 meaning none.</summary>
    private int SlowestSpeed(int speedHere, int tile, int direction)
    {
        var next = _layer.Follow(tile, direction);
        var speed = Speed(_layer.TypeAt(next));
        return Slower(speedHere, speed);
    }

    private static int Slower(int a, int b) => a == 0 ? b : b == 0 ? a : Math.Min(a, b);

    private int Speed(int typeIndex) => typeIndex < 0 || _speeds is null || typeIndex >= _speeds.Length ? 0 : _speeds[typeIndex];

    private int[]? _speeds;

    /// <summary>Speed limits per road type index (km/h, 0 = none), used for edge speeds.</summary>
    public void SetTypeSpeeds(ReadOnlySpan<int> speeds) => _speeds = speeds.ToArray();

    private int Allocate()
    {
        if (_freeCount > 0)
        {
            return _free[--_freeCount];
        }

        if (_nodeHigh == _nodeTile.Length)
        {
            var size = _nodeTile.Length * 2;
            Array.Resize(ref _nodeTile, size);
            Array.Resize(ref _nodeRegion, size);
            Array.Resize(ref _nodeFirst, size);
            Array.Resize(ref _nodeCount, size);
        }

        return _nodeHigh++;
    }

    private void Free(int node)
    {
        if (_freeCount == _free.Length)
        {
            Array.Resize(ref _free, _free.Length * 2);
        }

        _free[_freeCount++] = node;
    }

    /// <summary>The nodes of one region and the flat arrays of their out-edges.</summary>
    private sealed class Region
    {
        public int[] Nodes = new int[16];
        public int NodeCount;
        public int[] EdgeTo = new int[32];
        public int[] EdgeLength = new int[32];
        public int[] EdgeSpeed = new int[32];
        public byte[] EdgeFirstDir = new byte[32];
        public int EdgeCount;

        public void EnsureNodes(int count)
        {
            if (count > Nodes.Length)
            {
                Array.Resize(ref Nodes, Math.Max(count, Nodes.Length * 2));
            }
        }

        public void EnsureEdges(int count)
        {
            if (count > EdgeTo.Length)
            {
                var size = Math.Max(count, EdgeTo.Length * 2);
                Array.Resize(ref EdgeTo, size);
                Array.Resize(ref EdgeLength, size);
                Array.Resize(ref EdgeSpeed, size);
                Array.Resize(ref EdgeFirstDir, size);
            }
        }
    }
}
