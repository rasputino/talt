using System.Numerics;
using System.Runtime.InteropServices;
using Talt.Core.Maps;
using Talt.Core.Roads;

namespace Talt.Client.Rendering;

/// <summary>A road mesh vertex (render-only floats): world position, normal and linear RGBA8 colour (28 bytes).</summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal readonly record struct RoadVertex(Vector3 Position, Vector3 Normal, uint Color);

/// <summary>
/// Turns road pieces into triangles on top of the terrain (research R10, FR-050). GPU-free so it is
/// unit-tested. <see cref="BuildDetail"/> draws every piece of a tile rectangle: an octagonal hub with one
/// arm per link (straight to the edge, diagonal to the corner), ramps rising one step on the lower tile with
/// side wedges, centre or edge markings, one-way arrows, bridge decks with railings and pillars, tunnel
/// portals and the acceleration lanes of motorway connectors. <see cref="BuildSimplified"/> is the far
/// version: straight runs at one height merged into single strips, no markings or details. Buffers are
/// reused; the triangle list is non-indexed.
/// </summary>
internal sealed class RoadMesher
{
    /// <summary>Height of the road surface above the voxel top (avoids depth fighting with the terrain).</summary>
    public const float Lift = 0.04f;

    private const float MarkingLift = 0.008f;
    private const float RoadHalfWidth = 0.3f;
    private const float MotorwayHalfWidth = 0.4f;
    private const float MarkingHalfWidth = 0.025f;
    private const float DeckThickness = 0.25f;

    private static readonly Vector3 Up = Vector3.UnitY;

    private readonly RoadLayer _roads;
    private readonly MapData _map;
    private readonly RoadRules _rules;
    private readonly uint[] _surface;
    private readonly uint[] _marking;
    private readonly uint _embankment = Pack(0x6b6250);
    private readonly uint _deck = Pack(0x9a9890);
    private readonly uint _rail = Pack(0xc8c4b8);
    private readonly uint _pillar = Pack(0x847f75);
    private readonly uint _portal = Pack(0x8a8478);
    private readonly uint _mouth = Pack(0x141414);
    private RoadVertex[] _vertices = new RoadVertex[4096];

    public RoadMesher(RoadLayer roads, MapData map, RoadRules rules)
    {
        _roads = roads ?? throw new ArgumentNullException(nameof(roads));
        _map = map ?? throw new ArgumentNullException(nameof(map));
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _surface = [.. rules.Types.Select(t => Pack(t.SurfaceColor))];
        _marking = [.. rules.Types.Select(t => Pack(t.MarkingColor))];
    }

    public int VertexCount { get; private set; }

    public ReadOnlySpan<RoadVertex> Vertices => _vertices.AsSpan(0, VertexCount);

    public int TriangleCount => VertexCount / 3;

    /// <summary>Bounds of the last mesh (meaningless when it is empty).</summary>
    public Vector3 Min { get; private set; }

    public Vector3 Max { get; private set; }

    /// <summary>Every piece of the tiles in [x0, x1) × [z0, z1), in full detail.</summary>
    public void BuildDetail(int x0, int z0, int x1, int z1)
    {
        Begin();
        var width = _map.Width;
        for (var z = Math.Max(0, z0); z < Math.Min(z1, _map.Depth); z++)
        {
            for (var x = Math.Max(0, x0); x < Math.Min(x1, width); x++)
            {
                var tile = x + (z * width);
                if (_roads.HasRoad(tile))
                {
                    DetailPiece(tile, x, z);
                }
            }
        }

        End();
    }

    /// <summary>The far version of the tiles in [x0, x1) × [z0, z1): merged straight runs, plain colours.</summary>
    public void BuildSimplified(int x0, int z0, int x1, int z1)
    {
        Begin();
        var width = _map.Width;
        x0 = Math.Max(0, x0);
        z0 = Math.Max(0, z0);
        x1 = Math.Min(x1, width);
        z1 = Math.Min(z1, _map.Depth);
        for (var z = z0; z < z1; z++)
        {
            for (var x = x0; x < x1; x++)
            {
                var tile = x + (z * width);
                if (!_roads.HasRoad(tile))
                {
                    continue;
                }

                var piece = _roads.PieceAt(tile);
                var flat = Ramp(tile, piece) < 0;
                var h = _map.Heights[tile];
                var color = Surface(piece);
                var hw = HalfWidth(piece);

                // Straight runs along X and along Z: emitted once from the run's first tile.
                var inRunX = flat && IsRun(tile, piece, Dir8.E);
                var inRunZ = flat && IsRun(tile, piece, Dir8.S);
                if (inRunX && !IsRunContinuing(tile, x > x0 ? tile - 1 : -1, Dir8.E, h))
                {
                    var end = x;
                    while (end + 1 < x1 && IsRunContinuing(tile + (end + 1 - x), tile + (end - x), Dir8.E, h))
                    {
                        end++;
                    }

                    var y = h + Lift;
                    Quad(new Vector3(x, y, z + 0.5f - hw), new Vector3(end + 1, y, z + 0.5f - hw), new Vector3(end + 1, y, z + 0.5f + hw), new Vector3(x, y, z + 0.5f + hw), Up, color);
                }

                if (inRunZ && !IsRunContinuing(tile, z > z0 ? tile - width : -1, Dir8.S, h))
                {
                    var end = z;
                    while (end + 1 < z1 && IsRunContinuing(tile + ((end + 1 - z) * width), tile + ((end - z) * width), Dir8.S, h))
                    {
                        end++;
                    }

                    var y = h + Lift;
                    Quad(new Vector3(x + 0.5f - hw, y, z), new Vector3(x + 0.5f + hw, y, z), new Vector3(x + 0.5f + hw, y, end + 1), new Vector3(x + 0.5f - hw, y, end + 1), Up, color);
                }

                // Everything a run does not cover: other arms and the hub of non-run tiles.
                for (var d = 0; d < 8; d++)
                {
                    if (!piece.HasLink(d) || (inRunX && (d == Dir8.E || d == Dir8.W)) || (inRunZ && (d == Dir8.N || d == Dir8.S)))
                    {
                        continue;
                    }

                    Arm(tile, x, z, piece, d, hw, color);
                }

                if (!inRunX && !inRunZ)
                {
                    Square(tile, x, z, piece, hw, color);
                }

                if (piece.IsBridgeEnd && IsPositive(piece.LongLinkDirection))
                {
                    var (a, b) = DeckEnds(x, z, piece);
                    DeckTop(a, b, h + 1 + Lift, hw + 0.08f, _deck);
                }
            }
        }

        End();
    }

    private void DetailPiece(int tile, int x, int z)
    {
        var piece = _roads.PieceAt(tile);
        var hw = HalfWidth(piece);
        var color = Surface(piece);
        var h = _map.Heights[tile];

        for (var d = 0; d < 8; d++)
        {
            if (piece.HasLink(d))
            {
                Arm(tile, x, z, piece, d, hw, color);
            }
        }

        Octagon(tile, x, z, piece, hw, color);
        Markings(tile, x, z, piece, hw);
        var ramp = Ramp(tile, piece);
        if (ramp >= 0 && !piece.IsBridgeEnd)
        {
            Wedges(x, z, h, ramp, hw);
        }

        if (piece.IsBridgeEnd)
        {
            Wedges(x, z, h, piece.LongLinkDirection, hw);
            if (IsPositive(piece.LongLinkDirection))
            {
                Bridge(x, z, piece, hw);
            }
        }

        if (piece.IsTunnelPortal)
        {
            Portal(x, z, h, piece.LongLinkDirection, hw);
        }

        if (piece.IsConnector)
        {
            AccelerationLane(tile, x, z, piece, hw, color);
        }
    }

    // ----- Piece parts -----

    /// <summary>An arm from the tile centre to the edge (straight) or the corner (diagonal) in direction d.</summary>
    private void Arm(int tile, int x, int z, RoadPiece piece, int d, float hw, uint color)
    {
        var dir = Direction(d);
        var length = Dir8.IsStraight(d) ? 0.5f : 0.7072f;
        var side = new Vector2(-dir.Y, dir.X) * hw;
        var c = new Vector2(0.5f, 0.5f);
        var e = c + (dir * length);
        Surface4(tile, x, z, piece, c - side, e - side, e + side, c + side, color, 0f);
    }

    private void Octagon(int tile, int x, int z, RoadPiece piece, float hw, uint color)
    {
        var r = hw / MathF.Cos(MathF.PI / 8);
        Span<Vector2> ring = stackalloc Vector2[8];
        for (var i = 0; i < 8; i++)
        {
            var angle = (MathF.PI / 8) + (i * MathF.PI / 4);
            ring[i] = new Vector2(0.5f + (r * MathF.Cos(angle)), 0.5f + (r * MathF.Sin(angle)));
        }

        var centre = new Vector2(0.5f, 0.5f);
        for (var i = 0; i < 8; i++)
        {
            Triangle(Point(tile, x, z, piece, centre, 0f), Point(tile, x, z, piece, ring[(i + 1) & 7], 0f), Point(tile, x, z, piece, ring[i], 0f), Normal(tile, piece), color);
        }
    }

    private void Square(int tile, int x, int z, RoadPiece piece, float hw, uint color)
    {
        var a = new Vector2(0.5f - hw, 0.5f - hw);
        var b = new Vector2(0.5f + hw, 0.5f - hw);
        var c = new Vector2(0.5f + hw, 0.5f + hw);
        var d = new Vector2(0.5f - hw, 0.5f + hw);
        Surface4(tile, x, z, piece, a, b, c, d, color, 0f);
    }

    private void Markings(int tile, int x, int z, RoadPiece piece, float hw)
    {
        var type = piece.TypeIndex;
        var markings = _rules.Types[type].Markings;
        var color = _marking[type];
        var twoLinks = piece.LinkCount == 2;
        if (markings != RoadMarkings.None && twoLinks)
        {
            for (var d = 0; d < 8; d++)
            {
                if (!piece.HasLink(d))
                {
                    continue;
                }

                var dir = Direction(d);
                var length = Dir8.IsStraight(d) ? 0.5f : 0.7072f;
                var c = new Vector2(0.5f, 0.5f);
                var e = c + (dir * length);
                var normal = new Vector2(-dir.Y, dir.X);
                if (markings == RoadMarkings.Centre)
                {
                    Strip(tile, x, z, piece, c, e, normal, 0f, color);
                }
                else
                {
                    Strip(tile, x, z, piece, c, e, normal, hw - 0.05f, color);
                    Strip(tile, x, z, piece, c, e, normal, -(hw - 0.05f), color);
                }
            }
        }

        // One-way arrow on a straight piece with one exit.
        if (twoLinks && Dir8.Count(piece.Exits) == 1 && !_rules.IsMotorway(type))
        {
            var exit = System.Numerics.BitOperations.TrailingZeroCount(piece.Exits);
            var dir = Direction(exit);
            var normal = new Vector2(-dir.Y, dir.X);
            var c = new Vector2(0.5f, 0.5f);
            Triangle(
                Point(tile, x, z, piece, c + (dir * 0.22f), MarkingLift * 2),
                Point(tile, x, z, piece, c - (dir * 0.12f) - (normal * 0.16f), MarkingLift * 2),
                Point(tile, x, z, piece, c - (dir * 0.12f) + (normal * 0.16f), MarkingLift * 2),
                Normal(tile, piece),
                Pack(0xf4f1e6));
        }
    }

    /// <summary>A thin marking strip along c → e, offset sideways by <paramref name="offset"/>.</summary>
    private void Strip(int tile, int x, int z, RoadPiece piece, Vector2 c, Vector2 e, Vector2 normal, float offset, uint color)
    {
        var o = normal * offset;
        var w = normal * MarkingHalfWidth;
        Surface4(tile, x, z, piece, c + o - w, e + o - w, e + o + w, c + o + w, color, MarkingLift);
    }

    /// <summary>Vertical triangles closing the gap under a ramp, on both sides of the strip.</summary>
    private void Wedges(int x, int z, int h, int direction, float hw)
    {
        var dir = Direction(direction);
        var side = new Vector2(-dir.Y, dir.X) * hw;
        var c = new Vector2(0.5f, 0.5f);
        var back = c - (dir * 0.5f);
        var front = c + (dir * 0.5f);
        foreach (var s in (ReadOnlySpan<Vector2>)[side, -side])
        {
            var b = back + s;
            var f = front + s;
            var normal = Vector3.Normalize(new Vector3(s.X, 0, s.Y));
            Triangle(new Vector3(x + b.X, h, z + b.Y), new Vector3(x + f.X, h, z + f.Y), new Vector3(x + f.X, h + 1 + Lift, z + f.Y), normal, _embankment);
        }
    }

    /// <summary>Deck, railings and pillars from this end (the one whose long link points east or south).</summary>
    private void Bridge(int x, int z, RoadPiece piece, float hw)
    {
        var h = _map.Heights[_map.Index(x, z)];
        var (a, b) = DeckEnds(x, z, piece);
        var top = h + 1 + Lift;
        var half = hw + 0.08f;
        var alongX = Dir8.OffsetX(piece.LongLinkDirection) != 0;
        var min = alongX ? new Vector3(a.X, top - DeckThickness, a.Y - half) : new Vector3(a.X - half, top - DeckThickness, a.Y);
        var max = alongX ? new Vector3(b.X, top, b.Y + half) : new Vector3(b.X + half, top, b.Y);
        Box(min, max, _deck);
        var surfaceColor = Surface(piece);
        DeckTop(a, b, top + 0.002f, hw, surfaceColor);

        // Railings on both sides.
        for (var s = -1; s <= 1; s += 2)
        {
            var o = s * (half - 0.03f);
            var rmin = alongX ? new Vector3(a.X, top, a.Y + o - 0.03f) : new Vector3(a.X + o - 0.03f, top, a.Y);
            var rmax = alongX ? new Vector3(b.X, top + 0.18f, a.Y + o + 0.03f) : new Vector3(a.X + o + 0.03f, top + 0.18f, b.Y);
            Box(rmin, rmax, _rail);
        }

        // Pillars every second tile of the span, down to the ground or the water.
        var dx = Dir8.OffsetX(piece.LongLinkDirection);
        var dz = Dir8.OffsetZ(piece.LongLinkDirection);
        for (var k = 1; k < piece.Span; k += 2)
        {
            var px = x + (dx * k);
            var pz = z + (dz * k);
            var tile = _map.Index(px, pz);
            var bottom = Math.Max(_map.Heights[tile], _map.IsWater(tile) ? _map.WaterLevels[tile] - 1 : 0);
            Box(new Vector3(px + 0.38f, bottom, pz + 0.38f), new Vector3(px + 0.62f, top - DeckThickness, pz + 0.62f), _pillar);
        }
    }

    /// <summary>The deck runs from the front edge of this end to the back edge of the far end.</summary>
    private static (Vector2 A, Vector2 B) DeckEnds(int x, int z, RoadPiece piece)
    {
        var d = piece.LongLinkDirection;
        var dx = Dir8.OffsetX(d);
        var dz = Dir8.OffsetZ(d);
        var span = piece.Span;
        return dx != 0
            ? (new Vector2(x + 1, z + 0.5f), new Vector2(x + span, z + 0.5f))
            : (new Vector2(x + 0.5f, z + 1), new Vector2(x + 0.5f, z + span));
    }

    private void DeckTop(Vector2 a, Vector2 b, float y, float half, uint color)
    {
        if (a.Y == b.Y)
        {
            Quad(new Vector3(a.X, y, a.Y - half), new Vector3(b.X, y, a.Y - half), new Vector3(b.X, y, a.Y + half), new Vector3(a.X, y, a.Y + half), Up, color);
        }
        else
        {
            Quad(new Vector3(a.X - half, y, a.Y), new Vector3(a.X + half, y, a.Y), new Vector3(a.X + half, y, b.Y), new Vector3(a.X - half, y, b.Y), Up, color);
        }
    }

    /// <summary>A stone frame and a dark mouth on the hill face at the portal's front edge.</summary>
    private void Portal(int x, int z, int h, int direction, float hw)
    {
        var dir = Direction(direction);
        var c = new Vector2(x + 0.5f, z + 0.5f) + (dir * 0.5f);
        var side = new Vector2(-dir.Y, dir.X);
        var half = hw + 0.06f;
        var mouthA = c - (side * hw) - (dir * 0.01f);
        var mouthB = c + (side * hw) - (dir * 0.01f);
        var normal = new Vector3(-dir.X, 0, -dir.Y);
        Quad(new Vector3(mouthA.X, h, mouthA.Y), new Vector3(mouthB.X, h, mouthB.Y), new Vector3(mouthB.X, h + 0.8f, mouthB.Y), new Vector3(mouthA.X, h + 0.8f, mouthA.Y), normal, _mouth);
        for (var s = -1; s <= 1; s += 2)
        {
            var p = c + (side * (s * half)) - (dir * 0.06f);
            Box(new Vector3(MathF.Min(p.X, p.X + (dir.X * 0.06f)) - 0.06f, h, MathF.Min(p.Y, p.Y + (dir.Y * 0.06f)) - 0.06f),
                new Vector3(MathF.Max(p.X, p.X + (dir.X * 0.06f)) + 0.06f, h + 0.95f, MathF.Max(p.Y, p.Y + (dir.Y * 0.06f)) + 0.06f), _portal);
        }

        var l0 = c - (side * (half + 0.06f)) - (dir * 0.12f);
        var l1 = c + (side * (half + 0.06f));
        Box(new Vector3(MathF.Min(l0.X, l1.X), h + 0.8f, MathF.Min(l0.Y, l1.Y)), new Vector3(MathF.Max(l0.X, l1.X), h + 1.0f, MathF.Max(l0.Y, l1.Y)), _portal);
    }

    /// <summary>A widening strip along a connector tile on its outer side (deceleration or acceleration lane).</summary>
    private void AccelerationLane(int tile, int x, int z, RoadPiece piece, float hw, uint color)
    {
        var travel = -1;
        for (var d = 0; d < 8; d += 2)
        {
            if (piece.HasLink(d) && piece.HasLink(Dir8.Opposite(d)) && piece.HasExit(d))
            {
                travel = d;
            }
        }

        if (travel < 0)
        {
            return;
        }

        var dir = Direction(travel);
        var outer = Direction(Dir8.Rotate(travel, 2));
        var c = new Vector2(0.5f, 0.5f);
        var back = c - (dir * 0.5f);
        var front = c + (dir * 0.5f);
        var inner = outer * hw;
        var wide = outer * (hw + 0.24f);
        Surface4(tile, x, z, piece, back + inner, front + inner, front + wide, back + wide, color, 0f);
        Strip(tile, x, z, piece, back, front, outer, hw + 0.2f, _marking[piece.TypeIndex]);
    }

    // ----- Geometry helpers -----

    /// <summary>The straight direction in which the piece climbs one step (a ramp), or −1.</summary>
    private int Ramp(int tile, RoadPiece piece)
    {
        if (piece.IsBridgeEnd)
        {
            return piece.LongLinkDirection;
        }

        var h = _map.Heights[tile];
        for (var d = 0; d < 8; d += 2)
        {
            if (piece.HasLink(d) && d != piece.LongLinkDirection)
            {
                var n = _roads.Neighbour(tile, d);
                if (n >= 0 && _map.Heights[n] == h + 1)
                {
                    return d;
                }
            }
        }

        return -1;
    }

    /// <summary>A point of the tile at local (lx, lz), on the road surface (following the ramp).</summary>
    private Vector3 Point(int tile, int x, int z, RoadPiece piece, Vector2 local, float extra)
    {
        var h = _map.Heights[tile];
        var ramp = Ramp(tile, piece);
        var rise = ramp switch
        {
            Dir8.N => 1f - local.Y,
            Dir8.S => local.Y,
            Dir8.E => local.X,
            Dir8.W => 1f - local.X,
            _ => 0f,
        };
        return new Vector3(x + local.X, h + Lift + rise + extra, z + local.Y);
    }

    private Vector3 Normal(int tile, RoadPiece piece)
    {
        var ramp = Ramp(tile, piece);
        if (ramp < 0)
        {
            return Up;
        }

        return Vector3.Normalize(new Vector3(-Dir8.OffsetX(ramp), 1, -Dir8.OffsetZ(ramp)));
    }

    private void Surface4(int tile, int x, int z, RoadPiece piece, Vector2 a, Vector2 b, Vector2 c, Vector2 d, uint color, float extra)
    {
        var normal = Normal(tile, piece);
        Quad(Point(tile, x, z, piece, a, extra), Point(tile, x, z, piece, b, extra), Point(tile, x, z, piece, c, extra), Point(tile, x, z, piece, d, extra), normal, color);
    }

    private static Vector2 Direction(int d)
    {
        var v = new Vector2(Dir8.OffsetX(d), Dir8.OffsetZ(d));
        return Dir8.IsStraight(d) ? v : v * 0.70710678f;
    }

    private static bool IsPositive(int direction) => direction == Dir8.E || direction == Dir8.S;

    private float HalfWidth(RoadPiece piece) => _rules.IsMotorway(piece.TypeIndex) ? MotorwayHalfWidth : RoadHalfWidth;

    private uint Surface(RoadPiece piece) => _surface[piece.TypeIndex];

    /// <summary>A flat straight piece linked both ways along the axis of <paramref name="direction"/> (E or S).</summary>
    private static bool IsRun(int tile, RoadPiece piece, int direction) =>
        piece.HasLink(direction) && piece.HasLink(Dir8.Opposite(direction)) && piece.LongDir == 0 && tile >= 0;

    /// <summary>True when <paramref name="previous"/> belongs to the same run as <paramref name="tile"/>.</summary>
    private bool IsRunContinuing(int tile, int previous, int direction, int height)
    {
        if (previous < 0 || !_roads.HasRoad(previous) || !_roads.HasRoad(tile))
        {
            return false;
        }

        var piece = _roads.PieceAt(previous);
        var current = _roads.PieceAt(tile);
        return IsRun(previous, piece, direction) && Ramp(previous, piece) < 0 && _map.Heights[previous] == height
            && IsRun(tile, current, direction) && Ramp(tile, current) < 0 && _map.Heights[tile] == height
            && piece.Type == current.Type;
    }

    /// <summary>An axis-aligned box (all six faces).</summary>
    private void Box(Vector3 min, Vector3 max, uint color)
    {
        var (x0, y0, z0) = (min.X, min.Y, min.Z);
        var (x1, y1, z1) = (max.X, max.Y, max.Z);
        Quad(new(x0, y1, z0), new(x1, y1, z0), new(x1, y1, z1), new(x0, y1, z1), Up, color);
        Quad(new(x0, y0, z1), new(x1, y0, z1), new(x1, y0, z0), new(x0, y0, z0), -Up, color);
        Quad(new(x0, y0, z0), new(x1, y0, z0), new(x1, y1, z0), new(x0, y1, z0), -Vector3.UnitZ, color);
        Quad(new(x1, y0, z1), new(x0, y0, z1), new(x0, y1, z1), new(x1, y1, z1), Vector3.UnitZ, color);
        Quad(new(x0, y0, z1), new(x0, y0, z0), new(x0, y1, z0), new(x0, y1, z1), -Vector3.UnitX, color);
        Quad(new(x1, y0, z0), new(x1, y0, z1), new(x1, y1, z1), new(x1, y1, z0), Vector3.UnitX, color);
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

    private void Begin() => VertexCount = 0;

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

    /// <summary>sRGB 0xRRGGBB to linear RGBA8 (alpha 255), packed little-endian as R, G, B, A bytes.</summary>
    internal static uint Pack(int srgb)
    {
        static uint Channel(int value)
        {
            var c = value / 255f;
            var linear = c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
            return (uint)Math.Clamp((int)MathF.Round(linear * 255f), 0, 255);
        }

        return Channel((srgb >> 16) & 0xff) | (Channel((srgb >> 8) & 0xff) << 8) | (Channel(srgb & 0xff) << 16) | (255u << 24);
    }
}
