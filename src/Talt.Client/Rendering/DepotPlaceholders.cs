using System.Numerics;
using Talt.Core.Maps;
using Talt.Core.Roads;
using Talt.Core.Vehicles;

namespace Talt.Client.Rendering;

/// <summary>
/// Placeholder models for road depots (feature 012), as boxes in the industry placeholder instances like the station
/// placeholders: a garage with its open side towards the road and a flat roof. Colours come from <c>vehicles.json</c>.
/// A <c>depot</c> voxel model can replace them later (feature 018's pipeline).
/// </summary>
internal static class DepotPlaceholders
{
    /// <summary>Writes the boxes from <paramref name="index"/> on and returns the next free index.</summary>
    public static int Build(DepotRegistry depots, MapData map, IndustryInstance[] into, int index)
    {
        ArgumentNullException.ThrowIfNull(depots);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(into);
        var rules = VehicleRules.Default;
        for (var slot = 0; slot < depots.Count; slot++)
        {
            var tile = depots.TileAt(slot);
            var x = tile % map.Width;
            var z = tile / map.Width;
            var ground = map.HeightAt(x, z);
            var direction = depots.DirectionAt(slot);
            // The back wall away from the road, two side walls, a roof: a garage open towards the road.
            var bx = -Dir8.OffsetX(direction) * 0.38f;
            var bz = -Dir8.OffsetZ(direction) * 0.38f;
            var alongX = Dir8.OffsetX(direction) != 0;
            into[index++] = Box(x + 0.5f + bx, ground, z + 0.5f + bz, alongX ? 0.12f : 0.9f, 0.9f, alongX ? 0.9f : 0.12f, rules.DepotBaseColor);
            into[index++] = Box(x + 0.5f + (alongX ? 0 : -0.42f), ground, z + 0.5f + (alongX ? -0.42f : 0), alongX ? 0.9f : 0.08f, 0.9f, alongX ? 0.08f : 0.9f, rules.DepotBaseColor);
            into[index++] = Box(x + 0.5f + (alongX ? 0 : 0.42f), ground, z + 0.5f + (alongX ? 0.42f : 0), alongX ? 0.9f : 0.08f, 0.9f, alongX ? 0.08f : 0.9f, rules.DepotBaseColor);
            into[index++] = Box(x + 0.5f, ground + 0.9f, z + 0.5f, 0.98f, 0.14f, 0.98f, rules.DepotRoofColor);
        }

        return index;
    }

    /// <summary>Upper bound of the boxes <see cref="Build"/> writes.</summary>
    public static int BoxCount(DepotRegistry depots) => (depots?.Count ?? 0) * 4;

    private static IndustryInstance Box(float cx, float y, float cz, float w, float h, float d, int rgb) =>
        new(new Vector4(cx, y, cz, 0f), new Vector4(w, h, d, 0f),
            new Vector4(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f));
}
