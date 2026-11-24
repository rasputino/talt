using System.Numerics;
using System.Runtime.InteropServices;
using Talt.Core.Towns;

namespace Talt.Client.Rendering;

/// <summary>
/// One box of a town building, ready for the instanced renderer (feature 008, research R12). Layout must match
/// <c>towns.wgsl</c>.
/// </summary>
/// <param name="Position">xyz: centre of the box's base; w: window style (0 none, 1 rows, 2 grid; roofs 0).</param>
/// <param name="Extent">xyz: width, height and depth in world units; w: storey height for the window rows.</param>
/// <param name="Color">RGB colour; w: a per-building value 0–1 for small variations of the windows.</param>
[StructLayout(LayoutKind.Sequential)]
internal readonly record struct TownInstance(Vector4 Position, Vector4 Extent, Vector4 Color);

/// <summary>
/// Turns town buildings into boxes (pure, testable): walls with shader-drawn windows, plus roof boxes. Walls and
/// roofs go into separate lists so the far LOD can draw the walls alone.
/// </summary>
internal static class TownInstances
{
    /// <summary>World height of one storey (the data's building height counts storeys).</summary>
    public const float StoreyHeight = 0.6f;

    /// <summary>Gap left on each side of a footprint, so neighbouring buildings read as separate.</summary>
    public const float Inset = 0.08f;

    /// <summary>Appends the boxes of one building (walls first list, roofs second); returns the top height.</summary>
    public static float Append(BuildingType type, int x, int z, int baseHeight, int id, List<TownInstance> walls, List<TownInstance> roofs)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(walls);
        ArgumentNullException.ThrowIfNull(roofs);
        var width = type.Width - (2 * Inset);
        var depth = type.Depth - (2 * Inset);
        var centreX = x + (type.Width / 2f);
        var centreZ = z + (type.Depth / 2f);
        var wallHeight = type.Height * StoreyHeight;
        var ground = (float)baseHeight;
        // A per-building value in 0..1 (multiplicative hash on 32 bits; int * uint would widen to long).
        var variation = (unchecked((uint)id * 2654435761u) >> 24) / 255f;
        var wall = Color(type.Model.WallColor) * (0.92f + (0.16f * variation));
        wall.W = variation;
        walls.Add(new TownInstance(
            new Vector4(centreX, ground, centreZ, (float)type.Model.Windows),
            new Vector4(width, wallHeight, depth, StoreyHeight),
            wall));

        var roof = Color(type.Model.RoofColor);
        var top = ground + wallHeight;
        if (type.Model.Roof == RoofStyle.Pitched)
        {
            // A stepped "pitch": a full slab and a narrower ridge along the longer side.
            var alongX = type.Width >= type.Depth;
            roofs.Add(new TownInstance(new Vector4(centreX, top, centreZ, 0f), new Vector4(width + 0.06f, 0.25f, depth + 0.06f, 0f), roof));
            roofs.Add(new TownInstance(
                new Vector4(centreX, top + 0.25f, centreZ, 0f),
                new Vector4(alongX ? width * 0.9f : width * 0.45f, 0.3f, alongX ? depth * 0.45f : depth * 0.9f, 0f),
                roof * 0.85f));
            return top + 0.55f;
        }

        roofs.Add(new TownInstance(new Vector4(centreX, top, centreZ, 0f), new Vector4(width + 0.04f, 0.12f, depth + 0.04f, 0f), roof));
        if (type.Height >= 4)
        {
            // A small rooftop structure on taller buildings (stairs, plant room).
            roofs.Add(new TownInstance(
                new Vector4(centreX + (width * 0.15f), top + 0.12f, centreZ - (depth * 0.15f), 0f),
                new Vector4(width * 0.35f, 0.35f, depth * 0.35f, 0f),
                roof * 0.9f));
            return top + 0.47f;
        }

        return top + 0.12f;
    }

    private static Vector4 Color(int rgb) =>
        new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
}
