using Talt.Client.Rendering;
using Talt.Core.Construction;
using Talt.Core.Maps;
using Talt.Core.Roads;

namespace Talt.Client.Tests.Rendering;

/// <summary>Road meshes on top of the terrain (research R10, FR-050), GPU-free.</summary>
public class RoadMesherTests
{
    private const int Size = 64;

    private static (ConstructionState State, ConstructionPlanner Planner) Flat(Action<byte[]>? shape = null)
    {
        var count = Size * Size;
        var heights = new byte[count];
        Array.Fill(heights, (byte)10);
        shape?.Invoke(heights);
        var flags = new byte[count];
        Array.Fill(flags, (byte)TileTraits.Buildable);
        var map = new MapData(new MapSettings(Size, Size, 1), heights, new byte[count], new byte[count], flags, new byte[count], [], default);
        return (new ConstructionState(ConstructionRules.Default with { StartingBalance = 100_000_000 }, map), new ConstructionPlanner());
    }

    /// <summary>Applies a plan to the state the way a command does (through the planner's plan).</summary>
    private static void Build(ConstructionState state, ConstructionCommand command)
    {
        var plan = new ConstructionPlan();
        Assert.True(command.Preview(state, new ConstructionPlanner(), plan).Succeeded);
        foreach (var change in plan.Changes)
        {
            if (change.RoadChanged)
            {
                state.Roads.Set(change.Index, change.NewRoad);
            }
        }
    }

    [Fact]
    public void FlatRoad_LiesJustAboveTheTerrain()
    {
        var (state, _) = Flat();
        Build(state, new BuildRoadCommand(0, "paved", 5, 5, 20, 5));
        var mesher = new RoadMesher(state.Roads, state.Map, state.RoadRules);

        mesher.BuildDetail(0, 0, 32, 32);

        Assert.True(mesher.TriangleCount > 16 * 10);
        Assert.Equal(0, mesher.VertexCount % 3);
        Assert.All(mesher.Vertices.ToArray(), v => Assert.InRange(v.Position.Y, 10f, 10.1f));
        Assert.InRange(mesher.Min.X, 5f, 5.5f);
        Assert.InRange(mesher.Max.X, 20.5f, 21f);
    }

    [Fact]
    public void RampPieces_RiseOneStepAcrossTheLowerTile()
    {
        var (state, _) = Flat(h =>
        {
            for (var z = 0; z < Size; z++)
            {
                for (var x = 30; x < Size; x++)
                {
                    h[x + (z * Size)] = 11;
                }
            }
        });
        Build(state, new BuildRoadCommand(0, "paved", 20, 5, 40, 5));
        var mesher = new RoadMesher(state.Roads, state.Map, state.RoadRules);

        mesher.BuildDetail(29, 5, 30, 6);

        var heights = mesher.Vertices.ToArray().Select(v => v.Position.Y).ToArray();
        Assert.Contains(heights, y => y < 10.1f);
        Assert.Contains(heights, y => y > 10.9f);
    }

    [Fact]
    public void Bridge_HasADeckAboveTheEnds()
    {
        var (state, _) = Flat();
        Build(state, new BuildBridgeCommand(0, "paved", 10, 10, 18, 10));
        var mesher = new RoadMesher(state.Roads, state.Map, state.RoadRules);

        mesher.BuildDetail(0, 0, 32, 32);

        Assert.Contains(mesher.Vertices.ToArray(), v => v.Position.X >= 17.9f && v.Position.Y > 10.9f);
        Assert.Contains(mesher.Vertices.ToArray(), v => v.Position.X <= 11.1f && v.Position.X >= 10.9f && v.Position.Y > 10.9f);
        Assert.True(mesher.Max.X >= 18f);
    }

    [Fact]
    public void Simplified_MergesStraightRuns()
    {
        var (state, _) = Flat();
        Build(state, new BuildRoadCommand(0, "paved", 2, 5, 60, 5));
        var mesher = new RoadMesher(state.Roads, state.Map, state.RoadRules);

        mesher.BuildDetail(0, 0, 64, 64);
        var detailed = mesher.TriangleCount;
        mesher.BuildSimplified(0, 0, 64, 64);

        Assert.True(mesher.TriangleCount * 20 < detailed, $"{mesher.TriangleCount} vs {detailed}");
        Assert.True(mesher.TriangleCount > 0);
    }

    [Fact]
    public void Pack_TurnsSrgbIntoLinearBytes()
    {
        Assert.Equal(0xFF_FF_FF_FFu, RoadMesher.Pack(0xffffff));
        Assert.Equal(0xFF_00_00_00u, RoadMesher.Pack(0x000000));
        Assert.Equal(55u, RoadMesher.Pack(0x800000) & 0xff);
    }
}
