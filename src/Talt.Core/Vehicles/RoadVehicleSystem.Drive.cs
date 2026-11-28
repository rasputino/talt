using System.Numerics;
using Talt.Core.Network;
using Talt.Core.Roads;

namespace Talt.Core.Vehicles;

/// <summary>
/// Phase 1 of the tick, the drive step (research R3–R7): every vehicle reads the previous tick of the others and
/// writes only its own slot of <c>_next</c>, so batches run in parallel with results identical to a sequential run.
/// </summary>
public sealed partial class RoadVehicleSystem
{
    /// <summary>Most lanes a vehicle looks through ahead of it (look-ahead is also bounded by distance).</summary>
    private const int MaxScanLanes = 6;
    internal Simulation.TrafficHistory? Traffic { get; set; }
    private int[][] _crossedRoadLinks = [];
    private int[] _crossedCount = [];
    private bool[] _leftAcrossLaneEnd = [];

    /// <summary>Advances every vehicle by one tick (contracts/vehicles-api.md).</summary>
    /// <param name="tick">The simulation's tick number before this tick (for waiting keys and breakdown pulses).</param>
    /// <param name="parallel">Run the drive step on all cores; results are identical either way.</param>
    public void Tick(long tick, bool parallel)
    {
        _tick = tick;
        if (Count == 0)
        {
            return;
        }

        // Phase 1: read the previous tick, decide, write _next.
        var batches = (Count + BatchSize - 1) / BatchSize;
        EnsureBatchLists(batches);
        _noticeTicks = Rules.StuckNoticeDays * Simulation.GameClock.TicksPerDay;
        if (parallel && batches > 1)
        {
            Parallel.For(0, batches, _driveBatch);
        }
        else
        {
            for (var b = 0; b < batches; b++)
            {
                DriveBatch(b);
            }
        }

        if (Traffic is { } traffic)
        {
            for (var b = 0; b < batches; b++)
            {
                for (var i = 0; i < _crossedCount[b]; i++)
                {
                    var lane = _crossedRoadLinks[b][i];
                    if (lane >= 0)
                    {
                        traffic.RecordRoadLane(lane);
                    }
                    else
                    {
                        // A fast vehicle may cross an intermediate lane before it joins that lane's list.
                        var key = ~lane;
                        traffic.Record(key >> 3, _roads.FollowLink(key >> 3, key & 7), rail: false);
                    }
                }
            }
        }

        (_hot, _next) = (_next, _hot);

        Lines.InvalidateOpenDistances();

        // Phase 2: lane lists. All leavers first, then all arrivals, so a list never holds a stale member while
        // another is inserted (research R7). Batches hold their slots in order, so this is slot order.
        for (var b = 0; b < batches; b++)
        {
            var list = _batchLaneChanges[b];
            for (var i = 0; i < _batchLaneChangeCount[b]; i++)
            {
                var slot = list[i];
                if (_leftAcrossLaneEnd[slot])
                {
                    Traffic?.RecordRoadLane(_laneOf[slot]);
                }

                Unlink(slot);
            }
        }

        for (var b = 0; b < batches; b++)
        {
            var list = _batchLaneChanges[b];
            for (var i = 0; i < _batchLaneChangeCount[b]; i++)
            {
                Link(list[i]);
            }
        }

        // Phase 3: events, sequential in slot order, for the vehicles phase 1 marked as having something to do.
        _activeCount = 0;
        for (var b = 0; b < batches; b++)
        {
            Array.Copy(_batchActive[b], 0, _active, _activeCount, _batchActiveCount[b]);
            _activeCount += _batchActiveCount[b];
        }

        RunEvents();
        _occupiedTick = -1;
    }

    private void DriveBatch(int batch)
    {
        var end = Math.Min((batch + 1) * BatchSize, Count);
        _crossedCount[batch] = 0;
        long travelled = 0;
        var laneChanges = _batchLaneChanges[batch];
        var active = _batchActive[batch];
        var changed = 0;
        var busy = 0;
        for (var v = batch * BatchSize; v < end; v++)
        {
            var distance = Drive(v);
            travelled += distance;
            if (distance != 0 && _targetKind[v] == VehicleTargetKind.Stop)
            {
                _unpostedDistance[v] += distance;
            }

            ref readonly var n = ref _next[v];
            if ((n.Flags & RoadVehicleBits.LaneChanged) != 0)
            {
                laneChanges[changed++] = v;
            }

            if ((n.Flags & ActiveBits) != 0 || n.StillTicks >= _noticeTicks
                || ((_orders[v] & (VehicleOrders.Repair | VehicleOrders.SendToDepot)) != 0 && _targetKind[v] != VehicleTargetKind.Depot))
            {
                active[busy++] = v;
            }
        }

        _batchLaneChangeCount[batch] = changed;
        _batchActiveCount[batch] = busy;
        Interlocked.Add(ref _distance, travelled);
    }

    /// <summary>Vehicles with one of these bits have work in phase 3.</summary>
    private const RoadVehicleBits ActiveBits = RoadVehicleBits.Events | RoadVehicleBits.AtStop | RoadVehicleBits.InDepot
        | RoadVehicleBits.UrgentRoute | RoadVehicleBits.NoRoute;

    // Per drive batch: the slots that changed lane and the slots with phase-3 work, in slot order (no locks, no
    // allocation per tick); merged in batch order, which is slot order.
    private int[][] _batchLaneChanges = [];
    private int[][] _batchActive = [];
    private int[] _batchLaneChangeCount = [];
    private int[] _batchActiveCount = [];
    private int[] _active = [];
    private int _activeCount;
    private int _noticeTicks;

    private void EnsureBatchLists(int batches)
    {
        if (_batchActive.Length < batches)
        {
            var old = _batchActive.Length;
            Array.Resize(ref _crossedRoadLinks, batches);
            Array.Resize(ref _crossedCount, batches);
            Array.Resize(ref _batchLaneChanges, batches);
            Array.Resize(ref _batchActive, batches);
            Array.Resize(ref _batchLaneChangeCount, batches);
            Array.Resize(ref _batchActiveCount, batches);
            for (var b = old; b < batches; b++)
            {
                _batchLaneChanges[b] = new int[BatchSize];
                _batchActive[b] = new int[BatchSize];
                _crossedRoadLinks[b] = new int[BatchSize * (MaxScanLanes + 1)];
            }
        }

        if (_active.Length < batches * BatchSize)
        {
            _active = new int[batches * BatchSize];
            _leftAcrossLaneEnd = new bool[batches * BatchSize];
        }
    }

    private long[] _stopTable = [];

    /// <summary>Exact stop distance with the common braking, from a table for every speed a vehicle can have.</summary>
    private long Stop(int v) => (uint)v < (uint)_stopTable.Length ? _stopTable[v] : StopDistance(v, Rules.Braking);

    private void BuildStopTable()
    {
        var max = 0;
        foreach (var model in Rules.Models)
        {
            max = Math.Max(max, model.Speed + model.Acceleration);
        }

        _stopTable = new long[max + 1];
        for (var v = 0; v <= max; v++)
        {
            _stopTable[v] = StopDistance(v, Rules.Braking);
        }
    }

    /// <summary>Exact distance needed to stop from <paramref name="v"/> braking by <paramref name="d"/> every tick.</summary>
    internal static long StopDistance(long v, long d)
    {
        if (v <= 0)
        {
            return 0;
        }

        var k = v / d;
        return (k * v) - (d * k * (k + 1) / 2);
    }

    private int RoadSpeed(int tile)
    {
        var type = _roads.TypeAt(tile);
        return (uint)type < (uint)_typeSpeed.Length ? _typeSpeed[type] : int.MaxValue;
    }

    // Low bytes hold links and permitted exits. The third byte stores the plain continuation plus one.
    private int LaneMasks(int tile, int arrival)
    {
        if (tile < 0)
        {
            return 0;
        }

        var links = _roads.LinksAt(tile);
        var exits = _roads.ExitsAt(tile);
        var other = links & ~(1 << Dir8.Opposite(arrival));
        var plain = BitOperations.PopCount(links) == 2 && other != 0
            ? BitOperations.TrailingZeroCount(other) : -1;
        if (plain >= 0 && (exits & (1 << plain)) == 0)
        {
            plain = -1;
        }

        return links | (exits << 8) | ((plain + 1) << 16);
    }

    private static int PlainDirection(int masks) => (masks >> 16) - 1;

    /// <summary>
    /// The direction a vehicle takes when its front reaches the centre of <paramref name="tile"/> arriving along
    /// the incoming link, or −1 when its route no longer matches the road (research R6). Dead ends are handled
    /// by the caller. Read-only.
    /// </summary>
    private int NextDirection(int slot, int tile, int masks, ref int routeIndex)
    {
        var route = _route[slot];
        var dir = -1;
        if (routeIndex < route.Length && route[routeIndex].FromTile == tile)
        {
            dir = route[routeIndex].FirstDirection;
            routeIndex++;
        }
        else if (routeIndex >= route.Length && _tailFrom[slot] == tile && routeIndex == route.Length)
        {
            dir = _tailDir[slot];
            routeIndex++;
        }
        else
        {
            return PlainDirection(masks);
        }

        var allowedLinks = (byte)masks & (byte)(masks >> 8);
        if (dir < 0 || (allowedLinks & (1 << dir)) == 0)
        {
            return -1;
        }

        return dir;
    }

    /// <returns>Distance travelled, units.</returns>
    private int Drive(int v)
    {
        ref readonly var cur = ref _hot[v];
        ref var nx = ref _next[v];
        nx = cur;
        nx.Flags &= ~RoadVehicleBits.Events;
        nx.Requested = -1;
        if (cur.Lane < 0 || (cur.Flags & RoadVehicleBits.AtStop) != 0)
        {
            nx.Speed = 0;
            return 0;
        }

        var rules = Rules;
        var braking = rules.Braking;
        var gap = rules.MinGap;
        var radius = rules.JunctionRadius;
        var extent = rules.DeadEndExtent;

        // Speed cap: model, breakdown pulses (research R9), player stop, road type, ramps.
        var cap = cur.ModelSpeed;
        if ((cur.Flags & RoadVehicleBits.BrokenDown) != 0)
        {
            cap = ((_tick / rules.BreakdownPulseTicks) & 1) == 0 ? cap / 2 : cap / 4;
            if (cur.BreakdownTicks > 0 && --nx.BreakdownTicks == 0)
            {
                nx.Flags |= RoadVehicleBits.RecoveredEvent;
            }
        }

        ref var lc = ref _cache[v]; // Own slot only (read and written by this vehicle alone).
        if (lc.CacheVersion != _network.Version)
        {
            CacheLane(v, nx.Lane, _laneOf[v]); // Roads changed somewhere: the lane caches are refilled (a junction may have appeared ahead).
        }

        var lane = cur.Lane;
        var tile = lane >> 3;
        var dir = lane & 7;
        var to = lc.To; // Cached when the vehicle entered the lane (the link is protected while it stands on it).
        cap = Math.Min(cap, lc.RoadCap);

        if ((cur.Flags & RoadVehicleBits.Stopped) != 0)
        {
            cap = 0;
        }

        // ----- Obstacle search (research R3, R4, R5) -----
        long free = long.MaxValue;   // Hard limit: the front may advance at most this far.
        long budget = long.MaxValue; // Soft limit: v + B(v) must fit.
        Span<int> passLane = stackalloc int[MaxScanLanes];
        Span<int> passStart = stackalloc int[MaxScanLanes];
        Span<int> passEnd = stackalloc int[MaxScanLanes];
        Span<int> passRoute = stackalloc int[MaxScanLanes];
        var passes = 0;
        var routeIndex = cur.RouteIndex;
        long offset = -cur.S; // Distance from the front to the current lane's origin.
        var scanLane = lane;
        var arrivalAt = -1;    // Stop place on the lane this vehicle will stand on (arrival detection).
        var deadEndAt = -1;
        var mismatchAt = -1;
        var requestTile = -1;
        var leader = _ahead[v];
        var target = cur.TargetTile;
        var form = _targetForm[v];

        if (leader >= 0)
        {
            ref readonly var l = ref _hot[leader];
            free = offset + l.S - l.Length - gap;
            budget = free + Stop(l.Speed);
            if (to == target && form != TargetForm.None)
            {
                // Queueing for the stop on this lane: the stop place is known for the second bay place.
                var len = _roads.LinkLength(tile, dir);
                arrivalAt = form == TargetForm.DriveThrough ? len : len + extent;
            }
        }

        var lookAhead = rules.LookAhead + (2L * cur.Speed) + Stop(cur.Speed);
        for (var scanned = 0; leader < 0 && scanned < MaxScanLanes && offset < lookAhead; scanned++)
        {
            var sTile = scanLane >> 3;
            var sDir = scanLane & 7;
            // Lane 0 is the vehicle's own lane and lane 1 the one after it: both come from the state's caches.
            int sTo, len, masks;
            if (scanned == 0)
            {
                sTo = to;
                len = lc.LaneLength;
                masks = lc.ToMasks;
            }
            else if (scanned == 1 && lc.NextKey == scanLane)
            {
                sTo = lc.NextTo;
                len = lc.NextLength;
                masks = lc.NextMasks;
            }
            else
            {
                sTo = _roads.FollowLink(sTile, sDir);
                len = _roads.LinkLength(sTile, sDir);
                masks = LaneMasks(sTo, sDir);
            }

            var links = (byte)masks;
            var count = BitOperations.PopCount(links);

            // The target stop or depot (research R5).
            if (sTo == target && form != TargetForm.None)
            {
                var stop = form == TargetForm.DriveThrough ? len : len + extent;
                Limit(ref free, ref budget, offset + stop, 0, braking);
                if (scanned == 0 || passes == scanned)
                {
                    arrivalAt = passes == 0 ? stop : -2; // −2: on a later lane (resolved after moving).
                }

                break;
            }

            // A dead end that is not the target: drive to the turning point, then turn round (phase 3).
            if (count == 1)
            {
                Limit(ref free, ref budget, offset + len + extent, 0, braking);
                deadEndAt = passes == 0 ? len + extent : -2;
                break;
            }

            if (Crossings is { } crossings && crossings.IsClosed(sTo))
            {
                // Feature 015 (research R12): a level crossing a train owns; stop before it unless already committed.
                var stopLine = len - (Dir8.StraightLength / 2) - gap;
                if (offset + stopLine >= 0)
                {
                    Limit(ref free, ref budget, offset + stopLine, 0, braking);
                    break;
                }
            }

            var ri = routeIndex;
            var nextDir = NextDirection(v, sTo, masks, ref ri);
            if (nextDir < 0)
            {
                var stopAt = count >= 3 ? len - radius - gap : len;
                Limit(ref free, ref budget, offset + stopAt, 0, braking);
                mismatchAt = passes == 0 ? stopAt : -2;
                break;
            }

            if (count >= 3 && cur.HeldJunction != sTo)
            {
                // A junction this vehicle does not hold: stop at the stop line and ask for it (research R4).
                var stopLine = len - radius - gap;
                Limit(ref free, ref budget, offset + stopLine, 0, braking);
                if (cur.HeldJunction < 0 && requestTile < 0 && offset + stopLine <= (2L * cur.Speed) + Stop(cur.Speed) + (Dir8.StraightLength / 4))
                {
                    var exitTail = TailOf((sTo * 8) + nextDir);
                    if (exitTail < 0 || _hot[exitTail].S - _hot[exitTail].Length >= radius + cur.Length + gap)
                    {
                        requestTile = sTo;
                    }
                }

                break;
            }

            // Passable: continue on the next lane.
            if (Dir8.Rotate(sDir, 2) == nextDir || Dir8.Rotate(sDir, 6) == nextDir || Dir8.Opposite(sDir) == nextDir
                || Dir8.Rotate(sDir, 3) == nextDir || Dir8.Rotate(sDir, 5) == nextDir)
            {
                // A turn of 90° or more: arrive at the centre no faster than the curve speed (soft only).
                var curve = cur.ModelSpeed * rules.CurveSpeedPercent / 100;
                var c = offset + len + Stop(curve);
                if (c < budget)
                {
                    budget = c;
                }
            }

            var nextLane = (sTo * 8) + nextDir;
            passLane[passes] = nextLane;
            passEnd[passes] = len;
            passStart[passes] = 0;
            passRoute[passes] = ri;
            passes++;
            offset += len;
            routeIndex = ri;
            scanLane = nextLane;
            int tail;
            if (scanned == 0)
            {
                if (lc.NextKey != nextLane)
                {
                    // Own slot only: a cache of the next lane for the following ticks on this lane.
                    lc.NextPlainIndex = -1;
                    lc.NextKey = nextLane;
                    lc.NextIndex = FindLane(nextLane);
                    if (lc.NextIndex >= 0 && _geometry[lc.NextIndex].Version == lc.CacheVersion)
                    {
                        ref readonly var next = ref _geometry[lc.NextIndex];
                        lc.NextPlainIndex = next.SuccessorIndex;
                        lc.NextTo = next.To;
                        lc.NextLength = next.Length;
                        lc.NextMasks = next.Masks;
                    }
                    else
                    {
                        lc.NextTo = _roads.FollowLink(sTo, nextDir);
                        lc.NextLength = _roads.LinkLength(sTo, nextDir);
                        lc.NextMasks = LaneMasks(lc.NextTo, nextDir);
                    }
                }
                else if (lc.NextIndex < 0)
                {
                    lc.NextIndex = FindLane(nextLane); // Lanes keep their index once created, so a found index stays valid.
                }

                tail = lc.NextIndex < 0 ? -1 : _laneTail[lc.NextIndex];
            }
            else
            {
                var index = scanned == 1 && nextDir == PlainDirection(masks) ? lc.NextPlainIndex : -1;
                if (index < 0)
                {
                    index = FindLane(nextLane);
                }

                tail = index < 0 ? -1 : _laneTail[index];
            }

            if (tail >= 0 && tail != v)
            {
                ref readonly var t = ref _hot[tail];
                var rear = offset + t.S - t.Length - gap;
                Limit(ref free, ref budget, rear, t.Speed, braking);
                break;
            }
        }

        // ----- Speed (research R3) -----
        var speed = cur.Speed;
        var accel = cur.Accel;
        var targetSpeed = speed > cap ? Math.Max(speed - braking, cap) : Math.Min(speed + accel, cap);
        var chosen = -1;
        if (Fits(targetSpeed, free, budget, braking))
        {
            chosen = targetSpeed;
        }
        else if (Fits(Math.Min(speed, targetSpeed), free, budget, braking))
        {
            chosen = Math.Min(speed, targetSpeed);
        }
        else if (Fits(speed - (braking / 2), free, budget, braking))
        {
            chosen = speed - (braking / 2);
        }
        else if (Fits(speed - braking, free, budget, braking))
        {
            chosen = speed - braking;
        }
        else
        {
            var creep = (int)Math.Min(targetSpeed, Math.Max(0, free));
            chosen = Fits(creep, free, budget, braking) ? creep : (int)Math.Clamp(Math.Min(speed - braking, free), 0, int.MaxValue);
        }

        chosen = Math.Max(0, chosen);

        // ----- Move along the lanes passed in the scan -----
        long s = cur.S + chosen;
        var pass = 0;
        while (pass < passes && s >= passEnd[pass])
        {
            if (pass > 0 && Traffic is not null)
            {
                RecordRoadCrossing(v, ~lane);
            }

            s = s - passEnd[pass] + passStart[pass];
            lane = passLane[pass];
            nx.RouteIndex = passRoute[pass];
            pass++;
        }

        if (pass > 0)
        {
            // Phase 2 already visits every vehicle that left its lane. Count its first physical
            // crossing there, unless it had reached the endpoint earlier (for example at a bay).
            _leftAcrossLaneEnd[v] = cur.S < passEnd[0];
            nx.Lane = lane;
            nx.Flags |= RoadVehicleBits.LaneChanged;
            CacheLane(v, lane, lc.NextKey == lane ? lc.NextIndex : -1);
        }

        // At a terminus a vehicle crosses the centre while staying on its incoming lane.
        if (s >= lc.LaneLength && chosen > 0 && (pass > 0 || cur.S < lc.LaneLength) && Traffic is not null)
        {
            RecordRoadCrossing(v, pass == 0 ? _laneOf[v] : ~lane);
        }

        nx.S = (int)s;
        nx.Speed = chosen;

        // Events detected where the vehicle now stands.
        if (pass == passes)
        {
            if (arrivalAt == -2 || deadEndAt == -2 || mismatchAt == -2)
            {
                // The stop place was on the lane just entered: recompute it there.
                var len = _roads.LinkLength(lane >> 3, lane & 7);
                if (arrivalAt == -2)
                {
                    arrivalAt = form == TargetForm.DriveThrough ? len : len + extent;
                }

                if (deadEndAt == -2)
                {
                    deadEndAt = len + extent;
                }

                if (mismatchAt == -2)
                {
                    var count = BitOperations.PopCount(_roads.LinksAt(_roads.Follow(lane >> 3, lane & 7)));
                    mismatchAt = count >= 3 ? len - radius - gap : len;
                }
            }

            if (chosen == 0)
            {
                if (arrivalAt >= 0 && (s == arrivalAt || SecondBayPlace(v, lane, s, cur.Length, arrivalAt, form)))
                {
                    nx.Flags |= RoadVehicleBits.ArrivedEvent;
                }
                else if (deadEndAt >= 0 && s == deadEndAt)
                {
                    nx.Flags |= RoadVehicleBits.DeadEndEvent;
                }
                else if (mismatchAt >= 0 && s == mismatchAt)
                {
                    nx.Flags |= RoadVehicleBits.RouteMismatchEvent;
                }
            }
        }

        if (requestTile >= 0)
        {
            nx.Requested = requestTile;
            nx.Flags |= RoadVehicleBits.RequestEvent;
            if (nx.WaitingSince < 0)
            {
                nx.WaitingSince = (int)Math.Min(_tick, int.MaxValue);
            }
        }

        // Junction release: the rear has left the box on the way out (research R4).
        if (cur.HeldJunction >= 0)
        {
            var from = lane >> 3;
            var toTile = _roads.Follow(from, lane & 7);
            var leaving = from == cur.HeldJunction;
            if ((leaving && s - cur.Length >= radius) || (!leaving && toTile != cur.HeldJunction))
            {
                nx.HeldJunction = -1;
                nx.Requested = cur.HeldJunction; // The released tile, for phase 3 (a holder never requests).
                nx.Flags = (nx.Flags & ~RoadVehicleBits.HoldsJunction) | RoadVehicleBits.ReleasedEvent;
            }
        }

        // Stuck counting (research R8): standing still while wanting to move, not queueing for its own stop.
        var wantsToMove = (cur.Flags & RoadVehicleBits.Stopped) == 0;
        if (chosen == 0 && wantsToMove && !QueueingForStop(v, in cur))
        {
            nx.StillTicks = cur.StillTicks + 1;
        }
        else if (chosen > 0)
        {
            nx.StillTicks = 0;
            nx.Flags &= ~RoadVehicleBits.Stuck;
        }

        return chosen;
    }

    /// <summary>Fills the lane caches of a state after <see cref="RoadVehicleState.Lane"/> changed (reads only the road layer).</summary>
    private void CacheLane(int slot, int laneKey, int knownIndex = -1)
    {
        ref var h = ref _cache[slot];
        h.Index = knownIndex;
        h.NextKey = -1;
        h.NextIndex = -1;
        h.CacheVersion = _network.Version;
        if (knownIndex >= 0 && _geometry[knownIndex].Version == h.CacheVersion)
        {
            ref readonly var geometry = ref _geometry[knownIndex];
            h.To = geometry.To;
            h.LaneLength = geometry.Length;
            h.RoadCap = geometry.RoadCap;
            h.ToMasks = geometry.Masks;
            var successor = geometry.SuccessorIndex;
            if (successor >= 0 && _geometry[successor].Version == h.CacheVersion)
            {
                ref readonly var next = ref _geometry[successor];
                h.NextKey = _laneKey[successor];
                h.NextIndex = successor;
                h.NextPlainIndex = next.SuccessorIndex;
                h.NextTo = next.To;
                h.NextLength = next.Length;
                h.NextMasks = next.Masks;
            }

            return;
        }

        var tile = laneKey >> 3;
        var dir = laneKey & 7;
        var to = _roads.Follow(tile, dir);
        h.To = to;
        h.LaneLength = _roads.LinkLength(tile, dir);
        var cap = to < 0 ? 0 : Math.Min(RoadSpeed(tile), RoadSpeed(to));
        var heights = _state.Map.Heights;
        if (to >= 0 && heights[tile] != heights[to])
        {
            cap = (int)((long)cap * Rules.RampSpeedPercent / 100);
        }

        h.RoadCap = cap;
        h.ToMasks = LaneMasks(to, dir);
    }

    private void RecordRoadCrossing(int slot, int lane)
    {
        var batch = slot / BatchSize;
        _crossedRoadLinks[batch][_crossedCount[batch]++] = lane;
    }

    private bool Fits(int v, long free, long budget, int braking) =>
        v >= 0 && v <= free && v + Stop(v) <= budget;

    private void Limit(ref long free, ref long budget, long distance, int obstacleSpeed, int braking)
    {
        if (distance < free)
        {
            free = distance;
        }

        var soft = distance + Stop(obstacleSpeed);
        if (soft < budget)
        {
            budget = soft;
        }
    }

    /// <summary>The second place of a bay (research R5): stopped behind a vehicle at the front place, body inside the bay tile.</summary>
    private bool SecondBayPlace(int v, int lane, long s, int length, int frontPlace, TargetForm form)
    {
        if (form != TargetForm.Bay || Rules.BayPlaces < 2)
        {
            return false;
        }

        var leader = _ahead[v];
        if (leader < 0 || _hot[leader].Lane != lane || _hot[leader].S != frontPlace)
        {
            return false;
        }

        var len = _roads.LinkLength(lane >> 3, lane & 7);
        return s - length >= len - (Dir8.StraightLength / 2);
    }

    /// <summary>True when the vehicle waits behind a vehicle that is at, or queueing for, the same stop.</summary>
    private bool QueueingForStop(int v, in RoadVehicleState cur)
    {
        var leader = _ahead[v];
        if (cur.TargetTile < 0 || leader < 0)
        {
            return false;
        }

        ref readonly var l = ref _hot[leader];
        return l.TargetTile == cur.TargetTile || (l.Flags & RoadVehicleBits.AtStop) != 0;
    }
}
