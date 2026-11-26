using System.Numerics;
using Talt.Core.Maps;
using Talt.Core.Rails;
using Talt.Core.Roads;

namespace Talt.Client.Rendering;

/// <summary>
/// Turns rail pieces into triangles on top of the terrain (feature 015, research R14) for <see cref="RoadRenderer"/>: per
/// link from the tile centre to its edge (or corner) a ballast bed, sleepers and two rails; ramps rise across the lower
/// tile; bridge decks join their ends at the end height, tunnel mouths are dark; electrified track has wire posts and an
/// overhead wire; signals are small posts with a head coloured by kind. GPU-free; buffers reused.
/// </summary>
internal sealed class RailMesher : ITileLayerSource
{
    /// <summary>Above the road surface so level crossings show the rails on top.</summary>
    private const float Lift = 0.07f;

    private const float BedHalfWidth = 0.24f;
    private const float RailOffset = 0.11f;
    private const float RailHalfWidth = 0.018f;
    private const float RailHeight = 0.05f;
    private const float SleeperHalfLength = 0.17f;
    private const float SleeperHalfWidth = 0.035f;

    private static readonly Vector3 Up = Vector3.UnitY;

    private readonly RailLayer _rails;
    private readonly MapData _map;
    private readonly uint[] _bed;
    private readonly uint _sleeper = RoadMesher.Pack(0x5A4636);
    private readonly uint _rail = RoadMesher.Pack(0xC8C4BC);
    private readonly uint _deck = RoadMesher.Pack(0x8C8780);
    private readonly uint _pillar = RoadMesher.Pack(0x6E6A63);
    private readonly uint _mouth = RoadMesher.Pack(0x121212);
    private readonly uint _post = RoadMesher.Pack(0x4A4A4A);
    private readonly uint _wire = RoadMesher.Pack(0x2A2A2A);
    private readonly uint[] _signalHead = [RoadMesher.Pack(0xFFFFFF), RoadMesher.Pack(0xE8E2D0), RoadMesher.Pack(0x3A7BC8), RoadMesher.Pack(0xF2D23C)];
    private RoadVertex[] _vertices = new RoadVertex[4096];

    public RailMesher(RailLayer rails, MapData map, RailRules rules)
    {
        _rails = rails ?? throw new ArgumentNullException(nameof(rails));
        _map = map ?? throw new ArgumentNullException(nameof(map));
        ArgumentNullException.ThrowIfNull(rules);
        _bed = [.. rules.Types.Select(t => RoadMesher.Pack(t.Color))];
        Electrified = [.. rules.Types.Select(t => t.Electrified)];
    }

    public int Width => _rails.Width;

    public int Depth => _rails.Depth;

    public int VertexCount { get; private set; }

    public ReadOnlySpan<RoadVertex> Vertices => _vertices.AsSpan(0, VertexCount);

    public Vector3 Min { get; private set; }

    public Vector3 Max { get; private set; }

    private bool[] Electrified { get; }

    public bool HasContent(int tile) => _rails.HasRail(tile) || (_rails.FlagsAt(tile) & RailFeatures.Passing) != 0;

    public bool TryTakeChangedTiles(List<int> into) => _rails.TryTakeChangedTiles(into);

    public void BuildDetail(int x0, int z0, int x1, int z1) => Build(x0, z0, x1, z1, detail: true);

    public void BuildSimplified(int x0, int z0, int x1, int z1) => Build(x0, z0, x1, z1, detail: false);

    private void Build(int x0, int z0, int x1, int z1, bool detail)
    {
        VertexCount = 0;
        var width = _map.Width;
        for (var z = Math.Max(0, z0); z < Math.Min(z1, _map.Depth); z++)
        {
            for (var x = Math.Max(0, x0); x < Math.Min(x1, width); x++)
            {
                var tile = x + (z * width);
                if (_rails.HasRail(tile))
                {
                    Piece(tile, x, z, detail);
                }
            }
        }

        End();
    }

    private void Piece(int tile, int x, int z, bool detail)
    {
        var piece = _rails.PieceAt(tile);
        var type = Math.Clamp(piece.TypeIndex, 0, _bed.Length - 1);
        var ramp = Ramp(tile, piece);
        var h = _map.Heights[tile];
        if (piece.Links == 0)
        {
            // A lone piece (a depot not joined yet): a short bed.
            Bed(x, z, h, ramp, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f) + new Vector2(0.3f, 0), _bed[type]);
            return;
        }

        for (var d = 0; d < 8; d++)
        {
            if (!piece.HasLink(d))
            {
                continue;
            }

            var dir = Direction(d);
            var length = Dir8.IsStraight(d) ? 0.5f : 0.7072f;
            var centre = new Vector2(0.5f, 0.5f);
            var end = centre + (dir * length);
            Bed(x, z, h, ramp, centre, end, _bed[type]);
            if (detail)
            {
                Track(x, z, h, ramp, centre, end, dir, length);
            }

            if (detail && Electrified[type] && (d & 1) == 0)
            {
                Wire(x, z, h, ramp, centre, end);
            }

            if (piece.LongLinkDirection == d && (d < 4))
            {
                LongLink(tile, x, z, h, piece, d);
            }
        }

        if (piece.IsTunnelPortal)
        {
            Mouth(x, z, h, piece.LongLinkDirection);
        }

        if (piece.Signal != 0)
        {
            Signal(x, z, h, piece);
        }
    }

    /// <summary>The ballast bed from the centre towards an edge.</summary>
    private void Bed(int x, int z, int h, int ramp, Vector2 a, Vector2 b, uint color)
    {
        var dir = Vector2.Normalize(b - a);
        var side = new Vector2(-dir.Y, dir.X) * BedHalfWidth;
        var a0 = a - (dir * BedHalfWidth * 0.4f);
        Quad(P(x, z, h, ramp, a0 - side, 0), P(x, z, h, ramp, b - side, 0), P(x, z, h, ramp, b + side, 0), P(x, z, h, ramp, a0 + side, 0), Up, color);
    }

    private void Track(int x, int z, int h, int ramp, Vector2 a, Vector2 b, Vector2 dir, float length)
    {
        var side = new Vector2(-dir.Y, dir.X);
        // Sleepers every ~0.2 tiles.
        for (var t = 0.08f; t < length; t += 0.2f)
        {
            var c = a + (dir * t);
            var across = side * SleeperHalfLength;
            var along = dir * SleeperHalfWidth;
            Quad(P(x, z, h, ramp, c - across - along, 0.012f), P(x, z, h, ramp, c - across + along, 0.012f),
                P(x, z, h, ramp, c + across + along, 0.012f), P(x, z, h, ramp, c + across - along, 0.012f), Up, _sleeper);
        }

        foreach (var offset in (ReadOnlySpan<float>)[-RailOffset, RailOffset])
        {
            var o = side * offset;
            var w = side * RailHalfWidth;
            Quad(P(x, z, h, ramp, a + o - w, RailHeight), P(x, z, h, ramp, b + o - w, RailHeight),
                P(x, z, h, ramp, b + o + w, RailHeight), P(x, z, h, ramp, a + o + w, RailHeight), Up, _rail);
        }
    }

    /// <summary>A post at the tile centre and a wire above the track.</summary>
    private void Wire(int x, int z, int h, int ramp, Vector2 a, Vector2 b)
    {
        var dir = Vector2.Normalize(b - a);
        var side = new Vector2(-dir.Y, dir.X);
        var foot = a + (side * 0.3f);
        Box(P(x, z, h, ramp, foot, 0), 0.025f, 0.9f, _post);
        var w = side * 0.012f;
        Quad(P(x, z, h, ramp, a - w, 0.85f), P(x, z, h, ramp, b - w, 0.85f), P(x, z, h, ramp, b + w, 0.85f), P(x, z, h, ramp, a + w, 0.85f), Up, _wire);
    }

    /// <summary>A bridge deck from this end to the far end at the end height, with pillars to the ground.</summary>
    private void LongLink(int tile, int x, int z, int h, RailPiece piece, int d)
    {
        if (!piece.IsBridgeEnd)
        {
            return;
        }

        var dir = Direction(d);
        var start = new Vector2(x + 0.5f, z + 0.5f);
        var end = start + (dir * piece.Span);
        var side = new Vector2(-dir.Y, dir.X) * 0.3f;
        var y = h + 0.2f;
        Quad(new Vector3(start.X - side.X, y, start.Y - side.Y), new Vector3(end.X - side.X, y, end.Y - side.Y),
            new Vector3(end.X + side.X, y, end.Y + side.Y), new Vector3(start.X + side.X, y, start.Y + side.Y), Up, _deck);
        foreach (var offset in (ReadOnlySpan<float>)[-RailOffset, RailOffset])
        {
            var o = new Vector2(-dir.Y, dir.X) * offset;
            var w = new Vector2(-dir.Y, dir.X) * RailHalfWidth;
            var ry = y + RailHeight;
            Quad(new Vector3(start.X + o.X - w.X, ry, start.Y + o.Y - w.Y), new Vector3(end.X + o.X - w.X, ry, end.Y + o.Y - w.Y),
                new Vector3(end.X + o.X + w.X, ry, end.Y + o.Y + w.Y), new Vector3(start.X + o.X + w.X, ry, start.Y + o.Y + w.Y), Up, _rail);
        }

        for (var k = 1; k < piece.Span; k++)
        {
            var at = start + (dir * k);
            var ground = _map.Heights[_map.Index((int)at.X, (int)at.Y)];
            Box(new Vector3(at.X, ground, at.Y), 0.09f, y - ground, _pillar);
        }

        _ = tile;
    }

    private void Mouth(int x, int z, int h, int d)
    {
        var dir = Direction(d);
        var side = new Vector2(-dir.Y, dir.X) * 0.32f;
        var edge = new Vector2(x + 0.5f, z + 0.5f) + (dir * 0.5f);
        var normal = new Vector3(-dir.X, 0, -dir.Y);
        Quad(new Vector3(edge.X - side.X, h, edge.Y - side.Y), new Vector3(edge.X + side.X, h, edge.Y + side.Y),
            new Vector3(edge.X + side.X, h + 0.75f, edge.Y + side.Y), new Vector3(edge.X - side.X, h + 0.75f, edge.Y - side.Y), normal, _mouth);
    }

    /// <summary>A post beside the track at each face's edge, with a head coloured by the signal kind.</summary>
    private void Signal(int x, int z, int h, RailPiece piece)
    {
        var axis = RailSignal.Axis(piece.Links);
        var faces = RailSignal.Faces(piece.Signal);
        var kind = (int)RailSignal.Kind(piece.Signal);
        for (var f = 0; f < 2; f++)
        {
            if ((faces & (1 << f)) == 0)
            {
                continue;
            }

            var d = f == 0 ? axis : axis + 4;
            var dir = Direction(d);
            var side = new Vector2(-dir.Y, dir.X);
            // Right-hand side of the travel direction, near the exit edge.
            var at = new Vector2(x + 0.5f, z + 0.5f) + (dir * 0.32f) + (side * 0.3f);
            Box(new Vector3(at.X, h + Lift, at.Y), 0.03f, 0.55f, _post);
            Box(new Vector3(at.X, h + Lift + 0.55f, at.Y), 0.07f, 0.16f, _signalHead[kind]);
        }
    }

    // ----- Geometry helpers -----

    /// <summary>The straight direction towards a neighbour one step higher (a ramp), or −1.</summary>
    private int Ramp(int tile, RailPiece piece)
    {
        var h = _map.Heights[tile];
        for (var d = 0; d < 8; d += 2)
        {
            if (!piece.HasLink(d) || piece.LongLinkDirection == d)
            {
                continue;
            }

            var n = _rails.Neighbour(tile, d);
            if (n >= 0 && _map.Heights[n] == h + 1)
            {
                return d;
            }
        }

        return -1;
    }

    /// <summary>A point on the tile (local u, v in 0–1) at its surface height, raised by <paramref name="lift"/>.</summary>
    private static Vector3 P(int x, int z, int h, int ramp, Vector2 uv, float lift)
    {
        var y = h + Lift + lift;
        if (ramp >= 0)
        {
            var dir = Direction(ramp);
            y += 0.5f + Vector2.Dot(uv - new Vector2(0.5f, 0.5f), dir);
        }

        return new Vector3(x + uv.X, y, z + uv.Y);
    }

    private static Vector2 Direction(int d) => Vector2.Normalize(new Vector2(Dir8.OffsetX(d), Dir8.OffsetZ(d)));

    /// <summary>A square column standing on <paramref name="foot"/>.</summary>
    private void Box(Vector3 foot, float half, float height, uint color)
    {
        var top = foot.Y + height;
        var x0 = foot.X - half;
        var x1 = foot.X + half;
        var z0 = foot.Z - half;
        var z1 = foot.Z + half;
        Quad(new(x0, top, z0), new(x1, top, z0), new(x1, top, z1), new(x0, top, z1), Up, color);
        Quad(new(x0, foot.Y, z0), new(x1, foot.Y, z0), new(x1, top, z0), new(x0, top, z0), -Vector3.UnitZ, color);
        Quad(new(x1, foot.Y, z1), new(x0, foot.Y, z1), new(x0, top, z1), new(x1, top, z1), Vector3.UnitZ, color);
        Quad(new(x0, foot.Y, z1), new(x0, foot.Y, z0), new(x0, top, z0), new(x0, top, z1), -Vector3.UnitX, color);
        Quad(new(x1, foot.Y, z0), new(x1, foot.Y, z1), new(x1, top, z1), new(x1, top, z0), Vector3.UnitX, color);
    }

    private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, uint color)
    {
        Triangle(a, b, c, normal, color);
        Triangle(a, c, d, normal, color);
    }

    private void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 normal, uint color)
    {
        if (VertexCount + 3 > _vertices.Length)
        {
            Array.Resize(ref _vertices, _vertices.Length * 2);
        }

        _vertices[VertexCount++] = new RoadVertex(a, normal, color);
        _vertices[VertexCount++] = new RoadVertex(b, normal, color);
        _vertices[VertexCount++] = new RoadVertex(c, normal, color);
    }

    private void End()
    {
        if (VertexCount == 0)
        {
            Min = Max = Vector3.Zero;
            return;
        }

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var v in Vertices)
        {
            min = Vector3.Min(min, v.Position);
            max = Vector3.Max(max, v.Position);
        }

        Min = min;
        Max = max;
    }
}
