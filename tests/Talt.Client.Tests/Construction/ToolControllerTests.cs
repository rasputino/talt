using System.Numerics;
using Talt.Client.Construction;
using Talt.Client.Localization;
using Talt.Client.Rendering;
using Talt.Core.Construction;
using Talt.Core.Maps;

namespace Talt.Client.Tests.Construction;

/// <summary>Tool selection, preview, drag, confirm and cancel (US1, US4; contracts/construction-api.md).</summary>
public class ToolControllerTests
{
    private static readonly MapData SharedMap = MapGenerator.Generate(new MapSettings(128, 128, Seed: 4, Roughness: 40, MaxHeight: 30, WaterAmount: 10, SeaLevel: 4));

    private static ConstructionState NewState(long balance = 1_000_000) =>
        new(ConstructionRules.Default with { StartingBalance = balance }, MapGenerator.Generate(SharedMap.Settings));

    /// <summary>A dry 30 × 30 block (the generated map is deterministic).</summary>
    private static (int X, int Z) DryCorner(ConstructionState state)
    {
        var map = state.Map;
        for (var z = 2; z < map.Depth - 32; z++)
        {
            for (var x = 2; x < map.Width - 32; x++)
            {
                var dry = true;
                for (var dz = -2; dz < 32 && dry; dz++)
                {
                    for (var dx = -2; dx < 32 && dry; dx++)
                    {
                        dry = !map.IsWater(map.Index(x + dx, z + dz));
                    }
                }

                if (dry)
                {
                    return (x, z);
                }
            }
        }

        throw new InvalidOperationException("No dry block on the test map.");
    }

    [Fact]
    public void Hover_PreviewsOneTile_WithTheCostTheCommandWouldSpend()
    {
        var state = NewState();
        var tile = DryCorner(state);
        var tools = new ToolController();
        tools.Select(ConstructionTool.Raise);
        tools.Hover(tile);

        Assert.True(tools.UpdatePreview(state));
        Assert.True(tools.HasPreview);
        Assert.True(tools.PreviewResult.Succeeded, tools.PreviewResult.ReasonCode);
        Assert.Contains(tools.Preview.Changes.ToArray(), c => c.Index == state.Map.Index(tile.X, tile.Z));

        var commandPlan = new ConstructionPlan();
        var command = ConstructionTool.Raise.Create(state.Player.Id, tile, tile);
        Assert.True(command.Preview(state, new ConstructionPlanner(), commandPlan).Succeeded);
        Assert.Equal(commandPlan.Cost, tools.Preview.Cost);
    }

    [Theory]
    [MemberData(nameof(Tools))]
    public void EveryTool_PreviewsLikeItsCommand(string id)
    {
        var state = NewState();
        var tool = ConstructionTool.All.Single(t => t.Id == id);
        var (x, z) = DryCorner(state);
        var tools = new ToolController();
        tools.Select(tool);
        tools.Press((x, z));
        tools.Hover((x + 24, z + 19));
        tools.UpdatePreview(state);

        var commandPlan = new ConstructionPlan();
        var result = tool.Create(state.Player.Id, (x, z), (x + 24, z + 19)).Preview(state, new ConstructionPlanner(), commandPlan);

        Assert.Equal(result.ReasonCode, tools.PreviewResult.ReasonCode);
        Assert.Equal(commandPlan.Cost, tools.Preview.Cost);
        Assert.Equal(commandPlan.ChangeCount, tools.Preview.ChangeCount);
    }

    public static TheoryData<string> Tools() => [.. ConstructionTool.All.Select(t => t.Id)];

    [Fact]
    public void InvalidTile_ShowsTheReason()
    {
        var state = NewState();
        var map = state.Map;
        var water = Enumerable.Range(0, map.Width * map.Depth).First(map.IsWater);
        var tools = new ToolController();
        tools.Select(ConstructionTool.Lower);
        tools.Hover((water % map.Width, water / map.Width));
        tools.UpdatePreview(state);

        Assert.False(tools.PreviewResult.Succeeded);
        Assert.Equal(ConstructionReasons.Water, tools.PreviewResult.ReasonCode);
    }

    [Fact]
    public void NotEnoughMoney_IsShownBeforeConfirming_AndReleaseSubmitsNothing()
    {
        var state = NewState(balance: 10);
        var (x, z) = DryCorner(state);
        var tools = new ToolController();
        tools.Select(ConstructionTool.Raise);
        tools.Press((x, z));
        tools.Hover((x + 5, z + 5));
        tools.UpdatePreview(state);

        Assert.Equal(ConstructionReasons.NotEnoughMoney, tools.PreviewResult.ReasonCode);
        Assert.Null(tools.Release(state, out var reason));
        Assert.Equal(ConstructionReasons.NotEnoughMoney, reason);
    }

    [Fact]
    public void Preview_IsPlannedAgainOnlyWhenSomethingChanged()
    {
        var state = NewState();
        var tile = DryCorner(state);
        var tools = new ToolController();
        tools.Select(ConstructionTool.Level);
        tools.Hover(tile);

        Assert.True(tools.UpdatePreview(state));
        Assert.False(tools.UpdatePreview(state));
        tools.Hover((tile.X + 1, tile.Z));
        Assert.True(tools.UpdatePreview(state));
    }

    [Fact]
    public void AreaDrag_InAnyDirection_SubmitsOneCommandWithTheRectangle()
    {
        var state = NewState();
        var (x, z) = DryCorner(state);
        var tools = new ToolController();
        tools.Select(ConstructionTool.Clear);
        tools.Press((x + 10, z + 10));
        tools.Hover((x, z));
        tools.UpdatePreview(state);

        Assert.Equal(new TileRect(x, z, x + 10, z + 10), tools.Preview.Area);
        var command = tools.Release(state, out _);
        if (command is null)
        {
            // No tree in this block: the clear is refused, which is also a valid outcome for the shape test.
            Assert.False(tools.IsDragging);
            return;
        }

        var clear = Assert.IsType<ClearAreaCommand>(command);
        Assert.Equal(new TileRect(x, z, x + 10, z + 10), clear.Area);
        Assert.False(tools.IsDragging);
    }

    [Fact]
    public void ReleasingAValidDrag_ReturnsTheCommand()
    {
        var state = NewState();
        var (x, z) = DryCorner(state);
        var tools = new ToolController();
        tools.Select(ConstructionTool.Lower);
        tools.Press((x, z));
        tools.Hover((x + 3, z + 2));

        var command = Assert.IsType<TerraformCommand>(tools.Release(state, out var reason));

        Assert.Null(reason);
        Assert.Equal(TerraformMode.Lower, command.Mode);
        Assert.Equal(new TileRect(x, z, x + 3, z + 2), command.Area);
    }

    [Fact]
    public void LineShape_FollowsTheDominantAxis()
    {
        var tools = new ToolController();
        tools.Select(new ConstructionTool("line", "toolLine", DragShape.Line, ConstructionTool.Raise.Plan, ConstructionTool.Raise.Create));

        Assert.Equal(((5, 5), (12, 5)), tools.Shape((5, 5), (12, 8)));
        Assert.Equal(((5, 5), (5, -3)), tools.Shape((5, 5), (7, -3)));
    }

    [Fact]
    public void Cancel_EndsTheDrag_ThenDeselectsTheTool()
    {
        var state = NewState();
        var tile = DryCorner(state);
        var tools = new ToolController();
        tools.Select(ConstructionTool.Raise);
        tools.Press(tile);

        Assert.True(tools.Cancel());
        Assert.False(tools.IsDragging);
        Assert.NotNull(tools.Active);
        Assert.Null(tools.Release(state, out var reason));
        Assert.Null(reason);

        Assert.True(tools.Cancel());
        Assert.Null(tools.Active);
        Assert.False(tools.Cancel());
    }

    [Fact]
    public void CancellingOrDeselecting_ReportsThePreviewGone()
    {
        // Hand test 2026-11-23: after cancelling, the marks on roads stayed on screen, because the
        // controller did not report that the preview had gone, so the old boxes were never replaced.
        var state = NewState();
        var (x, z) = DryCorner(state);
        var tools = new ToolController();
        tools.Select(ConstructionTool.Clear);
        tools.Press((x, z));
        tools.Hover((x + 10, z + 10));
        Assert.True(tools.UpdatePreview(state));

        tools.Cancel();       // Ends the drag; the cursor left the map at the same time.
        tools.Hover(null);
        Assert.True(tools.UpdatePreview(state));
        Assert.False(tools.HasPreview);

        tools.Hover((x, z));
        tools.UpdatePreview(state);
        tools.Cancel();       // Deselects the tool.
        Assert.True(tools.UpdatePreview(state));
        Assert.False(tools.HasPreview);
        Assert.False(tools.UpdatePreview(state));
    }

    [Fact]
    public void PreviewBoxes_MarkTheWholeArea_IncludingTilesThatDoNotChange()
    {
        // Hand test 2026-11-23: the player wants to see the whole dragged rectangle, not only changed tiles.
        var state = NewState();
        var (x, z) = DryCorner(state);
        var tools = new ToolController();
        tools.Select(ConstructionTool.Clear);
        tools.Press((x, z));
        tools.Hover((x + 9, z + 7));
        tools.UpdatePreview(state);
        var theme = Talt.Client.Ui.Theme.Load();
        var boxes = new PreviewBoxes();

        boxes.Build(tools.Preview, tools.PreviewResult.Succeeded, state.Map, theme);

        Assert.Equal(10 * 8, boxes.Boxes.Length); // One box per tile of the area, whatever its state.
        var neutral = boxes.Boxes.ToArray().Count(b => b.Color.W < 0.3f);
        Assert.Equal(80 - tools.Preview.ChangeCount - tools.Preview.ProblemCount, neutral);
    }

    [Fact]
    public void WithoutATool_PressIsLeftToTheCamera()
    {
        var tools = new ToolController();

        Assert.False(tools.Press((3, 3)));
        Assert.False(tools.IsDragging);
    }

    [Fact]
    public void RepeatedPreviewsOfA500TileDrag_AllocateNothing()
    {
        // SC-007: once warm, dragging over 25 × 20 tiles plans every new tile without allocating.
        var state = NewState();
        var (x, z) = DryCorner(state);
        var tools = new ToolController();
        tools.Select(ConstructionTool.Level);
        tools.Press((x, z));
        for (var i = 0; i < 2; i++)
        {
            tools.Hover((x + 24, z + 19 - i));
            tools.UpdatePreview(state);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 1000; i++)
        {
            tools.Hover((x + 24 - (i % 2), z + 19));
            tools.UpdatePreview(state);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void EveryReasonCode_HasATextInBothLanguages()
    {
        foreach (var language in new[] { "en", "es" })
        {
            var strings = Strings.Load(language);
            foreach (var code in ConstructionReasons.All)
            {
                Assert.True(strings.Contains($"construction.reason.{code}"), $"{language}: construction.reason.{code}");
            }

            foreach (var tool in ConstructionTool.All)
            {
                Assert.True(strings.Contains($"tool.{tool.Id}.tooltip"), $"{language}: tool.{tool.Id}.tooltip");
                Assert.True(strings.Contains($"action.{tool.ActionId}"), $"{language}: action.{tool.ActionId}");
            }
        }
    }

    [Fact]
    public void Trees_AreGatheredNearTheCentre_AndRebuiltOnlyWhenNeeded()
    {
        var map = SharedMap;
        var trees = new TreeInstances(radius: 20, maxTrees: 100);
        var center = new Vector3(64, 0, 64);

        Assert.True(trees.Update(map, center, sceneryVersion: 0));
        Assert.Equal(trees.TreeCount * 2, trees.Boxes.Length);
        Assert.True(trees.TreeCount <= 100);
        foreach (var box in trees.Boxes)
        {
            Assert.InRange(box.Min.X, 64 - 21, 64 + 21);
            Assert.True(box.Max.Y > box.Min.Y);
        }

        Assert.False(trees.Update(map, center + new Vector3(3, 0, 3), sceneryVersion: 0));
        Assert.True(trees.Update(map, center, sceneryVersion: 1));
        Assert.True(trees.Update(map, center + new Vector3(TreeInstances.RebuildStep, 0, 0), sceneryVersion: 1));
    }
}
