using Talt.Core.Saves;

namespace Talt.Core.Stations;

public sealed partial class StationRegistry
{
    /// <summary>
    /// The <c>STNS</c> section (feature 014, research R1): stations, pieces and lookups, coverage and producer tables,
    /// groups with ratings and waiting cargo, packet pools with their free lists, destination buckets and their pools,
    /// supply carries and totals, all verbatim. Content lookups (<c>_kindAccepts</c>, <c>_kindProduces</c>) are rebuilt
    /// at construction from the content.
    /// </summary>
    internal void WriteState(SaveWriter writer)
    {
        writer.WriteInt(_cargoCount);

        // Stations.
        writer.WriteInt(_count);
        writer.WriteInt(_nextId);
        writer.WriteInt(_today);
        writer.WriteInt(Version);
        writer.WriteArray(_id);
        writer.WriteArray(_owner);
        writer.WriteArray(_kind);
        writer.WriteArray(_warehouseType);
        writer.WriteArray(_x0);
        writer.WriteArray(_z0);
        writer.WriteArray(_x1);
        writer.WriteArray(_z1);
        writer.WriteArray(_town);
        writer.WriteArray(_nameKind);
        writer.WriteArray(_namePlace);
        writer.WriteArray(_nameNumber);
        writer.WriteInt(_customName.Length);
        foreach (var name in _customName)
        {
            writer.WriteString(name);
        }

        writer.WriteArray(_categories);
        writer.WriteArray(_radius);
        writer.WriteArray(_group);
        WriteLists(writer, _tiles);
        writer.WriteDictionary(_pieces);
        writer.WriteDictionary(_slotById);
        writer.WriteDictionary(_nameCounters);
        writer.WriteDictionary(_removedCentre);
        writer.WriteLong(_totalSupplied);
        writer.WriteLong(_totalLost);
        writer.WriteLong(_totalDelivered);
        writer.WriteLong(_totalDebugCollected);

        // Coverage (feature 010) and producer table.
        writer.WriteDictionary(_grid, static (w, list) => w.WriteList(list));
        writer.WriteList(_buildingWatch);
        writer.WriteDictionary(_industryRow);
        WriteLists(writer, _covIndustries);
        WriteLists(writer, _covBuildings);
        writer.WriteArray(_accept);
        writer.WriteArray(_acceptMask);
        writer.WriteArray(_supplyMask);
        writer.WriteArray(_dirty);
        writer.WriteArray(_gx0);
        writer.WriteArray(_gz0);
        writer.WriteArray(_gx1);
        writer.WriteArray(_gz1);
        writer.WriteArray(_groupAccept);
        writer.WriteArray(_prodKind);
        writer.WriteArray(_prodId);
        writer.WriteArray(_prodTown);
        writer.WriteArray(_prodPassengers);
        writer.WriteArray(_prodMail);
        writer.WriteArray(_prodStart);
        writer.WriteArray(_prodCount);
        writer.WriteArray(_coverSlots);
        writer.WriteArray(_pairs);
        writer.WriteInt(_prodRows);
        writer.WriteInt(_industryVersion);
        writer.WriteBool(_coverageChanged);

        // Groups (feature 010) and packets.
        writer.WriteInt(_groupCount);
        writer.WriteInt(_groupCapacity);
        writer.WriteInt(_memberCount);
        writer.WriteArray(_groupKey);
        writer.WriteArray(_groupStationCount);
        writer.WriteArray(_groupMemberStart);
        writer.WriteArray(_groupMembers);
        writer.WriteArray(_served);
        writer.WriteArray(_rating);
        writer.WriteArray(_waiting);
        writer.WriteArray(_head);
        writer.WriteArray(_tail);
        writer.WriteArray(_lastPickupDay);
        writer.WriteArray(_lastSpeed);
        writer.WriteArray(_receivedThis);
        writer.WriteArray(_receivedLast);
        writer.WriteArray(_pickedUpThis);
        writer.WriteArray(_pickedUpLast);
        writer.WriteArray(_deliveredThis);
        writer.WriteArray(_deliveredLast);
        writer.WriteArray(_lostThis);
        writer.WriteArray(_lostLast);
        writer.WriteArray(_overflowed);
        writer.WriteArray(_pendingLoss);
        writer.WriteArray(_pkAmount);
        writer.WriteArray(_pkOrigin);
        writer.WriteArray(_pkSourceTown);
        writer.WriteArray(_pkDestination);
        writer.WriteArray(_pkPickupDay);
        writer.WriteArray(_pkNext);
        writer.WriteInt(_pkFree);
        writer.WriteInt(_pkUsed);

        // Supply (feature 010) and debug hold.
        writer.WriteDictionary(_carry);
        _debugHold.WriteState(writer);

        // Destinations (feature 013).
        writer.WriteDictionary(_bucketOf);
        writer.WriteDictionaryInOrder(_pendingSupply);
        writer.WriteDictionary(_groupRemap);
        writer.WriteArray(_bkGroup);
        writer.WriteArray(_bkCargo);
        writer.WriteArray(_bkHop);
        writer.WriteArray(_bkHead);
        writer.WriteArray(_bkTail);
        writer.WriteArray(_bkAmount);
        writer.WriteInt(_bucketCount);
        writer.WriteArray(_dpAmount);
        writer.WriteArray(_dpOrigin);
        writer.WriteArray(_dpSourceTown);
        writer.WriteArray(_dpDestination);
        writer.WriteArray(_dpPickupDay);
        writer.WriteArray(_dpSince);
        writer.WriteArray(_dpCredit);
        writer.WriteArray(_dpNext);
        writer.WriteInt(_dpFree);
        writer.WriteInt(_dpUsed);
        writer.WriteLong(_dirGenerated);
        writer.WriteLong(_dirDelivered);
        writer.WriteLong(_dirLost);
        writer.WriteLong(_dirExpired);
        writer.WriteLong(_dirUnserved);
        writer.WriteLong(_dirTransferred);
        writer.WriteInt(_rerouteCursor);
    }

    internal void ReadState(SaveReader reader)
    {
        if (reader.ReadInt() != reader.Content.SavedCount(ContentTable.Cargo, _cargoCount))
        {
            throw reader.Fail("station cargo count differs from the installed cargo types");
        }

        _count = reader.ReadInt();
        _nextId = reader.ReadInt();
        _today = reader.ReadInt();
        Version = reader.ReadInt();
        _id = reader.ReadRequiredArray<int>();
        _owner = reader.ReadRequiredArray<int>();
        _kind = reader.ReadRequiredArray<byte>();
        _warehouseType = reader.ReadRequiredArray<byte>();
        _x0 = reader.ReadRequiredArray<int>();
        _z0 = reader.ReadRequiredArray<int>();
        _x1 = reader.ReadRequiredArray<int>();
        _z1 = reader.ReadRequiredArray<int>();
        _town = reader.ReadRequiredArray<int>();
        _nameKind = reader.ReadRequiredArray<byte>();
        _namePlace = reader.ReadRequiredArray<byte>();
        _nameNumber = reader.ReadRequiredArray<int>();
        _customName = new string?[reader.ReadCount(4)];
        for (var i = 0; i < _customName.Length; i++)
        {
            _customName[i] = reader.ReadString();
        }

        _categories = reader.ReadRequiredArray<int>();
        _radius = reader.ReadRequiredArray<int>();
        _group = reader.ReadRequiredArray<int>();
        _tiles = ReadLists(reader);
        reader.ReadDictionaryInto(_pieces);
        reader.ReadDictionaryInto(_slotById);
        reader.ReadDictionaryInto(_nameCounters);
        reader.ReadDictionaryInto(_removedCentre);
        _totalSupplied = reader.ReadLong();
        _totalLost = reader.ReadLong();
        _totalDelivered = reader.ReadLong();
        _totalDebugCollected = reader.ReadLong();
        if (_count < 0 || _count > _id.Length || _tiles.Length != _id.Length || _customName.Length != _id.Length)
        {
            throw reader.Fail("station arrays are inconsistent");
        }

        var cells = reader.ReadCount(8);
        _grid.Clear();
        for (var i = 0; i < cells; i++)
        {
            var key = reader.ReadInt();
            var list = new List<int>();
            reader.ReadListInto(list);
            _grid.Add(key, list);
        }

        reader.ReadListInto(_buildingWatch);
        reader.ReadDictionaryInto(_industryRow);
        _covIndustries = ReadLists(reader);
        _covBuildings = ReadLists(reader);
        _accept = reader.Content.Rows(ContentTable.Cargo, reader.ReadRequiredArray<int>());
        _acceptMask = reader.ReadRequiredArray<ulong>();
        _supplyMask = reader.ReadRequiredArray<ulong>();
        _dirty = reader.ReadRequiredArray<bool>();
        _gx0 = reader.ReadRequiredArray<int>();
        _gz0 = reader.ReadRequiredArray<int>();
        _gx1 = reader.ReadRequiredArray<int>();
        _gz1 = reader.ReadRequiredArray<int>();
        _groupAccept = reader.ReadRequiredArray<ulong>();
        _prodKind = reader.ReadRequiredArray<byte>();
        _prodId = reader.ReadRequiredArray<int>();
        _prodTown = reader.ReadRequiredArray<int>();
        _prodPassengers = reader.ReadRequiredArray<int>();
        _prodMail = reader.ReadRequiredArray<int>();
        _prodStart = reader.ReadRequiredArray<int>();
        _prodCount = reader.ReadRequiredArray<int>();
        _coverSlots = reader.ReadRequiredArray<int>();
        _pairs = reader.ReadRequiredArray<long>();
        _prodRows = reader.ReadInt();
        _industryVersion = reader.ReadInt();
        _coverageChanged = reader.ReadBool();

        _groupCount = reader.ReadInt();
        _groupCapacity = reader.ReadInt();
        _memberCount = reader.ReadInt();
        _groupKey = reader.ReadRequiredArray<int>();
        _groupStationCount = reader.ReadRequiredArray<int>();
        _groupMemberStart = reader.ReadRequiredArray<int>();
        _groupMembers = reader.ReadRequiredArray<int>();
        _served = reader.Content.Rows(ContentTable.Cargo, reader.ReadRequiredArray<byte>());
        _rating = reader.Content.Rows(ContentTable.Cargo, reader.ReadRequiredArray<int>());
        _waiting = reader.Content.Rows(ContentTable.Cargo, reader.ReadRequiredArray<int>());
        _head = reader.Content.Rows(ContentTable.Cargo, reader.ReadRequiredArray<int>(), -1);
        _tail = reader.Content.Rows(ContentTable.Cargo, reader.ReadRequiredArray<int>(), -1);
        _lastPickupDay = reader.Content.Rows(ContentTable.Cargo, reader.ReadRequiredArray<int>(), -1);
        _lastSpeed = reader.Content.Rows(ContentTable.Cargo, reader.ReadRequiredArray<int>());
        _receivedThis = reader.Content.Rows(ContentTable.Cargo, reader.ReadRequiredArray<int>());
        _receivedLast = reader.Content.Rows(ContentTable.Cargo, reader.ReadRequiredArray<int>());
        _pickedUpThis = reader.Content.Rows(ContentTable.Cargo, reader.ReadRequiredArray<int>());
        _pickedUpLast = reader.Content.Rows(ContentTable.Cargo, reader.ReadRequiredArray<int>());
        _deliveredThis = reader.Content.Rows(ContentTable.Cargo, reader.ReadRequiredArray<int>());
        _deliveredLast = reader.Content.Rows(ContentTable.Cargo, reader.ReadRequiredArray<int>());
        _lostThis = reader.Content.Rows(ContentTable.Cargo, reader.ReadRequiredArray<int>());
        _lostLast = reader.Content.Rows(ContentTable.Cargo, reader.ReadRequiredArray<int>());
        _overflowed = reader.Content.Rows(ContentTable.Cargo, reader.ReadRequiredArray<byte>());
        _pendingLoss = reader.Content.Rows(ContentTable.Cargo, reader.ReadRequiredArray<int>());
        _pkAmount = reader.ReadRequiredArray<int>();
        _pkOrigin = reader.ReadRequiredArray<int>();
        _pkSourceTown = reader.ReadRequiredArray<int>();
        _pkDestination = reader.ReadRequiredArray<int>();
        _pkPickupDay = reader.ReadRequiredArray<int>();
        _pkNext = reader.ReadRequiredArray<int>();
        _pkFree = reader.ReadInt();
        _pkUsed = reader.ReadInt();

        reader.ReadDictionaryInto(_carry);
        _debugHold.ReadState(reader);

        reader.ReadDictionaryInto(_bucketOf);
        reader.ReadDictionaryInto(_pendingSupply);
        reader.ReadDictionaryInto(_groupRemap);
        _bkGroup = reader.ReadRequiredArray<int>();
        _bkCargo = reader.ReadRequiredArray<int>();
        _bkHop = reader.ReadRequiredArray<int>();
        _bkHead = reader.ReadRequiredArray<int>();
        _bkTail = reader.ReadRequiredArray<int>();
        _bkAmount = reader.ReadRequiredArray<int>();
        _bucketCount = reader.ReadInt();
        _dpAmount = reader.ReadRequiredArray<int>();
        _dpOrigin = reader.ReadRequiredArray<int>();
        _dpSourceTown = reader.ReadRequiredArray<int>();
        _dpDestination = reader.ReadRequiredArray<int>();
        _dpPickupDay = reader.ReadRequiredArray<int>();
        _dpSince = reader.ReadRequiredArray<int>();
        _dpCredit = reader.ReadRequiredArray<long>();
        _dpNext = reader.ReadRequiredArray<int>();
        _dpFree = reader.ReadInt();
        _dpUsed = reader.ReadInt();
        _dirGenerated = reader.ReadLong();
        _dirDelivered = reader.ReadLong();
        _dirLost = reader.ReadLong();
        _dirExpired = reader.ReadLong();
        _dirUnserved = reader.ReadLong();
        _dirTransferred = reader.ReadLong();
        _rerouteCursor = reader.ReadInt();
        reader.Content.CargoMasks(_acceptMask);
        reader.Content.CargoMasks(_supplyMask);
        reader.Content.CargoMasks(_groupAccept);
        reader.Content.CargoKeys(_carry, 0);
        reader.Content.CargoKeys(_pendingSupply, 24);
        reader.Content.CargoKeys(_bucketOf, 32);
        reader.Content.MapInPlace(ContentTable.Cargo, _bkCargo.AsSpan(0, _bucketCount));
        if (!reader.Content.IsIdentityFor(ContentTable.StationPiece))
        {
            foreach (var (tile, piece) in _pieces.ToArray())
            {
                if (piece.Type != WarehousePiece)
                {
                    _pieces[tile] = piece with { Type = checked((byte)reader.Content.Map(ContentTable.StationPiece, piece.Type)) };
                }
            }
        }

        for (var slot = 0; slot < _count; slot++)
        {
            if (_kind[slot] == (byte)StationKind.Warehouse)
            {
                _warehouseType[slot] = checked((byte)reader.Content.Map(ContentTable.Warehouse, _warehouseType[slot]));
            }
        }
    }

    private static void WriteLists(SaveWriter writer, List<int>[] lists)
    {
        writer.WriteInt(lists.Length);
        foreach (var list in lists)
        {
            writer.WriteBool(list is not null);
            if (list is not null)
            {
                writer.WriteList(list);
            }
        }
    }

    private static List<int>[] ReadLists(SaveReader reader)
    {
        var lists = new List<int>[reader.ReadCount(1)];
        for (var i = 0; i < lists.Length; i++)
        {
            if (reader.ReadBool())
            {
                var list = new List<int>();
                reader.ReadListInto(list);
                lists[i] = list;
            }
        }

        return lists;
    }
}

public sealed partial class CargoHold
{
    internal void WriteState(SaveWriter writer)
    {
        writer.WriteInt(Count);
        writer.WriteArray(_parts);
    }

    internal void ReadState(SaveReader reader)
    {
        var count = reader.ReadInt();
        _parts = reader.ReadRequiredArray<HeldCargo>();
        if (count < 0 || count > _parts.Length)
        {
            throw reader.Fail("cargo hold count outside its buffer");
        }

        Count = count;
        if (!reader.Content.IsIdentityFor(ContentTable.Cargo))
        {
            for (var i = 0; i < count; i++)
            {
                _parts[i] = _parts[i] with { Cargo = reader.Content.Map(ContentTable.Cargo, _parts[i].Cargo) };
            }
        }
    }

    /// <summary>Reads a hold written by <see cref="WriteState"/> (null when none was written).</summary>
    internal static CargoHold? ReadOptional(SaveReader reader)
    {
        if (!reader.ReadBool())
        {
            return null;
        }

        var hold = new CargoHold();
        hold.ReadState(reader);
        return hold;
    }

    internal static void WriteOptional(SaveWriter writer, CargoHold? hold)
    {
        writer.WriteBool(hold is not null);
        hold?.WriteState(writer);
    }
}
