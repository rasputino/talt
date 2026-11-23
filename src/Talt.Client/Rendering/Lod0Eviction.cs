namespace Talt.Client.Rendering;

/// <summary>What to do with a region's resident full-detail (LOD 0) buffer this frame.</summary>
internal enum Lod0EvictionAction
{
    /// <summary>Keep the buffer.</summary>
    Keep,

    /// <summary>The buffer may go, but no coarser level is built yet: build one first (no holes).</summary>
    BuildCoarserLevel,

    /// <summary>Release the buffer; a coarser built level takes its place.</summary>
    Evict,
}

/// <summary>
/// Decides when a region's full-detail buffer is released (research R3). Simplified levels are small and
/// stay; LOD 0 buffers are the bulk of world GPU memory and are released once the region has not wanted
/// full detail for <see cref="IdleSeconds"/> and is far enough that it cannot want it again soon.
/// </summary>
/// <remarks>
/// Hysteresis in distance: full detail is only rebuilt inside <see cref="LodSelector.FullDetailDistance"/>
/// and inside the fog, while release needs <see cref="DistanceFactor"/> times the nearer of the two.
/// Hysteresis in time: hovering just outside the threshold never releases a buffer that was wanted
/// in the last few seconds.
/// </remarks>
internal static class Lod0Eviction
{
    /// <summary>Seconds without wanting LOD 0 before its buffer may be released.</summary>
    public const double IdleSeconds = 5;

    /// <summary>Release distance as a multiple of the farthest distance where LOD 0 can be (re)built.</summary>
    public const float DistanceFactor = 1.5f;

    /// <summary>
    /// Distance beyond which a region's LOD 0 buffer may be released: LOD 0 is built only where
    /// <see cref="LodSelector.Select"/> can choose it and only inside the fog.
    /// </summary>
    public static float ReleaseDistance(float fullDetailDistance, float fogFullDistance) =>
        DistanceFactor * MathF.Min(fullDetailDistance, fogFullDistance);

    /// <param name="secondsSinceWanted">Seconds since the region last wanted LOD 0.</param>
    /// <param name="distance">Distance from the camera to the region.</param>
    /// <param name="releaseDistance">From <see cref="ReleaseDistance"/>.</param>
    /// <param name="busy">A chunk mesh of the region is being built or waits to be assembled.</param>
    /// <param name="coarserLevelBuilt">A simplified level is built and can be drawn instead.</param>
    public static Lod0EvictionAction Decide(double secondsSinceWanted, float distance, float releaseDistance,
        bool busy, bool coarserLevelBuilt)
    {
        if (busy || secondsSinceWanted < IdleSeconds || distance <= releaseDistance)
        {
            return Lod0EvictionAction.Keep;
        }

        return coarserLevelBuilt ? Lod0EvictionAction.Evict : Lod0EvictionAction.BuildCoarserLevel;
    }
}
