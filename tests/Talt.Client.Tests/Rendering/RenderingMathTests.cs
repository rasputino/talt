using System.Numerics;
using Talt.Client.Rendering;

namespace Talt.Client.Tests.Rendering;

public class FrustumTests
{
    private static Frustum LookingDownMinusZ()
    {
        var view = Matrix4x4.CreateLookAt(Vector3.Zero, -Vector3.UnitZ, Vector3.UnitY);
        var projection = Matrix4x4.CreatePerspectiveFieldOfView(MathF.PI / 2, 1f, 1f, 100f);
        return new Frustum(view * projection);
    }

    [Fact]
    public void BoxInFront_IsInside() =>
        Assert.True(LookingDownMinusZ().Intersects(new Vector3(-1, -1, -11), new Vector3(1, 1, -9)));

    [Theory]
    [InlineData(-1, -1, 5, 1, 1, 8)]        // Behind the camera.
    [InlineData(-1, -1, -300, 1, 1, -200)] // Beyond the far plane.
    [InlineData(30, -1, -11, 40, 1, -9)]   // Right of the view.
    [InlineData(-40, -1, -11, -30, 1, -9)] // Left.
    [InlineData(-1, 30, -11, 1, 40, -9)]   // Above.
    [InlineData(-1, -40, -11, 1, -30, -9)] // Below.
    public void BoxOutsideOnePlane_IsCulled(float x0, float y0, float z0, float x1, float y1, float z1) =>
        Assert.False(LookingDownMinusZ().Intersects(new Vector3(x0, y0, z0), new Vector3(x1, y1, z1)));

    [Fact]
    public void BoxStraddlingAPlane_IsKept() =>
        Assert.True(LookingDownMinusZ().Intersects(new Vector3(5, -1, -11), new Vector3(40, 1, -9)));
}

public class LodSelectorTests
{
    private const float Fov = 35f * MathF.PI / 180f;

    [Fact]
    public void PixelsPerUnit_HalvesWhenTheDistanceDoubles()
    {
        var near = LodSelector.PixelsPerUnit(100, 900, Fov);
        var far = LodSelector.PixelsPerUnit(200, 900, Fov);

        Assert.Equal(near / 2, far, 3);
    }

    [Fact]
    public void CloseRegions_UseFullDetail_AndFarRegionsTheCoarsestLevel()
    {
        Assert.Equal(0, LodSelector.Select(LodSelector.PixelsPerUnit(50, 900, Fov), 0));
        Assert.Equal(3, LodSelector.Select(LodSelector.PixelsPerUnit(20_000, 900, Fov), 0));
    }

    [Fact]
    public void Level_IncreasesWithDistance()
    {
        var previous = 0;
        for (var d = 10f; d < 30_000; d *= 1.2f)
        {
            var lod = LodSelector.Select(LodSelector.PixelsPerUnit(d, 900, Fov), previous);
            Assert.True(lod >= previous);
            previous = lod;
        }

        Assert.Equal(3, previous);
    }

    [Fact]
    public void ChosenLevel_KeepsCellsUnderTheErrorLimit_WithHysteresis()
    {
        for (var ppu = 0.01f; ppu < 10; ppu *= 1.1f)
        {
            for (var current = 0; current <= 3; current++)
            {
                var lod = LodSelector.Select(ppu, current);
                if (lod > 0)
                {
                    Assert.True((1 << lod) * ppu < LodSelector.MaxErrorPixels * (1 + LodSelector.Hysteresis));
                }
            }
        }
    }

    [Fact]
    public void Hysteresis_KeepsTheCurrentLevelNearTheThreshold()
    {
        // Exactly at the LOD 1 threshold: a region already at LOD 1 stays; one at LOD 0 does not switch yet.
        var ppu = LodSelector.MaxErrorPixels / 2f;
        Assert.Equal(1, LodSelector.Select(ppu, 1));
        Assert.Equal(0, LodSelector.Select(ppu, 0));
    }

    [Fact]
    public void DistanceToBox_IsZeroInside_AndToTheNearestFaceOutside()
    {
        Assert.Equal(0, LodSelector.DistanceToBox(new Vector3(5), Vector3.Zero, new Vector3(10)));
        Assert.Equal(3, LodSelector.DistanceToBox(new Vector3(13, 5, 5), Vector3.Zero, new Vector3(10)));
    }
}

public class OrbitCameraTests
{
    [Fact]
    public void SmallMaps_KeepTheDefaultZoom_AndLargeMapsCanSeeTheWholeMap()
    {
        var small = new OrbitCamera();
        small.FitMap(512);
        small.Distance = float.MaxValue;
        Assert.Equal(OrbitCamera.DefaultMaxDistance, small.Distance);

        var large = new OrbitCamera();
        large.FitMap(4096);
        large.Distance = float.MaxValue;
        Assert.Equal(4096 * OrbitCamera.WholeMapDistancePerTile, large.Distance, 1);
    }

    [Fact]
    public void Fog_LeavesTheWholeMapVisible_AtTheFarthestZoom()
    {
        var camera = new OrbitCamera();
        camera.FitMap(4096);
        camera.Distance = float.MaxValue;
        var fogDistance = camera.Distance * 2.5f; // As in WebGpuRenderer.
        camera.Target = new Vector3(2048, 0, 2048);
        var farthestCorner = new[] { new Vector3(0, 0, 0), new Vector3(4096, 0, 0), new Vector3(0, 0, 4096), new Vector3(4096, 0, 4096) }
            .Max(corner => Vector3.Distance(camera.Position, corner));
        // Fog starts at half the fog distance (voxel.wgsl); the far corner of the map is within it.
        Assert.True(farthestCorner < fogDistance * 1.5f);
    }

    [Fact]
    public void NearPlane_ScalesWithDistance_AndStaysBelowTheCameraHeight()
    {
        var camera = new OrbitCamera();
        camera.FitMap(4096);
        foreach (var distance in new[] { 10f, 350f, 2000f, 6000f })
        {
            camera.Distance = distance;
            camera.Pitch = 0f; // Clamped to the lowest pitch.
            var height = camera.Position.Y - camera.Target.Y;
            Assert.True(camera.NearPlane >= 0.5f);
            Assert.True(camera.NearPlane < height);
        }
    }
}

public class FrameTimeTrackerTests
{
    [Fact]
    public void ReportsAverageAndWorst_OncePerWindow()
    {
        var tracker = new FrameTimeTracker(windowSeconds: 0.1);

        Assert.False(tracker.Record(10));
        Assert.False(tracker.Record(30));
        Assert.False(tracker.Record(20));
        Assert.True(tracker.Record(40)); // 100 ms reached.

        Assert.Equal(25, tracker.AverageMs, 6);
        Assert.Equal(40, tracker.WorstMs);
        Assert.Equal(40, tracker.FramesPerSecond, 6);
    }

    [Fact]
    public void StartsAFreshWindow_AfterReporting()
    {
        var tracker = new FrameTimeTracker(windowSeconds: 0.05);
        tracker.Record(60);
        tracker.Record(10);
        tracker.Record(10);
        tracker.Record(10);
        Assert.True(tracker.Record(20));

        Assert.Equal(20, tracker.WorstMs);
    }
}
