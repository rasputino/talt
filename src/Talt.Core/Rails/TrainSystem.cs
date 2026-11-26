using System.Numerics;
using Talt.Core.Construction;
using Talt.Core.Content;
using Talt.Core.Determinism;
using Talt.Core.Finance;
using Talt.Core.Roads;
using Talt.Core.Stations;
using Talt.Core.Vehicles;

namespace Talt.Core.Rails;

/// <summary>Rendering only: where one locomotive or wagon is.</summary>
public readonly record struct TrainPartPose(int TrainId, int Model, Vector3 Position, Vector2 Direction, bool Reversed);

/// <summary>
/// The trains of a game with a map (feature 015, plan.md, research R4–R13): consists bought in rail depots, running on lines
/// over owned track, stopping at red signals, loading and unloading wagon by wagon at platforms and earning money.
/// </summary>
/// <remarks>
/// <para>
/// Safety (research R4): every track tile has at most one owner train, the train whose body covers it or that reserved it.
/// Trains move only over tiles they own, so they never collide; <see cref="CheckInvariants"/> verifies it.
/// </para>
/// <para>
/// Tick (research R7): (1) the move phase runs in parallel batches; each train reads the rail layer and its own state and
/// writes only its speed and head position; (2) the sequential phase visits trains in ascending id: it releases the tiles
/// their tails left, reserves track (signals decide when, research R5), handles stops, loading, depots and routes. Parallel
/// and sequential ticks are therefore identical.
/// </para>
/// </remarks>
public sealed partial class TrainSystem : IStateHashable, IRailOccupancy, ILevelCrossingGuard
{
    private const int BatchSize = 256;

    private readonly ConstructionState _state;
    private readonly RailLayer _rails;
    private readonly StationRegistry _stations;
    private readonly FinanceSystem? _finance;
    private readonly GameContent _content;
    private readonly RoadVehicleSystem? _roadVehicles;
    private readonly List<Train> _trains = [];
    private readonly List<Train> _byId = []; // Sorted by id (the sequential phase order).
    private readonly Dictionary<int, int> _slotById = [];
    private readonly Action<int> _moveBatch;
    private readonly int[] _owner;
    private readonly int[] _typeSpeed;
    private readonly ulong _seed;
    private int _nextId = 1;
    private long _tick;
    private int _searchBudget;
    private long _deliveries;
    private long _distance;

    public TrainSystem(ConstructionState state, StationRegistry stations, FinanceSystem? finance, LineRegistry lines, GameContent content,
        RailRules rules, VehicleRules vehicleRules, ulong seed, RoadVehicleSystem? roadVehicles)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _stations = stations ?? throw new ArgumentNullException(nameof(stations));
        Lines = lines ?? throw new ArgumentNullException(nameof(lines));
        _content = content ?? throw new ArgumentNullException(nameof(content));
        Rules = rules ?? throw new ArgumentNullException(nameof(rules));
        VehicleRules = vehicleRules ?? throw new ArgumentNullException(nameof(vehicleRules));
        _finance = finance;
        _roadVehicles = roadVehicles;
        _seed = seed;
        _rails = state.Rails;
        _owner = new int[state.Map.Width * state.Map.Depth];
        _typeSpeed = new int[rules.Types.Count];
        for (var t = 0; t < rules.Types.Count; t++)
        {
            _typeSpeed[t] = rules.SpeedFromKmh(rules.Types[t].SpeedKmh);
        }

        _moveBatch = MoveBatch;
        Router = new RailRouter(state);
        SignalBlocks = new RailBlocks(_rails);
        state.RailRules = rules;
        state.RailOccupancy = this;
        if (roadVehicles is not null)
        {
            roadVehicles.Crossings = this;
        }
    }

    public RailRules Rules { get; }

    public VehicleRules VehicleRules { get; }

    /// <summary>The line registry shared with road vehicles (research R13).</summary>
    public LineRegistry Lines { get; }

    public RailDepotRegistry Depots => _state.RailDepots;

    public RailBlocks SignalBlocks { get; }

    internal RailRouter Router { get; }

    /// <summary>Stuck, no-route, skipped-stop and breakdown notices for the client (not state).</summary>
    public VehicleNoticeFeed Notices { get; } = new();

    public int Count => _trains.Count;

    /// <summary>Incremented whenever trains are added, removed or recomposed (windows refresh).</summary>
    public int Version { get; private set; }

    public long Deliveries => _deliveries;

    /// <summary>Distance run by all trains, units.</summary>
    public long TotalDistance => Interlocked.Read(ref _distance);

    /// <summary>Route searches made since the start (benchmarks, tests).</summary>
    public long RouteSearches { get; private set; }

    public int LastCreatedId => _nextId - 1;

    public int NextId => _nextId;

    /// <summary>True once anything rail existed (turns hashing on, research R15).</summary>
    public bool EverUsed => _nextId > 1;

    public Train TrainAt(int slot) => _trains[slot];

    public int SlotOf(int trainId) => _slotById.TryGetValue(trainId, out var slot) ? slot : -1;

    public Train? Find(int trainId) => _slotById.TryGetValue(trainId, out var slot) ? _trains[slot] : null;

    /// <summary>The train owning a tile (covering or reserved), or 0.</summary>
    public int OwnerOfTile(int tile) => _owner[tile];

    public RollingStockModel ModelOf(TrainPart part) => Rules.Models[part.Model];

    public int SpeedKmh(Train train) => Rules.KmhFromSpeed((train ?? throw new ArgumentNullException(nameof(train))).Speed / RailRules.SpeedScale);

    /// <summary>Current value of a train: each part's price falling linearly over its life to the minimum share.</summary>
    public long ValueOf(Train train)
    {
        ArgumentNullException.ThrowIfNull(train);
        long value = 0;
        foreach (var part in train.Parts)
        {
            value = Money.Add(value, PartValue(part));
        }

        return value;
    }

    public static TrainStatus StatusOf(Train train)
    {
        ArgumentNullException.ThrowIfNull(train);
        if (train.Mode == TrainMode.InDepot)
        {
            return TrainStatus.InDepot;
        }

        if ((train.Flags & TrainBits.Started) == 0)
        {
            return TrainStatus.Stopped;
        }

        if (train.IsBrokenDown)
        {
            return TrainStatus.BrokenDown;
        }

        if (train.Mode == TrainMode.Loading)
        {
            return TrainStatus.Loading;
        }

        if ((train.Flags & TrainBits.NoRoute) != 0)
        {
            return TrainStatus.NoRoute;
        }

        if (train.TargetKind == RouteGoalKind.None)
        {
            return TrainStatus.NoOrders;
        }

        if ((train.Flags & TrainBits.StuckNotified) != 0)
        {
            return TrainStatus.Stuck;
        }

        if ((train.Flags & TrainBits.Waiting) != 0 && train.Speed == 0)
        {
            return TrainStatus.WaitingAtSignal;
        }

        return train.TargetKind is RouteGoalKind.Depot or RouteGoalKind.AnyDepot ? TrainStatus.GoingToDepot : TrainStatus.Running;
    }

    /// <summary>Capacity of a train for a cargo.</summary>
    public int CapacityOf(Train train, int cargo)
    {
        ArgumentNullException.ThrowIfNull(train);
        var total = 0;
        foreach (var part in train.Parts)
        {
            if (part.Cargo == cargo)
            {
                total += Rules.Models[part.Model].Capacity;
            }
        }

        return total;
    }

    // ----- Purchases (called by commands after validation) -----

    /// <summary>Buys a part into a train standing in a depot (a new train when <paramref name="trainId"/> is −1); returns the train id.</summary>
    internal int Buy(int companyId, int depotId, int model, int cargo, int trainId, long price)
    {
        var m = Rules.Models[model];
        Train train;
        if (trainId < 0)
        {
            train = new Train(_nextId++, companyId, depotId)
            {
                Reliability = m.Reliability,
                ServiceInterval = VehicleRules.DefaultServiceIntervalDays,
            };
            _slotById[train.Id] = _trains.Count;
            _trains.Add(train);
            _byId.Add(train);
        }
        else
        {
            train = Find(trainId)!;
        }

        var handle = _finance?.RunningCosts.Register(companyId, m.RunningCostPerYear, price) ?? -1;
        var part = new TrainPart(model, m.CargoMask == 0 ? -1 : (m.Carries(cargo) ? cargo : m.DefaultCargo), handle, _stations.Today, price);
        if (m.Kind == RollingStockKind.Locomotive)
        {
            var at = 0;
            while (at < train.Parts.Count && Rules.Models[train.Parts[at].Model].Kind == RollingStockKind.Locomotive)
            {
                at++;
            }

            train.Parts.Insert(at, part);
        }
        else
        {
            train.Parts.Add(part);
        }

        _state.FindCompany(companyId)!.Book(LedgerCategory.VehiclePurchase, -price);
        Recompute(train);
        Version++;
        return train.Id;
    }

    /// <summary>Sells a part (or the whole train with −1) of a train standing in a depot.</summary>
    internal void Sell(Train train, int partIndex)
    {
        var company = _state.FindCompany(train.Owner)!;
        if (partIndex < 0)
        {
            for (var i = train.Parts.Count - 1; i >= 0; i--)
            {
                SellPart(company, train, i);
            }

            RemoveTrain(train);
            return;
        }

        SellPart(company, train, partIndex);
        if (train.Parts.Count == 0)
        {
            RemoveTrain(train);
            return;
        }

        Recompute(train);
        Version++;
    }

    private void SellPart(Company company, Train train, int index)
    {
        var part = train.Parts[index];
        company.Book(LedgerCategory.VehiclePurchase, PartValue(part));
        if (part.RunningHandle >= 0)
        {
            _finance?.RunningCosts.Unregister(part.RunningHandle);
        }

        train.Parts.RemoveAt(index);
    }

    private void RemoveTrain(Train train)
    {
        ReleaseAll(train);
        var slot = _slotById[train.Id];
        var last = _trains.Count - 1;
        if (slot != last)
        {
            _trains[slot] = _trains[last];
            _slotById[_trains[slot].Id] = slot;
        }

        _trains.RemoveAt(last);
        _slotById.Remove(train.Id);
        _byId.Remove(train);
        Version++;
    }

    internal static void SetCargo(Train train, int partIndex, int cargo) => train.Parts[partIndex].Cargo = cargo;

    internal void SetStarted(Train train, bool started)
    {
        train.Flags = started ? train.Flags | TrainBits.Started : train.Flags & ~TrainBits.Started;
        if (started && train.Mode == TrainMode.InDepot)
        {
            ChooseTarget(train, advance: false);
        }
    }

    internal void AssignLine(Train train, int lineId)
    {
        train.Line = lineId;
        train.StopIndex = 0;
        train.Orders &= ~VehicleOrders.SendToDepot;
        if (lineId >= 0)
        {
            train.Flags |= TrainBits.Started;
        }

        ChooseTarget(train, advance: false);
    }

    internal void ToggleSendToDepot(Train train)
    {
        train.Orders ^= VehicleOrders.SendToDepot;
        if (train.Mode != TrainMode.InDepot)
        {
            ChooseTarget(train, advance: false);
        }
    }

    internal static void SetServiceInterval(Train train, int days) => train.ServiceInterval = days;

    /// <summary>Recomputes the derived consist values (data-model.md Train derived fields).</summary>
    private void Recompute(Train train)
    {
        int length = 0, mass = 0, power = 0, effort = 0, speed = int.MaxValue, reliability = 1000;
        var electric = false;
        foreach (var part in train.Parts)
        {
            var m = Rules.Models[part.Model];
            length += m.Length;
            mass += m.MassTonnes;
            power += m.PowerKw;
            effort += m.TractiveEffortKn;
            speed = Math.Min(speed, m.Speed);
            if (m.Kind == RollingStockKind.Locomotive)
            {
                electric |= m.Power == RailPower.Electric;
                reliability = Math.Min(reliability, m.Reliability);
            }
        }

        train.Length = length;
        train.MassTonnes = Math.Max(1, mass);
        train.PowerKw = power;
        train.EffortKn = effort;
        train.MaxSpeed = speed == int.MaxValue ? 0 : speed;
        train.Electric = electric;
        train.Reliability = Math.Min(train.Reliability, reliability);
    }

    private long PartValue(TrainPart part)
    {
        var model = Rules.Models[part.Model];
        var lifeDays = model.LifeYears * 365L;
        var age = Math.Max(0, _stations.Today - part.PurchaseDay);
        var percent = Math.Max(VehicleRules.MinValuePercent, 100 - (age * 100L / lifeDays));
        return part.Price * percent / 100;
    }

    // ----- Occupancy (FR-005, FR-008) and level crossings (research R12) -----

    public bool IsTileUsed(int tile)
    {
        if (_owner[tile] != 0)
        {
            return true;
        }

        var depot = Depots.IndexAtTile(tile);
        if (depot >= 0)
        {
            var id = Depots.IdAt(depot);
            foreach (var train in _trains)
            {
                if (train.Depot == id)
                {
                    return true;
                }
            }
        }

        return false;
    }

    public bool Blocks(ConstructionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (_trains.Count == 0)
        {
            return false;
        }

        foreach (var change in plan.Changes)
        {
            // Only the track's shape matters to trains: a new type (electrification) or signal under a train is safe.
            var reshaped = change.RailChanged && (change.OldRail.Links != change.NewRail.Links || change.OldRail.LongDir != change.NewRail.LongDir
                || change.OldRail.Span != change.NewRail.Span || change.OldRail.HasRail != change.NewRail.HasRail);
            if ((reshaped && IsTileUsed(change.Index))
                || (change.OldObject != change.NewObject && change.OldObject is TileObjectKind.RailDepot or TileObjectKind.Station && IsTileUsed(change.Index)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True when road vehicles must not enter the tile: a level crossing a train owns.</summary>
    public bool IsClosed(int tile) => _owner[tile] != 0 && _rails.HasRail(tile);

    // ----- Rendering -----

    /// <summary>Number of parts of all trains on track (the size <see cref="CopyPoses"/> needs).</summary>
    public int PartsOnTrack()
    {
        var n = 0;
        foreach (var train in _trains)
        {
            if (train.Mode != TrainMode.InDepot)
            {
                n += train.Parts.Count;
            }
        }

        return n;
    }

    /// <summary>Writes the pose of every part of every train on track (render-only floats); returns the count written.</summary>
    public int CopyPoses(Span<TrainPartPose> into)
    {
        var map = _state.Map;
        var n = 0;
        foreach (var train in _trains)
        {
            if (train.Mode == TrainMode.InDepot || train.StepCount == 0)
            {
                continue;
            }

            var head = train.HeadPosition;
            var reversed = (train.Flags & TrainBits.Reversed) != 0;
            long offset = 0;
            for (var k = 0; k < train.Parts.Count && n < into.Length; k++)
            {
                var index = reversed ? train.Parts.Count - 1 - k : k;
                var part = train.Parts[index];
                var len = Rules.Models[part.Model].Length;
                var centre = head - offset - (len / 2);
                offset += len;
                if (!PointAt(train, centre, out var x, out var z, out var y, out var dx, out var dz))
                {
                    continue;
                }

                into[n++] = new TrainPartPose(train.Id, part.Model, new Vector3(x, y, z), new Vector2(dx, dz), reversed);
            }
        }

        _ = map;
        return n;
    }

    /// <summary>The point at a position along the train's steps (tile centres joined by straight lines), in tile units.</summary>
    private bool PointAt(Train train, long position, out float x, out float z, out float y, out float dx, out float dz)
    {
        x = z = y = dx = dz = 0;
        var width = _rails.Width;
        var heights = _state.Map.Heights;
        if (position < 0)
        {
            position = 0; // Still inside the depot: drawn at its entrance.
        }

        for (var i = 0; i < train.StepCount; i++)
        {
            var len = train.StepLength(i);
            if (position <= len || i == train.StepCount - 1)
            {
                var from = train.StepFrom(i);
                var dir = train.StepDir(i);
                var to = _rails.FollowLink(from, dir);
                if (to < 0)
                {
                    return false;
                }

                var t = Math.Clamp(position / (float)len, 0f, 1f);
                var fx = (from % width) + 0.5f;
                var fz = (from / width) + 0.5f;
                var tx = (to % width) + 0.5f;
                var tz = (to / width) + 0.5f;
                x = fx + ((tx - fx) * t);
                z = fz + ((tz - fz) * t);
                y = heights[from] + ((heights[to] - heights[from]) * t);
                var l = MathF.Sqrt(((tx - fx) * (tx - fx)) + ((tz - fz) * (tz - fz)));
                dx = (tx - fx) / l;
                dz = (tz - fz) / l;
                return true;
            }

            position -= len;
        }

        return false;
    }

    // ----- Invariants and hashing -----

    /// <summary>
    /// Null, or the first broken safety invariant (FR-018): every train's owned tiles match the owner index, no tile is
    /// owned by two trains, and every owned tile has track.
    /// </summary>
    public string? CheckInvariants()
    {
        var seen = new Dictionary<int, int>();
        foreach (var train in _trains)
        {
            if (train.Mode == TrainMode.InDepot)
            {
                if (train.StepCount != 0)
                {
                    return $"train {train.Id}: in a depot with track";
                }

                continue;
            }

            for (var i = 0; i <= train.StepCount; i++)
            {
                var tile = i < train.StepCount ? train.StepFrom(i) : train.EndTile;
                if (tile < 0)
                {
                    continue;
                }

                if (seen.TryGetValue(tile, out var other) && other != train.Id)
                {
                    return $"tile {tile}: owned by trains {other} and {train.Id}";
                }

                if (seen.ContainsKey(tile) && i < train.StepCount && i > 0)
                {
                    return $"train {train.Id}: tile {tile} twice";
                }

                seen[tile] = train.Id;
                if (_owner[tile] != train.Id)
                {
                    return $"tile {tile}: index says {_owner[tile]}, train {train.Id} owns it";
                }

                if (!_rails.HasRail(tile))
                {
                    return $"train {train.Id}: tile {tile} has no track";
                }
            }

            if (train.HeadStep < 0 || train.HeadStep >= Math.Max(1, train.StepCount) || train.HeadOffset < 0
                || (train.StepCount > 0 && train.HeadOffset > train.StepLength(train.HeadStep)))
            {
                return $"train {train.Id}: head outside its steps";
            }
        }

        for (var tile = 0; tile < _owner.Length; tile++)
        {
            if (_owner[tile] != 0 && !seen.ContainsKey(tile))
            {
                return $"tile {tile}: index names train {_owner[tile]} that does not own it";
            }
        }

        return null;
    }

    public void AppendTo(StateHasher hasher)
    {
        ArgumentNullException.ThrowIfNull(hasher);
        if (!EverUsed)
        {
            return; // Research R15: games without trains keep their fingerprints.
        }

        hasher.Append(_nextId);
        hasher.Append(_deliveries);
        hasher.Append<long>(_maintenanceCarry);
        hasher.Append(_byId.Count);
        foreach (var train in _byId)
        {
            train.AppendTo(hasher);
        }
    }
}

/// <summary>Lets road vehicles stop before level crossings a train owns (research R12).</summary>
public interface ILevelCrossingGuard
{
    bool IsClosed(int tile);
}
