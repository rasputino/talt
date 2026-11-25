using Talt.Core.Meshing.Models;

namespace Talt.Core.Tests.Meshing.Models;

public class ModelMesherTests
{
    [Fact]
    public void SingleVoxel_OnTheGround_HasFiveFaces()
    {
        var model = new VoxModel(1, 1, 1);
        model.Set(0, 0, 0, 1);

        var mesh = ModelMesher.Build(model);

        Assert.Equal(5, mesh.QuadCount(0)); // The bottom face touches the ground.
    }

    [Fact]
    public void FloatingVoxel_HasSixFaces()
    {
        var model = new VoxModel(1, 2, 1);
        model.Set(0, 1, 0, 1);

        Assert.Equal(6, ModelMesher.Build(model).QuadCount(0));
    }

    [Fact]
    public void Slab_OfOneColour_MergesTopIntoOneQuad()
    {
        var model = new VoxModel(4, 1, 4);
        Fill(model, 0, 0, 0, 4, 1, 4, 5);

        var mesh = ModelMesher.Build(model);

        var tops = mesh.Levels[0].Where(v => v.FaceIndex == 2).Count() / 4;
        Assert.Equal(1, tops);
    }

    [Fact]
    public void DifferentColours_AreNotMerged()
    {
        var model = new VoxModel(2, 1, 1);
        model.Set(0, 0, 0, 1);
        model.Set(1, 0, 0, 2);

        var tops = ModelMesher.Build(model).Levels[0].Where(v => v.FaceIndex == 2).Count() / 4;

        Assert.Equal(2, tops);
    }

    [Fact]
    public void Positions_AreHalfVoxelsFromTheCentreOfTheBase()
    {
        var model = new VoxModel(3, 2, 5);
        Fill(model, 0, 0, 0, 3, 2, 5, 1);

        var vertices = ModelMesher.Build(model).Levels[0];

        Assert.Equal(-3, vertices.Min(v => v.X2));
        Assert.Equal(3, vertices.Max(v => v.X2));
        Assert.Equal(0, vertices.Min(v => v.Y2));
        Assert.Equal(4, vertices.Max(v => v.Y2));
        Assert.Equal(-5, vertices.Min(v => v.Z2));
        Assert.Equal(5, vertices.Max(v => v.Z2));
    }

    [Fact]
    public void WallFoot_IsDarkenedByTheGround()
    {
        var model = new VoxModel(1, 3, 1);
        Fill(model, 0, 0, 0, 1, 3, 1, 1);

        var side = ModelMesher.Build(model).Levels[0].Where(v => v.FaceIndex == 0).ToList();

        Assert.Contains(side, v => v.Y2 == 0 && v.AmbientOcclusion < 3);
        Assert.Contains(side, v => v.Y2 == 6 && v.AmbientOcclusion == 3);
    }

    [Fact]
    public void InnerCorner_HasAmbientOcclusion()
    {
        // An L shape: the top of the low block next to the tall one is occluded along the wall.
        var model = new VoxModel(2, 2, 1);
        model.Set(0, 0, 0, 1);
        model.Set(1, 0, 0, 1);
        model.Set(1, 1, 0, 1);

        var vertices = ModelMesher.Build(model).Levels[0];

        Assert.Contains(vertices, v => v.FaceIndex == 2 && v.Y2 == 2 && v.X2 == 0 && v.AmbientOcclusion < 3);
    }

    [Fact]
    public void Levels_KeepTheSilhouetteWithFewerQuads()
    {
        var model = new VoxModel(16, 16, 16);
        for (var z = 0; z < 16; z++)
        {
            for (var y = 0; y < 16; y++)
            {
                for (var x = 0; x < 16; x++)
                {
                    if ((x + y + z) % 3 != 0)
                    {
                        model.Set(x, y, z, (byte)(1 + ((x + z) % 4)));
                    }
                }
            }
        }

        var mesh = ModelMesher.Build(model);

        Assert.True(mesh.QuadCount(1) < mesh.QuadCount(0));
        Assert.True(mesh.QuadCount(2) < mesh.QuadCount(1));
        foreach (var level in mesh.Levels)
        {
            Assert.Equal(-16, level.Min(v => v.X2));
            Assert.Equal(16, level.Max(v => v.X2));
            Assert.Equal(32, level.Max(v => v.Y2));
        }
    }

    [Fact]
    public void Build_IsDeterministic()
    {
        var model = VoxReaderTests.SampleModel();

        Assert.Equal(ModelMesher.Build(model).Levels[0], ModelMesher.Build(model).Levels[0]);
    }

    private static void Fill(VoxModel model, int x0, int y0, int z0, int sx, int sy, int sz, byte color)
    {
        for (var z = z0; z < z0 + sz; z++)
        {
            for (var y = y0; y < y0 + sy; y++)
            {
                for (var x = x0; x < x0 + sx; x++)
                {
                    model.Set(x, y, z, color);
                }
            }
        }
    }
}
