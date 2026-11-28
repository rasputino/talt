namespace Talt.Core.Vehicles;

/// <summary>
/// Lane lists (research R2): per lane a doubly linked list of vehicle slots ordered by front position, front first.
/// Derived from the vehicle states (never hashed); with no overtaking the order inside a lane never changes, so lists
/// change only when a vehicle enters or leaves a lane. Lanes get a dense index on first use and keep it.
/// </summary>
public sealed partial class RoadVehicleSystem
{
    private readonly LaneIndexMap _laneIndex = new();
    private int[] _laneKey = new int[256];
    private int[] _laneHead = new int[256]; // Front-most vehicle slot, or −1.
    private int[] _laneTail = new int[256]; // Rear-most vehicle slot, or −1.
    // Published only by the sequential lane phase. Workers can read these records without locks.
    internal struct LaneGeometry
    {
        internal int Version, To, Length, RoadCap, Masks, SuccessorIndex;
    }

    private LaneGeometry[] _geometry = new LaneGeometry[256];
    private int _laneCount;

    // Per vehicle slot: the lane index it is listed in (−1 none) and its neighbours in the list.
    private int[] _laneOf = new int[64];
    private int[] _ahead = new int[64];
    private int[] _behind = new int[64];

    private void InitLanes()
    {
        Array.Fill(_laneOf, -1);
        Array.Fill(_ahead, -1);
        Array.Fill(_behind, -1);
    }

    private void ResizeLaneSlots(int size)
    {
        var old = _laneOf.Length;
        Array.Resize(ref _laneOf, size);
        Array.Resize(ref _ahead, size);
        Array.Resize(ref _behind, size);
        Array.Fill(_laneOf, -1, old, size - old);
        Array.Fill(_ahead, -1, old, size - old);
        Array.Fill(_behind, -1, old, size - old);
    }

    /// <summary>The lane index of a lane key, or −1 when no vehicle ever used it. Read-only: safe in the parallel step.</summary>
    private int FindLane(int laneKey) => _laneIndex.Find(laneKey);

    /// <summary>The rear-most vehicle of a lane key, or −1.</summary>
    private int TailOf(int laneKey)
    {
        var index = FindLane(laneKey);
        return index < 0 ? -1 : _laneTail[index];
    }

    private int LaneFor(int laneKey)
    {
        var index = _laneIndex.Find(laneKey);
        if (index >= 0)
        {
            return index;
        }

        if (_laneCount == _laneKey.Length)
        {
            var size = _laneCount * 2;
            Array.Resize(ref _laneKey, size);
            Array.Resize(ref _geometry, size);
            Array.Resize(ref _laneHead, size);
            Array.Resize(ref _laneTail, size);
        }

        index = _laneCount++;
        _laneKey[index] = laneKey;
        _laneHead[index] = -1;
        _laneTail[index] = -1;
        _laneIndex.Add(laneKey, index);
        return index;
    }

    /// <summary>Takes a vehicle out of its lane list.</summary>
    private void Unlink(int slot)
    {
        var lane = _laneOf[slot];
        if (lane < 0)
        {
            return;
        }

        var a = _ahead[slot];
        var b = _behind[slot];
        if (a >= 0)
        {
            _behind[a] = b;
        }
        else
        {
            _laneHead[lane] = b;
        }

        if (b >= 0)
        {
            _ahead[b] = a;
        }
        else
        {
            _laneTail[lane] = a;
        }

        _laneOf[slot] = -1;
        _ahead[slot] = -1;
        _behind[slot] = -1;
    }

    /// <summary>
    /// Puts a vehicle into the list of its current lane (<see cref="RoadVehicleState.Lane"/>), keeping the list sorted by
    /// front position and then by vehicle id, so the result does not depend on the order of insertions.
    /// </summary>
    private void Link(int slot)
    {
        var key = _hot[slot].Lane;
        if (key < 0)
        {
            return;
        }

        var lane = _cache[slot].Index;
        if (lane < 0)
        {
            lane = LaneFor(key);
            _cache[slot].Index = lane;
        }

        ref readonly var cache = ref _cache[slot];
        ref var geometry = ref _geometry[lane];
        if (geometry.Length == 0 || geometry.Version != cache.CacheVersion)
        {
            geometry = new LaneGeometry
            {
                Version = cache.CacheVersion,
                To = cache.To,
                Length = cache.LaneLength,
                RoadCap = cache.RoadCap,
                Masks = cache.ToMasks,
                SuccessorIndex = -1,
            };
        }

        var direction = PlainDirection(geometry.Masks);
        if (direction >= 0 && geometry.SuccessorIndex < 0)
        {
            geometry.SuccessorIndex = FindLane((geometry.To * 8) + direction);
        }

        Traffic?.RegisterRoadLane(lane, key >> 3, _cache[slot].To);
        var s = _hot[slot].S;
        var id = _id[slot];

        // Walk from the rear: vehicles mostly enter at the start of a lane.
        var after = _laneTail[lane]; // The vehicle that will be ahead of this one.
        while (after >= 0 && (_hot[after].S < s || (_hot[after].S == s && _id[after] > id)))
        {
            after = _ahead[after];
        }

        var before = after >= 0 ? _behind[after] : _laneHead[lane];
        _ahead[slot] = after;
        _behind[slot] = before;
        if (after >= 0)
        {
            _behind[after] = slot;
        }
        else
        {
            _laneHead[lane] = slot;
        }

        if (before >= 0)
        {
            _ahead[before] = slot;
        }
        else
        {
            _laneTail[lane] = slot;
        }

        _laneOf[slot] = lane;
    }

    /// <summary>Re-points the lane list from slot <paramref name="from"/> to <paramref name="to"/> (swap-with-last removal).</summary>
    private void MoveLaneSlot(int from, int to)
    {
        // The slot "to" was unlinked by the caller (a sold vehicle is in a depot, so it is in no lane).
        var lane = _laneOf[from];
        _laneOf[to] = lane;
        _ahead[to] = _ahead[from];
        _behind[to] = _behind[from];
        if (lane >= 0)
        {
            if (_ahead[to] >= 0)
            {
                _behind[_ahead[to]] = to;
            }
            else
            {
                _laneHead[lane] = to;
            }

            if (_behind[to] >= 0)
            {
                _ahead[_behind[to]] = to;
            }
            else
            {
                _laneTail[lane] = to;
            }
        }

        _laneOf[from] = -1;
        _ahead[from] = -1;
        _behind[from] = -1;
    }
}
