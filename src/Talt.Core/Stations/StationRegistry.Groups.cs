using Talt.Core.Determinism;

namespace Talt.Core.Stations;

/// <summary>
/// Station groups (research R4, data-model.md Groups): connected components of stations and warehouses,
/// each with one served flag, rating, waiting stock and statistics per cargo; and the waiting cargo packet
/// pool (data-model.md CargoPacket).
/// </summary>
public sealed partial class StationRegistry
{
    private int _groupCount;
    private int _groupCapacity;
    private int _memberCount;
    private int[] _groupKey = [];
    private int[] _groupStationCount = [];
    private int[] _groupMemberStart = [];
    private int[] _groupMembers = [];

    // Per group × cargo (slot * cargoCount + cargo).
    private byte[] _served = [];
    private int[] _rating = [];
    private int[] _waiting = [];
    private int[] _head = [];
    private int[] _tail = [];
    private int[] _lastPickupDay = [];
    private int[] _lastSpeed = [];
    private int[] _receivedThis = [];
    private int[] _receivedLast = [];
    private int[] _pickedUpThis = [];
    private int[] _pickedUpLast = [];
    private int[] _deliveredThis = [];
    private int[] _deliveredLast = [];
    private int[] _lostThis = [];
    private int[] _lostLast = [];
    private byte[] _overflowed = [];
    private int[] _pendingLoss = [];

    // Packet pool.
    private int[] _pkAmount = new int[64];
    private int[] _pkOrigin = new int[64];
    private int[] _pkSourceTown = new int[64];
    private int[] _pkDestination = new int[64];
    private int[] _pkPickupDay = new int[64];
    private int[] _pkNext = new int[64];
    private int _pkFree = -1;
    private int _pkUsed;

    /// <summary>Number of station groups; slots are <c>0..GroupCount-1</c>.</summary>
    public int GroupCount => _groupCount;

    /// <summary>Group slot of a station, or −1.</summary>
    public int GroupOf(int stationId)
    {
        var slot = SlotOf(stationId);
        return slot < 0 ? -1 : _group[slot];
    }

    /// <summary>The lowest station id of a group (its stable key).</summary>
    public int GroupKeyAt(int group) => _groupKey[group];

    /// <summary>Station ids of a group, in id order.</summary>
    public ReadOnlySpan<int> StationsInGroup(int group) =>
        _groupMembers.AsSpan(_groupMemberStart[group], _groupStationCount[group]);

    /// <summary>Units of a cargo waiting for the station's group (0 for an unknown station).</summary>
    public int WaitingAt(int stationId, int cargo)
    {
        var g = GroupOf(stationId);
        return g < 0 ? 0 : _waiting[(g * _cargoCount) + cargo];
    }

    /// <summary>The group's rating of a cargo (0–1 000), or −1 when the cargo is not served there.</summary>
    public int RatingAt(int stationId, int cargo)
    {
        var g = GroupOf(stationId);
        return g < 0 || _served[(g * _cargoCount) + cargo] == 0 ? -1 : _rating[(g * _cargoCount) + cargo];
    }

    /// <summary>True when a vehicle (or the debug service) started serving the cargo at the station's group.</summary>
    public bool IsServed(int stationId, int cargo)
    {
        var g = GroupOf(stationId);
        return g >= 0 && _served[(g * _cargoCount) + cargo] != 0;
    }

    /// <summary>Total units waiting at every station (all cargo).</summary>
    public long TotalWaiting
    {
        get
        {
            long total = 0;
            var used = _groupCount * _cargoCount;
            for (var i = 0; i < used; i++)
            {
                total += _waiting[i];
            }

            return total;
        }
    }

    /// <summary>Last and this month's figures of a group and cargo (window).</summary>
    public (int ReceivedLast, int PickedUpLast, int DeliveredLast, int LostLast, int ReceivedThis, int PickedUpThis, int DeliveredThis, int LostThis, int LastPickupDay)
        StatisticsOf(int group, int cargo)
    {
        var i = (group * _cargoCount) + cargo;
        return (_receivedLast[i], _pickedUpLast[i], _deliveredLast[i], _lostLast[i], _receivedThis[i], _pickedUpThis[i], _deliveredThis[i], _lostThis[i], _lastPickupDay[i]);
    }

    private void InitGroups() => AllocateGroups(0);

    private void AllocateGroups(int groups)
    {
        _groupCount = groups;
        _groupCapacity = groups;
        _groupKey = new int[groups];
        _groupStationCount = new int[groups];
        _groupMemberStart = new int[groups];
        var cells = groups * _cargoCount;
        _served = new byte[cells];
        _rating = new int[cells];
        _waiting = new int[cells];
        _head = new int[cells];
        _tail = new int[cells];
        _lastPickupDay = new int[cells];
        _lastSpeed = new int[cells];
        _receivedThis = new int[cells];
        _receivedLast = new int[cells];
        _pickedUpThis = new int[cells];
        _pickedUpLast = new int[cells];
        _deliveredThis = new int[cells];
        _deliveredLast = new int[cells];
        _lostThis = new int[cells];
        _lostLast = new int[cells];
        _overflowed = new byte[cells];
        _pendingLoss = new int[cells];
        Array.Fill(_head, -1);
        Array.Fill(_tail, -1);
        Array.Fill(_lastPickupDay, -1);
    }

    /// <summary>Grows the group tables (doubling) keeping their contents; new cells start empty.</summary>
    private void EnsureGroupCapacity(int groups)
    {
        if (groups <= _groupCapacity)
        {
            return;
        }

        var capacity = Math.Max(groups, Math.Max(16, _groupCapacity * 2));
        var oldCells = _groupCapacity * _cargoCount;
        var cells = capacity * _cargoCount;
        Array.Resize(ref _groupKey, capacity);
        Array.Resize(ref _groupStationCount, capacity);
        Array.Resize(ref _groupMemberStart, capacity);
        Array.Resize(ref _served, cells);
        Array.Resize(ref _rating, cells);
        Array.Resize(ref _waiting, cells);
        Array.Resize(ref _head, cells);
        Array.Resize(ref _tail, cells);
        Array.Resize(ref _lastPickupDay, cells);
        Array.Resize(ref _lastSpeed, cells);
        Array.Resize(ref _receivedThis, cells);
        Array.Resize(ref _receivedLast, cells);
        Array.Resize(ref _pickedUpThis, cells);
        Array.Resize(ref _pickedUpLast, cells);
        Array.Resize(ref _deliveredThis, cells);
        Array.Resize(ref _deliveredLast, cells);
        Array.Resize(ref _lostThis, cells);
        Array.Resize(ref _lostLast, cells);
        Array.Resize(ref _overflowed, cells);
        Array.Resize(ref _pendingLoss, cells);
        Array.Fill(_head, -1, oldCells, cells - oldCells);
        Array.Fill(_tail, -1, oldCells, cells - oldCells);
        Array.Fill(_lastPickupDay, -1, oldCells, cells - oldCells);
        _groupCapacity = capacity;
    }

    /// <summary>
    /// Fast path of <see cref="RecomputeGroups"/> for a new station when the company has no warehouse: the station
    /// becomes a group of its own at the end (the same result as a full recomputation, since new stations have the
    /// highest id).
    /// </summary>
    private void AppendGroup(int slot)
    {
        EnsureGroupCapacity(_groupCount + 1);
        if (_memberCount == _groupMembers.Length)
        {
            Array.Resize(ref _groupMembers, Math.Max(16, _groupMembers.Length * 2));
        }

        var g = _groupCount++;
        _groupKey[g] = _id[slot];
        _groupStationCount[g] = 1;
        _groupMemberStart[g] = _memberCount;
        _groupMembers[_memberCount++] = _id[slot];
        _group[slot] = g;
    }
    /// <summary>
    /// Recomputes the groups after construction (research R4): union–find over the stations in id order,
    /// then moves the old groups' state into the new ones (merge: added, split: divided by station count),
    /// so no cargo is created or lost except the stock of groups whose every station was removed.
    /// </summary>
    private void RecomputeGroups()
    {
        // Old state, by old group slot.
        var oldCount = _groupCount;
        var c = _cargoCount;
        var oldServed = _served;
        var oldRating = _rating;
        var oldWaiting = _waiting;
        var oldHead = _head;
        var oldLastPickup = _lastPickupDay;
        var oldSpeed = _lastSpeed;
        var oldStats = new[] { _receivedThis, _receivedLast, _pickedUpThis, _pickedUpLast, _deliveredThis, _deliveredLast, _lostThis, _lostLast };
        var oldKey = _groupKey;

        // Stations in id order.
        var order = new int[_count];
        for (var slot = 0; slot < _count; slot++)
        {
            order[slot] = slot;
        }

        Array.Sort(order, (a, b) => _id[a].CompareTo(_id[b]));
        var parent = new int[_count];
        for (var i = 0; i < _count; i++)
        {
            parent[i] = i;
        }

        int Find(int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }

            return i;
        }

        void Union(int a, int b)
        {
            var ra = Find(a);
            var rb = Find(b);
            if (ra != rb)
            {
                // Keep the root with the lower id so roots are deterministic.
                if (_id[order[ra]] < _id[order[rb]])
                {
                    parent[rb] = ra;
                }
                else
                {
                    parent[ra] = rb;
                }
            }
        }

        // Order index of every slot, and the warehouses in id order.
        var indexOfSlot = new int[_count];
        var warehouses = new List<int>();
        for (var i = 0; i < _count; i++)
        {
            indexOfSlot[order[i]] = i;
            if (_kind[order[i]] == (byte)StationKind.Warehouse)
            {
                warehouses.Add(i);
            }
        }

        foreach (var i in warehouses)
        {
            var w = order[i];
            var radius = Rules.Warehouses[_warehouseType[w]].InfluenceRadius;
            var wx0 = _x0[w] - radius;
            var wz0 = _z0[w] - radius;
            var wx1 = _x1[w] + radius;
            var wz1 = _z1[w] + radius;

            // Warehouses whose influence squares overlap.
            foreach (var k in warehouses)
            {
                var s = order[k];
                var r2 = Rules.Warehouses[_warehouseType[s]].InfluenceRadius;
                if (k != i && _owner[s] == _owner[w]
                    && _x0[s] - r2 <= wx1 && _x1[s] + r2 >= wx0 && _z0[s] - r2 <= wz1 && _z1[s] + r2 >= wz0)
                {
                    Union(i, k);
                }
            }

            // Road stations with a piece inside the influence square, found through the station grid (their
            // grid cells cover their pieces).
            var cx0 = Math.Max(0, wx0) / GridCell;
            var cz0 = Math.Max(0, wz0) / GridCell;
            var cx1 = Math.Min(_map.Width - 1, wx1) / GridCell;
            var cz1 = Math.Min(_map.Depth - 1, wz1) / GridCell;
            for (var cz = cz0; cz <= cz1; cz++)
            {
                for (var cx = cx0; cx <= cx1; cx++)
                {
                    if (!_grid.TryGetValue(cx + (cz * 65536), out var list))
                    {
                        continue;
                    }

                    foreach (var stationId in list)
                    {
                        var s = SlotOf(stationId);
                        if (s < 0 || _owner[s] != _owner[w] || _kind[s] != (byte)StationKind.Road)
                        {
                            continue;
                        }

                        foreach (var tile in _tiles[s])
                        {
                            var x = tile % _map.Width;
                            var z = tile / _map.Width;
                            if (x >= wx0 && x <= wx1 && z >= wz0 && z <= wz1)
                            {
                                Union(i, indexOfSlot[s]);
                                break;
                            }
                        }
                    }
                }
            }
        }

        // New groups in order of their lowest station id.
        var rootGroup = new int[_count];
        Array.Fill(rootGroup, -1);
        var newCount = 0;
        for (var i = 0; i < _count; i++)
        {
            var root = Find(i);
            if (rootGroup[root] < 0)
            {
                rootGroup[root] = newCount++;
            }
        }

        var oldGroupOfSlot = new int[_count];
        for (var slot = 0; slot < _count; slot++)
        {
            oldGroupOfSlot[slot] = _group[slot];
        }

        AllocateGroups(newCount);
        var members = new int[_count];
        var counts = new int[newCount];
        for (var i = 0; i < _count; i++)
        {
            var g = rootGroup[Find(i)];
            _group[order[i]] = g;
            counts[g]++;
        }

        var start = 0;
        for (var g = 0; g < newCount; g++)
        {
            _groupMemberStart[g] = start;
            _groupStationCount[g] = 0;
            start += counts[g];
        }

        for (var i = 0; i < _count; i++)
        {
            var slot = order[i];
            var g = _group[slot];
            members[_groupMemberStart[g] + _groupStationCount[g]++] = _id[slot];
            if (_groupStationCount[g] == 1)
            {
                _groupKey[g] = _id[slot];
            }
        }

        _groupMembers = members;
        _memberCount = _count;

        // Move the old groups' state (in old key order, so merges are deterministic).
        var oldOrder = new int[oldCount];
        for (var g = 0; g < oldCount; g++)
        {
            oldOrder[g] = g;
        }

        Array.Sort(oldOrder, (a, b) => oldKey[a].CompareTo(oldKey[b]));
        Span<int> share = newCount <= 256 ? stackalloc int[newCount] : new int[newCount];
        foreach (var old in oldOrder)
        {
            share.Clear();
            var survivors = 0;
            var firstTarget = -1;
            var firstId = int.MaxValue;
            for (var slot = 0; slot < _count; slot++)
            {
                if (oldGroupOfSlot[slot] != old)
                {
                    continue;
                }

                share[_group[slot]]++;
                survivors++;
                if (_id[slot] < firstId)
                {
                    firstId = _id[slot];
                    firstTarget = _group[slot];
                }
            }

            // Feature 013: destination cargo follows the old group's lowest surviving station (MigrateDirected).
            _groupRemap[oldKey[old]] = survivors == 0 ? -1 : _groupKey[firstTarget];
            for (var cargo = 0; cargo < c; cargo++)
            {
                var o = (old * c) + cargo;
                if (survivors == 0)
                {
                    // Every station of the group was removed: its waiting cargo is lost.
                    if (oldWaiting[o] > 0)
                    {
                        _totalLost += oldWaiting[o];
                    }

                    FreeList(oldHead[o]);
                    continue;
                }

                for (var g = 0; g < newCount; g++)
                {
                    if (share[g] == 0)
                    {
                        continue;
                    }

                    var n = (g * c) + cargo;
                    if (oldServed[o] != 0)
                    {
                        if (_served[n] == 0 || oldRating[o] > _rating[n])
                        {
                            _rating[n] = oldRating[o];
                            _lastSpeed[n] = oldSpeed[o];
                        }

                        _served[n] = 1;
                        _lastPickupDay[n] = Math.Max(_lastPickupDay[n], oldLastPickup[o]);
                    }

                    if (g == firstTarget)
                    {
                        _receivedThis[n] += oldStats[0][o];
                        _receivedLast[n] += oldStats[1][o];
                        _pickedUpThis[n] += oldStats[2][o];
                        _pickedUpLast[n] += oldStats[3][o];
                        _deliveredThis[n] += oldStats[4][o];
                        _deliveredLast[n] += oldStats[5][o];
                        _lostThis[n] += oldStats[6][o];
                        _lostLast[n] += oldStats[7][o];
                    }
                }

                // Packets: each is divided among the new groups in proportion to the stations each got.
                for (var p = oldHead[o]; p >= 0;)
                {
                    var next = _pkNext[p];
                    var amount = _pkAmount[p];
                    var given = 0;
                    for (var g = 0; g < newCount; g++)
                    {
                        if (share[g] == 0 || g == firstTarget)
                        {
                            continue;
                        }

                        var part = (int)((long)amount * share[g] / survivors);
                        if (part > 0)
                        {
                            AddPacket((g * c) + cargo, part, _pkOrigin[p], _pkSourceTown[p], _pkDestination[p], _pkPickupDay[p]);
                            given += part;
                        }
                    }

                    if (amount - given > 0)
                    {
                        AddPacket((firstTarget * c) + cargo, amount - given, _pkOrigin[p], _pkSourceTown[p], _pkDestination[p], _pkPickupDay[p]);
                    }

                    FreePacket(p);
                    p = next;
                }
            }
        }

        MigrateDirected();
    }

    private void AppendGroups(StateHasher hasher)
    {
        var groups = _groupCount;
        var cells = groups * _cargoCount;
        hasher.Append(groups);
        hasher.Append<int>(_groupKey.AsSpan(0, groups));
        hasher.Append<int>(_groupStationCount.AsSpan(0, groups));
        hasher.Append<byte>(_served.AsSpan(0, cells));
        hasher.Append<int>(_rating.AsSpan(0, cells));
        hasher.Append<int>(_waiting.AsSpan(0, cells));
        hasher.Append<int>(_lastPickupDay.AsSpan(0, cells));
        hasher.Append<int>(_lastSpeed.AsSpan(0, cells));
        hasher.Append<int>(_receivedThis.AsSpan(0, cells));
        hasher.Append<int>(_receivedLast.AsSpan(0, cells));
        hasher.Append<int>(_pickedUpThis.AsSpan(0, cells));
        hasher.Append<int>(_pickedUpLast.AsSpan(0, cells));
        hasher.Append<int>(_deliveredThis.AsSpan(0, cells));
        hasher.Append<int>(_deliveredLast.AsSpan(0, cells));
        hasher.Append<int>(_lostThis.AsSpan(0, cells));
        hasher.Append<int>(_lostLast.AsSpan(0, cells));
        hasher.Append<byte>(_overflowed.AsSpan(0, cells));
        for (var i = 0; i < cells; i++)
        {
            for (var p = _head[i]; p >= 0; p = _pkNext[p])
            {
                hasher.Append(_pkAmount[p]);
                hasher.Append(_pkOrigin[p]);
                hasher.Append(_pkSourceTown[p]);
                hasher.Append(_pkDestination[p]);
                hasher.Append(_pkPickupDay[p]);
            }

            hasher.Append(-1);
        }
    }

    private void RotateStatistics()
    {
        var cells = _groupCount * _cargoCount;
        Array.Copy(_receivedThis, _receivedLast, cells);
        Array.Clear(_receivedThis, 0, cells);
        Array.Copy(_pickedUpThis, _pickedUpLast, cells);
        Array.Clear(_pickedUpThis, 0, cells);
        Array.Copy(_deliveredThis, _deliveredLast, cells);
        Array.Clear(_deliveredThis, 0, cells);
        Array.Copy(_lostThis, _lostLast, cells);
        Array.Clear(_lostThis, 0, cells);
    }

    // ----- Packet pool -----

    /// <summary>Adds an amount to a group-cargo list, merging it into a packet with the same key.</summary>
    private void AddPacket(int cell, int amount, int origin, int sourceTown, int destination, int pickupDay)
    {
        for (var p = _head[cell]; p >= 0; p = _pkNext[p])
        {
            if (_pkOrigin[p] == origin && _pkSourceTown[p] == sourceTown && _pkDestination[p] == destination && _pkPickupDay[p] == pickupDay)
            {
                _pkAmount[p] += amount;
                _waiting[cell] += amount;
                return;
            }
        }

        var packet = AllocatePacket();
        _pkAmount[packet] = amount;
        _pkOrigin[packet] = origin;
        _pkSourceTown[packet] = sourceTown;
        _pkDestination[packet] = destination;
        _pkPickupDay[packet] = pickupDay;
        _pkNext[packet] = -1;
        if (_tail[cell] >= 0)
        {
            _pkNext[_tail[cell]] = packet;
        }
        else
        {
            _head[cell] = packet;
        }

        _tail[cell] = packet;
        _waiting[cell] += amount;
    }

    /// <summary>Removes up to <paramref name="amount"/> units from the oldest packets of a list; returns the amount removed.</summary>
    private int RemoveOldest(int cell, int amount)
    {
        var removed = 0;
        while (removed < amount && _head[cell] >= 0)
        {
            var p = _head[cell];
            var take = Math.Min(amount - removed, _pkAmount[p]);
            _pkAmount[p] -= take;
            removed += take;
            if (_pkAmount[p] == 0)
            {
                UnlinkHead(cell);
            }
        }

        _waiting[cell] -= removed;
        return removed;
    }

    private void UnlinkHead(int cell)
    {
        var p = _head[cell];
        _head[cell] = _pkNext[p];
        if (_head[cell] < 0)
        {
            _tail[cell] = -1;
        }

        FreePacket(p);
    }

    private int AllocatePacket()
    {
        if (_pkFree >= 0)
        {
            var free = _pkFree;
            _pkFree = _pkNext[free];
            return free;
        }

        if (_pkUsed == _pkAmount.Length)
        {
            var size = _pkAmount.Length * 2;
            Array.Resize(ref _pkAmount, size);
            Array.Resize(ref _pkOrigin, size);
            Array.Resize(ref _pkSourceTown, size);
            Array.Resize(ref _pkDestination, size);
            Array.Resize(ref _pkPickupDay, size);
            Array.Resize(ref _pkNext, size);
        }

        return _pkUsed++;
    }

    private void FreePacket(int packet)
    {
        _pkNext[packet] = _pkFree;
        _pkFree = packet;
    }

    private void FreeList(int head)
    {
        for (var p = head; p >= 0;)
        {
            var next = _pkNext[p];
            FreePacket(p);
            p = next;
        }
    }
}
