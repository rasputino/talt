using System.Numerics;
using Talt.Core.Construction;
using Talt.Core.Content;
using Talt.Core.Determinism;
using Talt.Core.Finance;
using Talt.Core.Network;
using Talt.Core.Roads;
using Talt.Core.Simulation;
using Talt.Core.Stations;

namespace Talt.Core.Vehicles;

/// <summary>Where a vehicle is going (data-model.md RoadVehicle.target).</summary>
public enum VehicleTargetKind : byte
{
    None = 0,
    Stop = 1,
    Depot = 2,
}

/// <summary>How the target tile is used (research R5).</summary>
public enum TargetForm : byte
{
    None = 0,

    /// <summary>A bay: stop at the dead end's turning point (or the second place behind it).</summary>
    Bay = 1,

    /// <summary>A drive-through stop: stop at the tile's centre.</summary>
    DriveThrough = 2,

    /// <summary>A depot: drive inside at the turning point.</summary>
    Depot = 3,
}

/// <summary>Standing orders besides the line (data-model.md RoadVehicle.orderFlags).</summary>
[Flags]
public enum VehicleOrders : byte
{
    None = 0,
    ServiceDue = 1,
    SendToDepot = 2,
    Repair = 4,
}

/// <summary>What the vehicle window shows (data-model.md States).</summary>
public enum RoadVehicleStatus : byte
{
    InDepot,
    Stopped,
    Driving,
    Loading,
    Waiting,
    GoingToDepot,
    BrokenDown,
    Stuck,
    NoRoute,
    NoOrders,
}

/// <summary>Rendering only: what the client needs to draw a vehicle.</summary>
public readonly record struct RoadVehiclePose(Vector2 Position, Vector2 Direction, float Height, bool Visible, bool BrokenDown);

/// <summary>
/// The road vehicles of a game with a map (feature 012; research R1–R14; contracts/vehicles-api.md): buses and trucks
/// bought in depots, running on lines over the road tiles in lanes, interacting with each other, loading and unloading
/// at stations and earning money.
/// </summary>
/// <remarks>
/// <para>
/// Storage: dense slots with stable ids (swap-with-last removal) holding a packed hot <see cref="RoadVehicleState"/>
/// (double buffered) and cold arrays. Lanes, junction holders and occupancy are derived indexes, never hashed.
/// </para>
/// <para>
/// Tick (research R7): (1) drive step in parallel batches, each vehicle reading only the previous tick and writing its
/// own slot of the second buffer; (2) lane lists updated sequentially for the vehicles that changed lane; (3) events
/// sequentially in slot order: junction grants, turnarounds, depots, stops, loading, routes, stuck vehicles. Parallel
/// and sequential ticks are therefore identical.
/// </para>
/// </remarks>
public sealed partial class RoadVehicleSystem : IStateHashable, IVehicleOccupancy
{
    private const int BatchSize = 1024;

    private readonly ConstructionState _state;
    private readonly RoadLayer _roads;
    private readonly RoadNetwork _network;
    private readonly GameContent _content;
    private readonly StationRegistry _stations;
    private readonly FinanceSystem? _finance;
    private readonly Action<int> _driveBatch;
    private readonly Dictionary<int, int> _slotById = [];
    private readonly int[] _typeSpeed;
    private readonly ulong _seed;

    // Hot state, double buffered (research R7).
    private RoadVehicleState[] _hot = new RoadVehicleState[64];
    private RoadVehicleState[] _next = new RoadVehicleState[64];
    private LaneCache[] _cache = new LaneCache[64]; // Derived, written by each vehicle for itself (not hashed).

    // Cold state by slot (data-model.md RoadVehicle).
    private int[] _id = new int[64];
    private int[] _owner = new int[64];
    private int[] _model = new int[64];
    private int[] _cargo = new int[64];
    private int[] _homeDepot = new int[64];
    private int[] _line = new int[64];
    private int[] _stopIndex = new int[64];
    private VehicleTargetKind[] _targetKind = new VehicleTargetKind[64];
    private int[] _targetId = new int[64];
    private TargetForm[] _targetForm = new TargetForm[64];
    private VehicleOrders[] _orders = new VehicleOrders[64];
    private CargoHold[] _hold = new CargoHold[64];
    private int[] _loadTimer = new int[64];
    private int[] _loadedThisStep = new int[64];
    private RouteStep[][] _route = new RouteStep[64][];
    private int[] _tailFrom = new int[64];
    private int[] _tailDir = new int[64];
    private int[] _ageDays = new int[64];
    private int[] _reliability = new int[64];
    private int[] _daysSinceService = new int[64];
    private int[] _serviceInterval = new int[64];
    private int[] _depotInside = new int[64];
    private int[] _runningHandle = new int[64];
    private long[] _profitThisYear = new long[64];
    private long[] _profitLastYear = new long[64];
    private long[] _runningCarry = new long[64];
    private int[] _purchaseDay = new int[64];

    private int _nextId = 1;
    private long _tick;
    private long _deliveries;
    private long _distance;

    /// <summary>Creates the vehicle system of a game with a map.</summary>
    public RoadVehicleSystem(ConstructionState state, StationRegistry stations, FinanceSystem? finance, GameContent content, VehicleRules rules, ulong seed)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _stations = stations ?? throw new ArgumentNullException(nameof(stations));
        _content = content ?? throw new ArgumentNullException(nameof(content));
        Rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _finance = finance;
        _roads = state.Roads;
        _network = state.Network;
        _seed = seed;
        _driveBatch = DriveBatch;
        Router = new RoadRouter(_network, useCache: false);
        var types = state.RoadRules.Types;
        _typeSpeed = new int[types.Count];
        for (var t = 0; t < types.Count; t++)
        {
            _typeSpeed[t] = types[t].SpeedKmh <= 0 ? int.MaxValue : rules.SpeedFromKmh(types[t].SpeedKmh);
        }

        Depots = new DepotRegistry();
        Lines = new LineRegistry();
        state.AttachDepots(Depots);
        state.Occupancy = this;
        InitLanes();
        BuildStopTable();
    }

    public VehicleRules Rules { get; }

    /// <summary>Level crossings closed by trains (feature 015, research R12); null without a train system.</summary>
    public Rails.ILevelCrossingGuard? Crossings { get; set; }

    public DepotRegistry Depots { get; }

    public LineRegistry Lines { get; }

    /// <summary>Stuck, no-route, skipped-stop and breakdown notices for the client (not state).</summary>
    public VehicleNoticeFeed Notices { get; } = new();

    /// <summary>Number of vehicles.</summary>
    public int Count { get; private set; }

    /// <summary>Incremented whenever vehicles are added or removed (windows refresh).</summary>
    public int Version { get; private set; }

    /// <summary>Deliveries made by vehicles since the start.</summary>
    public long Deliveries => _deliveries;

    /// <summary>Distance driven by all vehicles, units.</summary>
    public long TotalDistance => Interlocked.Read(ref _distance);

    /// <summary>Route searches not served by the route cache since the start (benchmarks, tests).</summary>
    public long RouteSearches { get; private set; }

    internal RoadRouter Router { get; }

    /// <summary>True once anything existed (turns hashing on, FR-031).</summary>
    public bool EverUsed => _nextId > 1 || Depots.EverUsed || Lines.EverUsed;

    // ----- Queries (contracts/vehicles-api.md) -----

    public int IdAt(int slot) => _id[slot];

    public int SlotOf(int vehicleId) => _slotById.TryGetValue(vehicleId, out var slot) ? slot : -1;

    public bool Contains(int vehicleId) => _slotById.ContainsKey(vehicleId);

    /// <summary>Id given to the most recently bought vehicle (0 if none).</summary>
    public int LastCreatedId => _nextId - 1;

    /// <summary>The id the next vehicle will get.</summary>
    public int NextId => _nextId;

    public int OwnerAt(int slot) => _owner[slot];

    public VehicleModel ModelAt(int slot) => Rules.Models[_model[slot]];

    public int ModelIndexAt(int slot) => _model[slot];

    public int CargoAt(int slot) => _cargo[slot];

    public int LineAt(int slot) => _line[slot];

    public int StopIndexAt(int slot) => _stopIndex[slot];

    public CargoHold HoldAt(int slot) => _hold[slot];

    public int AgeDaysAt(int slot) => _ageDays[slot];

    public int ReliabilityAt(int slot) => _reliability[slot];

    public int ServiceIntervalAt(int slot) => _serviceInterval[slot];

    public int DaysSinceServiceAt(int slot) => _daysSinceService[slot];

    public long ProfitThisYearAt(int slot) => _profitThisYear[slot];

    public long ProfitLastYearAt(int slot) => _profitLastYear[slot];

    /// <summary>The depot the vehicle is inside, or −1.</summary>
    public int DepotAt(int slot) => _depotInside[slot];

    public int HomeDepotAt(int slot) => _homeDepot[slot];

    public VehicleTargetKind TargetKindAt(int slot) => _targetKind[slot];

    public int TargetIdAt(int slot) => _targetId[slot];

    public VehicleOrders OrdersAt(int slot) => _orders[slot];

    /// <summary>The hot state of a slot (tests, tools).</summary>
    public RoadVehicleState StateAt(int slot) => _hot[slot];

    public int SpeedKmhAt(int slot) => Rules.KmhFromSpeed(_hot[slot].Speed);

    /// <summary>The vehicle's current value: its price falling linearly over its life to the minimum share (FR-008).</summary>
    public long ValueAt(int slot)
    {
        var model = Rules.Models[_model[slot]];
        var lifeDays = model.LifeYears * 365L;
        var percent = Math.Max(Rules.MinValuePercent, 100 - (_ageDays[slot] * 100L / lifeDays));
        return model.Price * percent / 100;
    }

    public RoadVehicleStatus StatusAt(int slot)
    {
        ref readonly var h = ref _hot[slot];
        if (h.Has(RoadVehicleBits.InDepot))
        {
            return h.Has(RoadVehicleBits.Stopped) || _line[slot] < 0 ? RoadVehicleStatus.InDepot : RoadVehicleStatus.Waiting;
        }

        if (h.Has(RoadVehicleBits.Stopped))
        {
            return RoadVehicleStatus.Stopped;
        }

        if (h.Has(RoadVehicleBits.BrokenDown))
        {
            return RoadVehicleStatus.BrokenDown;
        }

        if (h.Has(RoadVehicleBits.NoRoute))
        {
            return RoadVehicleStatus.NoRoute;
        }

        if (h.Has(RoadVehicleBits.Stuck))
        {
            return RoadVehicleStatus.Stuck;
        }

        if (h.Has(RoadVehicleBits.AtStop))
        {
            return RoadVehicleStatus.Loading;
        }

        if (_targetKind[slot] == VehicleTargetKind.Depot)
        {
            return RoadVehicleStatus.GoingToDepot;
        }

        if (_targetKind[slot] == VehicleTargetKind.None)
        {
            return RoadVehicleStatus.NoOrders;
        }

        return h.Speed == 0 ? RoadVehicleStatus.Waiting : RoadVehicleStatus.Driving;
    }

    /// <summary>Ids of the vehicles inside a depot, in slot order.</summary>
    public void VehiclesInDepot(int depotId, List<int> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        into.Clear();
        for (var slot = 0; slot < Count; slot++)
        {
            if (_depotInside[slot] == depotId)
            {
                into.Add(_id[slot]);
            }
        }
    }

    /// <summary>Ids of the vehicles assigned to a line, in slot order.</summary>
    public void VehiclesOnLine(int lineId, List<int> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        into.Clear();
        for (var slot = 0; slot < Count; slot++)
        {
            if (_line[slot] == lineId)
            {
                into.Add(_id[slot]);
            }
        }
    }

    /// <summary>Copies the vehicle id of every slot.</summary>
    public void CopyIds(Span<int> ids) => _id.AsSpan(0, Count).CopyTo(ids);

    /// <summary>Rendering only: the pose of every vehicle, in slot order.</summary>
    public void CopyPoses(VehiclePose[] poses, float[] heights, byte[] flags, bool parallel)
    {
        ArgumentNullException.ThrowIfNull(poses);
        ArgumentNullException.ThrowIfNull(heights);
        ArgumentNullException.ThrowIfNull(flags);
        if (!parallel || Count <= BatchSize)
        {
            for (var v = 0; v < Count; v++)
            {
                WritePose(v, poses, heights, flags);
            }

            return;
        }

        var batches = (Count + BatchSize - 1) / BatchSize;
        Parallel.For(0, batches, batch =>
        {
            var end = Math.Min((batch + 1) * BatchSize, Count);
            for (var v = batch * BatchSize; v < end; v++)
            {
                WritePose(v, poses, heights, flags);
            }
        });
    }

    /// <summary>Flag bits of <see cref="CopyPoses"/>.</summary>
    public const byte PoseVisible = 1;

    /// <summary>The vehicle is broken down (smoke or sparks).</summary>
    public const byte PoseBrokenDown = 2;

    /// <summary>The model is electric (sparks instead of smoke).</summary>
    public const byte PoseElectric = 4;

    /// <summary>Use <c>heights</c> instead of the ground (bridges).</summary>
    public const byte PoseHeight = 8;

    private void WritePose(int v, VehiclePose[] poses, float[] heights, byte[] flags)
    {
        ref readonly var h = ref _hot[v];
        if (h.Lane < 0)
        {
            poses[v] = default;
            flags[v] = 0;
            heights[v] = 0;
            return;
        }

        var tile = h.Lane >> 3;
        var dir = h.Lane & 7;
        var width = _roads.Width;
        var dx = Dir8.OffsetX(dir);
        var dz = Dir8.OffsetZ(dir);
        var along = h.S / (float)Dir8.StepLength(dir);
        var position = new Vector2((tile % width) + 0.5f + (dx * along), (tile / width) + 0.5f + (dz * along));
        var direction = Vector2.Normalize(new Vector2(dx, dz));
        poses[v] = new VehiclePose(position, direction);
        byte f = PoseVisible;
        if (h.Has(RoadVehicleBits.BrokenDown))
        {
            f |= PoseBrokenDown;
        }

        if (Rules.Models[_model[v]].Power == VehiclePower.Electric)
        {
            f |= PoseElectric;
        }

        var features = _roads.FlagsAt(tile);
        var span = _roads.PieceAt(tile).Span;
        if ((features & RoadFeatures.LongLinkEnd) != 0 && _roads.PieceAt(tile).LongDir == dir + 1)
        {
            if ((features & RoadFeatures.TunnelPortal) != 0 && along > 0.5f && along < span - 0.5f)
            {
                f &= unchecked((byte)~PoseVisible); // Inside the tunnel.
            }
            else if ((features & RoadFeatures.BridgeEnd) != 0)
            {
                f |= PoseHeight;
                heights[v] = _state.Map.Heights[tile] + (along is > 0.5f && along < span - 0.5f ? 1f : Math.Clamp(along < 1 ? along : span - along, 0f, 1f));
            }
        }

        flags[v] = f;
    }

    // ----- Low-level changes used by the commands -----

    /// <summary>Buys a vehicle into a depot (validated by the command); returns its id.</summary>
    internal int Add(int ownerId, int modelIndex, int cargo, int depotId, long price, int dayNumber)
    {
        EnsureCapacity(Count + 1);
        var model = Rules.Models[modelIndex];
        var slot = Count++;
        var id = _nextId++;
        _id[slot] = id;
        _owner[slot] = ownerId;
        _model[slot] = modelIndex;
        _cargo[slot] = cargo;
        _homeDepot[slot] = depotId;
        _line[slot] = -1;
        _stopIndex[slot] = 0;
        _targetKind[slot] = VehicleTargetKind.None;
        _targetId[slot] = -1;
        _targetForm[slot] = TargetForm.None;
        _orders[slot] = VehicleOrders.None;
        _hold[slot] ??= new CargoHold();
        _hold[slot].Clear();
        _loadTimer[slot] = 0;
        _loadedThisStep[slot] = 0;
        _route[slot] = [];
        _tailFrom[slot] = -1;
        _tailDir[slot] = -1;
        _ageDays[slot] = 0;
        _reliability[slot] = model.Reliability;
        _daysSinceService[slot] = 0;
        _serviceInterval[slot] = Rules.DefaultServiceIntervalDays;
        _depotInside[slot] = depotId;
        _profitThisYear[slot] = 0;
        _profitLastYear[slot] = 0;
        _runningCarry[slot] = 0;
        _purchaseDay[slot] = dayNumber;
        _runningHandle[slot] = _finance?.RunningCosts.Register(ownerId, model.RunningCostPerYear, price) ?? -1;
        _hot[slot] = new RoadVehicleState
        {
            Lane = -1,
            ModelSpeed = model.Speed,
            Accel = model.Acceleration,
            Length = model.Length,
            Flags = RoadVehicleBits.InDepot | RoadVehicleBits.Stopped,
            HeldJunction = -1,
            Requested = -1,
            WaitingSince = -1,
            TargetTile = -1,
        };
        _cache[slot] = default;
        _slotById.Add(id, slot);
        Version++;
        return id;
    }

    /// <summary>Removes a vehicle that is inside a depot (sold).</summary>
    internal void Remove(int vehicleId)
    {
        if (!_slotById.Remove(vehicleId, out var slot))
        {
            return;
        }

        if (_runningHandle[slot] >= 0)
        {
            _finance?.RunningCosts.Unregister(_runningHandle[slot]);
        }

        var last = Count - 1;
        if (slot != last)
        {
            MoveSlot(last, slot);
        }

        _route[last] = [];
        Count--;
        Version++;
    }

    private void MoveSlot(int from, int to)
    {
        _hot[to] = _hot[from];
        _cache[to] = _cache[from];
        _id[to] = _id[from];
        _owner[to] = _owner[from];
        _model[to] = _model[from];
        _cargo[to] = _cargo[from];
        _homeDepot[to] = _homeDepot[from];
        _line[to] = _line[from];
        _stopIndex[to] = _stopIndex[from];
        _targetKind[to] = _targetKind[from];
        _targetId[to] = _targetId[from];
        _targetForm[to] = _targetForm[from];
        _orders[to] = _orders[from];
        (_hold[to], _hold[from]) = (_hold[from], _hold[to]);
        _loadTimer[to] = _loadTimer[from];
        _loadedThisStep[to] = _loadedThisStep[from];
        _route[to] = _route[from];
        _tailFrom[to] = _tailFrom[from];
        _tailDir[to] = _tailDir[from];
        _ageDays[to] = _ageDays[from];
        _reliability[to] = _reliability[from];
        _daysSinceService[to] = _daysSinceService[from];
        _serviceInterval[to] = _serviceInterval[from];
        _depotInside[to] = _depotInside[from];
        _runningHandle[to] = _runningHandle[from];
        _profitThisYear[to] = _profitThisYear[from];
        _profitLastYear[to] = _profitLastYear[from];
        _runningCarry[to] = _runningCarry[from];
        _purchaseDay[to] = _purchaseDay[from];
        _slotById[_id[to]] = to;
        MoveLaneSlot(from, to);
    }

    private void EnsureCapacity(int required)
    {
        if (required <= _id.Length)
        {
            return;
        }

        var size = Math.Max(required, _id.Length * 2);
        Array.Resize(ref _hot, size);
        Array.Resize(ref _next, size);
        Array.Resize(ref _cache, size);
        Array.Resize(ref _id, size);
        Array.Resize(ref _owner, size);
        Array.Resize(ref _model, size);
        Array.Resize(ref _cargo, size);
        Array.Resize(ref _homeDepot, size);
        Array.Resize(ref _line, size);
        Array.Resize(ref _stopIndex, size);
        Array.Resize(ref _targetKind, size);
        Array.Resize(ref _targetId, size);
        Array.Resize(ref _targetForm, size);
        Array.Resize(ref _orders, size);
        Array.Resize(ref _hold, size);
        Array.Resize(ref _loadTimer, size);
        Array.Resize(ref _loadedThisStep, size);
        Array.Resize(ref _route, size);
        Array.Resize(ref _tailFrom, size);
        Array.Resize(ref _tailDir, size);
        Array.Resize(ref _ageDays, size);
        Array.Resize(ref _reliability, size);
        Array.Resize(ref _daysSinceService, size);
        Array.Resize(ref _serviceInterval, size);
        Array.Resize(ref _depotInside, size);
        Array.Resize(ref _runningHandle, size);
        Array.Resize(ref _profitThisYear, size);
        Array.Resize(ref _profitLastYear, size);
        Array.Resize(ref _runningCarry, size);
        Array.Resize(ref _purchaseDay, size);
        ResizeLaneSlots(size);
    }

    internal void SetStopped(int slot, bool stopped)
    {
        ref var h = ref _hot[slot];
        h.Flags = stopped ? h.Flags | RoadVehicleBits.Stopped : h.Flags & ~RoadVehicleBits.Stopped;
        if (!stopped && _targetKind[slot] == VehicleTargetKind.Depot && (_orders[slot] & VehicleOrders.SendToDepot) != 0 && h.Has(RoadVehicleBits.InDepot))
        {
            _orders[slot] &= ~VehicleOrders.SendToDepot;
            ChooseNextTarget(slot, advance: false);
        }
        else if (!stopped && _targetKind[slot] == VehicleTargetKind.None)
        {
            ChooseNextTarget(slot, advance: false);
        }
    }

    internal void SetCargo(int slot, int cargo) => _cargo[slot] = cargo;

    internal void SetServiceInterval(int slot, int days) => _serviceInterval[slot] = days;

    /// <summary>Assigns a line (−1 none); the vehicle restarts from the line's first stop.</summary>
    internal void AssignLine(int slot, int lineId)
    {
        _line[slot] = lineId;
        _stopIndex[slot] = 0;
        if ((_orders[slot] & (VehicleOrders.SendToDepot | VehicleOrders.Repair | VehicleOrders.ServiceDue)) == 0 || _targetKind[slot] != VehicleTargetKind.Depot)
        {
            ChooseNextTarget(slot, advance: false);
        }

        if (lineId >= 0 && _hot[slot].Has(RoadVehicleBits.InDepot))
        {
            _hot[slot].Flags &= ~RoadVehicleBits.Stopped; // A vehicle given a line in a depot starts (quickstart step 2).
        }
    }

    /// <summary>Toggles "go to depot"; returns false when no depot can be reached.</summary>
    internal bool ToggleSendToDepot(int slot)
    {
        if ((_orders[slot] & VehicleOrders.SendToDepot) != 0)
        {
            _orders[slot] &= ~VehicleOrders.SendToDepot;
            ChooseNextTarget(slot, advance: false);
            return true;
        }

        if (_hot[slot].Has(RoadVehicleBits.InDepot))
        {
            _hot[slot].Flags |= RoadVehicleBits.Stopped;
            return true;
        }

        _orders[slot] |= VehicleOrders.SendToDepot;
        return TargetNearestDepot(slot);
    }

    /// <summary>Lines changed: vehicles on a deleted line lose it; stop indexes are kept in range.</summary>
    internal void OnLineChanged(int lineId, bool deleted)
    {
        for (var slot = 0; slot < Count; slot++)
        {
            if (_line[slot] != lineId)
            {
                continue;
            }

            if (deleted)
            {
                _line[slot] = -1;
                if (_targetKind[slot] == VehicleTargetKind.Stop)
                {
                    ClearTarget(slot);
                }

                continue;
            }

            var count = Lines.StopCount(lineId);
            if (_stopIndex[slot] >= count)
            {
                _stopIndex[slot] = 0;
            }

            if (_targetKind[slot] == VehicleTargetKind.Stop && !_hot[slot].Has(RoadVehicleBits.AtStop))
            {
                ChooseNextTarget(slot, advance: false);
            }
        }
    }

    // ----- Occupancy (research R12) -----

    private readonly HashSet<int> _occupied = [];
    private long _occupiedTick = -1;

    /// <summary>True when a vehicle stands on the tile (its lane's from- or to-tile) or is inside a depot on it.</summary>
    public bool IsTileOccupied(int tile)
    {
        if (_occupiedTick != _tick)
        {
            _occupied.Clear();
            for (var slot = 0; slot < Count; slot++)
            {
                var lane = _hot[slot].Lane;
                if (lane >= 0)
                {
                    _occupied.Add(lane >> 3);
                    var to = _roads.Follow(lane >> 3, lane & 7);
                    if (to >= 0)
                    {
                        _occupied.Add(to);
                    }
                }
                else if (_depotInside[slot] >= 0 && Depots.SlotOf(_depotInside[slot]) is var d and >= 0)
                {
                    _occupied.Add(Depots.TileAt(d));
                }
            }

            _occupiedTick = _tick;
        }

        return _occupied.Contains(tile);
    }

    public bool Blocks(ConstructionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (Count == 0)
        {
            return false;
        }

        foreach (var change in plan.Changes)
        {
            var removesLinks = change.RoadChanged && (change.OldRoad.Links & ~change.NewRoad.Links) != 0;
            var removesObject = change.OldObject is TileObjectKind.Station or TileObjectKind.Depot && change.NewObject != change.OldObject;
            var touchesStop = _stations.HasPiece(change.Index) && change.OldObject == change.NewObject && !change.RoadChanged;
            if ((removesLinks || removesObject || touchesStop) && IsTileOccupied(change.Index))
            {
                return true;
            }
        }

        return false;
    }

    // ----- Hashing (FR-031) -----

    public void AppendTo(StateHasher hasher)
    {
        ArgumentNullException.ThrowIfNull(hasher);
        if (!EverUsed)
        {
            return; // Games without vehicles keep their fingerprints.
        }

        hasher.Append(Rules.Fingerprint);
        hasher.Append(_nextId);
        hasher.Append(Count);
        hasher.Append(_deliveries);
        hasher.Append(TotalDistance);
        hasher.Append(Depots);
        hasher.Append(Lines);
        hasher.Append<RoadVehicleState>(_hot.AsSpan(0, Count));
        hasher.Append<int>(_id.AsSpan(0, Count));
        hasher.Append<int>(_owner.AsSpan(0, Count));
        hasher.Append<int>(_model.AsSpan(0, Count));
        hasher.Append<int>(_cargo.AsSpan(0, Count));
        hasher.Append<int>(_homeDepot.AsSpan(0, Count));
        hasher.Append<int>(_line.AsSpan(0, Count));
        hasher.Append<int>(_stopIndex.AsSpan(0, Count));
        hasher.Append<VehicleTargetKind>(_targetKind.AsSpan(0, Count));
        hasher.Append<int>(_targetId.AsSpan(0, Count));
        hasher.Append<TargetForm>(_targetForm.AsSpan(0, Count));
        hasher.Append<VehicleOrders>(_orders.AsSpan(0, Count));
        hasher.Append<int>(_loadTimer.AsSpan(0, Count));
        hasher.Append<int>(_loadedThisStep.AsSpan(0, Count));
        hasher.Append<int>(_tailFrom.AsSpan(0, Count));
        hasher.Append<int>(_tailDir.AsSpan(0, Count));
        hasher.Append<int>(_ageDays.AsSpan(0, Count));
        hasher.Append<int>(_reliability.AsSpan(0, Count));
        hasher.Append<int>(_daysSinceService.AsSpan(0, Count));
        hasher.Append<int>(_serviceInterval.AsSpan(0, Count));
        hasher.Append<int>(_depotInside.AsSpan(0, Count));
        hasher.Append<long>(_profitThisYear.AsSpan(0, Count));
        hasher.Append<long>(_profitLastYear.AsSpan(0, Count));
        hasher.Append<long>(_runningCarry.AsSpan(0, Count));
        hasher.Append<int>(_purchaseDay.AsSpan(0, Count));
        for (var slot = 0; slot < Count; slot++)
        {
            hasher.Append(_hold[slot]);
            var route = _route[slot];
            hasher.Append(route.Length);
            for (var i = 0; i < route.Length; i++)
            {
                hasher.Append(route[i].FromTile);
                hasher.Append(route[i].FirstDirection);
            }
        }
    }
}
