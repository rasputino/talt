using Talt.Core.Construction;
using Talt.Core.Roads;

namespace Talt.Core.Stations;

/// <summary>
/// Adding and removing station pieces and warehouses after their construction plan was committed, automatic
/// names (research R11) and renaming. Called from the station commands' <c>Committed</c> step, so the tiles
/// already show the new state; directions and joins are read back from them.
/// </summary>
public sealed partial class StationRegistry
{
    /// <summary>True when the tile holds a station piece.</summary>
    public bool HasPiece(int tile) => _pieces.ContainsKey(tile);

    /// <summary>
    /// True when a road change on <paramref name="tile"/> to <paramref name="newPiece"/> would break a station
    /// (research R2): the road under a drive-through stop may not lose or gain links, and a road tile a bay
    /// connects to must keep its link to the bay.
    /// </summary>
    public bool RoadChangeBlocked(int tile, in RoadPiece newPiece)
    {
        if (_pieces.Count == 0)
        {
            return false;
        }

        if (_pieces.TryGetValue(tile, out var own))
        {
            if (own.Type == WarehousePiece)
            {
                return false;
            }

            var type = Rules.Pieces[own.Type];
            if (type.Form == StationPieceForm.DriveThrough)
            {
                return !newPiece.HasRoad || newPiece.Links != _state.Roads.LinksAt(tile);
            }

            return false; // A bay tile's own road changes only through the station tool.
        }

        var x = tile % _map.Width;
        var z = tile / _map.Width;
        for (var d = 0; d < 8; d += 2)
        {
            var nx = x + Dir8.OffsetX(d);
            var nz = z + Dir8.OffsetZ(d);
            if (!_map.Contains(nx, nz) || !_pieces.TryGetValue(_map.Index(nx, nz), out var piece) || piece.Type == WarehousePiece)
            {
                continue;
            }

            if (Rules.Pieces[piece.Type].Form == StationPieceForm.Bay && piece.Direction == Dir8.Opposite(d)
                && (!newPiece.HasRoad || !newPiece.HasLink(d)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The company station a new road piece on (x, z) would join: the station with a piece on one of the eight
    /// neighbours (research R3). Returns −2 when two different stations touch the tile, −1 for none.
    /// </summary>
    public int JoinTarget(int companyId, int x, int z)
    {
        var found = -1;
        for (var d = 0; d < 8; d++)
        {
            var nx = x + Dir8.OffsetX(d);
            var nz = z + Dir8.OffsetZ(d);
            if (!_map.Contains(nx, nz) || !_pieces.TryGetValue(_map.Index(nx, nz), out var piece) || piece.Type == WarehousePiece)
            {
                continue;
            }

            var slot = SlotOf(piece.StationId);
            if (_owner[slot] != companyId)
            {
                continue;
            }

            if (found >= 0 && found != piece.StationId)
            {
                return -2;
            }

            found = piece.StationId;
        }

        return found;
    }

    /// <summary>Records a committed road station piece (the bay's road piece, or the road under a drive-through, is already on the tile).</summary>
    internal int AddRoadPiece(int companyId, int pieceType, int x, int z)
    {
        var tile = _map.Index(x, z);
        var type = Rules.Pieces[pieceType];
        var links = _state.Roads.LinksAt(tile);
        int direction;
        if (type.Form == StationPieceForm.Bay)
        {
            direction = System.Numerics.BitOperations.TrailingZeroCount(links);
        }
        else
        {
            direction = (links & Dir8.Bit(Dir8.N)) != 0 ? Dir8.N : Dir8.E;
        }

        var join = JoinTarget(companyId, x, z);
        int slot;
        if (join >= 0)
        {
            slot = SlotOf(join);
            _x0[slot] = Math.Min(_x0[slot], x);
            _z0[slot] = Math.Min(_z0[slot], z);
            _x1[slot] = Math.Max(_x1[slot], x);
            _z1[slot] = Math.Max(_z1[slot], z);
        }
        else
        {
            var kind = type.Kind == StationPieceKind.Bus ? StationNameKind.BusStop : StationNameKind.TruckStop;
            slot = CreateStation(companyId, StationKind.Road, 0, x, z, x, z, kind);
        }

        _tiles[slot].Add(tile);
        _categories[slot] |= type.CategoryMask;
        _radius[slot] = Math.Max(_radius[slot], type.CatchmentRadius);
        _pieces[tile] = new StationPiece(_id[slot], (byte)pieceType, (byte)direction);
        AfterStationChange(slot);
        return _id[slot];
    }

    /// <summary>
    /// Records committed rail platform tiles (feature 015, research R10): the rectangle (x0, z0)–(x1, z1) joins the
    /// company's adjacent station or forms a new rail station. The track is already on the tiles.
    /// </summary>
    internal int AddRailPieces(int companyId, int pieceType, int x0, int z0, int x1, int z1, int axis)
    {
        var type = Rules.Pieces[pieceType];
        var join = -1;
        for (var z = z0; z <= z1 && join < 0; z++)
        {
            for (var x = x0; x <= x1 && join < 0; x++)
            {
                join = JoinTarget(companyId, x, z);
            }
        }

        int slot;
        if (join >= 0)
        {
            slot = SlotOf(join);
            _x0[slot] = Math.Min(_x0[slot], x0);
            _z0[slot] = Math.Min(_z0[slot], z0);
            _x1[slot] = Math.Max(_x1[slot], x1);
            _z1[slot] = Math.Max(_z1[slot], z1);
            if (_kind[slot] == (byte)StationKind.Road)
            {
                _kind[slot] = (byte)StationKind.Rail; // A road station joined by platforms becomes a rail station.
            }
        }
        else
        {
            slot = CreateStation(companyId, StationKind.Rail, 0, x0, z0, x1, z1, StationNameKind.RailStation);
        }

        var direction = axis == 0 ? Dir8.E : Dir8.S;
        for (var z = z0; z <= z1; z++)
        {
            for (var x = x0; x <= x1; x++)
            {
                var tile = _map.Index(x, z);
                _tiles[slot].Add(tile);
                _pieces[tile] = new StationPiece(_id[slot], (byte)pieceType, (byte)direction);
            }
        }

        _categories[slot] |= type.CategoryMask;
        _radius[slot] = Math.Max(_radius[slot], type.CatchmentRadius);
        AfterStationChange(slot);
        return _id[slot];
    }

    /// <summary>Records a committed warehouse whose footprint's minimum corner is (x, z).</summary>
    internal int AddWarehouse(int companyId, int warehouseType, int x, int z)
    {
        var type = Rules.Warehouses[warehouseType];
        var slot = CreateStation(companyId, StationKind.Warehouse, warehouseType, x, z, x + type.Width - 1, z + type.Depth - 1, StationNameKind.Warehouse);
        for (var dz = 0; dz < type.Depth; dz++)
        {
            for (var dx = 0; dx < type.Width; dx++)
            {
                var tile = _map.Index(x + dx, z + dz);
                _tiles[slot].Add(tile);
                _pieces[tile] = new StationPiece(_id[slot], WarehousePiece, 0);
            }
        }

        AfterStationChange(slot);
        return _id[slot];
    }

    /// <summary>
    /// Removes the piece on (x, z) (a whole warehouse for a warehouse tile). The last piece removes the station;
    /// its group's state moves to what remains (research R4).
    /// </summary>
    internal void RemovePieceAt(int x, int z)
    {
        var tile = _map.Index(x, z);
        if (!_pieces.TryGetValue(tile, out var piece))
        {
            return;
        }

        var slot = SlotOf(piece.StationId);
        UnregisterGrid(slot);
        if (piece.Type == WarehousePiece)
        {
            foreach (var t in _tiles[slot])
            {
                _pieces.Remove(t);
            }

            _tiles[slot].Clear();
        }
        else
        {
            _pieces.Remove(tile);
            _tiles[slot].Remove(tile);
        }

        if (_tiles[slot].Count == 0)
        {
            RemoveStationSlot(slot);
        }
        else
        {
            RecomputeShape(slot);
            RegisterGrid(slot);
            _dirty[slot] = true;
        }

        _coverageChanged = true;
        RecomputeGroups();
        Version++;
    }

    /// <summary>Gives a station a custom name; an empty name restores the automatic one.</summary>
    internal void Rename(int stationId, string? name)
    {
        var slot = SlotOf(stationId);
        if (slot < 0)
        {
            return;
        }

        var trimmed = name?.Trim();
        _customName[slot] = string.IsNullOrEmpty(trimmed) ? null : trimmed;
        Version++;
    }

    private int CreateStation(int companyId, StationKind kind, int warehouseType, int x0, int z0, int x1, int z1, StationNameKind nameKind)
    {
        EnsureStationCapacity(_count + 1);
        var slot = _count++;
        var id = _nextId++;
        _history.AddStation(slot, _id.Length);
        _id[slot] = id;
        _owner[slot] = companyId;
        _kind[slot] = (byte)kind;
        _warehouseType[slot] = (byte)warehouseType;
        _x0[slot] = x0;
        _z0[slot] = z0;
        _x1[slot] = x1;
        _z1[slot] = z1;
        _categories[slot] = 0;
        _radius[slot] = 0;
        _group[slot] = -1;
        _customName[slot] = null;
        _tiles[slot] = [];
        ResetCoverage(slot);

        var centreX = (x0 + x1) / 2;
        var centreZ = (z0 + z1) / 2;
        var town = NearestTown(centreX, centreZ);
        var place = StationPlace.Central;
        if (nameKind is StationNameKind.Warehouse or StationNameKind.RailStation && town >= 0)
        {
            var (cx, cz) = _towns!.CentreOf(town);
            var dx = centreX - cx;
            var dz = centreZ - cz;
            if (Math.Max(Math.Abs(dx), Math.Abs(dz)) > _towns.RadiusOf(town) / 2)
            {
                place = (StationPlace)(1 + Dir8.Octant(dx, dz));
            }
        }

        var counterKey = ((long)companyId << 40) | ((long)(town + 1) << 8) | (((long)nameKind) << 4) | (long)place;
        _nameCounters.TryGetValue(counterKey, out var used);
        _nameCounters[counterKey] = used + 1;
        _town[slot] = town;
        _nameKind[slot] = (byte)nameKind;
        _namePlace[slot] = (byte)place;
        _nameNumber[slot] = used + 1;
        _slotById.Add(id, slot);
        return slot;
    }

    private void AfterStationChange(int slot)
    {
        UnregisterGrid(slot);
        RegisterGrid(slot);
        _dirty[slot] = true;
        _coverageChanged = true;
        if (!HasWarehouse(_owner[slot]))
        {
            // Without warehouses every station is its own group: a new station appends one, a join changes none.
            if (_group[slot] < 0)
            {
                AppendGroup(slot);
            }
        }
        else
        {
            RecomputeGroups();
        }

        Version++;
    }

    private bool HasWarehouse(int companyId)
    {
        for (var slot = 0; slot < _count; slot++)
        {
            if (_kind[slot] == (byte)StationKind.Warehouse && _owner[slot] == companyId)
            {
                return true;
            }
        }

        return false;
    }

    private int NearestTown(int x, int z)
    {
        if (_towns is null || _towns.Count == 0)
        {
            return -1;
        }

        var best = -1;
        var bestDistance = long.MaxValue;
        for (var town = 0; town < _towns.Count; town++)
        {
            var (cx, cz) = _towns.CentreOf(town);
            var distance = ((long)(x - cx) * (x - cx)) + ((long)(z - cz) * (z - cz));
            if (distance < bestDistance)
            {
                best = town;
                bestDistance = distance;
            }
        }

        return best;
    }

    /// <summary>Bounding box, categories and radius from the remaining pieces of a road station.</summary>
    private void RecomputeShape(int slot)
    {
        int x0 = int.MaxValue, z0 = int.MaxValue, x1 = int.MinValue, z1 = int.MinValue;
        var categories = 0;
        var radius = 0;
        foreach (var tile in _tiles[slot])
        {
            var x = tile % _map.Width;
            var z = tile / _map.Width;
            x0 = Math.Min(x0, x);
            z0 = Math.Min(z0, z);
            x1 = Math.Max(x1, x);
            z1 = Math.Max(z1, z);
            var piece = _pieces[tile];
            if (piece.Type != WarehousePiece)
            {
                categories |= Rules.Pieces[piece.Type].CategoryMask;
                radius = Math.Max(radius, Rules.Pieces[piece.Type].CatchmentRadius);
            }
        }

        (_x0[slot], _z0[slot], _x1[slot], _z1[slot]) = (x0, z0, x1, z1);
        _categories[slot] = categories;
        _radius[slot] = radius;
    }

    /// <summary>Removes a station with no pieces left (swap with the last slot).</summary>
    private void RemoveStationSlot(int slot)
    {
        // Feature 011 (research R3): cargo picked up here may still be delivered, paid by distance from here.
        _removedCentre[_id[slot]] = PackCentre((_x0[slot] + _x1[slot]) / 2, (_z0[slot] + _z1[slot]) / 2);
        _slotById.Remove(_id[slot]);
        var last = _count - 1;
        if (slot != last)
        {
            UnregisterGrid(last);
            _id[slot] = _id[last];
            _owner[slot] = _owner[last];
            _kind[slot] = _kind[last];
            _warehouseType[slot] = _warehouseType[last];
            _x0[slot] = _x0[last];
            _z0[slot] = _z0[last];
            _x1[slot] = _x1[last];
            _z1[slot] = _z1[last];
            _town[slot] = _town[last];
            _nameKind[slot] = _nameKind[last];
            _namePlace[slot] = _namePlace[last];
            _nameNumber[slot] = _nameNumber[last];
            _customName[slot] = _customName[last];
            _categories[slot] = _categories[last];
            _radius[slot] = _radius[last];
            _group[slot] = _group[last];
            _tiles[slot] = _tiles[last];
            MoveCoverage(last, slot);
            _slotById[_id[slot]] = slot;
            RegisterGrid(slot);
        }

        _history.RemoveStation(slot, last);
        _tiles[last] = null!;
        _customName[last] = null;
        _count = last;
    }
}
