using System.Numerics;
using Talt.Core.Construction;
using Talt.Core.Maps;
using Talt.Core.Roads;
using Talt.Core.Stations;

namespace Talt.Client.Rendering;

/// <summary>
/// Simple placeholder models for stations (feature 010, FR-026), as boxes in the industry placeholder
/// instances: a bay is a platform with a shelter at its back, a drive-through stop two kerb shelters along the
/// road, a warehouse a shed over its footprint. Colours come from <c>stations.json</c>. Feature 018 replaces
/// them with final models.
/// </summary>
internal static class StationPlaceholders
{
    private const int BoxesPerPiece = 2;

    /// <summary>Upper bound of the boxes <see cref="Build"/> writes.</summary>
    public static int BoxCount(StationRegistry stations) => stations.PieceCount * BoxesPerPiece;

    /// <summary>Writes the boxes from <paramref name="index"/> on and returns the next free index.</summary>
    public static int Build(StationRegistry stations, ConstructionState state, MapData map, IndustryInstance[] into, int index)
    {
        var width = map.Width;
        for (var slot = 0; slot < stations.Count; slot++)
        {
            if (stations.KindAt(slot) == StationKind.Warehouse)
            {
                var type = stations.Rules.Warehouses[stations.WarehouseTypeAt(slot)];
                var (x0, z0, x1, z1) = stations.BoundsAt(slot);
                var ground = map.HeightAt(x0, z0);
                var w = x1 - x0 + 1;
                var d = z1 - z0 + 1;
                var cx = x0 + (w / 2f);
                var cz = z0 + (d / 2f);
                into[index++] = Box(cx, ground, cz, w - 0.3f, 2.2f, d - 0.3f, type.BaseColor);
                into[index++] = Box(cx, ground + 2.2f, cz, w - 0.1f, 0.5f, d - 0.1f, type.RoofColor);
                continue;
            }

            foreach (var tile in stations.TilesAt(slot))
            {
                if (!stations.TryGetPieceAt(tile, out _, out var pieceType, out var direction))
                {
                    continue;
                }

                var piece = stations.Rules.Pieces[pieceType];
                var x = tile % width;
                var z = tile / width;
                var ground = map.HeightAt(x, z);
                if (piece.Form == StationPieceForm.Bay)
                {
                    // Platform over the tile and a shelter on the side away from the road.
                    into[index++] = Box(x + 0.5f, ground, z + 0.5f, 0.9f, 0.15f, 0.9f, piece.BaseColor);
                    var back = Dir8.Opposite(direction);
                    var sx = x + 0.5f + (Dir8.OffsetX(back) * 0.35f);
                    var sz = z + 0.5f + (Dir8.OffsetZ(back) * 0.35f);
                    var alongX = Dir8.OffsetX(back) == 0;
                    into[index++] = Box(sx, ground + 0.15f, sz, alongX ? 0.8f : 0.2f, 1.1f, alongX ? 0.2f : 0.8f, piece.RoofColor);
                }
                else
                {
                    // Two kerb shelters on both sides of the road (axis N–S or E–W).
                    var northSouth = direction == Dir8.N;
                    for (var side = -1; side <= 1; side += 2)
                    {
                        var sx = x + 0.5f + (northSouth ? side * 0.42f : 0f);
                        var sz = z + 0.5f + (northSouth ? 0f : side * 0.42f);
                        into[index++] = Box(sx, ground, sz, northSouth ? 0.14f : 0.8f, 1.1f, northSouth ? 0.8f : 0.14f, piece.RoofColor);
                    }
                }
            }
        }

        _ = state;
        return index;
    }

    private static IndustryInstance Box(float cx, float y, float cz, float w, float h, float d, int rgb) =>
        new(new Vector4(cx, y, cz, 0f), new Vector4(w, h, d, 0f),
            new Vector4(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f));
}
