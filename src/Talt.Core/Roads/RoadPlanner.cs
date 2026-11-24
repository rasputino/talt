using Talt.Core.Construction;
using Talt.Core.Maps;

namespace Talt.Core.Roads;

/// <summary>
/// Plans road actions without changing anything (research R3, R4, R7–R9): the road tool's proposed path,
/// bridges, tunnels, removal, one-way switching, motorways and motorway connectors. The same planner serves
/// command validation and the client's preview (through <see cref="ConstructionPlanner.Roads"/>), so both
/// always agree. One instance per consumer and thread; scratch buffers are reused, so planning allocates
/// nothing once they have grown.
/// </summary>
/// <remarks>
/// Planning works on a "view": the tiles the action touches are tracked with their proposed road piece,
/// object and scenery; everything else reads the state. The plan's <see cref="TileChange"/>s carry the
/// pieces before and after, so <see cref="ConstructionState"/> commits roads exactly like terrain.
/// </remarks>
public sealed partial class RoadPlanner
{
    // Path search costs (research R4), in abstract units; money comes from roads.json only.
    private const int StraightStep = 1000;
    private const int DiagonalStep = 1414;
    private const int ReuseStraight = 400;
    private const int ReuseDiagonal = 566;
    private const int Turn45 = 150;
    private const int Turn90 = 400;
    private const int RampPenalty = 300;
    private const int TreePenalty = 60;
    private const int BridgePerTile = 3000;
    private const int StatesPerTile = 17;
    private const int StartState = 16;

    // The view.
    private readonly TileSet _view = new(1024);
    private RoadPiece[] _piece = new RoadPiece[1024];
    private TileObjectKind[] _kind = new TileObjectKind[1024];
    private TileOwner[] _owner = new TileOwner[1024];
    private byte[] _scenery = new byte[1024];

    private ConstructionState _state = null!;
    private MapData _map = null!;
    private RoadLayer _roads = null!;
    private RoadRules _rules = null!;
    private ConstructionPlan _plan = null!;
    private int _companyId;

    /// <summary>Starts planning: clears the view and the plan.</summary>
    private void Begin(ConstructionState state, int companyId, ConstructionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(plan);
        _state = state;
        _map = state.Map;
        _roads = state.Roads;
        _rules = state.RoadRules;
        _plan = plan;
        _companyId = companyId;
        _view.Clear();
        plan.Reset();
    }

    private int Width => _map.Width;

    private int Height(int tile) => _map.Heights[tile];

    private bool IsWater(int tile) => _map.IsWater(tile);

    /// <summary>Ground or water surface, whichever is higher.</summary>
    private int Surface(int tile) => Math.Max(_map.Heights[tile], _map.IsWater(tile) ? _map.WaterLevels[tile] : 0);

    private bool IsIndustry(int tile) => _state.IsIndustry(tile % Width, tile / Width);

    private int Neighbour(int tile, int direction) => _roads.Neighbour(tile, direction);

    private int Step(int tile, int direction, int steps)
    {
        var x = (tile % Width) + (Dir8.OffsetX(direction) * steps);
        var z = (tile / Width) + (Dir8.OffsetZ(direction) * steps);
        return _map.Contains(x, z) ? _map.Index(x, z) : -1;
    }

    // ----- View -----

    private int Track(int tile)
    {
        var slot = _view.Add(tile, out var added);
        if (added)
        {
            if (slot >= _piece.Length)
            {
                var size = _piece.Length * 2;
                Array.Resize(ref _piece, size);
                Array.Resize(ref _kind, size);
                Array.Resize(ref _owner, size);
                Array.Resize(ref _scenery, size);
            }

            _piece[slot] = _roads.PieceAt(tile);
            _kind[slot] = _state.Objects.KindAt(tile);
            _owner[slot] = _state.Objects.OwnerAt(tile);
            _scenery[slot] = _map.SceneryData[tile];
        }

        return slot;
    }

    private RoadPiece Piece(int tile)
    {
        var slot = _view.Find(tile);
        return slot >= 0 ? _piece[slot] : _roads.PieceAt(tile);
    }

    private TileObjectKind KindAt(int tile)
    {
        var slot = _view.Find(tile);
        return slot >= 0 ? _kind[slot] : _state.Objects.KindAt(tile);
    }

    private TileOwner OwnerAt(int tile)
    {
        var slot = _view.Find(tile);
        return slot >= 0 ? _owner[slot] : _state.Objects.OwnerAt(tile);
    }

    /// <summary>Follows a link in the view: the far end for a long link, else the neighbour.</summary>
    private int Follow(int tile, int direction)
    {
        var piece = Piece(tile);
        return piece.LongLinkDirection == direction ? Step(tile, direction, piece.Span) : Neighbour(tile, direction);
    }

    /// <summary>Places a new road piece of the company on a tile that has none (object, owner, tree removed).</summary>
    private void NewRoad(int slot, int type)
    {
        var tile = _view[slot];
        _piece[slot] = _piece[slot] with { Type = (byte)(type + 1) };
        _kind[slot] = TileObjectKind.Road;
        _owner[slot] = TileOwner.Company(_companyId);
        if (Scenery.Kind(_scenery[slot]) != 0)
        {
            _plan.Cost += _state.Rules.ClearTreeCost;
            _scenery[slot] = Scenery.None;
        }

        _plan.Cost += _rules.Types[type].BuildCost;
        _ = tile;
    }

    private void Link(int a, int direction, int b, bool exitAB, bool exitBA)
    {
        var sa = Track(a);
        var sb = Track(b);
        var back = Dir8.Opposite(direction);
        var pa = _piece[sa];
        var pb = _piece[sb];
        _piece[sa] = pa with { Links = (byte)(pa.Links | Dir8.Bit(direction)), Exits = exitAB ? (byte)(pa.Exits | Dir8.Bit(direction)) : pa.Exits };
        _piece[sb] = pb with { Links = (byte)(pb.Links | Dir8.Bit(back)), Exits = exitBA ? (byte)(pb.Exits | Dir8.Bit(back)) : pb.Exits };
    }

    /// <summary>Makes a long link (bridge or tunnel) between two end tiles and marks the tiles in between.</summary>
    private void LongLink(int a, int direction, int span, RoadFeatures endFlag, bool exitAB, bool exitBA)
    {
        var b = Step(a, direction, span);
        Link(a, direction, b, exitAB, exitBA);
        var sa = Track(a);
        var sb = Track(b);
        _piece[sa] = _piece[sa] with { Flags = _piece[sa].Flags | endFlag, Span = (byte)span, LongDir = (byte)(direction + 1) };
        _piece[sb] = _piece[sb] with { Flags = _piece[sb].Flags | endFlag, Span = (byte)span, LongDir = (byte)(Dir8.Opposite(direction) + 1) };
        var passing = endFlag == RoadFeatures.BridgeEnd
            ? (Dir8.OffsetX(direction) != 0 ? RoadFeatures.OverBridgeX : RoadFeatures.OverBridgeZ)
            : (Dir8.OffsetX(direction) != 0 ? RoadFeatures.UnderTunnelX : RoadFeatures.UnderTunnelZ);
        for (var k = 1; k < span; k++)
        {
            var slot = Track(Step(a, direction, k));
            _piece[slot] = _piece[slot] with { Flags = _piece[slot].Flags | passing };
        }
    }

    /// <summary>Turns the view into the plan's tile changes (only tiles that differ).</summary>
    private void Emit()
    {
        var plan = _plan;
        var objects = _state.Objects;
        for (var slot = 0; slot < _view.Count; slot++)
        {
            var tile = _view[slot];
            var oldPiece = _roads.PieceAt(tile);
            var oldKind = objects.KindAt(tile);
            var oldOwner = objects.OwnerAt(tile);
            var oldScenery = _map.SceneryData[tile];
            if (oldPiece == _piece[slot] && oldKind == _kind[slot] && oldOwner == _owner[slot] && oldScenery == _scenery[slot])
            {
                continue;
            }

            var height = (byte)Height(tile);
            plan.AddChange(new TileChange(tile, height, height, oldScenery, _scenery[slot], oldKind, _kind[slot], oldOwner, _owner[slot],
                oldPiece, _piece[slot], oldPiece != _piece[slot]));
        }

        if (plan.Reason is null)
        {
            if (plan.ChangeCount == 0)
            {
                plan.Reason = ConstructionReasons.NothingToDo;
            }
            else if (plan.ChangeCount > _state.Rules.MaxTilesPerAction || plan.Cost > _state.Rules.MaxActionCost)
            {
                plan.Reason = ConstructionReasons.TooLarge;
            }
        }
    }

    private void Refuse(int tile, string reason)
    {
        if (tile >= 0)
        {
            _plan.AddProblem(tile, reason);
        }
        else
        {
            _plan.Reason ??= reason;
        }
    }

    // ----- Geometry (research R3), on the view -----

    /// <summary>The terrain allows a short link from <paramref name="a"/> towards <paramref name="direction"/>.</summary>
    private bool TerrainAllows(int a, int direction, out int b)
    {
        b = Neighbour(a, direction);
        if (b < 0 || IsWater(a) || IsWater(b))
        {
            return false;
        }

        var ha = Height(a);
        var hb = Height(b);
        if (Dir8.IsStraight(direction))
        {
            return Math.Abs(ha - hb) <= 1;
        }

        if (ha != hb)
        {
            return false;
        }

        // Diagonal: the two tiles it cuts across must not be higher, and no diagonal may cross it.
        var x = a % Width;
        var z = a / Width;
        var s1 = _map.Index(x + Dir8.OffsetX(direction), z);
        var s2 = _map.Index(x, z + Dir8.OffsetZ(direction));
        if (Surface(s1) > ha || Surface(s2) > ha)
        {
            return false;
        }

        var cross = Dir8.FromOffset(-Dir8.OffsetX(direction), Dir8.OffsetZ(direction));
        return !Piece(s1).HasLink(cross);
    }

    /// <summary>
    /// The shape of a piece with these links is possible on its tile: at most one ramp (a straight short
    /// link to a tile one step higher) and then only straight; bridge and tunnel ends straight and not
    /// ramps; diagonals only on tiles that are not ramps.
    /// </summary>
    private bool ShapeAllows(int tile, byte links, int longDirection)
    {
        var h = Height(tile);
        var ramp = -1;
        for (var d = 0; d < 8; d += 2)
        {
            if ((links & (1 << d)) == 0 || d == longDirection)
            {
                continue;
            }

            var n = Neighbour(tile, d);
            if (n >= 0 && Height(n) == h + 1)
            {
                if (ramp >= 0)
                {
                    return false;
                }

                ramp = d;
            }
        }

        var straight = ramp >= 0 ? ramp : longDirection;
        if (ramp >= 0 && longDirection >= 0)
        {
            return false;
        }

        if (straight >= 0)
        {
            var allowed = Dir8.Bit(straight) | Dir8.Bit(Dir8.Opposite(straight));
            return (links & ~allowed) == 0;
        }

        return true;
    }

    /// <summary>Why a road may not be placed on (or joined to) a tile, or null.</summary>
    private string? EnterBlocker(int tile, bool allowMotorwayEnd)
    {
        if (IsWater(tile))
        {
            return ConstructionReasons.Water;
        }

        if (IsIndustry(tile))
        {
            return ConstructionReasons.Industry;
        }

        var kind = KindAt(tile);
        if (kind == TileObjectKind.None)
        {
            return null;
        }

        if (kind != TileObjectKind.Road)
        {
            return OwnerAt(tile).IsCompany(_companyId) ? ConstructionReasons.ObjectInTheWay : ConstructionReasons.OwnedByOther;
        }

        var piece = Piece(tile);
        if (_rules.IsMotorway(piece.TypeIndex) && !(allowMotorwayEnd && IsMotorwayEnd(piece)))
        {
            return ConstructionReasons.MotorwayJunction;
        }

        return null;
    }

    /// <summary>A motorway carriageway tile where its carriageway ends (one link, not a bridge or tunnel end).</summary>
    private static bool IsMotorwayEnd(RoadPiece piece) => piece.LinkCount == 1 && piece.LongDir == 0;

    /// <summary>Checks every tracked piece's shape and links in the view; records problems.</summary>
    private void VerifyView()
    {
        for (var slot = 0; slot < _view.Count; slot++)
        {
            var piece = _piece[slot];
            if (!piece.HasRoad)
            {
                continue;
            }

            var tile = _view[slot];
            if (!ShapeAllows(tile, piece.Links, piece.LongLinkDirection))
            {
                _plan.AddProblem(tile, ConstructionReasons.TooSteep);
            }
        }
    }

    private static int TurnSteps(int from, int to)
    {
        var diff = Math.Abs(from - to);
        return Math.Min(diff, 8 - diff);
    }
}
