using Talt.Core.Maps;

namespace Talt.Core.Construction;

/// <summary>How a terraform action changes the area.</summary>
public enum TerraformMode
{
    Raise,
    Lower,
    Level,
}

/// <summary>
/// Works out what a construction action would do without changing anything (research R4, R5, R8). The
/// same planner serves the command's validation and the client's preview, so both always agree. One
/// instance per consumer and thread: it keeps scratch buffers that are reused between plans, so planning
/// allocates nothing once they have grown.
/// </summary>
/// <remarks>
/// <para>
/// Terraforming keeps the one-step rule of feature 004 on the 4-neighbourhood: after the action, two
/// neighbouring land tiles differ by at most one voxel whenever at least one of them changed. Raise sets
/// every area tile one higher and then raises neighbours (inside or outside the area) just enough; lower
/// is the mirror image; level fixes the area at the start tile's height and alternates raise and lower
/// passes over the surroundings. Each pass is a monotone fixpoint (values only move one way and are
/// bounded), so its result does not depend on processing order.
/// </para>
/// <para>
/// Tiles that would have to change but cannot (water, objects, height limits, flooding) are recorded as
/// problems and then treated as fixed, so the preview can show every blocking tile.
/// </para>
/// </remarks>
public sealed class ConstructionPlanner
{
    private const byte Fixed = 1;
    private const byte Blocked = 2;
    private const byte Queued = 4;
    private const int MaxLevelRounds = 8;

    private readonly TileSet _tiles = new(4096);
    private int[] _value = new int[4096];
    private int[] _original = new int[4096];
    private byte[] _flags = new byte[4096];
    private int[] _queue = new int[4096];
    private int _queueHead;
    private int _queueCount;
    private int _changed;
    private bool _tooLarge;

    private ConstructionState _state = null!;

    /// <summary>The road planner of this consumer (feature 007), sharing its "one per consumer and thread" rule.</summary>
    public Roads.RoadPlanner Roads { get; } = new();

    private MapData _map = null!;
    private ConstructionPlan _plan = null!;
    private int _companyId;

    /// <summary>Plans raising, lowering or levelling an area (contracts/commands.md <c>terraform</c>).</summary>
    /// <param name="startX">Level only: tile whose height the area takes.</param>
    /// <param name="startZ">Level only.</param>
    public void PlanTerraform(ConstructionState state, int companyId, TerraformMode mode, TileRect area, int startX, int startZ, ConstructionPlan plan)
    {
        if (!Begin(state, companyId, area, plan, out var rect))
        {
            return;
        }

        var map = _map;
        var level = 0;
        if (mode == TerraformMode.Level)
        {
            if (!map.Contains(startX, startZ))
            {
                (startX, startZ) = (rect.X0, rect.Z0);
            }

            level = map.HeightAt(startX, startZ);
        }

        var heights = map.Heights;
        for (var z = rect.Z0; z <= rect.Z1 && !_tooLarge; z++)
        {
            for (var x = rect.X0; x <= rect.X1 && !_tooLarge; x++)
            {
                var i = map.Index(x, z);
                if (map.IsWater(i))
                {
                    plan.AddProblem(i, ConstructionReasons.Water);
                    continue;
                }

                var h = heights[i];
                var slot = Track(i);
                if (mode == TerraformMode.Level)
                {
                    if (h != level)
                    {
                        TrySet(slot, level);
                    }

                    _flags[slot] |= Fixed;
                }
                else
                {
                    TrySet(slot, mode == TerraformMode.Raise ? h + 1 : h - 1);
                }
            }
        }

        if (!_tooLarge)
        {
            switch (mode)
            {
                case TerraformMode.Raise:
                    Pass(+1);
                    break;
                case TerraformMode.Lower:
                    Pass(-1);
                    break;
                default:
                    for (var round = 0; round < MaxLevelRounds && !_tooLarge; round++)
                    {
                        var raised = Pass(+1);
                        var lowered = Pass(-1);
                        if (!raised && !lowered && round > 0)
                        {
                            break;
                        }
                    }

                    break;
            }
        }

        if (_tooLarge)
        {
            Abort(ConstructionReasons.TooLarge);
            return;
        }

        if (plan.ProblemCount == 0)
        {
            VerifySlopes();
        }

        EmitTerraform();
    }

    private readonly HashSet<int> _demolishedBuildings = [];

    /// <summary>Adds every footprint tile of a town building to a clear plan, at the demolition cost per tile.</summary>
    private static void AddTownBuildingDemolition(ConstructionState state, Towns.TownRegistry towns, int buildingId, ConstructionPlan plan)
    {
        var buildings = towns.Buildings;
        var slot = buildings.SlotOf(buildingId);
        var definition = buildings.TypeDefinitionAt(slot);
        var map = state.Map;
        for (var dz = 0; dz < definition.Depth; dz++)
        {
            for (var dx = 0; dx < definition.Width; dx++)
            {
                var tile = map.Index(buildings.XAt(slot) + dx, buildings.ZAt(slot) + dz);
                var height = map.Heights[tile];
                var scenery = map.SceneryData[tile];
                plan.Cost += state.Rules.DemolishCost(TileObjectKind.TownBuilding);
                plan.AddChange(new TileChange(tile, height, height, scenery, Scenery.None, TileObjectKind.TownBuilding, TileObjectKind.None,
                    state.Objects.OwnerAt(tile), TileOwner.None));
            }
        }
    }

    /// <summary>
    /// Plans clearing an area (contracts/commands.md <c>clearArea</c>): trees are removed, objects of the
    /// company are demolished, objects of others are skipped and shown as non-blocking problems.
    /// </summary>
    public void PlanClear(ConstructionState state, int companyId, TileRect area, ConstructionPlan plan)
    {
        if (!Begin(state, companyId, area, plan, out var rect))
        {
            return;
        }

        var map = _map;
        var objects = state.Objects;
        var rules = state.Rules;
        var skipped = false;
        var roadSkipped = false;
        var townRefused = false;
        var heights = map.Heights;
        _demolishedBuildings.Clear();
        for (var z = rect.Z0; z <= rect.Z1; z++)
        {
            for (var x = rect.X0; x <= rect.X1; x++)
            {
                var i = map.Index(x, z);
                var kind = objects.KindAt(i);
                var owner = objects.OwnerAt(i);
                var scenery = map.SceneryData[i];
                var hasTree = Scenery.Kind(scenery) != 0;
                var height = heights[i];
                if (kind == TileObjectKind.Road)
                {
                    // Roads keep their links consistent only through the road removal tool (feature 007).
                    plan.AddProblem(i, ConstructionReasons.UseRoadRemoval, blocking: false);
                    roadSkipped = true;
                    continue;
                }

                if (kind == TileObjectKind.TownBuilding && owner.Kind == TileOwnerKind.Town && state.Towns is { } towns
                    && towns.Buildings.TryGetAt(i, out var buildingId))
                {
                    // Feature 008: a town building goes as a whole, if the town lets the company (FR-019).
                    if (!towns.MayDemolish(owner.Index, companyId))
                    {
                        plan.AddProblem(i, ConstructionReasons.TownRefuses, blocking: false);
                        townRefused = true;
                    }
                    else if (_demolishedBuildings.Add(buildingId))
                    {
                        AddTownBuildingDemolition(state, towns, buildingId, plan);
                    }

                    continue;
                }

                if (kind != TileObjectKind.None)
                {
                    if (!owner.IsCompany(companyId))
                    {
                        plan.AddProblem(i, ConstructionReasons.OwnedByOther, blocking: false);
                        skipped = true;
                        continue;
                    }

                    plan.Cost += rules.DemolishCost(kind) + (hasTree ? rules.ClearTreeCost : 0);
                    plan.AddChange(new TileChange(i, height, height, scenery, Scenery.None, kind, TileObjectKind.None, owner, TileOwner.None));
                }
                else if (hasTree)
                {
                    plan.Cost += rules.ClearTreeCost;
                    plan.AddChange(new TileChange(i, height, height, scenery, Scenery.None, kind, kind, owner, owner));
                }
            }
        }

        if (plan.ChangeCount == 0)
        {
            plan.Reason = townRefused ? ConstructionReasons.TownRefuses
                : skipped ? ConstructionReasons.OwnedByOther
                : roadSkipped ? ConstructionReasons.UseRoadRemoval
                : ConstructionReasons.NothingToDo;
        }
        else if (plan.ChangeCount > rules.MaxTilesPerAction || plan.Cost > rules.MaxActionCost)
        {
            plan.Reason = ConstructionReasons.TooLarge;
        }
    }

    private bool Begin(ConstructionState state, int companyId, TileRect area, ConstructionPlan plan, out TileRect rect)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(plan);
        _state = state;
        _map = state.Map;
        _plan = plan;
        _companyId = companyId;
        _tiles.Clear();
        _queueHead = 0;
        _queueCount = 0;
        _changed = 0;
        _tooLarge = false;
        plan.Reset();

        if (!area.TryClip(_map.Width, _map.Depth, out rect))
        {
            plan.Reason = ConstructionReasons.NothingToDo;
            return false;
        }

        plan.Area = rect;
        plan.HasArea = true;
        if (rect.TileCount > state.Rules.MaxTilesPerAction)
        {
            plan.Reason = ConstructionReasons.TooLarge;
            return false;
        }

        return true;
    }

    /// <summary>Adds a land tile to the scratch set (its planned height starts at the current one).</summary>
    private int Track(int tile)
    {
        var slot = _tiles.Add(tile, out var added);
        if (added)
        {
            if (slot >= _value.Length)
            {
                var size = _value.Length * 2;
                Array.Resize(ref _value, size);
                Array.Resize(ref _original, size);
                Array.Resize(ref _flags, size);
            }

            var h = _map.Heights[tile];
            _value[slot] = h;
            _original[slot] = h;
            _flags[slot] = 0;
        }

        return slot;
    }

    private int Current(int tile)
    {
        var slot = _tiles.Find(tile);
        return slot >= 0 ? _value[slot] : _map.Heights[tile];
    }

    private bool IsChanged(int tile)
    {
        var slot = _tiles.Find(tile);
        return slot >= 0 && _value[slot] != _original[slot];
    }

    /// <summary>
    /// Moves a tile to a new planned height unless something forbids it; then the tile is blocked (a
    /// problem is recorded once) and keeps its height. Returns whether the height changed.
    /// </summary>
    private bool TrySet(int slot, int height)
    {
        var flags = _flags[slot];
        if ((flags & (Fixed | Blocked)) != 0)
        {
            return false;
        }

        var tile = _tiles[slot];
        var reason = Blocker(tile, height, _original[slot]);
        if (reason is not null)
        {
            _flags[slot] = (byte)(flags | Blocked);
            _plan.AddProblem(tile, reason);
            return false;
        }

        var wasChanged = _value[slot] != _original[slot];
        _value[slot] = height;
        var isChanged = height != _original[slot];
        if (isChanged != wasChanged)
        {
            _changed += isChanged ? 1 : -1;
            if (_changed > _state.Rules.MaxTilesPerAction)
            {
                _tooLarge = true;
            }
        }

        Enqueue(slot);
        return true;
    }

    private string? Blocker(int tile, int height, int original)
    {
        if (height == original)
        {
            return null;
        }

        var objects = _state.Objects;
        var kind = objects.KindAt(tile);
        if (kind != TileObjectKind.None)
        {
            return objects.OwnerAt(tile).IsCompany(_companyId) ? ConstructionReasons.ObjectInTheWay : ConstructionReasons.OwnedByOther;
        }

        var roads = _state.Roads;
        if ((roads.FlagsAt(tile) & global::Talt.Core.Roads.RoadFeatures.Passing) != 0
            || (height > original && UnderDiagonalRoad(tile, height)))
        {
            // A bridge above, a tunnel below, or a diagonal road cutting across this tile's corner (feature 007).
            return ConstructionReasons.ObjectInTheWay;
        }

        if (height < 1 || height > _state.Rules.MaxHeight)
        {
            return ConstructionReasons.HeightLimit;
        }

        if (height < original && height < NeighbourWaterLevel(tile))
        {
            return ConstructionReasons.WouldFlood;
        }

        return null;
    }

    /// <summary>
    /// True when a diagonal road between two orthogonal neighbours of <paramref name="tile"/> cuts across it
    /// and would end lower than <paramref name="height"/> (research R3: side tiles may not rise above it).
    /// </summary>
    private bool UnderDiagonalRoad(int tile, int height)
    {
        var roads = _state.Roads;
        var width = _map.Width;
        var x = tile % width;
        var z = tile / width;
        for (var i = 0; i < 4; i++)
        {
            var first = i * 2;
            var second = (first + 2) & 7;
            var ax = x + global::Talt.Core.Roads.Dir8.OffsetX(first);
            var az = z + global::Talt.Core.Roads.Dir8.OffsetZ(first);
            var bx = x + global::Talt.Core.Roads.Dir8.OffsetX(second);
            var bz = z + global::Talt.Core.Roads.Dir8.OffsetZ(second);
            if (!_map.Contains(ax, az) || !_map.Contains(bx, bz))
            {
                continue;
            }

            var a = _map.Index(ax, az);
            var direction = global::Talt.Core.Roads.Dir8.FromOffset(bx - ax, bz - az);
            if ((roads.LinksAt(a) & (1 << direction)) != 0 && height > _map.Heights[a])
            {
                return true;
            }
        }

        return false;
    }

    private int NeighbourWaterLevel(int tile)
    {
        var map = _map;
        var width = map.Width;
        var x = tile % width;
        var z = tile / width;
        var levels = map.WaterLevels;
        var level = 0;
        if (x > 0 && map.IsWater(tile - 1))
        {
            level = Math.Max(level, levels[tile - 1]);
        }

        if (x < width - 1 && map.IsWater(tile + 1))
        {
            level = Math.Max(level, levels[tile + 1]);
        }

        if (z > 0 && map.IsWater(tile - width))
        {
            level = Math.Max(level, levels[tile - width]);
        }

        if (z < map.Depth - 1 && map.IsWater(tile + width))
        {
            level = Math.Max(level, levels[tile + width]);
        }

        return level;
    }

    private void Enqueue(int slot)
    {
        if ((_flags[slot] & Queued) != 0)
        {
            return;
        }

        if (_queueCount == _queue.Length)
        {
            var bigger = new int[_queue.Length * 2];
            for (var k = 0; k < _queueCount; k++)
            {
                bigger[k] = _queue[(_queueHead + k) % _queue.Length];
            }

            _queue = bigger;
            _queueHead = 0;
        }

        _queue[(_queueHead + _queueCount) % _queue.Length] = slot;
        _queueCount++;
        _flags[slot] |= Queued;
    }

    /// <summary>
    /// One monotone pass: while a changed tile and a land neighbour differ by more than one, raise the
    /// lower (<paramref name="direction"/> +1) or lower the higher (−1) of the two, unless it is fixed or
    /// blocked. Returns whether any height moved.
    /// </summary>
    private bool Pass(int direction)
    {
        for (var slot = 0; slot < _tiles.Count; slot++)
        {
            if (_value[slot] != _original[slot])
            {
                Enqueue(slot);
            }
        }

        var moved = false;
        var map = _map;
        var width = map.Width;
        var depth = map.Depth;
        while (_queueCount > 0 && !_tooLarge)
        {
            var slot = _queue[_queueHead];
            _queueHead = (_queueHead + 1) % _queue.Length;
            _queueCount--;
            _flags[slot] &= unchecked((byte)~Queued);

            var tile = _tiles[slot];
            var x = tile % width;
            var z = tile / width;
            if ((x > 0 && Relax(slot, tile - 1, direction, ref moved))
                || (x < width - 1 && Relax(slot, tile + 1, direction, ref moved))
                || (z > 0 && Relax(slot, tile - width, direction, ref moved))
                || (z < depth - 1 && Relax(slot, tile + width, direction, ref moved)))
            {
                Enqueue(slot); // The tile itself moved: look at all its neighbours again.
            }
        }

        return moved;
    }

    /// <summary>Fixes the pair (tile in <paramref name="slot"/>, neighbour). Returns true when the tile itself moved.</summary>
    private bool Relax(int slot, int neighbour, int direction, ref bool moved)
    {
        if (_map.IsWater(neighbour))
        {
            return false;
        }

        var v = _value[slot];
        var n = Current(neighbour);
        if (Math.Abs(v - n) <= 1 || (v == _original[slot] && !IsChanged(neighbour)))
        {
            return false;
        }

        // direction +1 moves the lower tile up to the other − 1; −1 moves the higher tile down to the other + 1.
        var moveSelf = direction > 0 ? v < n : v > n;
        if (moveSelf)
        {
            if (TrySet(slot, n - direction))
            {
                moved = true;
                return true;
            }

            return false;
        }

        var other = Track(neighbour);
        if (TrySet(other, v - direction))
        {
            moved = true;
        }

        return false;
    }

    /// <summary>Level passes may give up after a few rounds: any pair still too steep refuses the action.</summary>
    private void VerifySlopes()
    {
        var map = _map;
        var width = map.Width;
        var depth = map.Depth;
        for (var slot = 0; slot < _tiles.Count; slot++)
        {
            if (_value[slot] == _original[slot])
            {
                continue;
            }

            var tile = _tiles[slot];
            var x = tile % width;
            var z = tile / width;
            var v = _value[slot];
            if ((x > 0 && Steep(v, tile - 1)) || (x < width - 1 && Steep(v, tile + 1))
                || (z > 0 && Steep(v, tile - width)) || (z < depth - 1 && Steep(v, tile + width)))
            {
                _plan.AddProblem(tile, ConstructionReasons.TooSteep);
            }
        }

        bool Steep(int height, int neighbour) => !map.IsWater(neighbour) && Math.Abs(height - Current(neighbour)) > 1;
    }

    private void EmitTerraform()
    {
        var plan = _plan;
        var rules = _state.Rules;
        var map = _map;
        var objects = _state.Objects;
        plan.EnsureCapacity(_changed);
        long cost = 0;
        for (var slot = 0; slot < _tiles.Count; slot++)
        {
            var before = _original[slot];
            var after = _value[slot];
            if (before == after)
            {
                continue;
            }

            var tile = _tiles[slot];
            var scenery = map.SceneryData[tile];
            var hasTree = Scenery.Kind(scenery) != 0;
            cost += (Math.Abs(after - before) * rules.TerraformStepCost) + (hasTree ? rules.ClearTreeCost : 0);
            var kind = objects.KindAt(tile);
            var owner = objects.OwnerAt(tile);
            plan.AddChange(new TileChange(tile, (byte)before, (byte)after, scenery, hasTree ? Scenery.None : scenery, kind, kind, owner, owner));
        }

        plan.Cost = cost;
        if (plan.Reason is null && plan.ChangeCount == 0)
        {
            plan.Reason = ConstructionReasons.NothingToDo;
        }
        else if (plan.Reason is null && cost > rules.MaxActionCost)
        {
            plan.Reason = ConstructionReasons.TooLarge;
        }
    }

    private void Abort(string reason)
    {
        var area = _plan.Area;
        _plan.Reset();
        _plan.Area = area;
        _plan.HasArea = true;
        _plan.Reason = reason;
    }
}
