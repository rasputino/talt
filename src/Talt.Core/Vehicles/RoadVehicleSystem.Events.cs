using System.Numerics;
using Talt.Core.Construction;
using Talt.Core.Finance;
using Talt.Core.Network;
using Talt.Core.Roads;
using Talt.Core.Stations;

namespace Talt.Core.Vehicles;

/// <summary>
/// Phase 3 of the tick (research R4–R8): everything that touches shared state, in slot order — junction grants,
/// arrivals, loading, departures, turnarounds, depots, routes and stuck vehicles. Station loading and unloading
/// (feature 010) and money (feature 011) are only used here.
/// </summary>
public sealed partial class RoadVehicleSystem
{
    private readonly Dictionary<int, int> _holders = []; // Junction tile → holder vehicle id (derived).
    private readonly Dictionary<int, (long Key, int Slot)> _requests = [];
    private readonly List<RouteStep> _routeScratch = [];
    private readonly Dictionary<long, CachedRoute> _routeCache = [];
    private readonly List<int> _nearDepots = [];
    private int _searchBudget;
    private int _staleBudget;
    private int _stationsVersion = -1;
    private int _depotsVersion = -1;

    private sealed record CachedRoute(RouteStep[] Steps, long Length, int Version, bool Found);

    private enum RouteResult
    {
        Found,
        NoRoute,
        Deferred,
    }

    private void RunEvents()
    {
        _searchBudget = Rules.SearchNodesPerTick;
        _staleBudget = Rules.SearchNodesPerTick / 10;

        // Releases, then grants: smallest (waiting since, id) per free junction (research R4).
        _requests.Clear();
        for (var a = 0; a < _activeCount; a++)
        {
            var slot = _active[a];
            ref var h = ref _hot[slot];
            if ((h.Flags & RoadVehicleBits.ReleasedEvent) != 0)
            {
                ReleaseHeld(slot);
            }

            if ((h.Flags & RoadVehicleBits.RequestEvent) != 0 && h.HeldJunction < 0)
            {
                var key = ((long)h.WaitingSince << 32) | (uint)_id[slot];
                if (!_requests.TryGetValue(h.Requested, out var best) || key < best.Key)
                {
                    _requests[h.Requested] = (key, slot);
                }
            }
        }

        foreach (var (tile, request) in _requests)
        {
            if (!_holders.ContainsKey(tile))
            {
                ref var h = ref _hot[request.Slot];
                _holders[tile] = _id[request.Slot];
                h.HeldJunction = tile;
                h.Flags |= RoadVehicleBits.HoldsJunction;
                h.WaitingSince = -1;
            }
        }

        if (_stations.Version != _stationsVersion || Depots.Version != _depotsVersion)
        {
            ValidateTargets();
        }

        var noticeTicks = _noticeTicks;
        for (var a = 0; a < _activeCount; a++)
        {
            var slot = _active[a];
            var flags = _hot[slot].Flags;
            if ((flags & RoadVehicleBits.RecoveredEvent) != 0)
            {
                Recover(slot);
            }

            if ((flags & RoadVehicleBits.InDepot) != 0)
            {
                TryLeaveDepot(slot);
                continue;
            }

            if ((flags & RoadVehicleBits.ArrivedEvent) != 0)
            {
                Arrive(slot);
            }
            else if ((flags & RoadVehicleBits.AtStop) != 0)
            {
                LoadStep(slot);
            }
            else if ((flags & RoadVehicleBits.DeadEndEvent) != 0)
            {
                TurnAtDeadEnd(slot);
            }

            flags = _hot[slot].Flags;

            if ((flags & (RoadVehicleBits.RouteMismatchEvent | RoadVehicleBits.HoldsJunction | RoadVehicleBits.NoRoute)) == RoadVehicleBits.RouteMismatchEvent)
            {
                _hot[slot].Flags |= RoadVehicleBits.UrgentRoute; // Without a route it only retries once a day (below).
            }

            if ((_hot[slot].Flags & RoadVehicleBits.UrgentRoute) != 0 && (_hot[slot].Flags & RoadVehicleBits.HoldsJunction) == 0
                && (((flags & (RoadVehicleBits.RouteMismatchEvent | RoadVehicleBits.ArrivedEvent | RoadVehicleBits.DeadEndEvent)) != 0
                        && (flags & RoadVehicleBits.NoRoute) == 0)
                    || (_tick + _id[slot]) % 4 == 0))
            {
                if (_targetKind[slot] == VehicleTargetKind.None)
                {
                    ChooseNextTarget(slot, advance: false);
                }

                if (_targetKind[slot] == VehicleTargetKind.None)
                {
                    TargetNearestDepot(slot);
                }

                var result = _targetKind[slot] == VehicleTargetKind.None ? RouteResult.NoRoute : PlanRoute(slot);
                if (result == RouteResult.Found)
                {
                    _hot[slot].Flags &= ~(RoadVehicleBits.UrgentRoute | RoadVehicleBits.NoRoute);
                }
                else if (result == RouteResult.NoRoute)
                {
                    OnNoRoute(slot);
                }
            }
            else if ((_hot[slot].Flags & RoadVehicleBits.NoRoute) != 0 && (_tick + _id[slot]) % (5 * Simulation.GameClock.TicksPerDay) == 0)
            {
                _hot[slot].Flags |= RoadVehicleBits.UrgentRoute; // Retry every five days.
            }

            if ((_orders[slot] & (VehicleOrders.Repair | VehicleOrders.SendToDepot)) != 0 && _targetKind[slot] != VehicleTargetKind.Depot
                && (_hot[slot].Flags & (RoadVehicleBits.HoldsJunction | RoadVehicleBits.AtStop)) == 0 && (_tick + _id[slot]) % 8 == 0)
            {
                TargetNearestDepot(slot); // A depot order not placed yet (no depot reachable, or the search budget was spent).
            }

            var still = _hot[slot].StillTicks;
            if (still == noticeTicks)
            {
                _hot[slot].Flags |= RoadVehicleBits.Stuck;
                Notify(VehicleNoticeKind.Stuck, slot, -1);
            }
            else if (still >= 2 * noticeTicks && (_tick + _id[slot]) % 8 == 0 && TryTurnaround(slot))
            {
                _hot[slot].StillTicks = noticeTicks + 1;
            }
        }
    }

    private void ReleaseHeld(int slot)
    {
        var tile = _hot[slot].Requested; // Phase 1 leaves the released tile here.
        if (_holders.TryGetValue(tile, out var holder) && holder == _id[slot])
        {
            _holders.Remove(tile);
        }

        _hot[slot].Requested = -1;
    }

    // ----- Targets and orders -----

    private void ClearTarget(int slot)
    {
        _targetKind[slot] = VehicleTargetKind.None;
        _targetId[slot] = -1;
        _targetForm[slot] = TargetForm.None;
        _hot[slot].TargetTile = -1;
        _route[slot] = [];
        _tailFrom[slot] = -1;
        _tailDir[slot] = -1;
        _hot[slot].RouteIndex = 0;
    }

    /// <summary>Sets the target to the line's current stop (after advancing when asked), skipping stops it cannot use.</summary>
    private void ChooseNextTarget(int slot, bool advance)
    {
        var line = _line[slot];
        var count = line >= 0 && Lines.Exists(line) ? Lines.StopCount(line) : 0;
        if (count == 0)
        {
            ClearTarget(slot);
            return;
        }

        var index = advance ? (_stopIndex[slot] + 1) % count : _stopIndex[slot] % count;
        var kind = Rules.Models[_model[slot]].Kind;
        for (var tries = 0; tries < count; tries++)
        {
            var stationId = Lines.StopAt(line, index).StationId;
            if (FindPiece(slot, stationId, kind, out var tile, out var form))
            {
                _stopIndex[slot] = index;
                _targetKind[slot] = VehicleTargetKind.Stop;
                _targetId[slot] = stationId;
                _targetForm[slot] = form;
                _hot[slot].TargetTile = tile;
                _hot[slot].Flags |= RoadVehicleBits.UrgentRoute;
                return;
            }

            Notify(VehicleNoticeKind.SkippedStop, slot, stationId);
            index = (index + 1) % count;
        }

        ClearTarget(slot);
    }

    /// <summary>The station's piece of the vehicle's kind closest to the vehicle (ties by tile index).</summary>
    private bool FindPiece(int slot, int stationId, StationPieceKind kind, out int tile, out TargetForm form)
    {
        tile = -1;
        form = TargetForm.None;
        var stationSlot = _stations.SlotOf(stationId);
        if (stationSlot < 0 || _stations.OwnerAt(stationSlot) != _owner[slot])
        {
            return false;
        }

        var (x, z) = PositionTile(slot);
        var width = _roads.Width;
        var best = long.MaxValue;
        var tiles = _stations.TilesAt(stationSlot);
        for (var i = 0; i < tiles.Count; i++)
        {
            var t = tiles[i]; // Indexed: foreach over the read-only list would box an enumerator.
            if (!_stations.TryGetPieceAt(t, out _, out var type, out _) || type == StationRegistry.WarehousePiece)
            {
                continue;
            }

            var piece = _stations.Rules.Pieces[type];
            if (piece.Kind != kind)
            {
                continue;
            }

            var distance = ((long)Math.Abs((t % width) - x) + Math.Abs((t / width) - z)) * 100_000_000L + t;
            if (distance < best)
            {
                best = distance;
                tile = t;
                form = piece.Form == StationPieceForm.Bay ? TargetForm.Bay : TargetForm.DriveThrough;
            }
        }

        return tile >= 0;
    }

    private (int X, int Z) PositionTile(int slot)
    {
        var width = _roads.Width;
        var lane = _hot[slot].Lane;
        if (lane >= 0)
        {
            return ((lane >> 3) % width, (lane >> 3) / width);
        }

        var depot = Depots.SlotOf(_depotInside[slot] >= 0 ? _depotInside[slot] : _homeDepot[slot]);
        return depot >= 0 ? (Depots.TileAt(depot) % width, Depots.TileAt(depot) / width) : (0, 0);
    }

    /// <summary>Targets the nearest reachable depot of the company (by route length among the four closest).</summary>
    private bool TargetNearestDepot(int slot)
    {
        var (x, z) = PositionTile(slot);
        var width = _roads.Width;
        // The (up to) three closest depots by straight distance, ties by id: from a region index (derived, rebuilt when
        // depots change), searching rings of regions outwards; no sorting, no allocation.
        _nearDepots.Clear();
        Span<long> keys = stackalloc long[3];
        EnsureDepotIndex();
        var rx = x >> DepotRegionBits;
        var rz = z >> DepotRegionBits;
        for (var ring = 0; ring <= _depotRegionsX + _depotRegionsZ; ring++)
        {
            // Once three are found, one more ring can still hold a closer depot; after that none can.
            if (_nearDepots.Count == 3 && (ring - 1) * (1 << DepotRegionBits) > (int)(keys[2] >> 32))
            {
                break;
            }

            for (var qz = rz - ring; qz <= rz + ring; qz++)
            {
                for (var qx = rx - ring; qx <= rx + ring; qx++)
                {
                    if ((Math.Abs(qx - rx) != ring && Math.Abs(qz - rz) != ring) || qx < 0 || qz < 0 || qx >= _depotRegionsX || qz >= _depotRegionsZ)
                    {
                        continue;
                    }

                    var region = qx + (qz * _depotRegionsX);
                    for (var k = _depotRegionStart[region]; k < _depotRegionStart[region + 1]; k++)
                    {
                        var d = _depotRegionSlots[k];
                        if (Depots.OwnerAt(d) != _owner[slot])
                        {
                            continue;
                        }

                        var key = ((long)(Math.Abs((Depots.TileAt(d) % width) - x) + Math.Abs((Depots.TileAt(d) / width) - z)) << 32) | (uint)Depots.IdAt(d);
                        InsertNearest(keys, key, d);
                    }
                }
            }
        }

        var from = RouteOrigin(slot);
        var bestLength = long.MaxValue;
        var bestDepot = -1;
        CachedRoute? bestRoute = null;
        for (var i = 0; i < _nearDepots.Count; i++)
        {
            var depotTile = Depots.TileAt(_nearDepots[i]);
            if (from < 0)
            {
                break;
            }

            var route = Search(from, depotTile, ignoreBudget: false);
            if (route is null)
            {
                return false; // Budget spent: the order stays and is tried again on a later tick.
            }

            if (route is { Found: true } && route.Length < bestLength)
            {
                bestLength = route.Length;
                bestDepot = _nearDepots[i];
                bestRoute = route;
                break; // The closest reachable depot in straight distance (one search in the usual case).
            }
        }

        if (bestDepot < 0)
        {
            return false;
        }

        _targetKind[slot] = VehicleTargetKind.Depot;
        _targetId[slot] = Depots.IdAt(bestDepot);
        _targetForm[slot] = TargetForm.Depot;
        _hot[slot].TargetTile = Depots.TileAt(bestDepot);
        ApplyRoute(slot, bestRoute!.Steps, -1, -1);
        return true;
    }

    private const int DepotRegionBits = 6;
    private int[] _depotRegionStart = [];
    private int[] _depotRegionSlots = [];
    private int _depotRegionsX;
    private int _depotRegionsZ;
    private int _depotIndexVersion = -1;

    private void InsertNearest(Span<long> keys, long key, int depotSlot)
    {
        var at = _nearDepots.Count;
        while (at > 0 && keys[at - 1] > key)
        {
            at--;
        }

        if (at >= 3)
        {
            return;
        }

        if (_nearDepots.Count < 3)
        {
            _nearDepots.Add(0);
        }

        for (var i = _nearDepots.Count - 1; i > at; i--)
        {
            _nearDepots[i] = _nearDepots[i - 1];
            keys[i] = keys[i - 1];
        }

        _nearDepots[at] = depotSlot;
        keys[at] = key;
    }

    /// <summary>Depot slots bucketed by 64 × 64-tile region (counting sort), rebuilt when the depots change.</summary>
    private void EnsureDepotIndex()
    {
        if (_depotIndexVersion == Depots.Version)
        {
            return;
        }

        var width = _roads.Width;
        _depotRegionsX = (width + (1 << DepotRegionBits) - 1) >> DepotRegionBits;
        _depotRegionsZ = (_roads.Depth + (1 << DepotRegionBits) - 1) >> DepotRegionBits;
        var regions = _depotRegionsX * _depotRegionsZ;
        if (_depotRegionStart.Length != regions + 1)
        {
            _depotRegionStart = new int[regions + 1];
        }

        Array.Clear(_depotRegionStart);
        if (_depotRegionSlots.Length < Depots.Count)
        {
            _depotRegionSlots = new int[Math.Max(Depots.Count, _depotRegionSlots.Length * 2)];
        }

        for (var d = 0; d < Depots.Count; d++)
        {
            _depotRegionStart[DepotRegion(d, width) + 1]++;
        }

        for (var r = 0; r < regions; r++)
        {
            _depotRegionStart[r + 1] += _depotRegionStart[r];
        }

        Span<int> fill = regions <= 4096 ? stackalloc int[regions] : new int[regions];
        for (var r = 0; r < regions; r++)
        {
            fill[r] = _depotRegionStart[r];
        }

        for (var d = 0; d < Depots.Count; d++)
        {
            _depotRegionSlots[fill[DepotRegion(d, width)]++] = d;
        }

        _depotIndexVersion = Depots.Version;
    }

    private int DepotRegion(int depotSlot, int width)
    {
        var tile = Depots.TileAt(depotSlot);
        return ((tile % width) >> DepotRegionBits) + (((tile / width) >> DepotRegionBits) * _depotRegionsX);
    }

    /// <summary>Stations or depots changed: vehicles whose target disappeared choose again; lines drop removed stations.</summary>
    private void ValidateTargets()
    {
        _stationsVersion = _stations.Version;
        _depotsVersion = Depots.Version;
        for (var i = 0; i < Lines.Count; i++)
        {
            var line = Lines.IdAt(i);
            for (var s = Lines.StopCount(line) - 1; s >= 0; s--)
            {
                var station = Lines.StopAt(line, s).StationId;
                if (_stations.SlotOf(station) < 0)
                {
                    Lines.RemoveStation(station);
                    s = Lines.StopCount(line);
                }
            }
        }

        for (var slot = 0; slot < Count; slot++)
        {
            var valid = _targetKind[slot] switch
            {
                VehicleTargetKind.Stop => _stations.TryGetPieceAt(_hot[slot].TargetTile, out var id, out _, out _) && id == _targetId[slot],
                VehicleTargetKind.Depot => Depots.SlotOf(_targetId[slot]) >= 0,
                _ => true,
            };
            if (!valid && !_hot[slot].Has(RoadVehicleBits.AtStop))
            {
                var stopCount = _line[slot] >= 0 && Lines.Exists(_line[slot]) ? Lines.StopCount(_line[slot]) : 0;
                if (_stopIndex[slot] >= stopCount)
                {
                    _stopIndex[slot] = 0;
                }

                ChooseNextTarget(slot, advance: false);
            }
            else if (!valid)
            {
                _hot[slot].Flags &= ~RoadVehicleBits.AtStop;
                ChooseNextTarget(slot, advance: true);
            }
        }
    }

    // ----- Stops (research R5) -----

    private void Arrive(int slot)
    {
        PostDistance(slot); // The load may change at this stop.
        ref var h = ref _hot[slot];
        if (_targetKind[slot] == VehicleTargetKind.Depot)
        {
            EnterDepot(slot);
            return;
        }

        if (_targetKind[slot] != VehicleTargetKind.Stop || _line[slot] < 0 || !Lines.Exists(_line[slot]))
        {
            ClearTarget(slot);
            return;
        }

        h.Flags |= RoadVehicleBits.AtStop;
        h.StillTicks = 0;
        var model = Rules.Models[_model[slot]];
        var rule = CurrentRule(slot);
        var unloaded = 0;
        if (rule != StopRule.NoLoading && _hold[slot].Count > 0)
        {
            var before = _hold[slot].Total;
            var paidBefore = _finance?.TotalPaid ?? 0;
            if (_finance is not null)
            {
                _finance.CurrentVehicleId = _id[slot];
            }

            var result = _stations.Unload(_targetId[slot], _hold[slot], rule == StopRule.UnloadOnly ? UnloadMode.Transfer : UnloadMode.Normal, NextStopStation(slot));
            if (_finance is not null)
            {
                _finance.CurrentVehicleId = -1;

                // Feature 013 (research R12): transfer credits move profit between the legs of a trip, never money.
                var earned = _finance.TotalPaid - paidBefore - result.CreditDelivered + result.CreditEarned;
                _profitThisYear[slot] = Money.Add(_profitThisYear[slot], earned);
                Lines.RecordProfit(_line[slot], earned);
            }

            if (result.Delivered > 0)
            {
                _deliveries++;
            }

            unloaded = before - _hold[slot].Total;
        }

        var steps = (unloaded + model.LoadPerStep - 1) / model.LoadPerStep;
        _loadTimer[slot] = Math.Max(Rules.MinDwellTicks, steps * Rules.LoadIntervalTicks);
        _loadedThisStep[slot] = 0;
    }

    private StopRule CurrentRule(int slot)
    {
        var line = _line[slot];
        return line >= 0 && Lines.Exists(line) && _stopIndex[slot] < Lines.StopCount(line)
            ? Lines.StopAt(line, _stopIndex[slot]).Rule
            : StopRule.LoadIfAvailable;
    }

    private void LoadStep(int slot)
    {
        if (_loadTimer[slot] > 0)
        {
            _loadTimer[slot]--;
            return;
        }

        var rule = CurrentRule(slot);
        var model = Rules.Models[_model[slot]];
        var space = model.Capacity - _hold[slot].Total;
        if (rule is StopRule.UnloadOnly or StopRule.NoLoading || space <= 0)
        {
            Depart(slot);
            return;
        }

        var loaded = _stations.Load(_targetId[slot], _cargo[slot], Math.Min(model.LoadPerStep, space), _hold[slot], model.SpeedKmh, NextStopStation(slot));
        Lines.RecordBoarding(_line[slot], loaded, _content.Cargo(_cargo[slot]).Category == Content.CargoCategory.Passenger);
        if (loaded > 0 || rule == StopRule.FullLoad)
        {
            _loadTimer[slot] = Rules.LoadIntervalTicks;
            return;
        }

        Depart(slot);
    }

    private void Depart(int slot)
    {
        if (_stopIndex[slot] == 0 && !Lines.TryDispatch(_line[slot], _targetId[slot], _tick, rail: false))
        {
            return;
        }

        _hot[slot].Flags &= ~RoadVehicleBits.AtStop;
        if (_serviceInterval[slot] > 0 && _daysSinceService[slot] >= _serviceInterval[slot])
        {
            _orders[slot] |= VehicleOrders.ServiceDue;
        }

        _stopIndex[slot] = _line[slot] >= 0 && Lines.Exists(_line[slot]) && Lines.StopCount(_line[slot]) > 0
            ? (_stopIndex[slot] + 1) % Lines.StopCount(_line[slot])
            : 0;
        if ((_orders[slot] & (VehicleOrders.ServiceDue | VehicleOrders.SendToDepot | VehicleOrders.Repair)) != 0 && TargetNearestDepot(slot))
        {
            return;
        }

        _orders[slot] &= ~VehicleOrders.ServiceDue; // No depot reachable: try again after the next stop.
        ChooseNextTarget(slot, advance: false);
    }

    // ----- Depots -----

    private void EnterDepot(int slot)
    {
        ref var h = ref _hot[slot];
        Unlink(slot);
        h.Lane = -1;
        h.S = 0;
        h.Speed = 0;
        h.Flags = (h.Flags | RoadVehicleBits.InDepot) & ~(RoadVehicleBits.Stuck | RoadVehicleBits.UrgentRoute | RoadVehicleBits.NoRoute);
        h.StillTicks = 0;
        _depotInside[slot] = _targetId[slot];
        var orders = _orders[slot];
        if ((orders & (VehicleOrders.ServiceDue | VehicleOrders.Repair)) != 0 || h.Has(RoadVehicleBits.BrokenDown))
        {
            Service(slot);
        }

        if ((orders & VehicleOrders.SendToDepot) != 0)
        {
            h.Flags |= RoadVehicleBits.Stopped;
        }

        _orders[slot] = VehicleOrders.None;
        ChooseNextTarget(slot, advance: false);
    }

    private void Service(int slot)
    {
        ref var h = ref _hot[slot];
        h.Flags &= ~RoadVehicleBits.BrokenDown;
        h.BreakdownTicks = 0;
        _reliability[slot] = Rules.Models[_model[slot]].Reliability;
        _daysSinceService[slot] = 0;
        Notify(VehicleNoticeKind.Serviced, slot, -1);
    }

    private void Recover(int slot)
    {
        ref var h = ref _hot[slot];
        h.Flags &= ~RoadVehicleBits.BrokenDown;
        _reliability[slot] = Math.Min(Rules.Models[_model[slot]].Reliability, _reliability[slot] + Rules.RecoverPerMille);
        if ((_orders[slot] & VehicleOrders.Repair) != 0)
        {
            _orders[slot] &= ~VehicleOrders.Repair;
            if (_targetKind[slot] == VehicleTargetKind.Depot && (_orders[slot] & (VehicleOrders.SendToDepot | VehicleOrders.ServiceDue)) == 0 && !h.Has(RoadVehicleBits.InDepot))
            {
                ChooseNextTarget(slot, advance: false);
            }
        }
    }

    private void TryLeaveDepot(int slot)
    {
        ref var h = ref _hot[slot];
        var every = h.Has(RoadVehicleBits.NoRoute) ? Simulation.GameClock.TicksPerDay : 4;
        if (h.Has(RoadVehicleBits.Stopped) || (_tick + _id[slot]) % every != 0)
        {
            return; // Waiting vehicles try to leave every fourth tick (once a day without a route), spread by id.
        }

        if (_targetKind[slot] == VehicleTargetKind.None)
        {
            ChooseNextTarget(slot, advance: false);
            if (_targetKind[slot] == VehicleTargetKind.None)
            {
                return;
            }
        }

        var depot = Depots.SlotOf(_depotInside[slot]);
        if (depot < 0)
        {
            return;
        }

        var tile = Depots.TileAt(depot);
        var lane = (tile * 8) + Depots.DirectionAt(depot);
        var start = -Rules.DeadEndExtent + h.Length;
        if (!LaneStartFree(lane, start))
        {
            return;
        }

        // Plan from the exit lane before leaving; wait inside while the route budget is spent.
        var savedLane = h.Lane;
        h.Lane = lane;
        var result = PlanRoute(slot);
        if (result != RouteResult.Found)
        {
            h.Lane = savedLane;
            if (result == RouteResult.NoRoute && !h.Has(RoadVehicleBits.NoRoute))
            {
                h.Flags |= RoadVehicleBits.NoRoute;
                Notify(VehicleNoticeKind.NoRoute, slot, _targetId[slot]);
            }

            return;
        }

        h.S = start;
        h.Speed = 0;
        CacheLane(slot, h.Lane);
        h.Flags &= ~(RoadVehicleBits.InDepot | RoadVehicleBits.NoRoute | RoadVehicleBits.UrgentRoute);
        _depotInside[slot] = -1;
        Link(slot);
    }

    /// <summary>True when a vehicle placed with its front at <paramref name="start"/> would keep the gap to the lane's rear-most vehicle.</summary>
    private bool LaneStartFree(int laneKey, int start)
    {
        var tail = TailOf(laneKey);
        return tail < 0 || _hot[tail].S - _hot[tail].Length >= start + Rules.MinGap;
    }

    private void TurnAtDeadEnd(int slot)
    {
        ref var h = ref _hot[slot];
        var tile = _roads.Follow(h.Lane >> 3, h.Lane & 7);
        var links = _roads.LinksAt(tile);
        if (BitOperations.PopCount(links) != 1)
        {
            return;
        }

        var back = BitOperations.TrailingZeroCount(links);
        if ((_roads.ExitsAt(tile) & (1 << back)) == 0)
        {
            return; // A one-way dead end: nowhere to go (the stuck rule reports it).
        }

        var lane = (tile * 8) + back;
        var start = -Rules.DeadEndExtent + h.Length;
        if (!LaneStartFree(lane, start))
        {
            return;
        }

        Unlink(slot);
        h.Lane = lane;
        h.S = start;
        h.Speed = 0;
        CacheLane(slot, h.Lane);
        var route = _route[slot];
        if (h.RouteIndex < route.Length && route[h.RouteIndex].FromTile == tile)
        {
            h.RouteIndex++;
        }
        else
        {
            h.Flags |= RoadVehicleBits.UrgentRoute;
        }

        Link(slot);
    }

    // ----- Stuck vehicles (research R8) -----

    private bool TryTurnaround(int slot)
    {
        ref var h = ref _hot[slot];
        if (h.Lane < 0 || h.Has(RoadVehicleBits.HoldsJunction) || h.Has(RoadVehicleBits.AtStop))
        {
            return false;
        }

        var from = h.Lane >> 3;
        var dir = h.Lane & 7;
        var to = _roads.Follow(from, dir);
        var back = Dir8.Opposite(dir);
        if (to < 0 || (_roads.ExitsAt(to) & (1 << back)) == 0)
        {
            return false;
        }

        var len = _roads.LinkLength(from, dir);
        var radius = Rules.JunctionRadius;
        var newS = len - (h.S - h.Length);
        if (h.S - h.Length < radius || h.S > len - radius)
        {
            return false; // Too close to a junction box.
        }

        var lane = (to * 8) + back;
        var gap = Rules.MinGap;
        var index = FindLane(lane);
        var other = index < 0 ? -1 : _laneTail[index];
        for (; other >= 0; other = _ahead[other])
        {
            ref readonly var o = ref _hot[other];
            var oRear = o.S - o.Length;
            if (o.S >= newS - h.Length - gap && oRear <= newS + gap)
            {
                return false; // Overlaps the space it would take.
            }

            if (o.S < newS - h.Length && o.S + StopDistance(o.Speed, Rules.Braking) > newS - h.Length - gap)
            {
                return false; // The vehicle behind could not stop.
            }
        }

        Unlink(slot);
        h.Lane = lane;
        h.S = newS;
        h.Speed = 0;
        CacheLane(slot, h.Lane);
        h.WaitingSince = -1;
        h.Flags |= RoadVehicleBits.UrgentRoute;
        Link(slot);
        return true;
    }

    private void OnNoRoute(int slot)
    {
        ref var h = ref _hot[slot];
        h.Flags &= ~RoadVehicleBits.UrgentRoute;
        if (!h.Has(RoadVehicleBits.NoRoute))
        {
            h.Flags |= RoadVehicleBits.NoRoute;
            Notify(VehicleNoticeKind.NoRoute, slot, _targetId[slot]);
        }
    }

    // ----- Routes (research R6) -----

    /// <summary>The node ahead of the vehicle: where its next route starts (end of its lane's chain), or −1.</summary>
    private int RouteOrigin(int slot)
    {
        var lane = _hot[slot].Lane;
        if (lane < 0)
        {
            var depot = Depots.SlotOf(_depotInside[slot]);
            if (depot < 0)
            {
                return -1;
            }

            lane = (Depots.TileAt(depot) * 8) + Depots.DirectionAt(depot);
        }

        return NodeAhead(lane, -1, out _);
    }

    /// <summary>Follows the chain from a lane to the first node; reports whether <paramref name="watch"/> is passed on the way.</summary>
    private int NodeAhead(int lane, int watch, out bool passesWatch)
    {
        passesWatch = false;
        var tile = lane >> 3;
        var dir = lane & 7;
        for (var guard = 0; guard < 100_000; guard++)
        {
            var next = _roads.Follow(tile, dir);
            if (next < 0)
            {
                return -1;
            }

            if (next == watch)
            {
                passesWatch = true;
            }

            if (_network.TryGetNode(next, out _))
            {
                return next;
            }

            var other = _roads.LinksAt(next) & ~(1 << Dir8.Opposite(dir));
            if (other == 0)
            {
                return -1;
            }

            dir = BitOperations.TrailingZeroCount(other);
            tile = next;
        }

        return -1;
    }

    /// <summary>Plans the route to the current target from the node ahead (research R6).</summary>
    private RouteResult PlanRoute(int slot)
    {
        var target = _hot[slot].TargetTile;
        if (target < 0)
        {
            return RouteResult.NoRoute;
        }

        var lane = _hot[slot].Lane;
        if (lane < 0)
        {
            return RouteResult.NoRoute;
        }

        var from = NodeAhead(lane, target, out var passes);
        if (passes || from == target)
        {
            ApplyRoute(slot, [], -1, -1);
            return RouteResult.Found;
        }

        if (from < 0)
        {
            return RouteResult.NoRoute;
        }

        if (_targetForm[slot] != TargetForm.DriveThrough)
        {
            var route = Search(from, target, ignoreBudget: false);
            if (route is null)
            {
                return RouteResult.Deferred;
            }

            if (!route.Found)
            {
                return RouteResult.NoRoute;
            }

            ApplyRoute(slot, route.Steps, -1, -1);
            return RouteResult.Found;
        }

        // A drive-through stop lies inside a chain between two nodes: reach it through either end.
        var links = _roads.LinksAt(target);
        CachedRoute? best = null;
        var bestTailFrom = -1;
        var bestTailDir = -1;
        var bestLength = long.MaxValue;
        for (var d = 0; d < 8; d++)
        {
            if ((links & (1 << d)) == 0)
            {
                continue;
            }

            // Walk from the stop towards one end; the vehicle will come back along the chain in the other direction.
            var end = NodeAhead((target * 8) + d, -1, out _);
            if (end < 0)
            {
                continue;
            }

            var towards = DirectionFromNodeTowards(end, target, out var chainLength);
            if (towards < 0)
            {
                continue;
            }

            var route = from == end ? EmptyRoute : Search(from, end, ignoreBudget: false);
            if (route is null)
            {
                return RouteResult.Deferred;
            }

            if (route.Found && route.Length + chainLength < bestLength)
            {
                bestLength = route.Length + chainLength;
                best = route;
                bestTailFrom = end;
                bestTailDir = towards;
            }
        }

        if (best is null)
        {
            return RouteResult.NoRoute;
        }

        ApplyRoute(slot, best.Steps, bestTailFrom, bestTailDir);
        return RouteResult.Found;
    }

    private static readonly CachedRoute EmptyRoute = new([], 0, 0, true);

    /// <summary>The first direction from node <paramref name="node"/> along the chain that reaches <paramref name="tile"/> (travel allowed), or −1.</summary>
    private int DirectionFromNodeTowards(int node, int tile, out long length)
    {
        length = 0;
        var links = _roads.LinksAt(node);
        for (var d = 0; d < 8; d++)
        {
            if ((links & (1 << d)) == 0 || (_roads.ExitsAt(node) & (1 << d)) == 0)
            {
                continue;
            }

            var current = node;
            var dir = d;
            long run = 0;
            for (var guard = 0; guard < 100_000; guard++)
            {
                if ((_roads.ExitsAt(current) & (1 << dir)) == 0)
                {
                    break;
                }

                run += _roads.LinkLength(current, dir);
                var next = _roads.Follow(current, dir);
                if (next < 0)
                {
                    break;
                }

                if (next == tile)
                {
                    length = run;
                    return d;
                }

                if (_network.TryGetNode(next, out _))
                {
                    break;
                }

                var other = _roads.LinksAt(next) & ~(1 << Dir8.Opposite(dir));
                if (other == 0)
                {
                    break;
                }

                dir = BitOperations.TrailingZeroCount(other);
                current = next;
            }
        }

        return -1;
    }

    /// <summary>A cached route between two node tiles, or null when the search budget is spent (research R6).</summary>
    private CachedRoute? Search(int from, int to, bool ignoreBudget)
    {
        var key = ((long)from << 32) | (uint)to;
        var version = _network.Version;
        if (_routeCache.TryGetValue(key, out var cached) && cached.Version == version)
        {
            return cached;
        }

        if (!SameComponent(from, to))
        {
            // Not connected by any road: no search needed (research R6).
            var none = new CachedRoute([], 0, version, false);
            _routeCache[key] = none;
            return none;
        }

        if (!ignoreBudget && _searchBudget <= 0)
        {
            return cached; // A stale route is still followed (it is validated at junctions); null when none.
        }

        if (cached is { Found: true } && _staleBudget <= 0)
        {
            // The roads changed since this route was found. It is still safe (checked at every junction), so it is
            // refreshed only within a small share of the budget: a big network change never floods the tick.
            return cached;
        }

        RouteSearches++;
        var limit = ignoreBudget ? Rules.SearchNodesPerTick : _searchBudget;
        var found = Router.TryFindRoute(from, to, _routeScratch, out var length, limit);
        _searchBudget -= Router.LastExpandedNodes;
        if (cached is not null)
        {
            _staleBudget -= Router.LastExpandedNodes;
        }

        if (Router.LastHitLimit && limit < Rules.SearchNodesPerTick)
        {
            return cached; // Did not fit in what was left of this tick's budget: a later tick tries with a full one.
        }

        // A search that needs more than a whole tick's budget counts as "no route".
        var route = new CachedRoute(found ? [.. _routeScratch] : [], length, version, found);
        if (_routeCache.Count >= 65_536)
        {
            _routeCache.Clear();
        }

        _routeCache[key] = route;
        return route;
    }

    private int[] _component = [];
    private int _componentVersion = -1;

    /// <summary>
    /// True when two node tiles are in one weakly connected part of the road graph (derived labels, recomputed with a
    /// union-find over every edge when the network version changes).
    /// </summary>
    private bool SameComponent(int fromTile, int toTile)
    {
        if (!_network.TryGetNode(fromTile, out var a) || !_network.TryGetNode(toTile, out var b))
        {
            return false;
        }

        if (_componentVersion != _network.Version)
        {
            var capacity = _network.NodeCapacity;
            if (_component.Length < capacity)
            {
                _component = new int[Math.Max(capacity, _component.Length * 2)];
            }

            for (var n = 0; n < capacity; n++)
            {
                _component[n] = n;
            }

            for (var n = 0; n < capacity; n++)
            {
                if (_network.NodeTile(n) < 0)
                {
                    continue;
                }

                foreach (var target in _network.OutTargets(n))
                {
                    var ra = Root(n);
                    var rb = Root(target);
                    if (ra != rb)
                    {
                        _component[Math.Max(ra, rb)] = Math.Min(ra, rb);
                    }
                }
            }

            _componentVersion = _network.Version;
        }

        return Root(a) == Root(b);
    }

    private int Root(int node)
    {
        while (_component[node] != node)
        {
            _component[node] = _component[_component[node]]; // Path halving.
            node = _component[node];
        }

        return node;
    }

    private void ApplyRoute(int slot, RouteStep[] steps, int tailFrom, int tailDir)
    {
        _route[slot] = steps;
        _tailFrom[slot] = tailFrom;
        _tailDir[slot] = tailDir;
        _hot[slot].RouteIndex = 0;
        _hot[slot].Flags &= ~RoadVehicleBits.UrgentRoute;
    }

    private void Notify(VehicleNoticeKind kind, int slot, int stationId)
    {
        var (x, z) = PositionTile(slot);
        Notices.Add(new VehicleNotice(kind, _id[slot], _owner[slot], x, z, stationId));
    }
}
