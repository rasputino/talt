using Talt.Core.Network;
using Talt.Core.Saves;
using Talt.Core.Stations;

namespace Talt.Core.Vehicles;

public sealed partial class RoadVehicleSystem
{
    /// <summary>
    /// The <c>RVEH</c> section (feature 014, research R1): every vehicle array (hot, next and cold state, holds, routes),
    /// lanes, link tables, junction holders and requests, the budgeted route cache, version stamps, depot regions and
    /// road components, carries and counters, then the depots and the lines. The notice feed is interface state and starts
    /// empty; the router's search arrays are scratch.
    /// </summary>
    internal void WriteState(SaveWriter writer)
    {
        writer.WriteULong(_seed);
        writer.WriteInt(Count);
        writer.WriteInt(Version);
        writer.WriteLong(RouteSearches);
        writer.WriteInt(_nextId);
        writer.WriteLong(_tick);
        writer.WriteLong(_deliveries);
        writer.WriteLong(_distance);

        writer.WriteArray(_hot);
        writer.WriteArray(_next);
        writer.WriteArray(_cache);
        writer.WriteArray(_geometry);
        writer.WriteArray(_unpostedDistance);
        writer.WriteArray(_id);
        writer.WriteArray(_owner);
        writer.WriteArray(_model);
        writer.WriteArray(_cargo);
        writer.WriteArray(_homeDepot);
        writer.WriteArray(_line);
        writer.WriteArray(_stopIndex);
        writer.WriteArray(_targetKind);
        writer.WriteArray(_targetId);
        writer.WriteArray(_targetForm);
        writer.WriteArray(_orders);
        writer.WriteInt(_hold.Length);
        foreach (var hold in _hold)
        {
            CargoHold.WriteOptional(writer, hold);
        }

        writer.WriteArray(_loadTimer);
        writer.WriteArray(_loadedThisStep);
        writer.WriteInt(_route.Length);
        foreach (var route in _route)
        {
            writer.WriteArray(route);
        }

        writer.WriteArray(_tailFrom);
        writer.WriteArray(_tailDir);
        writer.WriteArray(_ageDays);
        writer.WriteArray(_reliability);
        writer.WriteArray(_daysSinceService);
        writer.WriteArray(_serviceInterval);
        writer.WriteArray(_depotInside);
        writer.WriteArray(_runningHandle);
        writer.WriteArray(_profitThisYear);
        writer.WriteArray(_profitLastYear);
        writer.WriteArray(_runningCarry);
        writer.WriteArray(_purchaseDay);
        writer.WriteDictionary(_slotById);

        // Drive step and lanes.
        writer.WriteArray(_active);
        writer.WriteInt(_activeCount);
        writer.WriteInt(_noticeTicks);
        writer.WriteArray(_stopTable);
        _laneIndex.WriteState(writer);
        writer.WriteArray(_laneKey);
        writer.WriteArray(_laneHead);
        writer.WriteArray(_laneTail);
        writer.WriteInt(_laneCount);
        writer.WriteArray(_laneOf);
        writer.WriteArray(_ahead);
        writer.WriteArray(_behind);

        // Events: junctions, routes, depots, components.
        writer.WriteDictionary(_holders);
        writer.WriteDictionaryInOrder(_requests);
        writer.WriteDictionary(_routeCache, static (w, cached) =>
        {
            w.WriteArray(cached.Steps);
            w.WriteLong(cached.Length);
            w.WriteInt(cached.Version);
            w.WriteBool(cached.Found);
        });
        writer.WriteList(_nearDepots);
        writer.WriteInt(_searchBudget);
        writer.WriteInt(_staleBudget);
        writer.WriteInt(_stationsVersion);
        writer.WriteInt(_depotsVersion);
        writer.WriteArray(_depotRegionStart);
        writer.WriteArray(_depotRegionSlots);
        writer.WriteInt(_depotRegionsX);
        writer.WriteInt(_depotRegionsZ);
        writer.WriteInt(_depotIndexVersion);
        writer.WriteArray(_component);
        writer.WriteInt(_componentVersion);
        writer.WriteSet(_occupied);
        writer.WriteLong(_occupiedTick);

        // Links (feature 013) and daily carries.
        writer.WriteArray(_lineUnits);
        writer.WriteArray(_lineMinKmh);
        writer.WriteInt(_linkChanges);
        writer.WriteArray(_depotCarry);

        Depots.WriteState(writer);
        Lines.WriteState(writer);
    }

    internal void ReadState(SaveReader reader, int version)
    {
        if (reader.ReadULong() != _seed)
        {
            throw reader.Fail("road vehicle seed differs (the game was not constructed like the saved one)");
        }

        Count = reader.ReadInt();
        Version = reader.ReadInt();
        RouteSearches = reader.ReadLong();
        _nextId = reader.ReadInt();
        _tick = reader.ReadLong();
        _deliveries = reader.ReadLong();
        _distance = reader.ReadLong();

        _hot = reader.ReadRequiredArray<RoadVehicleState>();
        _next = reader.ReadRequiredArray<RoadVehicleState>();
        if (version >= 2)
        {
            _cache = reader.ReadRequiredArray<LaneCache>();
            _geometry = reader.ReadRequiredArray<LaneGeometry>();
            _unpostedDistance = reader.ReadRequiredArray<long>();
        }
        else
        {
            // v1 cached ten integers per lane lookup. Rebuild from the network after restoring lane IDs.
            var slots = reader.ReadInt(0, _hot.Length);
            _cache = new LaneCache[slots];
            for (var slot = 0; slot < slots; slot++)
            {
                for (var field = 0; field < 10; field++)
                {
                    reader.ReadInt();
                }
            }
            _unpostedDistance = new long[_hot.Length];
        }
        _id = reader.ReadRequiredArray<int>();
        _owner = reader.ReadRequiredArray<int>();
        _model = reader.ReadRequiredArray<int>();
        _cargo = reader.ReadRequiredArray<int>();
        _homeDepot = reader.ReadRequiredArray<int>();
        _line = reader.ReadRequiredArray<int>();
        _stopIndex = reader.ReadRequiredArray<int>();
        _targetKind = reader.ReadRequiredArray<VehicleTargetKind>();
        _targetId = reader.ReadRequiredArray<int>();
        _targetForm = reader.ReadRequiredArray<TargetForm>();
        _orders = reader.ReadRequiredArray<VehicleOrders>();
        _hold = new CargoHold[reader.ReadCount(1)];
        for (var i = 0; i < _hold.Length; i++)
        {
            _hold[i] = CargoHold.ReadOptional(reader)!;
        }

        _loadTimer = reader.ReadRequiredArray<int>();
        _loadedThisStep = reader.ReadRequiredArray<int>();
        _route = new RouteStep[reader.ReadCount(4)][];
        for (var i = 0; i < _route.Length; i++)
        {
            _route[i] = reader.ReadArray<RouteStep>()!;
        }

        _tailFrom = reader.ReadRequiredArray<int>();
        _tailDir = reader.ReadRequiredArray<int>();
        _ageDays = reader.ReadRequiredArray<int>();
        _reliability = reader.ReadRequiredArray<int>();
        _daysSinceService = reader.ReadRequiredArray<int>();
        _serviceInterval = reader.ReadRequiredArray<int>();
        _depotInside = reader.ReadRequiredArray<int>();
        _runningHandle = reader.ReadRequiredArray<int>();
        _profitThisYear = reader.ReadRequiredArray<long>();
        _profitLastYear = reader.ReadRequiredArray<long>();
        _runningCarry = reader.ReadRequiredArray<long>();
        _purchaseDay = reader.ReadRequiredArray<int>();
        reader.ReadDictionaryInto(_slotById);
        var capacity = _id.Length;
        if (Count < 0 || Count > capacity || _hot.Length != capacity || _hold.Length != capacity || _route.Length != capacity || _model.Length != capacity)
        {
            throw reader.Fail("road vehicle arrays are inconsistent");
        }

        reader.Content.MapInPlace(ContentTable.Cargo, _cargo.AsSpan(0, Count));
        reader.Content.MapInPlace(ContentTable.RoadVehicleModel, _model.AsSpan(0, Count));

        _active = reader.ReadRequiredArray<int>();
        _leftAcrossLaneEnd = new bool[_active.Length];
        _activeCount = reader.ReadInt(0, _active.Length);
        _noticeTicks = reader.ReadInt();
        _stopTable = reader.ReadRequiredArray<long>();
        _laneIndex.ReadState(reader);
        _laneKey = reader.ReadRequiredArray<int>();
        _laneHead = reader.ReadRequiredArray<int>();
        _laneTail = reader.ReadRequiredArray<int>();
        _laneCount = reader.ReadInt(0, _laneKey.Length);
        _laneOf = reader.ReadRequiredArray<int>();
        _ahead = reader.ReadRequiredArray<int>();
        _behind = reader.ReadRequiredArray<int>();

        if (version == 1)
        {
            _geometry = new LaneGeometry[_laneKey.Length];
            for (var slot = 0; slot < Count; slot++)
            {
                if (_hot[slot].Lane >= 0)
                {
                    CacheLane(slot, _hot[slot].Lane);
                    _cache[slot].Index = _laneOf[slot];
                }
            }
        }

        if (_cache.Length != capacity || _unpostedDistance.Length != capacity || _geometry.Length != _laneKey.Length)
        {
            throw reader.Fail("road geometry or vehicle capacity differs");
        }

        for (var lane = 0; lane < _laneCount; lane++)
        {
            var key = _laneKey[lane];
            var end = version >= 2 ? _geometry[lane].To : _roads.Follow(key >> 3, key & 7);
            Traffic?.RegisterRoadLane(lane, key >> 3, end);
        }

        reader.ReadDictionaryInto(_holders);
        reader.ReadDictionaryInto(_requests);
        var cached = reader.ReadCount(8);
        _routeCache.Clear();
        for (var i = 0; i < cached; i++)
        {
            var key = reader.ReadLong();
            var steps = reader.ReadRequiredArray<RouteStep>();
            var length = reader.ReadLong();
            var routeVersion = reader.ReadInt();
            var found = reader.ReadBool();
            _routeCache.Add(key, new CachedRoute(steps, length, routeVersion, found));
        }

        reader.ReadListInto(_nearDepots);
        _searchBudget = reader.ReadInt();
        _staleBudget = reader.ReadInt();
        _stationsVersion = reader.ReadInt();
        _depotsVersion = reader.ReadInt();
        _depotRegionStart = reader.ReadRequiredArray<int>();
        _depotRegionSlots = reader.ReadRequiredArray<int>();
        _depotRegionsX = reader.ReadInt();
        _depotRegionsZ = reader.ReadInt();
        _depotIndexVersion = reader.ReadInt();
        _component = reader.ReadRequiredArray<int>();
        _componentVersion = reader.ReadInt();
        reader.ReadSetInto(_occupied);
        _occupiedTick = reader.ReadLong();

        _lineUnits = reader.Content.Rows(ContentTable.Cargo, reader.ReadRequiredArray<int>());
        _lineMinKmh = reader.Content.Rows(ContentTable.Cargo, reader.ReadRequiredArray<int>());
        _linkChanges = reader.ReadInt();
        _depotCarry = reader.ReadRequiredArray<long>();

        Depots.ReadState(reader);
        Lines.ReadState(reader);
    }
}

public sealed partial class DepotRegistry
{
    internal void WriteState(SaveWriter writer)
    {
        writer.WriteInt(Count);
        writer.WriteInt(Version);
        writer.WriteInt(_nextId);
        writer.WriteArray(_id);
        writer.WriteArray(_owner);
        writer.WriteArray(_tile);
        writer.WriteArray(_direction);
        writer.WriteDictionary(_slotById);
        writer.WriteDictionary(_idByTile);
    }

    internal void ReadState(SaveReader reader)
    {
        var count = reader.ReadInt();
        Version = reader.ReadInt();
        _nextId = reader.ReadInt();
        _id = reader.ReadRequiredArray<int>();
        _owner = reader.ReadRequiredArray<int>();
        _tile = reader.ReadRequiredArray<int>();
        _direction = reader.ReadRequiredArray<byte>();
        if (count < 0 || count > _id.Length || _owner.Length != _id.Length || _tile.Length != _id.Length || _direction.Length != _id.Length)
        {
            throw reader.Fail("depot arrays are inconsistent");
        }

        Count = count;
        reader.ReadDictionaryInto(_slotById);
        reader.ReadDictionaryInto(_idByTile);
    }
}

public sealed partial class LineRegistry
{
    internal void WriteState(SaveWriter writer)
    {
        writer.WriteInt(_nextId);
        writer.WriteInt(Version);
        writer.WriteInt(_lines.Count);
        foreach (var line in _lines)
        {
            writer.WriteInt(line.Id);
            writer.WriteInt(line.Owner);
            writer.WriteInt(line.Number);
            writer.WriteInt(line.Color);
            writer.WriteString(line.Name);
            writer.WriteList(line.Stops);
        }

        // The id lookup holds the same lines (sorted: never enumerated, research R3).
        writer.WriteDictionary(_byId, static (_, _) => { });
    }

    internal void ReadState(SaveReader reader)
    {
        _nextId = reader.ReadInt();
        Version = reader.ReadInt();
        var count = reader.ReadCount(20);
        _lines.Clear();
        var byId = new Dictionary<int, Line>();
        for (var i = 0; i < count; i++)
        {
            var line = new Line(reader.ReadInt(), reader.ReadInt(), reader.ReadInt(), reader.ReadInt()) { Name = reader.ReadString() };
            reader.ReadListInto(line.Stops);
            _lines.Add(line);
            if (!byId.TryAdd(line.Id, line))
            {
                throw reader.Fail("duplicate line id");
            }
        }

        var ids = reader.ReadCount(4); // Keys of the id lookup, sorted.
        _byId.Clear();
        for (var i = 0; i < ids; i++)
        {
            var id = reader.ReadInt();
            if (!byId.TryGetValue(id, out var line) || !_byId.TryAdd(id, line))
            {
                throw reader.Fail("line lookup refers to an unknown line");
            }
        }
    }
}

internal sealed partial class LaneIndexMap
{
    internal void WriteState(SaveWriter writer)
    {
        writer.WriteArray(_keys);
        writer.WriteArray(_values);
        writer.WriteInt(_shift);
        writer.WriteInt(Count);
    }

    internal void ReadState(SaveReader reader)
    {
        _keys = reader.ReadRequiredArray<int>();
        _values = reader.ReadRequiredArray<int>();
        _shift = reader.ReadInt();
        Count = reader.ReadInt();
        if (_keys.Length != _values.Length || _keys.Length == 0 || (_keys.Length & (_keys.Length - 1)) != 0)
        {
            throw reader.Fail("lane map is inconsistent");
        }
    }
}
