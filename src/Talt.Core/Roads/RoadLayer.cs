using Talt.Core.Determinism;

namespace Talt.Core.Roads;

/// <summary>
/// Dense per-tile road data (data-model.md RoadLayer, research R2), indexed like <see cref="Maps.MapData"/>
/// (<c>x + z * width</c>): link and exit masks, type, flags, and the long link of bridge and tunnel ends.
/// Owned by <see cref="Construction.ConstructionState"/>, changed only by construction commands (and the
/// seeder at game creation), hashed into the fingerprint.
/// </summary>
/// <remarks>
/// Invariants (<see cref="Validate"/>): a link bit implies the neighbour's opposite link bit (for long links,
/// the far end's); exits are a subset of links; a tile with links has a type (a piece without links, left by
/// removing its neighbours, is allowed); the two ends of a long link name each other with the same span.
/// </remarks>
public sealed partial class RoadLayer : IStateHashable
{
    private readonly byte[] _links;
    private readonly byte[] _exits;
    private readonly byte[] _types;
    private readonly byte[] _flags;
    private readonly byte[] _spans;
    private readonly byte[] _longDirs;
    private readonly List<int> _changed = [];
    private readonly int[] _delta = new int[8];

    public RoadLayer(int width, int depth)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(depth);
        Width = width;
        Depth = depth;
        var size = width * depth;
        _links = new byte[size];
        _exits = new byte[size];
        _types = new byte[size];
        _flags = new byte[size];
        _spans = new byte[size];
        _longDirs = new byte[size];
        for (var d = 0; d < 8; d++)
        {
            _delta[d] = Dir8.OffsetX(d) + (Dir8.OffsetZ(d) * width);
        }
    }

    public int Width { get; }

    public int Depth { get; }

    /// <summary>Tiles holding a road piece of their own.</summary>
    public int RoadTileCount { get; private set; }

    /// <summary>Raw link masks, for fast read-only scans (network, meshing).</summary>
    public ReadOnlySpan<byte> Links => _links;

    public ReadOnlySpan<byte> Exits => _exits;

    public ReadOnlySpan<byte> Types => _types;

    public int Index(int x, int z) => x + (z * Width);

    public bool Contains(int x, int z) => (uint)x < (uint)Width && (uint)z < (uint)Depth;

    public bool HasRoad(int tile) => _types[tile] != 0;

    public byte LinksAt(int tile) => _links[tile];

    public byte ExitsAt(int tile) => _exits[tile];

    /// <summary>Road type index, or −1.</summary>
    public int TypeAt(int tile) => _types[tile] - 1;

    public RoadFeatures FlagsAt(int tile) => (RoadFeatures)_flags[tile];

    public RoadPiece PieceAt(int tile) => new(_links[tile], _exits[tile], _types[tile], (RoadFeatures)_flags[tile], _spans[tile], _longDirs[tile]);

    /// <summary>
    /// The tile reached by following link <paramref name="direction"/> from <paramref name="tile"/>: the far
    /// end for a long link, otherwise the neighbour. −1 when it leaves the map.
    /// </summary>
    public int Follow(int tile, int direction)
    {
        var steps = _longDirs[tile] == direction + 1 ? _spans[tile] : 1;
        var x = (tile % Width) + (Dir8.OffsetX(direction) * steps);
        var z = (tile / Width) + (Dir8.OffsetZ(direction) * steps);
        return Contains(x, z) ? Index(x, z) : -1;
    }

    /// <summary>
    /// <see cref="Follow"/> for a direction the tile has a link in (feature 012's hot path): links never leave the map, so
    /// a short link is one array offset away, without dividing by the width.
    /// </summary>
    public int FollowLink(int tile, int direction) =>
        _longDirs[tile] == direction + 1 ? Follow(tile, direction) : tile + _delta[direction];

    /// <summary>Length in units of the link in <paramref name="direction"/> from <paramref name="tile"/>.</summary>
    public int LinkLength(int tile, int direction) =>
        _longDirs[tile] == direction + 1 ? _spans[tile] * Dir8.StraightLength : Dir8.StepLength(direction);

    /// <summary>The neighbour tile in a direction, or −1 off the map.</summary>
    public int Neighbour(int tile, int direction)
    {
        var x = (tile % Width) + Dir8.OffsetX(direction);
        var z = (tile / Width) + Dir8.OffsetZ(direction);
        return Contains(x, z) ? Index(x, z) : -1;
    }

    /// <summary>Writes a tile's piece and queues the tile for drawing. Callers keep the invariants.</summary>
    public void Set(int tile, in RoadPiece piece)
    {
        var had = _types[tile] != 0;
        _links[tile] = piece.Links;
        _exits[tile] = piece.Exits;
        _types[tile] = piece.Type;
        _flags[tile] = (byte)piece.Flags;
        _spans[tile] = piece.Span;
        _longDirs[tile] = piece.LongDir;
        var has = piece.Type != 0;
        if (had != has)
        {
            RoadTileCount += has ? 1 : -1;
        }

        _changed.Add(tile);
    }

    /// <summary>
    /// Moves the tiles changed since the last call into <paramref name="into"/> (for the client's road
    /// meshes). Not simulation state.
    /// </summary>
    public bool TryTakeChangedTiles(List<int> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        if (_changed.Count == 0)
        {
            return false;
        }

        into.AddRange(_changed);
        _changed.Clear();
        return true;
    }

    /// <summary>Forgets queued changes (after building the initial state, which the client draws in full).</summary>
    public void DiscardChangedTiles() => _changed.Clear();

    /// <summary>Returns the first broken invariant, or null (tests and debug checks).</summary>
    public string? Validate()
    {
        var count = 0;
        for (var tile = 0; tile < _links.Length; tile++)
        {
            var links = _links[tile];
            if ((_exits[tile] & ~links) != 0)
            {
                return $"tile {tile}: exits outside links";
            }

            if (links != 0 && _types[tile] == 0)
            {
                return $"tile {tile}: links without a road type";
            }

            if (_types[tile] != 0)
            {
                count++;
            }

            for (var d = 0; d < 8; d++)
            {
                if ((links & (1 << d)) == 0)
                {
                    continue;
                }

                var other = Follow(tile, d);
                if (other < 0 || (_links[other] & (1 << Dir8.Opposite(d))) == 0)
                {
                    return $"tile {tile}: link {d} not matched";
                }

                if (_longDirs[tile] == d + 1
                    && (_longDirs[other] != Dir8.Opposite(d) + 1 || _spans[other] != _spans[tile]))
                {
                    return $"tile {tile}: long link {d} not matched";
                }
            }
        }

        return count == RoadTileCount ? null : "road tile count";
    }

    public void AppendTo(StateHasher hasher)
    {
        ArgumentNullException.ThrowIfNull(hasher);
        hasher.Append<byte>(_links);
        hasher.Append<byte>(_exits);
        hasher.Append<byte>(_types);
        hasher.Append<byte>(_flags);
        hasher.Append<byte>(_spans);
        hasher.Append<byte>(_longDirs);
    }
}
