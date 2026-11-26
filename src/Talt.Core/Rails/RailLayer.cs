using Talt.Core.Determinism;
using Talt.Core.Roads;

namespace Talt.Core.Rails;

/// <summary>Per-tile rail flags (data-model.md RailLayer.flags).</summary>
[Flags]
public enum RailFeatures : byte
{
    None = 0,
    BridgeEnd = 1,
    TunnelPortal = 2,
    OverBridgeX = 8,
    OverBridgeZ = 16,
    UnderTunnelX = 32,
    UnderTunnelZ = 64,
    Passing = OverBridgeX | OverBridgeZ | UnderTunnelX | UnderTunnelZ,
    LongLinkEnd = BridgeEnd | TunnelPortal,
}

/// <summary>
/// Everything <see cref="RailLayer"/> stores for one tile (data-model.md RailPiece): link mask over <see cref="Dir8"/>,
/// track type (index + 1, 0 = none), flags, the long link of bridge and tunnel ends (direction + 1 and span) and the
/// signal byte (kind in bits 0–1, faces in bits 2–3, see <see cref="RailSignal"/>).
/// </summary>
public readonly record struct RailPiece(byte Links, byte Type, RailFeatures Flags, byte Span, byte LongDir, byte Signal)
{
    public bool HasRail => Type != 0;

    public int TypeIndex => Type - 1;

    public bool HasLink(int direction) => (Links & (1 << direction)) != 0;

    public int LinkCount => Dir8.Count(Links);

    public int LongLinkDirection => LongDir - 1;

    public bool IsBridgeEnd => (Flags & RailFeatures.BridgeEnd) != 0;

    public bool IsTunnelPortal => (Flags & RailFeatures.TunnelPortal) != 0;

    public SignalKind SignalKind => RailSignal.Kind(Signal);

    public RailPiece PassingOnly => new(0, 0, Flags & RailFeatures.Passing, 0, 0, 0);

    public bool IsEmpty => this == default;

    public long Pack() => Links | ((long)Type << 8) | ((long)Flags << 16) | ((long)Span << 24) | ((long)LongDir << 32) | ((long)Signal << 40);
}

/// <summary>
/// Signal byte helpers (research R5): a signal stands on a tile whose links are one opposite pair (a, a + 4) with a &lt; 4.
/// Face bit 2 protects travel towards a (it sits on the tile's edge towards a), bit 3 travel towards a + 4.
/// </summary>
public static class RailSignal
{
    public const int FaceLow = 1;
    public const int FaceHigh = 2;

    public static SignalKind Kind(byte signal) => (SignalKind)(signal & 3);

    public static int Faces(byte signal) => (signal >> 2) & 3;

    public static byte Make(SignalKind kind, int faces) => kind == SignalKind.None || faces == 0 ? (byte)0 : (byte)((int)kind | ((faces & 3) << 2));

    /// <summary>The axis (lower direction a, 0–3) of a tile that may carry a signal, or −1.</summary>
    public static int Axis(byte links)
    {
        for (var a = 0; a < 4; a++)
        {
            if (links == (byte)((1 << a) | (1 << (a + 4))))
            {
                return a;
            }
        }

        return -1;
    }

    /// <summary>The face bit for travel towards <paramref name="direction"/> on a tile with this axis.</summary>
    public static int FaceFor(int axis, int direction) => direction == axis ? FaceLow : direction == axis + 4 ? FaceHigh : 0;

    /// <summary>True when the signal on a tile (axis <paramref name="axis"/>) has a face for travel towards <paramref name="direction"/>.</summary>
    public static bool Faces(byte signal, int axis, int direction) => signal != 0 && (Faces(signal) & FaceFor(axis, direction)) != 0;
}

/// <summary>
/// Dense per-tile rail data (feature 015, data-model.md RailLayer, research R1), indexed like the map (<c>x + z * width</c>).
/// Owned by <see cref="Construction.ConstructionState"/>, changed only by construction commands; appended to the fingerprint
/// only once a rail tile ever existed, so games without rail keep their fingerprints (research R15).
/// </summary>
public sealed class RailLayer : IStateHashable
{
    private readonly byte[] _links;
    private readonly byte[] _types;
    private readonly byte[] _flags;
    private readonly byte[] _spans;
    private readonly byte[] _longDirs;
    private readonly byte[] _signals;
    private readonly int[] _delta = new int[8];
    private readonly List<int> _changed = [];

    public RailLayer(int width, int depth)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(depth);
        Width = width;
        Depth = depth;
        var size = width * depth;
        _links = new byte[size];
        _types = new byte[size];
        _flags = new byte[size];
        _spans = new byte[size];
        _longDirs = new byte[size];
        _signals = new byte[size];
        for (var d = 0; d < 8; d++)
        {
            _delta[d] = Dir8.OffsetX(d) + (Dir8.OffsetZ(d) * width);
        }
    }

    public int Width { get; }

    public int Depth { get; }

    /// <summary>Tiles holding track of their own.</summary>
    public int RailTileCount { get; private set; }

    /// <summary>True once any piece was ever written (turns hashing on).</summary>
    public bool EverUsed { get; private set; }

    /// <summary>Bumped on every change (blocks, routes and previews notice).</summary>
    public int Version { get; private set; }

    public ReadOnlySpan<byte> Links => _links;

    public int Index(int x, int z) => x + (z * Width);

    public bool Contains(int x, int z) => (uint)x < (uint)Width && (uint)z < (uint)Depth;

    public bool HasRail(int tile) => _types[tile] != 0;

    public byte LinksAt(int tile) => _links[tile];

    public int TypeAt(int tile) => _types[tile] - 1;

    public RailFeatures FlagsAt(int tile) => (RailFeatures)_flags[tile];

    public byte SignalAt(int tile) => _signals[tile];

    public RailPiece PieceAt(int tile) => new(_links[tile], _types[tile], (RailFeatures)_flags[tile], _spans[tile], _longDirs[tile], _signals[tile]);

    /// <summary>True when the link in <paramref name="direction"/> is a bridge or tunnel.</summary>
    public bool IsLongLink(int tile, int direction) => _longDirs[tile] == direction + 1;

    /// <summary>The tile reached through link <paramref name="direction"/> (far end of a long link), or −1 off the map.</summary>
    public int Follow(int tile, int direction)
    {
        var steps = _longDirs[tile] == direction + 1 ? _spans[tile] : 1;
        var x = (tile % Width) + (Dir8.OffsetX(direction) * steps);
        var z = (tile / Width) + (Dir8.OffsetZ(direction) * steps);
        return Contains(x, z) ? Index(x, z) : -1;
    }

    /// <summary><see cref="Follow"/> for an existing link (links never leave the map).</summary>
    public int FollowLink(int tile, int direction) =>
        _longDirs[tile] == direction + 1 ? Follow(tile, direction) : tile + _delta[direction];

    /// <summary>Length in units of the link in <paramref name="direction"/>.</summary>
    public int LinkLength(int tile, int direction) =>
        _longDirs[tile] == direction + 1 ? _spans[tile] * Dir8.StraightLength : Dir8.StepLength(direction);

    public int Neighbour(int tile, int direction)
    {
        var x = (tile % Width) + Dir8.OffsetX(direction);
        var z = (tile / Width) + Dir8.OffsetZ(direction);
        return Contains(x, z) ? Index(x, z) : -1;
    }

    /// <summary>Writes a tile's piece. Callers keep the invariants.</summary>
    public void Set(int tile, in RailPiece piece)
    {
        var had = _types[tile] != 0;
        _links[tile] = piece.Links;
        _types[tile] = piece.Type;
        _flags[tile] = (byte)piece.Flags;
        _spans[tile] = piece.Span;
        _longDirs[tile] = piece.LongDir;
        _signals[tile] = piece.Signal;
        var has = piece.Type != 0;
        if (had != has)
        {
            RailTileCount += has ? 1 : -1;
        }

        EverUsed |= !piece.IsEmpty;
        Version++;
        _changed.Add(tile);
    }

    /// <summary>Moves the tiles changed since the last call into <paramref name="into"/> (client meshes). Not state.</summary>
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

    /// <summary>
    /// Changed tiles not taken yet (the block index reads them after every commit; the client takes them later). The
    /// span is valid until the next change.
    /// </summary>
    internal ReadOnlySpan<int> PendingChanges => System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_changed);

    public void DiscardChangedTiles() => _changed.Clear();

    /// <summary>Returns the first broken invariant, or null (tests and debug checks).</summary>
    public string? Validate()
    {
        var count = 0;
        for (var tile = 0; tile < _links.Length; tile++)
        {
            var links = _links[tile];
            if (links != 0 && _types[tile] == 0)
            {
                return $"tile {tile}: links without a track type";
            }

            if (_types[tile] != 0)
            {
                count++;
            }

            if (_signals[tile] != 0 && (RailSignal.Axis(links) < 0 || _longDirs[tile] != 0))
            {
                return $"tile {tile}: signal on a tile that is not a plain straight or diagonal";
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

                if (_longDirs[tile] == d + 1 && (_longDirs[other] != Dir8.Opposite(d) + 1 || _spans[other] != _spans[tile]))
                {
                    return $"tile {tile}: long link {d} not matched";
                }

                if (Dir8.Count(links) > 1 && !RailGeometry.HasContinuation(links, d))
                {
                    return $"tile {tile}: link {d} has no continuation";
                }
            }
        }

        return count == RailTileCount ? null : "rail tile count";
    }

    public void AppendTo(StateHasher hasher)
    {
        ArgumentNullException.ThrowIfNull(hasher);
        if (!EverUsed)
        {
            return; // Research R15: games without rail keep their fingerprints.
        }

        hasher.Append<byte>(_links);
        hasher.Append<byte>(_types);
        hasher.Append<byte>(_flags);
        hasher.Append<byte>(_spans);
        hasher.Append<byte>(_longDirs);
        hasher.Append<byte>(_signals);
    }
}

/// <summary>The switch rule of research R2: which links a train may leave a tile by.</summary>
public static class RailGeometry
{
    /// <summary>
    /// Links a train heading <paramref name="heading"/> may leave by: <c>heading − 1</c>, <c>heading</c>, <c>heading + 1</c>
    /// among <paramref name="links"/>.
    /// </summary>
    public static byte Exits(byte links, int heading)
    {
        var allowed = Dir8.Bit(heading) | Dir8.Bit(Dir8.Rotate(heading, 1)) | Dir8.Bit(Dir8.Rotate(heading, 7));
        return (byte)(links & allowed);
    }

    /// <summary>True when a train entering through link <paramref name="link"/> (heading opposite to it) can leave the tile.</summary>
    public static bool HasContinuation(byte links, int link) => Exits(links, Dir8.Opposite(link)) != 0;

    /// <summary>True when every link of a piece with several links continues (no 90° kink); a single link is a buffer stop.</summary>
    public static bool IsValidShape(byte links)
    {
        if (Dir8.Count(links) <= 1)
        {
            return true;
        }

        for (var d = 0; d < 8; d++)
        {
            if ((links & (1 << d)) != 0 && !HasContinuation(links, d))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Number of 45° steps between two directions (0–4).</summary>
    public static int Turn(int from, int to)
    {
        var diff = Math.Abs(from - to);
        return Math.Min(diff, 8 - diff);
    }
}
