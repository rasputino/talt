using Talt.Client.Construction;
using Talt.Core.Construction;
using Talt.Core.Maps;
using Talt.Core.Roads;

namespace Talt.Client.Tests.Construction;

/// <summary>Road tools of feature 007: shapes, type choice and preview = command (FR-011, FR-051).</summary>
[Collection(nameof(RoadToolOptions))]
public class RoadToolTests
{
    private static ConstructionState FlatState(int year = 1950)
    {
        var size = 64;
        var count = size * size;
        var heights = new byte[count];
        Array.Fill(heights, (byte)10);
        var flags = new byte[count];
        Array.Fill(flags, (byte)TileTraits.Buildable);
        var map = new MapData(new MapSettings(size, size, 1), heights, new byte[count], new byte[count], flags, new byte[count], [], default);
        return new ConstructionState(ConstructionRules.Default with { StartingBalance = 10_000_000 }, map) { Year = year };
    }

    [Fact]
    public void RoadTool_PreviewsTheSamePlanAndCostAsItsCommand()
    {
        RoadToolOptions.Reset();
        var state = FlatState();
        var tools = new ToolController();
        tools.Select(ConstructionTool.Road);
        tools.Press((5, 5));
        tools.Hover((30, 18));
        tools.UpdatePreview(state);

        var command = tools.Release(state, out var reason);

        Assert.Null(reason);
        var plan = new ConstructionPlan();
        Assert.True(command!.Preview(state, new ConstructionPlanner(), plan).Succeeded);
        Assert.IsType<BuildRoadCommand>(command);
        Assert.Equal("paved", ((BuildRoadCommand)command).RoadType);
        Assert.True(plan.Cost > 0);
    }

    [Fact]
    public void Line8Shape_SnapsToTheNearest45DegreeLine()
    {
        var tools = new ToolController();
        tools.Select(ConstructionTool.Motorway);

        Assert.Equal(((10, 10), (20, 20)), tools.Shape((10, 10), (20, 19)));
        Assert.Equal(((10, 10), (22, 10)), tools.Shape((10, 10), (22, 12)));
        Assert.Equal(((10, 10), (10, 2)), tools.Shape((10, 10), (11, 2)));
    }

    [Fact]
    public void RoadType_CyclesThroughTheTypesOfTheYear_SkippingTheMotorway()
    {
        RoadToolOptions.Reset();
        var state = FlatState(year: 1990);

        var seen = new List<string>();
        for (var i = 0; i < 4; i++)
        {
            seen.Add(RoadToolOptions.TypeId);
            RoadToolOptions.Cycle(state);
        }

        Assert.Equal(["paved", "fast", "dirt", "paved"], seen);
        RoadToolOptions.Reset();
    }

    [Fact]
    public void UnavailableChoice_FallsBackToTheBestTypeOfTheYear()
    {
        RoadToolOptions.Reset();
        var state = FlatState(year: 1900);

        Assert.Equal(state.RoadRules.FindType("dirt"), RoadToolOptions.TypeFor(state));
        Assert.Equal("dirt", RoadToolOptions.TypeId);
        RoadToolOptions.Reset();
    }

    [Fact]
    public void RemovalPreview_MarksOnlyTheRemovedPiecesInTheRemovalColour()
    {
        // Hand test 2026-11-24: removing should not look like building (green).
        RoadToolOptions.Reset();
        var state = FlatState();
        var build = new ConstructionPlan();
        new ConstructionPlanner().Roads.PlanRoad(state, state.Player.Id, state.RoadRules.FindType("paved"), (5, 5), (25, 5), build);
        foreach (var change in build.Changes)
        {
            state.Roads.Set(change.Index, change.NewRoad);
            state.Objects.Set(change.Index, change.NewObject, change.NewOwner);
        }

        var tools = new ToolController();
        tools.Select(ConstructionTool.RemoveRoad);
        tools.Press((12, 5));
        tools.Hover((14, 5));
        tools.UpdatePreview(state);
        var theme = Talt.Client.Ui.Theme.Load();
        var boxes = new PreviewBoxes();

        boxes.Build(tools.Preview, tools.PreviewResult.Succeeded, state.Map, theme, ConstructionTool.RemoveRoad.Removes);
        var removal = boxes.Boxes.ToArray();
        boxes.Build(tools.Preview, tools.PreviewResult.Succeeded, state.Map, theme);
        var building = boxes.Boxes.ToArray();

        Assert.True(tools.PreviewResult.Succeeded, tools.PreviewResult.ReasonCode);
        Assert.Equal(3, removal.Length); // Tiles 12–14; their neighbours only lose a link.
        Assert.All(removal, b => Assert.True(b.Color.X > 0.5f && b.Color.Y > 0.4f && b.Color.Z < 0.2f, $"{b.Color}"));
        Assert.NotEqual(building[0].Color, removal[0].Color);
        Assert.False(ConstructionTool.Road.Removes);
    }

    [Fact]
    public void EveryRoadTool_HasAnIconTooltipAndKeyBinding()
    {
        var en = Talt.Client.Localization.Strings.Load("en");
        foreach (var tool in ConstructionTool.All)
        {
            Assert.True(en.Contains($"tool.{tool.Id}.tooltip"), tool.Id);
            Assert.True(en.Contains($"action.{tool.ActionId}"), tool.ActionId);
        }
    }
}
