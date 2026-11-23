using Talt.Client.Rendering;

namespace Talt.Client.Tests.Rendering;

public class Lod0EvictionTests
{
    private const float Fov = 35f * MathF.PI / 180f;
    private const float Release = 1_000f;

    [Fact]
    public void FullDetailDistance_IsTheLastDistanceWhereLod0IsChosen()
    {
        var limit = LodSelector.FullDetailDistance(900, Fov);

        Assert.Equal(0, LodSelector.Select(LodSelector.PixelsPerUnit(limit * 0.99f, 900, Fov), 0));
        Assert.Equal(1, LodSelector.Select(LodSelector.PixelsPerUnit(limit * 1.01f, 900, Fov), 0));
    }

    [Fact]
    public void ReleaseDistance_UsesTheNearerOfFullDetailAndFog()
    {
        Assert.Equal(1_500f, Lod0Eviction.ReleaseDistance(1_000f, 4_000f));
        Assert.Equal(600f, Lod0Eviction.ReleaseDistance(1_000f, 400f));
    }

    [Fact]
    public void ReleaseDistance_LeavesAMarginBeyondWhereLod0IsRebuilt()
    {
        var fullDetail = LodSelector.FullDetailDistance(900, Fov);
        var release = Lod0Eviction.ReleaseDistance(fullDetail, float.MaxValue);

        Assert.True(release > fullDetail * 1.4f);
        Assert.NotEqual(0, LodSelector.Select(LodSelector.PixelsPerUnit(release, 900, Fov), 0));
    }

    [Fact]
    public void FarAndIdle_WithACoarserLevel_IsEvicted() =>
        Assert.Equal(Lod0EvictionAction.Evict, Lod0Eviction.Decide(10, Release + 1, Release, busy: false, coarserLevelBuilt: true));

    [Fact]
    public void FarAndIdle_WithoutACoarserLevel_BuildsOneFirst() =>
        Assert.Equal(Lod0EvictionAction.BuildCoarserLevel, Lod0Eviction.Decide(10, Release + 1, Release, busy: false, coarserLevelBuilt: false));

    [Theory]
    [InlineData(4.9, 5_000f)]  // Wanted LOD 0 too recently, however far.
    [InlineData(60.0, 1_000f)] // Idle for long, but not beyond the release distance.
    [InlineData(60.0, 400f)]
    public void RecentlyWantedOrNear_IsKept(double idle, float distance) =>
        Assert.Equal(Lod0EvictionAction.Keep, Lod0Eviction.Decide(idle, distance, Release, busy: false, coarserLevelBuilt: true));

    [Fact]
    public void Busy_IsKept() =>
        Assert.Equal(Lod0EvictionAction.Keep, Lod0Eviction.Decide(60, 5_000f, Release, busy: true, coarserLevelBuilt: true));

    [Fact]
    public void HoveringAroundTheThreshold_DoesNotThrash()
    {
        // The camera oscillates between well inside the rebuild distance and just beyond the release
        // distance every second: LOD 0 is wanted again within the idle time, so it is never released.
        var fullDetail = LodSelector.FullDetailDistance(900, Fov);
        var release = Lod0Eviction.ReleaseDistance(fullDetail, float.MaxValue);
        var lastWanted = 0.0;
        var lod = 0;
        for (var t = 0.0; t < 120; t += 1.0 / 60)
        {
            var distance = (int)t % 2 == 0 ? fullDetail * 0.5f : release * 1.1f;
            lod = LodSelector.Select(LodSelector.PixelsPerUnit(distance, 900, Fov), lod);
            if (lod == 0)
            {
                lastWanted = t;
            }

            Assert.Equal(Lod0EvictionAction.Keep, Lod0Eviction.Decide(t - lastWanted, distance, release, busy: false, coarserLevelBuilt: true));
        }
    }
}
