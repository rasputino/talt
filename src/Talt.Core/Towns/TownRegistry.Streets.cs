using Talt.Core.Construction;
using Talt.Core.Roads;

namespace Talt.Core.Towns;

/// <summary>
/// Street extension (research R6, FR-012): when plots run out a town lays a short street segment outward from
/// one of its own road pieces, following its grid or organic pattern and the feature 007 terrain and ramp
/// rules. Towns never build bridges or tunnels, never terraform and never change or remove others' pieces;
/// a new segment may end by connecting to another road.
/// </summary>
public sealed partial class TownRegistry
{
    private const int StreetAttempts = 24;
    private const int MaxSegment = 64;

    private readonly int[] _segmentTiles = new int[(MaxSegment * 2) + 4];
    private readonly byte[] _segmentLinks = new byte[(MaxSegment * 2) + 4];
    private int _segmentCount;
    private bool _networkPending;

    /// <summary>Tries to lay one street segment for a town; true when road pieces were added.</summary>
    private bool TryExtendStreet(int town)
    {
        var type = _state.RoadRules.BestOrdinaryType(_state.Year);
        if (type < 0)
        {
            return false;
        }

        var random = _townRandom[town];
        var owner = TileOwner.Town(town);
        var radius = RadiusOf(town) + 2;
        var growth = Rules.Growth;
        var grid = (StreetPattern)_pattern[town] == StreetPattern.Grid;
        var roads = _state.Roads;
        for (var attempt = 0; attempt < StreetAttempts; attempt++)
        {
            var x = _centreX[town] + random.NextInt(-radius, radius + 1);
            var z = _centreZ[town] + random.NextInt(-radius, radius + 1);
            if (!_map.Contains(x, z))
            {
                continue;
            }

            var tile = _map.Index(x, z);
            if (!roads.HasRoad(tile) || _state.Objects.OwnerAt(tile) != owner || !OrdinaryPiece(roads.PieceAt(tile)))
            {
                continue;
            }

            var first = random.NextInt(4) * 2;
            for (var k = 0; k < 4; k++)
            {
                var direction = (first + (k * 2)) & 7;
                if (roads.PieceAt(tile).HasLink(direction))
                {
                    continue;
                }

                int length;
                if (grid)
                {
                    var alongX = Dir8.OffsetX(direction) != 0;
                    if ((alongX ? z - _centreZ[town] : x - _centreX[town]) % growth.GridSpacing != 0)
                    {
                        continue;
                    }

                    length = growth.GridSpacing;
                }
                else
                {
                    length = random.NextInt(growth.StreetSegmentMin, growth.StreetSegmentMax + 1);
                }

                if (TryBuildSegment(town, tile, direction, Math.Min(length, MaxSegment), grid, type))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Plans a straight (organic: possibly once-turning) segment, then writes it in one batch.</summary>
    private bool TryBuildSegment(int town, int start, int direction, int length, bool grid, int type)
    {
        _segmentCount = 0;
        var random = _townRandom[town];
        var turnAt = !grid && length > 2 && random.NextInt(100) < 30 ? random.NextInt(1, length) : -1;
        var turn = random.NextInt(2) == 0 ? 2 : -2;
        var limit = RadiusOf(town) + 8;
        var current = start;
        var added = 0;
        for (var step = 0; step < length; step++)
        {
            if (step == turnAt)
            {
                direction = Dir8.Rotate(direction, turn);
            }

            var cx = current % _map.Width;
            var cz = current / _map.Width;
            var nx = cx + Dir8.OffsetX(direction);
            var nz = cz + Dir8.OffsetZ(direction);
            if (!_map.Contains(nx, nz)
                || Math.Max(Math.Abs(nx - _centreX[town]), Math.Abs(nz - _centreZ[town])) > limit)
            {
                break;
            }

            var next = _map.Index(nx, nz);
            if (Math.Abs(_map.Heights[current] - _map.Heights[next]) > 1)
            {
                break;
            }

            var back = Dir8.Opposite(direction);
            var currentLinks = (byte)(PlannedLinks(current) | Dir8.Bit(direction));
            var nextLinks = (byte)(PlannedLinks(next) | Dir8.Bit(back));
            var joins = _state.Roads.HasRoad(next);
            if (joins)
            {
                if (!OrdinaryPiece(_state.Roads.PieceAt(next)))
                {
                    break;
                }
            }
            else if (!StreetTileFree(next) || (!grid && step > 0 && ParallelRoadBeside(next, direction, current)))
            {
                break;
            }

            if (!RoadSeeder.ShapeAllows(_map, current, currentLinks) || !RoadSeeder.ShapeAllows(_map, next, nextLinks))
            {
                break;
            }

            Plan(current, Dir8.Bit(direction));
            Plan(next, Dir8.Bit(back));
            added++;
            if (joins)
            {
                break; // Connected to an existing road: the segment ends here.
            }

            current = next;
        }

        if (added == 0)
        {
            return false;
        }

        ApplySegment(town, type);
        return true;
    }

    /// <summary>True when a road piece may be extended or connected to by a town: a plain, non-motorway piece.</summary>
    private bool OrdinaryPiece(in RoadPiece piece) =>
        piece.HasRoad
        && (piece.Flags & (RoadFeatures.LongLinkEnd | RoadFeatures.Connector)) == 0
        && !_state.RoadRules.IsMotorway(piece.TypeIndex);

    /// <summary>A tile a new street piece may use: land, nothing on it, nothing passing, no industry.</summary>
    private bool StreetTileFree(int tile)
    {
        var x = tile % _map.Width;
        var z = tile / _map.Width;
        return !_map.IsWater(tile)
            && _state.Objects.KindAt(tile) == TileObjectKind.None
            && (_state.Roads.FlagsAt(tile) & RoadFeatures.Passing) == 0
            && !_state.IsIndustry(x, z);
    }

    /// <summary>Organic streets keep one tile between parallel streets: no road on either side of the new tile.</summary>
    private bool ParallelRoadBeside(int tile, int direction, int from)
    {
        var x = tile % _map.Width;
        var z = tile / _map.Width;
        for (var side = -2; side <= 2; side += 4)
        {
            var d = Dir8.Rotate(direction, side);
            var sx = x + Dir8.OffsetX(d);
            var sz = z + Dir8.OffsetZ(d);
            if (_map.Contains(sx, sz) && _map.Index(sx, sz) != from && _state.Roads.HasRoad(_map.Index(sx, sz)))
            {
                return true;
            }
        }

        return false;
    }

    private byte PlannedLinks(int tile)
    {
        var links = _state.Roads.LinksAt(tile);
        for (var i = 0; i < _segmentCount; i++)
        {
            if (_segmentTiles[i] == tile)
            {
                links |= _segmentLinks[i];
            }
        }

        return links;
    }

    private void Plan(int tile, byte link)
    {
        for (var i = 0; i < _segmentCount; i++)
        {
            if (_segmentTiles[i] == tile)
            {
                _segmentLinks[i] |= link;
                return;
            }
        }

        _segmentTiles[_segmentCount] = tile;
        _segmentLinks[_segmentCount] = link;
        _segmentCount++;
    }

    /// <summary>Writes the planned links; new pieces belong to the town. Updates the network unless founding.</summary>
    private void ApplySegment(int town, int type)
    {
        var tiles = _segmentTiles.AsSpan(0, _segmentCount);
        if (!_founding)
        {
            _state.Network.BeforeChange(tiles);
        }

        var owner = TileOwner.Town(town);
        for (var i = 0; i < _segmentCount; i++)
        {
            var tile = _segmentTiles[i];
            var piece = _state.Roads.PieceAt(tile);
            var links = _segmentLinks[i];
            var updated = piece with
            {
                Links = (byte)(piece.Links | links),
                Exits = (byte)(piece.Exits | links),
                Type = piece.HasRoad ? piece.Type : (byte)(type + 1),
            };
            _state.PlaceTownRoad(tile, updated, owner, newPiece: !piece.HasRoad, notify: !_founding);
        }

        if (!_founding)
        {
            // Rebuilt once after the month's growth (AdvanceMonth), not per segment.
            _state.Network.MarkChanged(tiles);
            _networkPending = true;
        }

        Version++;
    }
}
