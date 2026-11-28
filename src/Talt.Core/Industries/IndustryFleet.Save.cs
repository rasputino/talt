using Talt.Core.Saves;

namespace Talt.Core.Industries;

public sealed partial class IndustryFleet
{
    /// <summary>
    /// The <c>INDY</c> section (feature 014): every industry array (full length), free sites, tile owners, slots, the
    /// random stream and the version. Site tables and recipe lookups are rebuilt from the map and the content (research R2).
    /// </summary>
    internal void WriteState(SaveWriter writer)
    {
        writer.WriteInt(_cargoCount);
        writer.WriteInt(_count);
        writer.WriteInt(_freeCount);
        writer.WriteInt(_nextId);
        writer.WriteInt(_version);
        writer.WriteArray(_kind);
        writer.WriteArray(_id);
        writer.WriteArray(_x);
        writer.WriteArray(_z);
        writer.WriteArray(_ratePerMille);
        writer.WriteArray(_monthsNeglected);
        writer.WriteArray(_siteIndex);
        writer.WriteArray(_town);
        writer.WriteArray(_deliveredThisMonth);
        writer.WriteArray(_available);
        writer.WriteArray(_pendingInput);
        writer.WriteArray(_acceptedThisMonth);
        writer.WriteArray(_acceptedLastMonth);
        writer.WriteArray(_shippedThisMonth);
        writer.WriteArray(_shippedLastMonth);
        writer.WriteArray(_siteOccupied);
        writer.WriteArray(_freeSites);
        writer.WriteDictionary(_tileOwner);
        writer.WriteDictionary(_slotById);
        writer.WriteRandom(_random);
    }

    internal void ReadState(SaveReader reader)
    {
        if (reader.ReadInt() != reader.Content.SavedCount(ContentTable.Cargo, _cargoCount))
        {
            throw reader.Fail("industry cargo count differs from the installed cargo types");
        }

        var count = reader.ReadInt();
        var freeCount = reader.ReadInt();
        _nextId = reader.ReadInt();
        _version = reader.ReadInt();
        _kind = reader.ReadRequiredArray<int>();
        _id = reader.ReadRequiredArray<int>();
        _x = reader.ReadRequiredArray<int>();
        _z = reader.ReadRequiredArray<int>();
        _ratePerMille = reader.ReadRequiredArray<int>();
        _monthsNeglected = reader.ReadRequiredArray<int>();
        _siteIndex = reader.ReadRequiredArray<int>();
        _town = reader.ReadRequiredArray<int>();
        _deliveredThisMonth = reader.ReadRequiredArray<int>();
        _available = reader.Content.Rows(ContentTable.Cargo, reader.ReadRequiredArray<int>());
        _pendingInput = reader.Content.Rows(ContentTable.Cargo, reader.ReadRequiredArray<int>());
        _acceptedThisMonth = reader.Content.Rows(ContentTable.Cargo, reader.ReadRequiredArray<int>());
        _acceptedLastMonth = reader.Content.Rows(ContentTable.Cargo, reader.ReadRequiredArray<int>());
        _shippedThisMonth = reader.Content.Rows(ContentTable.Cargo, reader.ReadRequiredArray<int>());
        _shippedLastMonth = reader.Content.Rows(ContentTable.Cargo, reader.ReadRequiredArray<int>());
        reader.ReadInto<bool>(_siteOccupied);
        reader.ReadInto<int>(_freeSites);
        reader.ReadDictionaryInto(_tileOwner);
        reader.ReadDictionaryInto(_slotById);
        reader.ReadRandomInto(_random);
        var capacity = _kind.Length;
        if (count < 0 || count > capacity || freeCount < 0 || freeCount > _freeSites.Length
            || _id.Length != capacity || _x.Length != capacity || _z.Length != capacity || _ratePerMille.Length != capacity
            || _monthsNeglected.Length != capacity || _siteIndex.Length != capacity || _town.Length != capacity || _deliveredThisMonth.Length != capacity
            || _available.Length < count * _cargoCount)
        {
            throw reader.Fail("industry arrays are inconsistent");
        }

        _count = count;
        _freeCount = freeCount;
        reader.Content.MapInPlace(ContentTable.IndustryKind, _kind.AsSpan(0, count));
    }
}
