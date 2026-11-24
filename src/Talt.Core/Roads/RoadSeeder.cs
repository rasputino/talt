using Talt.Core.Construction;
using Talt.Core.Maps;

namespace Talt.Core.Roads;

/// <summary>
/// Lays the starting roads of a game with a map (research R9, FR-060): a small street grid on every town
/// site, owned by that town, plus an optional debug grid over the whole map (performance checks). Runs once
/// at game creation, deterministically, before the network is built.
/// </summary>
public static class RoadSeeder
{
    /// <summary>Street lines at these offsets from a town site's centre, in both axes.</summary>
    private static readonly int[] StreetOffsets = [-4, 0, 4];

    private const int StreetHalfLength = 4;

    /// <summary>Places the street grids of all town sites. Returns the number of road tiles placed.</summary>
    public static int SeedTowns(ConstructionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var type = state.RoadRules.BestOrdinaryType(state.Year);
        if (type < 0)
        {
            return 0;
        }

        var before = state.Roads.RoadTileCount;
        var town = 0;
        foreach (var site in state.Map.Sites)
        {
            if (site.Kind != SiteKind.Town)
            {
                continue;
            }

            if (town > TileOwner.MaxIndex)
            {
                break;
            }

            var owner = TileOwner.Town(town++);
            foreach (var offset in StreetOffsets)
            {
                // A street along X at z = centre + offset, and one along Z at x = centre + offset.
                for (var i = -StreetHalfLength; i < StreetHalfLength; i++)
                {
                    TryLink(state, site.X + i, site.Z + offset, Dir8.E, type, owner);
                    TryLink(state, site.X + offset, site.Z + i, Dir8.S, type, owner);
                }
            }
        }

        return state.Roads.RoadTileCount - before;
    }

    /// <summary>
    /// Debug and benchmark helper: a full grid of two-way roads every <paramref name="spacing"/> tiles over
    /// the whole map (where the terrain allows), owned by town 0.
    /// </summary>
    public static int SeedGrid(ConstructionState state, int spacing, int type)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentOutOfRangeException.ThrowIfLessThan(spacing, 2);
        var map = state.Map;
        var owner = TileOwner.Town(0);
        var before = state.Roads.RoadTileCount;
        for (var z = spacing / 2; z < map.Depth; z += spacing)
        {
            for (var x = 0; x + 1 < map.Width; x++)
            {
                TryLink(state, x, z, Dir8.E, type, owner);
            }
        }

        for (var x = spacing / 2; x < map.Width; x += spacing)
        {
            for (var z = 0; z + 1 < map.Depth; z++)
            {
                TryLink(state, x, z, Dir8.S, type, owner);
            }
        }

        return state.Roads.RoadTileCount - before;
    }

    /// <summary>Links two neighbouring tiles when the terrain, objects and the shape rules allow it.</summary>
    private static void TryLink(ConstructionState state, int x, int z, int direction, int type, TileOwner owner)
    {
        var map = state.Map;
        var nx = x + Dir8.OffsetX(direction);
        var nz = z + Dir8.OffsetZ(direction);
        if (!map.Contains(x, z) || !map.Contains(nx, nz))
        {
            return;
        }

        var a = map.Index(x, z);
        var b = map.Index(nx, nz);
        if (!Usable(state, a, owner) || !Usable(state, b, owner))
        {
            return;
        }

        var ha = map.Heights[a];
        var hb = map.Heights[b];
        if (Math.Abs(ha - hb) > 1)
        {
            return;
        }

        var roads = state.Roads;
        var back = Dir8.Opposite(direction);
        var pa = roads.PieceAt(a);
        var pb = roads.PieceAt(b);
        var linksA = (byte)(pa.Links | Dir8.Bit(direction));
        var linksB = (byte)(pb.Links | Dir8.Bit(back));
        if (!ShapeAllows(map, a, linksA) || !ShapeAllows(map, b, linksB))
        {
            return;
        }

        // Neighbours that are ramps towards a or b must stay straight too.
        state.PlaceInitialRoad(a, pa with { Links = linksA, Exits = (byte)(pa.Exits | Dir8.Bit(direction)), Type = pa.HasRoad ? pa.Type : (byte)(type + 1) }, owner);
        state.PlaceInitialRoad(b, pb with { Links = linksB, Exits = (byte)(pb.Exits | Dir8.Bit(back)), Type = pb.HasRoad ? pb.Type : (byte)(type + 1) }, owner);
    }

    private static bool Usable(ConstructionState state, int tile, TileOwner owner)
    {
        var map = state.Map;
        if (map.IsWater(tile) || state.IsIndustry(tile % map.Width, tile / map.Width))
        {
            return false;
        }

        var kind = state.Objects.KindAt(tile);
        return kind == TileObjectKind.None || (kind == TileObjectKind.Road && state.Objects.OwnerAt(tile) == owner);
    }

    /// <summary>
    /// The ramp rule of research R3 on the layer: a piece with a straight link to a tile one step higher is
    /// straight; a lower linked neighbour must be straight itself (checked when that link is added from its side).
    /// </summary>
    internal static bool ShapeAllows(MapData map, int tile, byte links)
    {
        var h = map.Heights[tile];
        var width = map.Width;
        var x = tile % width;
        var z = tile / width;
        var ramp = -1;
        for (var d = 0; d < 8; d += 2)
        {
            if ((links & (1 << d)) == 0)
            {
                continue;
            }

            var nx = x + Dir8.OffsetX(d);
            var nz = z + Dir8.OffsetZ(d);
            if (map.Contains(nx, nz) && map.HeightAt(nx, nz) == h + 1)
            {
                if (ramp >= 0)
                {
                    return false;
                }

                ramp = d;
            }
        }

        if (ramp < 0)
        {
            return true;
        }

        var allowed = Dir8.Bit(ramp) | Dir8.Bit(Dir8.Opposite(ramp));
        if ((links & ~allowed) != 0)
        {
            return false;
        }

        return true;
    }
}
