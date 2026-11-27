using Talt.Core.Content;
using Talt.Core.Demand;
using Talt.Core.Determinism;

namespace Talt.Core.Stations;

/// <summary>Waiting destination cargo of one destination town at a station (window query).</summary>
/// <param name="Town">Destination town.</param>
/// <param name="NextGroupKey">Group key (lowest station id) of the next station group on its route, or −1 (no route).</param>
/// <param name="Amount">Units waiting.</param>
public readonly record struct DestinationAmount(int Town, int NextGroupKey, int Amount);

/// <summary>
/// Destination cargo at stations (feature 013, research R9–R13, data-model.md Destination packet): packets with a
/// destination town wait in buckets keyed by (group, cargo, next group on the route) so a vehicle loads exactly the
/// cargo its next stop brings closer. Supply splits destination cargo over the source's destination table; units
/// without a route are not moved and count as unserved. Packets expire after <see cref="DemandRules.ExpiryDays"/>.
/// Groups that are on none of the company's lines keep the feature 010 behaviour (any destination).
/// </summary>
public sealed partial class StationRegistry
{
    private const int NoHop = -1;

    private readonly Dictionary<long, int> _bucketOf = [];
    private readonly Dictionary<long, (int Amount, int Origin)> _pendingSupply = [];
    private readonly Dictionary<int, int> _groupRemap = [];

    private DemandRules? _demand;
    private DestinationTables? _tables;
    private CargoRouter? _router;

    // Buckets.
    private int[] _bkGroup = new int[64];
    private int[] _bkCargo = new int[64];
    private int[] _bkHop = new int[64];
    private int[] _bkHead = new int[64];
    private int[] _bkTail = new int[64];
    private int[] _bkAmount = new int[64];
    private int _bucketCount;

    // Directed packet pool.
    private int[] _dpAmount = new int[64];
    private int[] _dpOrigin = new int[64];
    private int[] _dpSourceTown = new int[64];
    private int[] _dpDestination = new int[64];
    private int[] _dpPickupDay = new int[64];
    private int[] _dpSince = new int[64];
    private long[] _dpCredit = new long[64];
    private int[] _dpNext = new int[64];
    private int _dpFree = -1;
    private int _dpUsed;

    // Destination cargo totals (conservation, FR-019).
    private long _dirGenerated;
    private long _dirDelivered;
    private long _dirLost;
    private long _dirExpired;
    private long _dirUnserved;
    private long _dirTransferred;
    private int _rerouteCursor = -1;

    /// <summary>Number of destination cargo buckets (group, cargo, next group).</summary>
    public int BucketCount => _bucketCount;

    /// <summary>Destination demand rules, or null when the game has none attached.</summary>
    public DemandRules? Demand => _demand;

    /// <summary>Estimates transfer credits (feature 011's finance). Not state.</summary>
    public ITransferValuer? TransferValuer { get; set; }

    /// <summary>Destination cargo moved from producers to stations, ever.</summary>
    public long DirectedGenerated => _dirGenerated;

    /// <summary>Destination cargo delivered at its destination, ever.</summary>
    public long DirectedDelivered => _dirDelivered;

    /// <summary>Destination cargo lost (waiting cap, expiry, removed stations), ever.</summary>
    public long DirectedLost => _dirLost;

    /// <summary>Destination cargo that expired, ever (part of <see cref="DirectedLost"/>).</summary>
    public long DirectedExpired => _dirExpired;

    /// <summary>Destination cargo nobody could carry (no route), ever; never moved to a station.</summary>
    public long DirectedUnserved => _dirUnserved;

    /// <summary>Destination cargo left at a station to change line, ever.</summary>
    public long DirectedTransferred => _dirTransferred;

    /// <summary>Destination cargo waiting at every station.</summary>
    public long DirectedWaiting
    {
        get
        {
            long total = 0;
            for (var b = 0; b < _bucketCount; b++)
            {
                total += _bkAmount[b];
            }

            return total;
        }
    }

    /// <summary>Connects the destination demand (feature 013); without it every cargo behaves as in feature 010.</summary>
    public void AttachDemand(DemandRules rules, DestinationTables tables, CargoRouter router)
    {
        _demand = rules ?? throw new ArgumentNullException(nameof(rules));
        _tables = tables ?? throw new ArgumentNullException(nameof(tables));
        _router = router ?? throw new ArgumentNullException(nameof(router));
    }

    /// <summary>The group key (lowest station id) of a station's group, or −1.</summary>
    public int GroupKeyOfStation(int stationId)
    {
        var g = GroupOf(stationId);
        return g < 0 ? -1 : _groupKey[g];
    }

    /// <summary>Waiting destination cargo of a station's group by destination town and next group (window).</summary>
    public void DestinationWaiting(int stationId, int cargo, List<DestinationAmount> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        into.Clear();
        var key = GroupKeyOfStation(stationId);
        if (key < 0)
        {
            return;
        }

        for (var b = 0; b < _bucketCount; b++)
        {
            if (_bkGroup[b] != key || _bkCargo[b] != cargo || _bkAmount[b] == 0)
            {
                continue;
            }

            for (var p = _bkHead[b]; p >= 0; p = _dpNext[p])
            {
                Accumulate(into, _dpDestination[p], _bkHop[b], _dpAmount[p]);
            }
        }

        into.Sort((a, b) => b.Amount != a.Amount ? b.Amount.CompareTo(a.Amount) : a.Town.CompareTo(b.Town));
    }

    private static void Accumulate(List<DestinationAmount> list, int town, int hop, int amount)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i].Town == town && list[i].NextGroupKey == hop)
            {
                list[i] = list[i] with { Amount = list[i].Amount + amount };
                return;
            }
        }

        list.Add(new DestinationAmount(town, hop, amount));
    }

    /// <summary>True when the group's catchment holds buildings of <paramref name="town"/> that accept the cargo.</summary>
    public bool GroupServesTown(int group, int town, int cargo)
    {
        if (_towns is null || (uint)group >= (uint)_groupCount)
        {
            return false;
        }

        var buildings = _towns.Buildings;
        foreach (var memberId in StationsInGroup(group))
        {
            var slot = SlotOf(memberId);
            if ((_acceptMask[slot] & (1UL << cargo)) == 0)
            {
                continue;
            }

            foreach (var buildingId in _covBuildings[slot])
            {
                var bslot = buildings.SlotOf(buildingId);
                if (bslot >= 0 && buildings.TownAt(bslot) == town && BuildingAccepts(buildings.TypeDefinitionAt(bslot), cargo))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // ----- Routing snapshot support (CargoRouter) -----

    /// <summary>Adds the towns a group serves for a cargo to a routing problem's node.</summary>
    internal void AddServing(int groupKey, int cargo, RoutingProblem problem, int node)
    {
        var group = GroupOf(groupKey);
        if (group < 0 || _towns is null)
        {
            return;
        }

        var buildings = _towns.Buildings;
        foreach (var memberId in StationsInGroup(group))
        {
            var slot = SlotOf(memberId);
            if ((_acceptMask[slot] & (1UL << cargo)) == 0)
            {
                continue;
            }

            foreach (var buildingId in _covBuildings[slot])
            {
                var bslot = buildings.SlotOf(buildingId);
                if (bslot >= 0 && BuildingAccepts(buildings.TypeDefinitionAt(bslot), cargo))
                {
                    problem.AddServing(buildings.TownAt(bslot), node);
                }
            }
        }
    }

    /// <summary>
    /// Adds the estimated monthly production of a destination cargo at every node of a routing problem (research R7),
    /// with copies of the destination table rows it uses.
    /// </summary>
    internal void AddFlows(int cargo, RoutingProblem problem)
    {
        if (_tables is null || _demand is null)
        {
            return;
        }

        EnsureCoverage();
        var rule = _demand.RuleOf(cargo);
        if (rule < 0)
        {
            return;
        }

        var fromBuildings = _demand.Cargo[rule].Source == DemandSource.Buildings;
        var categoryBit = 1 << (int)_cargoCategory[cargo];
        for (var row = 0; row < _prodRows; row++)
        {
            int monthly, tableRow;
            if (_prodKind[row] == ProducerBuilding)
            {
                if (!fromBuildings || _prodTown[row] < 0)
                {
                    continue;
                }

                monthly = cargo == _passengerCargo ? _prodPassengers[row] : cargo == _mailCargo ? _prodMail[row] : 0;
                tableRow = _tables.RowOf(rule, _prodTown[row]);
            }
            else
            {
                if (fromBuildings)
                {
                    continue;
                }

                var islot = _industries.SlotOf(_prodId[row]);
                if (islot < 0)
                {
                    continue;
                }

                monthly = Math.Max(_industries.EffectiveProductionPerMonth(islot, cargo), _industries.ShippedLastMonthAt(islot, cargo));
                tableRow = _tables.RowOf(rule, _prodId[row]);
            }

            if (tableRow < 0)
            {
                continue;
            }

            // A producer with no output yet still gets routes (its estimate only weighs congestion).
            monthly = Math.Max(1, monthly);

            // Spread over the covering groups that are nodes of this problem.
            var covering = 0;
            var start = _prodStart[row];
            var end = start + _prodCount[row];
            for (var k = start; k < end; k++)
            {
                var slot = _coverSlots[k];
                if ((_categories[slot] & categoryBit) != 0 && problem.NodeOf(_groupKey[_group[slot]]) >= 0)
                {
                    covering++;
                }
            }

            if (covering == 0)
            {
                continue;
            }

            for (var k = start; k < end; k++)
            {
                var slot = _coverSlots[k];
                var node = problem.NodeOf(_groupKey[_group[slot]]);
                if ((_categories[slot] & categoryBit) != 0 && node >= 0)
                {
                    problem.AddFlow(node, tableRow, Math.Max(1, monthly / covering));
                }
            }

            problem.AddTableRow(tableRow, _tables.Destinations(rule, tableRow), _tables.Shares(rule, tableRow));
        }
    }

    // ----- Supply -----

    /// <summary>
    /// The route table for cargo at a group, or null when the cargo is undirected, no demand is attached or the group
    /// is on none of its company's lines (then the feature 010 behaviour applies).
    /// </summary>
    private RouteTable? RoutedTable(int group, int cargo)
    {
        if (_router is null || _demand is null || !_demand.IsDirected(cargo))
        {
            return null;
        }

        var key = _groupKey[group];
        var table = _router.TableFor(_owner[SlotOf(key)], cargo);
        return table is not null && table.HasNode(key) ? table : null;
    }

    /// <summary>
    /// Moves one share of a producer's daily amount to a group (feature 010 <c>Distribute</c>): undirected cargo
    /// waits as before; building cargo is gathered for the day's split; industry cargo is split now. Returns the units
    /// actually moved (industry cargo without a route stays at the industry).
    /// </summary>
    private int SupplyPart(int prodRow, int group, int cargo, int amount, int origin, int sourceTown)
    {
        var table = RoutedTable(group, cargo);
        if (table is null)
        {
            Arrive(group, cargo, amount, origin, sourceTown, -1, -1, supplied: true);
            return amount;
        }

        if (_prodKind[prodRow] == ProducerBuilding)
        {
            var key = ((long)group << 32) | ((long)cargo << 24) | (uint)(sourceTown & 0xFFFFFF);
            _pendingSupply.TryGetValue(key, out var pending);
            _pendingSupply[key] = (pending.Amount + amount, pending.Amount == 0 ? origin : Math.Min(origin, pending.Origin));
            return amount;
        }

        var rule = _demand!.RuleOf(cargo);
        var row = _tables!.RowOf(rule, _prodId[prodRow]);
        var industryTown = -1;
        return row < 0 ? 0 : SplitToDestinations(table, group, cargo, amount, origin, industryTown, rule, row);
    }

    /// <summary>Splits the day's gathered building cargo (research R10).</summary>
    private void FlushPendingSupply()
    {
        if (_pendingSupply.Count == 0)
        {
            return;
        }

        foreach (var (key, value) in _pendingSupply)
        {
            var group = (int)(key >> 32);
            var cargo = (int)((key >> 24) & 0xFF);
            var town = (int)(key & 0xFFFFFF);
            var table = RoutedTable(group, cargo)!;
            var rule = _demand!.RuleOf(cargo);
            var row = _tables!.RowOf(rule, town);
            if (row >= 0)
            {
                SplitToDestinations(table, group, cargo, value.Amount, value.Origin, town, rule, row);
            }
        }

        _pendingSupply.Clear();
    }

    /// <summary>Splits units over a source's destinations; routed units wait for their next group. Returns units moved.</summary>
    private int SplitToDestinations(RouteTable table, int group, int cargo, int amount, int origin, int sourceTown, int rule, int row)
    {
        var tables = _tables!;
        Span<int> units = stackalloc int[tables.Width];
        var n = tables.Split(rule, row, amount, units);
        var destinations = tables.Destinations(rule, row);
        var key = _groupKey[group];
        var moved = 0;
        for (var i = 0; i < n; i++)
        {
            var u = units[i];
            if (u <= 0)
            {
                continue;
            }

            var entry = table.Find(key, destinations[i]);
            if (entry < 0)
            {
                tables.RecordUnserved(rule, row, i, u);
                _dirUnserved += u;
                continue;
            }

            tables.RecordMoved(rule, row, i, u);
            var second = table.SplitToSecond(entry, u);
            ArriveDirected(group, cargo, u - second, origin, sourceTown, destinations[i], table.Hop1At(entry), -1, 0, supplied: true);
            if (second > 0)
            {
                ArriveDirected(group, cargo, second, origin, sourceTown, destinations[i], table.Hop2At(entry), -1, 0, supplied: true);
            }

            moved += u;
        }

        return moved;
    }

    /// <summary>Adds destination cargo to a group's bucket up to the waiting cap; the excess is lost.</summary>
    private void ArriveDirected(int group, int cargo, int amount, int origin, int sourceTown, int destination, int hop, int pickupDay, long credit, bool supplied)
    {
        if (amount <= 0)
        {
            return;
        }

        var cell = (group * _cargoCount) + cargo;
        var kept = Math.Min(amount, Math.Max(0, Rules.WaitingCap - _waiting[cell]));
        if (kept > 0)
        {
            var bucket = BucketFor(_groupKey[group], cargo, hop);
            AddDirectedPacket(bucket, kept, origin, sourceTown, destination, pickupDay, _today, credit * kept / amount);
            _waiting[cell] += kept;
        }

        if (supplied)
        {
            _totalSupplied += amount;
            _receivedThis[cell] += amount;
            _dirGenerated += amount;
        }

        var lost = amount - kept;
        if (lost > 0)
        {
            _lostThis[cell] += lost;
            _totalLost += lost;
            _dirLost += lost;
            _overflowed[cell] = 1;
        }
    }

    // ----- Vehicles -----

    /// <summary>Loads destination cargo whose next group is the vehicle's next stop's group, oldest first.</summary>
    private int LoadDirected(int slot, int cargo, int maxAmount, CargoHold hold, int nextStationId)
    {
        var group = _group[slot];
        var nextKey = GroupKeyOfStation(nextStationId);
        if (nextKey < 0 || nextKey == _groupKey[group] || !_bucketOf.TryGetValue(BucketKey(_groupKey[group], cargo, nextKey), out var bucket))
        {
            return 0;
        }

        var cell = (group * _cargoCount) + cargo;
        var mail = _cargoCategory[cargo] == CargoCategory.Mail;
        var loaded = 0;
        while (loaded < maxAmount && _bkHead[bucket] >= 0)
        {
            var p = _bkHead[bucket];
            var take = Math.Min(maxAmount - loaded, _dpAmount[p]);
            var credit = _dpCredit[p] * take / _dpAmount[p];
            var pickupDay = _dpPickupDay[p] < 0 ? _today : _dpPickupDay[p];
            hold.Add(new HeldCargo(cargo, take, _dpOrigin[p], _dpSourceTown[p], _dpDestination[p], pickupDay, credit));
            if (_dpSourceTown[p] >= 0 && _dpPickupDay[p] < 0)
            {
                _towns?.RecordTransported(_dpSourceTown[p], mail, take);
            }

            _dpAmount[p] -= take;
            _dpCredit[p] -= credit;
            loaded += take;
            if (_dpAmount[p] == 0)
            {
                UnlinkBucketHead(bucket);
            }
        }

        _bkAmount[bucket] -= loaded;
        _waiting[cell] -= loaded;
        return loaded;
    }

    /// <summary>
    /// Unloads one destination part (research R11): delivered when this group serves its town and is not where it
    /// started; kept on board when its route's next group is the vehicle's next stop; otherwise left here to change
    /// line, with this leg's credit. Returns the units left in the hold.
    /// </summary>
    private int UnloadDirected(int slot, int group, CargoHold hold, int index, UnloadMode mode, int nextStationId, ref UnloadTotals totals)
    {
        var part = hold[index];
        var originSlot = SlotOf(part.Origin);
        var fromHere = originSlot >= 0 && _group[originSlot] == group;
        if (!fromHere && GroupServesTown(group, part.Destination, part.Cargo))
        {
            var remaining = DeliverToConsumers(slot, group, part, part.Amount, part.Destination);
            var delivered = part.Amount - remaining;
            if (delivered > 0)
            {
                var credit = part.Credit * delivered / part.Amount;
                totals.Delivered += delivered;
                totals.CreditDelivered += credit;
                _dirDelivered += delivered;
                hold.SetPart(index, remaining, part.Credit - credit);
            }

            return remaining;
        }

        var table = _router?.TableFor(_owner[slot], part.Cargo);
        var key = _groupKey[group];
        var entry = table?.Find(key, part.Destination) ?? -1;
        var nextKey = GroupKeyOfStation(nextStationId);
        if (mode != UnloadMode.Transfer && entry >= 0 && nextKey >= 0 && nextKey != key
            && (table!.Hop1At(entry) == nextKey || table.Hop2At(entry) == nextKey))
        {
            return part.Amount; // Its route continues with this vehicle.
        }

        // Change line here (or wait for a route): this leg's credit is what the trip so far is worth beyond earlier credits.
        var legCredit = 0L;
        if (TransferValuer is not null && TryGetCentre(part.Origin, out var ox, out var oz))
        {
            var (x0, z0, x1, z1) = BoundsAt(slot);
            var distance = Math.Abs(((x0 + x1) / 2) - ox) + Math.Abs(((z0 + z1) / 2) - oz);
            legCredit = Math.Max(0, TransferValuer.Estimate(part.Cargo, part.Amount, distance, Math.Max(0, _today - part.PickupDay)) - part.Credit);
        }

        var hop = NoHop;
        var second = 0;
        if (entry >= 0)
        {
            hop = table!.Hop1At(entry);
            second = table.SplitToSecond(entry, part.Amount);
        }

        var credit1 = (part.Credit + legCredit) * (part.Amount - second) / part.Amount;
        ArriveDirected(group, part.Cargo, part.Amount - second, part.Origin, part.SourceTown, part.Destination, hop, part.PickupDay, credit1, supplied: false);
        if (second > 0)
        {
            ArriveDirected(group, part.Cargo, second, part.Origin, part.SourceTown, part.Destination, table!.Hop2At(entry), part.PickupDay, part.Credit + legCredit - credit1, supplied: false);
        }

        totals.Transferred += part.Amount;
        totals.CreditEarned += legCredit;
        _dirTransferred += part.Amount;
        hold.SetPart(index, 0, 0);
        return 0;
    }

    /// <summary>Running totals of one unload.</summary>
    private struct UnloadTotals
    {
        public int Delivered;
        public int Transferred;
        public long CreditEarned;
        public long CreditDelivered;
    }

    // ----- Daily work -----

    /// <summary>Expires destination cargo that waited too long (FR-018).</summary>
    private void ExpireDirected()
    {
        if (_demand is null || _bucketCount == 0)
        {
            return;
        }

        var limit = _today - _demand.ExpiryDays;
        for (var b = 0; b < _bucketCount; b++)
        {
            var expired = 0;
            while (_bkHead[b] >= 0 && _dpSince[_bkHead[b]] <= limit)
            {
                expired += _dpAmount[_bkHead[b]];
                UnlinkBucketHead(b);
            }

            if (expired == 0)
            {
                continue;
            }

            _bkAmount[b] -= expired;
            var group = GroupOf(_bkGroup[b]);
            if (group >= 0)
            {
                var cell = (group * _cargoCount) + _bkCargo[b];
                _waiting[cell] -= expired;
                _lostThis[cell] += expired;
            }

            _totalLost += expired;
            _dirLost += expired;
            _dirExpired += expired;
        }
    }

    /// <summary>Starts re-routing every waiting packet with the router's new tables (sliced by <see cref="RerouteSlice"/>).</summary>
    internal void BeginReroute() => _rerouteCursor = 0;

    /// <summary>Re-routes the waiting packets of up to <paramref name="buckets"/> buckets (research R13).</summary>
    internal void RerouteSlice(int buckets)
    {
        if (_rerouteCursor < 0)
        {
            return;
        }

        var end = Math.Min(_bucketCount, _rerouteCursor + buckets);
        for (var b = _rerouteCursor; b < end; b++)
        {
            RerouteBucket(b);
        }

        _rerouteCursor = end >= _bucketCount ? -1 : end;
    }

    private void RerouteBucket(int b)
    {
        if (_bkHead[b] < 0)
        {
            return;
        }

        var groupKey = _bkGroup[b];
        var group = GroupOf(groupKey);
        if (group < 0)
        {
            return;
        }

        var cargo = _bkCargo[b];
        var table = _router?.TableFor(_owner[SlotOf(groupKey)], cargo);
        var p = _bkHead[b];
        _bkHead[b] = _bkTail[b] = -1;
        var kept = 0;
        while (p >= 0)
        {
            var next = _dpNext[p];
            var entry = table?.Find(groupKey, _dpDestination[p]) ?? -1;
            var hop = entry >= 0 ? table!.Hop1At(entry) : NoHop;
            if (hop == _bkHop[b] || (entry >= 0 && table!.Hop2At(entry) == _bkHop[b]))
            {
                LinkTail(b, p);
                kept += _dpAmount[p];
            }
            else
            {
                var target = BucketFor(groupKey, cargo, hop);
                LinkTail(target, p);
                _bkAmount[target] += _dpAmount[p];
            }

            p = next;
        }

        _bkAmount[b] = kept;
    }

    /// <summary>After groups were recomputed: moves buckets to their new groups and recounts waiting amounts.</summary>
    private void MigrateDirected()
    {
        if (_bucketCount == 0)
        {
            _groupRemap.Clear();
            return;
        }

        var count = _bucketCount;
        for (var b = 0; b < count; b++)
        {
            var oldKey = _bkGroup[b];
            var newKey = _groupRemap.TryGetValue(oldKey, out var mapped) ? mapped : (GroupOf(oldKey) >= 0 ? _groupKey[GroupOf(oldKey)] : -1);
            if (newKey < 0)
            {
                // Every station of the group was removed: the feature 010 code already counted the cargo lost.
                _dirLost += _bkAmount[b];
                for (var p = _bkHead[b]; p >= 0;)
                {
                    var next = _dpNext[p];
                    FreeDirected(p);
                    p = next;
                }

                _bkHead[b] = _bkTail[b] = -1;
                _bkAmount[b] = 0;
                continue;
            }

            var target = b;
            if (newKey != oldKey)
            {
                target = BucketFor(newKey, _bkCargo[b], _bkHop[b]);
                for (var p = _bkHead[b]; p >= 0;)
                {
                    var next = _dpNext[p];
                    LinkTail(target, p);
                    p = next;
                }

                _bkAmount[target] += _bkAmount[b];
                _bkHead[b] = _bkTail[b] = -1;
                _bkAmount[b] = 0;
            }
        }

        // Waiting amounts were reset with the groups: add the destination cargo back.
        for (var b = 0; b < _bucketCount; b++)
        {
            var group = GroupOf(_bkGroup[b]);
            if (group >= 0 && _bkAmount[b] > 0)
            {
                _waiting[(group * _cargoCount) + _bkCargo[b]] += _bkAmount[b];
            }
        }

        _groupRemap.Clear();
    }

    // ----- Buckets and packets -----

    private static long BucketKey(int groupKey, int cargo, int hop) => ((long)groupKey << 40) | ((long)cargo << 32) | (uint)(hop + 1);

    private int BucketFor(int groupKey, int cargo, int hop)
    {
        var key = BucketKey(groupKey, cargo, hop);
        if (_bucketOf.TryGetValue(key, out var bucket))
        {
            return bucket;
        }

        if (_bucketCount == _bkGroup.Length)
        {
            var size = _bucketCount * 2;
            Array.Resize(ref _bkGroup, size);
            Array.Resize(ref _bkCargo, size);
            Array.Resize(ref _bkHop, size);
            Array.Resize(ref _bkHead, size);
            Array.Resize(ref _bkTail, size);
            Array.Resize(ref _bkAmount, size);
        }

        bucket = _bucketCount++;
        _bkGroup[bucket] = groupKey;
        _bkCargo[bucket] = cargo;
        _bkHop[bucket] = hop;
        _bkHead[bucket] = -1;
        _bkTail[bucket] = -1;
        _bkAmount[bucket] = 0;
        _bucketOf.Add(key, bucket);
        return bucket;
    }

    /// <summary>Appends cargo to a bucket, merging it into the newest packet when everything but the amount matches.</summary>
    private void AddDirectedPacket(int bucket, int amount, int origin, int sourceTown, int destination, int pickupDay, int since, long credit)
    {
        var tail = _bkTail[bucket];
        if (tail >= 0 && _dpOrigin[tail] == origin && _dpSourceTown[tail] == sourceTown && _dpDestination[tail] == destination
            && _dpPickupDay[tail] == pickupDay && _dpSince[tail] == since)
        {
            _dpAmount[tail] += amount;
            _dpCredit[tail] += credit;
            _bkAmount[bucket] += amount;
            return;
        }

        var p = AllocateDirected();
        _dpAmount[p] = amount;
        _dpOrigin[p] = origin;
        _dpSourceTown[p] = sourceTown;
        _dpDestination[p] = destination;
        _dpPickupDay[p] = pickupDay;
        _dpSince[p] = since;
        _dpCredit[p] = credit;
        LinkTail(bucket, p);
        _bkAmount[bucket] += amount;
    }

    private void LinkTail(int bucket, int p)
    {
        _dpNext[p] = -1;
        if (_bkTail[bucket] >= 0)
        {
            _dpNext[_bkTail[bucket]] = p;
        }
        else
        {
            _bkHead[bucket] = p;
        }

        _bkTail[bucket] = p;
    }

    private void UnlinkBucketHead(int bucket)
    {
        var p = _bkHead[bucket];
        _bkHead[bucket] = _dpNext[p];
        if (_bkHead[bucket] < 0)
        {
            _bkTail[bucket] = -1;
        }

        FreeDirected(p);
    }

    private int AllocateDirected()
    {
        if (_dpFree >= 0)
        {
            var free = _dpFree;
            _dpFree = _dpNext[free];
            return free;
        }

        if (_dpUsed == _dpAmount.Length)
        {
            var size = _dpUsed * 2;
            Array.Resize(ref _dpAmount, size);
            Array.Resize(ref _dpOrigin, size);
            Array.Resize(ref _dpSourceTown, size);
            Array.Resize(ref _dpDestination, size);
            Array.Resize(ref _dpPickupDay, size);
            Array.Resize(ref _dpSince, size);
            Array.Resize(ref _dpCredit, size);
            Array.Resize(ref _dpNext, size);
        }

        return _dpUsed++;
    }

    private void FreeDirected(int p)
    {
        _dpNext[p] = _dpFree;
        _dpFree = p;
    }

    /// <summary>Hashes destination cargo (only once a bucket exists, so earlier games keep their fingerprints).</summary>
    private void AppendDirected(StateHasher hasher)
    {
        if (_bucketCount == 0 && _dirUnserved == 0)
        {
            return;
        }

        hasher.Append(_bucketCount);
        for (var b = 0; b < _bucketCount; b++)
        {
            hasher.Append(_bkGroup[b]);
            hasher.Append(_bkCargo[b]);
            hasher.Append(_bkHop[b]);
            hasher.Append(_bkAmount[b]);
            for (var p = _bkHead[b]; p >= 0; p = _dpNext[p])
            {
                hasher.Append(_dpAmount[p]);
                hasher.Append(_dpOrigin[p]);
                hasher.Append(_dpSourceTown[p]);
                hasher.Append(_dpDestination[p]);
                hasher.Append(_dpPickupDay[p]);
                hasher.Append(_dpSince[p]);
                hasher.Append(_dpCredit[p]);
            }

            hasher.Append(-1);
        }

        hasher.Append(_dirGenerated);
        hasher.Append(_dirDelivered);
        hasher.Append(_dirLost);
        hasher.Append(_dirExpired);
        hasher.Append(_dirUnserved);
        hasher.Append(_dirTransferred);
        hasher.Append(_rerouteCursor);
    }
}
