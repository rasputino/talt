namespace Talt.Core.Stations;

/// <summary>One cargo line of the station window (FR-025): figures of the station's group.</summary>
public readonly record struct StationCargoInfo(
    int Cargo,
    string CargoId,
    bool Served,
    int Rating,
    int Waiting,
    bool Accepted,
    bool Received,
    int ReceivedLastMonth,
    int PickedUpLastMonth,
    int DeliveredLastMonth,
    int LostLastMonth,
    int DaysSincePickup);

/// <summary>Read-only snapshot of a station for its window (contracts/stations-api.md). UI only: allocates.</summary>
public sealed record StationInfo(
    int Id,
    int Owner,
    StationKind Kind,
    int Town,
    StationNameKind NameKind,
    StationPlace Place,
    int NameNumber,
    string? CustomName,
    int WarehouseType,
    int PieceCount,
    int GroupStationCount,
    int GroupKey,
    IReadOnlyList<StationCargoInfo> Cargo);

public sealed partial class StationRegistry
{
    /// <summary>Snapshot for the station window: one line per cargo the group serves, receives, accepts or holds.</summary>
    public bool TryGetStation(int stationId, out StationInfo info)
    {
        var slot = SlotOf(stationId);
        if (slot < 0)
        {
            info = null!;
            return false;
        }

        EnsureCoverage();
        var group = _group[slot];
        ulong received = 0;
        foreach (var member in StationsInGroup(group))
        {
            received |= _supplyMask[SlotOf(member)];
        }

        var lines = new List<StationCargoInfo>();
        for (var c = 0; c < _cargoCount; c++)
        {
            var cell = (group * _cargoCount) + c;
            var accepted = (_groupAccept[group] & (1UL << c)) != 0;
            var receives = (received & (1UL << c)) != 0;
            if (_served[cell] == 0 && _waiting[cell] == 0 && !accepted && !receives && _deliveredLast[cell] == 0)
            {
                continue;
            }

            lines.Add(new StationCargoInfo(
                c,
                _content.Cargo(c).Id,
                _served[cell] != 0,
                _served[cell] != 0 ? _rating[cell] : -1,
                _waiting[cell],
                accepted,
                receives,
                _receivedLast[cell],
                _pickedUpLast[cell],
                _deliveredLast[cell],
                _lostLast[cell],
                _lastPickupDay[cell] < 0 ? -1 : _today - _lastPickupDay[cell]));
        }

        info = new StationInfo(
            _id[slot],
            _owner[slot],
            (StationKind)_kind[slot],
            _town[slot],
            (StationNameKind)_nameKind[slot],
            (StationPlace)_namePlace[slot],
            _nameNumber[slot],
            _customName[slot],
            _warehouseType[slot],
            _tiles[slot].Count,
            _groupStationCount[group],
            _groupKey[group],
            lines);
        return true;
    }
}
