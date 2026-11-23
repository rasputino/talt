using System.Numerics;
using Talt.Client.Picking;
using Talt.Client.Rendering;
using Talt.Core.Terrain;
using Talt.Core.Voxels;

namespace Talt.Client.Tests.Picking;

public class PickingTests
{
    private const int Size = 64;

    /// <summary>Flat land at height 10, a cliff up to 20 from x = 32, one road tile at (3, 3).</summary>
    private static PickContext CreateWorld(params VehicleInstance[] vehicles)
    {
        var world = new VoxelWorld();
        for (var z = 0; z < Size; z++)
        {
            for (var x = 0; x < Size; x++)
            {
                var height = x >= 32 ? 20 : 10;
                for (var y = 0; y < height; y++)
                {
                    world.SetVoxel(x, y, z, y == height - 1 ? VoxelMaterial.Grass : VoxelMaterial.Dirt);
                }
            }
        }

        world.SetVoxel(3, 9, 3, VoxelMaterial.Road);
        return new PickContext
        {
            Heightmap = Heightmap.FromWorld(world, Size, Size),
            World = world,
            Vehicles = vehicles,
            VehicleIds = Enumerable.Range(100, vehicles.Length).ToArray(),
            VehicleCount = vehicles.Length,
        };
    }

    private static VehicleInstance Vehicle(float x, float y, float z) => new(new Vector4(x, y, z, 0), Vector4.One);

    [Fact]
    public void RayStraightDown_HitsTheTileBelow()
    {
        var context = CreateWorld();
        var hit = new MapPicker().Pick(new Ray(new Vector3(5.5f, 100, 7.5f), -Vector3.UnitY), context);
        Assert.Equal(PickKind.Tile, hit.Kind);
        Assert.Equal((5, 7, 10), (hit.TileX, hit.TileZ, hit.Height));
        Assert.Equal(VoxelMaterial.Grass, hit.Material);
        Assert.Equal(90, hit.Distance, 3);
    }

    [Fact]
    public void LowRay_HitsTheSideOfTheCliff()
    {
        var context = CreateWorld();
        var hit = new MapPicker().Pick(new Ray(new Vector3(10.5f, 15, 5.5f), Vector3.UnitX), context);
        Assert.Equal((PickKind.Tile, 32, 5, 20), (hit.Kind, hit.TileX, hit.TileZ, hit.Height));
        Assert.Equal(21.5f, hit.Distance, 3);
    }

    [Fact]
    public void ObliqueRay_HitsTheFirstColumnItCrosses()
    {
        var context = CreateWorld();
        // Descending slowly towards the cliff: still above it at x = 32 (y ≈ 21.5), so it lands on the
        // cliff top where y reaches 20, at x = 20.2 + 5 / 0.3 ≈ 36.9.
        var direction = Vector3.Normalize(new Vector3(1, -0.3f, 0));
        var hit = new MapPicker().Pick(new Ray(new Vector3(20.2f, 25, 9.5f), direction), context);
        Assert.Equal(36, hit.TileX);
        Assert.Equal(20, hit.Height);

        // Descending steeply: it reaches the low land before the cliff.
        var steep = new MapPicker().Pick(new Ray(new Vector3(20.2f, 25, 9.5f), Vector3.Normalize(new Vector3(1, -2, 0))), context);
        Assert.Equal((27, 10), (steep.TileX, steep.Height));
    }

    [Fact]
    public void RoadTile_IsPickedAsRoad()
    {
        var context = CreateWorld();
        var hit = new MapPicker().Pick(new Ray(new Vector3(3.5f, 50, 3.5f), -Vector3.UnitY), context);
        Assert.Equal(PickKind.Road, hit.Kind);
        Assert.Equal("road:3,3", hit.Key);
    }

    [Fact]
    public void Vehicle_WinsOverTheGroundBelowIt()
    {
        var context = CreateWorld(Vehicle(5.5f, 10, 7.5f));
        var hit = new MapPicker().Pick(new Ray(new Vector3(5.5f, 100, 7.5f), -Vector3.UnitY), context);
        Assert.Equal(PickKind.Vehicle, hit.Kind);
        Assert.Equal(100, hit.VehicleId);
    }

    [Fact]
    public void VehicleHiddenBehindTheCliff_IsNotPicked()
    {
        var context = CreateWorld(Vehicle(40.5f, 20, 5.5f));
        var hit = new MapPicker().Pick(new Ray(new Vector3(10.5f, 15, 5.5f), Vector3.UnitX), context);
        Assert.Equal(PickKind.Tile, hit.Kind);
        Assert.Equal(32, hit.TileX);
    }

    [Fact]
    public void Sky_PicksNothing()
    {
        var context = CreateWorld();
        Assert.Equal(PickKind.None, new MapPicker().Pick(new Ray(new Vector3(5, 30, 5), Vector3.Normalize(new Vector3(1, 1, 0))), context).Kind);
        Assert.Equal(PickKind.None, new MapPicker().Pick(new Ray(new Vector3(-50, 30, -50), -Vector3.UnitX), context).Kind);
    }

    [Theory]
    [InlineData(20f)]
    [InlineData(400f)]
    [InlineData(4000f)]
    public void CursorAtScreenCentre_HitsTheCameraTarget_AtAnyZoom(float distance)
    {
        var context = CreateWorld();
        var camera = new OrbitCamera();
        camera.FitMap(4096);
        camera.Target = new Vector3(20.5f, 10, 20.5f);
        camera.Distance = distance;
        var viewport = new Vector2(1600, 900);
        var ray = Ray.FromScreen(camera.ViewProjection(viewport.X / viewport.Y), viewport / 2, viewport);
        var hit = new MapPicker().Pick(ray, context);
        Assert.Equal((20, 20), (hit.TileX, hit.TileZ));
    }

    [Fact]
    public void EditedTerrain_IsPickedAtItsNewHeight()
    {
        var context = CreateWorld();
        TerrainEditor.Raise(context.World, context.Heightmap, 6, 6);
        var hit = new MapPicker().Pick(new Ray(new Vector3(6.5f, 100, 6.5f), -Vector3.UnitY), context);
        Assert.Equal(11, hit.Height);
    }

    [Fact]
    public void NewKindsOfObjects_CanBeRegistered()
    {
        var context = CreateWorld();
        var picker = new MapPicker();
        picker.Register(new AlwaysLayer());
        var hit = picker.Pick(new Ray(new Vector3(5.5f, 100, 7.5f), -Vector3.UnitY), context);
        Assert.Equal(PickKind.Vehicle, hit.Kind);
        Assert.Equal(-7, hit.VehicleId);
    }

    private sealed class AlwaysLayer : IPickLayer
    {
        public int Priority => 200;

        public bool TryPick(in Ray ray, PickContext context, out PickResult result)
        {
            result = context.Terrain with { Kind = PickKind.Vehicle, VehicleId = -7 };
            return true;
        }
    }
}
