using System.Numerics;
using Talt.Core.Meshing;

namespace Talt.Client.Rendering;

/// <summary>
/// Chooses a region's level of detail from its screen-space error: level k merges 2^k voxels into one
/// cell, which is acceptable while that cell covers fewer than <see cref="MaxErrorPixels"/> pixels.
/// </summary>
internal static class LodSelector
{
    /// <summary>Largest on-screen size, in pixels, of a simplified cell.</summary>
    public const float MaxErrorPixels = 2f;

    /// <summary>Margin that keeps a level a little longer than strictly needed, to avoid flicker.</summary>
    public const float Hysteresis = 0.1f;

    /// <summary>Pixels covered by one world unit at a given distance from the camera.</summary>
    public static float PixelsPerUnit(float distance, float screenHeightPixels, float verticalFieldOfView) =>
        screenHeightPixels / (2f * MathF.Max(distance, 1f) * MathF.Tan(verticalFieldOfView * 0.5f));

    /// <summary>
    /// The coarsest level whose cells stay under the error limit. Moving to a coarser level than
    /// <paramref name="currentLod"/> needs a 10 % margin below the limit; staying at or returning to a
    /// level at most as coarse as the current one allows 10 % above it.
    /// </summary>
    public static int Select(float pixelsPerUnit, int currentLod)
    {
        for (var lod = GreedyMesher.MaxLod; lod > 0; lod--)
        {
            var limit = MaxErrorPixels * (lod > currentLod ? 1f - Hysteresis : 1f + Hysteresis);
            if ((1 << lod) * pixelsPerUnit < limit)
            {
                return lod;
            }
        }

        return 0;
    }

    /// <summary>
    /// Farthest distance at which <see cref="Select"/> can return full detail (LOD 0): a region already
    /// at LOD 0 keeps it until LOD 1 cells fall 10 % below the error limit. Returning to LOD 0 from a
    /// coarser level happens nearer still.
    /// </summary>
    public static float FullDetailDistance(float screenHeightPixels, float verticalFieldOfView)
    {
        var pixelsPerUnit = MaxErrorPixels * (1f - Hysteresis) / 2f;
        return screenHeightPixels / (2f * pixelsPerUnit * MathF.Tan(verticalFieldOfView * 0.5f));
    }

    /// <summary>Distance from a point to the nearest point of a box (0 inside).</summary>
    public static float DistanceToBox(Vector3 point, Vector3 min, Vector3 max) =>
        Vector3.Distance(point, Vector3.Clamp(point, min, max));
}
