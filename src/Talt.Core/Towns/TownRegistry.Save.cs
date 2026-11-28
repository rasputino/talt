using Talt.Core.Saves;

namespace Talt.Core.Towns;

public sealed partial class TownRegistry
{
    /// <summary>The <c>TOWN</c> section (feature 014): every town array, names, random streams, ratings and pending work, then the buildings.</summary>
    internal void WriteState(SaveWriter writer)
    {
        writer.WriteInt(Count);
        writer.WriteArray(_centreX);
        writer.WriteArray(_centreZ);
        writer.WriteArray(_siteIndex);
        writer.WriteArray(_pattern);
        foreach (var name in _names)
        {
            writer.WriteString(name);
        }

        writer.WriteRandom(_random);
        foreach (var stream in _townRandom)
        {
            writer.WriteBool(stream is not null);
            if (stream is not null)
            {
                writer.WriteRandom(stream);
            }
        }

        writer.WriteArray(_population);
        writer.WriteArray(_buildingCount);
        writer.WriteArray(_passengerProduction);
        writer.WriteArray(_mailProduction);
        writer.WriteArray(_goodsAcceptance);
        writer.WriteArray(_foodAcceptance);
        writer.WriteArray(_growthPoints);
        writer.WriteArray(_passengersGeneratedLastMonth);
        writer.WriteArray(_mailGeneratedLastMonth);
        writer.WriteArray(_passengersTransportedThisMonth);
        writer.WriteArray(_mailTransportedThisMonth);
        writer.WriteArray(_passengersTransportedLastMonth);
        writer.WriteArray(_mailTransportedLastMonth);
        writer.WriteArray(_serviceLastMonth);
        writer.WriteArray(_ratings);
        writer.WriteInt(_companyCapacity);
        writer.WriteInt(_year);
        writer.WriteInt(Version);
        writer.WriteArray(_penalty);
        writer.WriteList(_penalized);
        writer.WriteList(_removedBuildings);
        writer.WriteList(_zoneTowns);
        writer.WriteBool(_founding);
        writer.WriteInt(_segmentCount);
        writer.WriteBool(_networkPending);
        Buildings.WriteState(writer);
    }

    internal void ReadState(SaveReader reader)
    {
        if (reader.ReadInt() != Count)
        {
            throw reader.Fail("town count differs from the map's town sites");
        }

        reader.ReadInto<int>(_centreX);
        reader.ReadInto<int>(_centreZ);
        reader.ReadInto<int>(_siteIndex);
        reader.ReadInto<byte>(_pattern);
        for (var i = 0; i < _names.Length; i++)
        {
            _names[i] = reader.ReadString()!;
        }

        reader.ReadRandomInto(_random);
        for (var i = 0; i < _townRandom.Length; i++)
        {
            _townRandom[i] = reader.ReadBool() ? reader.ReadRandom() : null!;
        }

        reader.ReadInto<int>(_population);
        reader.ReadInto<int>(_buildingCount);
        reader.ReadInto<int>(_passengerProduction);
        reader.ReadInto<int>(_mailProduction);
        reader.ReadInto<int>(_goodsAcceptance);
        reader.ReadInto<int>(_foodAcceptance);
        reader.ReadInto<int>(_growthPoints);
        reader.ReadInto<int>(_passengersGeneratedLastMonth);
        reader.ReadInto<int>(_mailGeneratedLastMonth);
        reader.ReadInto<int>(_passengersTransportedThisMonth);
        reader.ReadInto<int>(_mailTransportedThisMonth);
        reader.ReadInto<int>(_passengersTransportedLastMonth);
        reader.ReadInto<int>(_mailTransportedLastMonth);
        reader.ReadInto<int>(_serviceLastMonth);
        _ratings = reader.ReadRequiredArray<int>();
        _companyCapacity = reader.ReadInt(1, int.MaxValue);
        _year = reader.ReadInt();
        Version = reader.ReadInt();
        _penalty = reader.ReadArray<int>();
        reader.ReadListInto(_penalized);
        reader.ReadListInto(_removedBuildings);
        reader.ReadListInto(_zoneTowns);
        _founding = reader.ReadBool();
        _segmentCount = reader.ReadInt();
        _networkPending = reader.ReadBool();
        Buildings.ReadState(reader);
    }
}

public sealed partial class TownBuildings
{
    internal void WriteState(SaveWriter writer)
    {
        writer.WriteInt(Count);
        writer.WriteInt(_nextId);
        writer.WriteArray(_id);
        writer.WriteArray(_type);
        writer.WriteArray(_x);
        writer.WriteArray(_z);
        writer.WriteArray(_town);
        writer.WriteArray(_year);
        writer.WriteArray(_baseHeight);
        writer.WriteDictionary(_buildingAtTile);
        writer.WriteDictionary(_slotById);
        writer.WriteList(_changedTiles);
    }

    internal void ReadState(SaveReader reader)
    {
        var count = reader.ReadInt();
        _nextId = reader.ReadInt();
        _id = reader.ReadRequiredArray<int>();
        _type = reader.ReadRequiredArray<int>();
        _x = reader.ReadRequiredArray<int>();
        _z = reader.ReadRequiredArray<int>();
        _town = reader.ReadRequiredArray<int>();
        _year = reader.ReadRequiredArray<int>();
        _baseHeight = reader.ReadRequiredArray<int>();
        if (count < 0 || count > _id.Length || _id.Length != _type.Length || _id.Length != _x.Length || _id.Length != _z.Length
            || _id.Length != _town.Length || _id.Length != _year.Length || _id.Length != _baseHeight.Length)
        {
            throw reader.Fail("town building arrays are inconsistent");
        }

        Count = count;
        reader.Content.MapInPlace(ContentTable.BuildingType, _type.AsSpan(0, count));
        reader.ReadDictionaryInto(_buildingAtTile);
        reader.ReadDictionaryInto(_slotById);
        reader.ReadListInto(_changedTiles);
    }
}
