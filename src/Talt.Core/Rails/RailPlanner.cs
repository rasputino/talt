using Talt.Core.Construction;
using Talt.Core.Maps;
using Talt.Core.Roads;

namespace Talt.Core.Rails;

/// <summary>
/// Plans rail construction without changing anything (feature 015, research R3): the track tool's proposed path with
/// curves, slopes, junctions and level crossings, bridges, tunnels, removal, conversion, signals, stations and depots.
/// The same planner serves command validation and the client's previews, so both always agree. One instance per
/// consumer and thread; buffers are reused.
/// </summary>
/// <remarks>
/// Like <see cref="RoadPlanner"/>, planning works on a view of the tiles the action touches (proposed rail piece,
/// object, owner, scenery); <see cref="Emit"/> turns it into <see cref="TileChange"/>s with the rail piece before and after.
/// </remarks>
public sealed partial class RailPlanner
{
    private const int StraightStep = 1000;
    private const int DiagonalStep = 1414;
    private const int ReuseStraight = 400;
    private const int ReuseDiagonal = 566;
    private const int Turn45 = 250;
    private const int RampPenalty = 300;
    private const int TreePenalty = 60;
    private const int CrossingPenalty = 600;
    private const int StatesPerTile = 9;
    private const int StartState = 8;

    private readonly TileSet _view = new(1024);
    private RailPiece[] _piece = new RailPiece[1024];
    private TileObjectKind[] _kind = new TileObjectKind[1024];
    private TileOwner[] _owner = new TileOwner[1024];
    private byte[] _scenery = new byte[1024];

    private ConstructionState _state = null!;
    private MapData _map = null!;
    private RailLayer _rails = null!;
    private RoadLayer _roads = null!;
    private RailRules _rules = null!;
    private ConstructionPlan _plan = null!;
    private int _companyId;

    private void Begin(ConstructionState state, int companyId, ConstructionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(plan);
        _state = state;
        _map = state.Map;
        _rails = state.Rails;
        _roads = state.Roads;
        _rules = state.RailRules;
        _plan = plan;
        _companyId = companyId;
        _view.Clear();
        plan.Reset();
    }

    private int Width => _map.Width;

    private int Height(int tile) => _map.Heights[tile];

    private bool IsWater(int tile) => _map.IsWater(tile);

    private int Surface(int tile) => Math.Max(_map.Heights[tile], _map.IsWater(tile) ? _map.WaterLevels[tile] : 0);

    private bool IsIndustry(int tile) => _state.IsIndustry(tile % Width, tile / Width);

    private int Neighbour(int tile, int direction) => _rails.Neighbour(tile, direction);

    private int Step(int tile, int direction, int steps)
    {
        var x = (tile % Width) + (Dir8.OffsetX(direction) * steps);
        var z = (tile / Width) + (Dir8.OffsetZ(direction) * steps);
        return _map.Contains(x, z) ? _map.Index(x, z) : -1;
    }

    private bool CheckType(int type)
    {
        if ((uint)type >= (uint)_rules.Types.Count)
        {
            _plan.Reason = ConstructionReasons.UnknownRailType;
            return false;
        }

        return true;
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

            _piece[slot] = _rails.PieceAt(tile);
            _kind[slot] = _state.Objects.KindAt(tile);
            _owner[slot] = _state.Objects.OwnerAt(tile);
            _scenery[slot] = _map.SceneryData[tile];
        }

        return slot;
    }

    private RailPiece Piece(int tile)
    {
        var slot = _view.Find(tile);
        return slot >= 0 ? _piece[slot] : _rails.PieceAt(tile);
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

    /// <summary>Gives a tile without track a new piece of the company (object, owner and tree, costs).</summary>
    private void NewTrack(int slot, int type)
    {
        if (_piece[slot].HasRail)
        {
            return;
        }

        _piece[slot] = _piece[slot] with { Type = (byte)(type + 1) };
        if (_kind[slot] == TileObjectKind.None)
        {
            _kind[slot] = TileObjectKind.Rail;
            _owner[slot] = TileOwner.Company(_companyId);
        }

        if (Scenery.Kind(_scenery[slot]) != 0)
        {
            _plan.Cost += _state.Rules.ClearTreeCost;
            _scenery[slot] = Scenery.None;
        }

        _plan.Cost += _rules.Types[type].BuildCost;
    }

    private void Link(int a, int direction, int b)
    {
        var sa = Track(a);
        var sb = Track(b);
        _piece[sa] = _piece[sa] with { Links = (byte)(_piece[sa].Links | Dir8.Bit(direction)) };
        _piece[sb] = _piece[sb] with { Links = (byte)(_piece[sb].Links | Dir8.Bit(Dir8.Opposite(direction))) };
    }

    private void LongLink(int a, int direction, int span, RailFeatures endFlag)
    {
        var b = Step(a, direction, span);
        Link(a, direction, b);
        var sa = Track(a);
        var sb = Track(b);
        _piece[sa] = _piece[sa] with { Flags = _piece[sa].Flags | endFlag, Span = (byte)span, LongDir = (byte)(direction + 1) };
        _piece[sb] = _piece[sb] with { Flags = _piece[sb].Flags | endFlag, Span = (byte)span, LongDir = (byte)(Dir8.Opposite(direction) + 1) };
        var passing = endFlag == RailFeatures.BridgeEnd
            ? (Dir8.OffsetX(direction) != 0 ? RailFeatures.OverBridgeX : RailFeatures.OverBridgeZ)
            : (Dir8.OffsetX(direction) != 0 ? RailFeatures.UnderTunnelX : RailFeatures.UnderTunnelZ);
        for (var k = 1; k < span; k++)
        {
            var slot = Track(Step(a, direction, k));
            _piece[slot] = _piece[slot] with { Flags = _piece[slot].Flags | passing };
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

    /// <summary>Turns the view into the plan's tile changes (only tiles that differ) and checks the action's size.</summary>
    private void Emit()
    {
        var plan = _plan;
        var objects = _state.Objects;
        for (var slot = 0; slot < _view.Count; slot++)
        {
            var tile = _view[slot];
            var oldPiece = _rails.PieceAt(tile);
            var oldKind = objects.KindAt(tile);
            var oldOwner = objects.OwnerAt(tile);
            var oldScenery = _map.SceneryData[tile];
            if (oldPiece == _piece[slot] && oldKind == _kind[slot] && oldOwner == _owner[slot] && oldScenery == _scenery[slot])
            {
                continue;
            }

            var height = (byte)Height(tile);
            var road = _roads.PieceAt(tile);
            plan.AddChange(new TileChange(tile, height, height, oldScenery, _scenery[slot], oldKind, _kind[slot], oldOwner, _owner[slot],
                road, road, false, oldPiece, _piece[slot], oldPiece != _piece[slot]));
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

    // ----- Geometry -----

    /// <summary>The terrain allows a link from <paramref name="a"/> towards <paramref name="direction"/> (road slope rules).</summary>
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

        var x = a % Width;
        var z = a / Width;
        var s1 = _map.Index(x + Dir8.OffsetX(direction), z);
        var s2 = _map.Index(x, z + Dir8.OffsetZ(direction));
        if (Surface(s1) > ha || Surface(s2) > ha)
        {
            return false;
        }

        // No diagonal (rail or road) may cross this one.
        var cross = Dir8.FromOffset(-Dir8.OffsetX(direction), Dir8.OffsetZ(direction));
        return !Piece(s1).HasLink(cross) && !_roads.PieceAt(s1).HasLink(cross);
    }

    /// <summary>
    /// The piece's links fit its tile: at most one ramp and then only a straight pair; a long-link end straight and not a
    /// ramp; no 90° kinks.
    /// </summary>
    private bool ShapeAllows(int tile, byte links, int longDirection)
    {
        if (!RailGeometry.IsValidShape(links))
        {
            return false;
        }

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

        if (ramp >= 0 && longDirection >= 0)
        {
            return false;
        }

        var straight = ramp >= 0 ? ramp : longDirection;
        if (straight >= 0)
        {
            var allowed = Dir8.Bit(straight) | Dir8.Bit(Dir8.Opposite(straight));
            return (links & ~allowed) == 0;
        }

        return true;
    }

    /// <summary>
    /// Why track arriving at <paramref name="tile"/> heading <paramref name="direction"/> may not use the tile, or null.
    /// Free land, the company's own track, a straight perpendicular road (level crossing), the company's platform along
    /// its axis and (as the last tile) the front of the company's depot are allowed.
    /// </summary>
    private string? EnterBlocker(int tile, int direction, bool isGoal)
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
        var owner = OwnerAt(tile);
        switch (kind)
        {
            case TileObjectKind.None:
                return _roads.HasRoad(tile) ? ConstructionReasons.ObjectInTheWay : null;
            case TileObjectKind.Rail:
                return owner.IsCompany(_companyId) ? null : ConstructionReasons.OwnedByOther;
            case TileObjectKind.Road:
                return CrossingAllowed(tile, direction) ? null : ConstructionReasons.ObjectInTheWay;
            case TileObjectKind.Station:
                if (!owner.IsCompany(_companyId))
                {
                    return ConstructionReasons.OwnedByOther;
                }

                var piece = Piece(tile);
                return piece.HasRail && direction >= 0 && (piece.HasLink(direction) || piece.HasLink(Dir8.Opposite(direction)))
                    ? null : ConstructionReasons.ObjectInTheWay;
            case TileObjectKind.RailDepot:
                if (!owner.IsCompany(_companyId))
                {
                    return ConstructionReasons.OwnedByOther;
                }

                return isGoal && direction >= 0 && _state.RailDepots.DirectionAtTile(tile) == Dir8.Opposite(direction)
                    ? null : ConstructionReasons.ObjectInTheWay;
            default:
                return owner.IsCompany(_companyId) ? ConstructionReasons.ObjectInTheWay : ConstructionReasons.OwnedByOther;
        }
    }

    /// <summary>A level crossing (clarification Q4): a straight, flat, ordinary road at right angles, without a station or long link.</summary>
    private bool CrossingAllowed(int tile, int direction)
    {
        if (direction < 0 || !Dir8.IsStraight(direction))
        {
            return false;
        }

        var road = _roads.PieceAt(tile);
        var across = Dir8.Rotate(direction, 2);
        var pair = (byte)(Dir8.Bit(across) | Dir8.Bit(Dir8.Opposite(across)));
        if (road.Links != pair || (road.Flags & (RoadFeatures.LongLinkEnd | RoadFeatures.Connector)) != 0
            || _state.RoadRules.IsMotorway(road.TypeIndex) || (_state.Stations?.HasPiece(tile) ?? false))
        {
            return false;
        }

        var h = Height(tile);
        for (var d = 0; d < 8; d += 2)
        {
            var n = Neighbour(tile, d);
            if (n >= 0 && Height(n) > h)
            {
                return false; // Crossings are flat.
            }
        }

        var owner = OwnerAt(tile);
        return owner.Kind == TileOwnerKind.Town || owner.IsCompany(_companyId);
    }

    /// <summary>True when a path through this tile must go straight on (crossings and platforms).</summary>
    private bool MustGoStraight(int tile) => KindAt(tile) is TileObjectKind.Road or TileObjectKind.Station;

    /// <summary>Checks every piece in the view; records problems (shape, signals, crossings).</summary>
    private void VerifyView()
    {
        for (var slot = 0; slot < _view.Count; slot++)
        {
            var piece = _piece[slot];
            if (!piece.HasRail)
            {
                continue;
            }

            var tile = _view[slot];
            var old = _rails.PieceAt(tile);
            if (!ShapeAllows(tile, piece.Links, piece.LongLinkDirection))
            {
                _plan.AddProblem(tile, RailGeometry.IsValidShape(piece.Links) ? ConstructionReasons.TooSteep : ConstructionReasons.TurnTooSharp);
                continue;
            }

            if (piece.Signal != 0 && piece.Links != old.Links)
            {
                _plan.AddProblem(tile, ConstructionReasons.SignalPlace);
            }

            if (_kind[slot] == TileObjectKind.Road && Dir8.Count(piece.Links) > 2)
            {
                _plan.AddProblem(tile, ConstructionReasons.ObjectInTheWay);
            }
        }
    }
}
