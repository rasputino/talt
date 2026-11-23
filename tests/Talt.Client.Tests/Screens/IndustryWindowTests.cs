using Talt.Client.Picking;
using Talt.Client.Screens;
using Talt.Client.Ui.Widgets;
using Talt.Core.Voxels;

namespace Talt.Client.Tests.Screens;

public class IndustryWindowTests
{
    [Fact]
    public void IndustryWindowUsesTheIndustryKeyAndHasContent()
    {
        var game = GameSetup.Create(new NewGameOptions(ChunksPerSide: 8, VehicleCount: 0, Seed: 7));
        var session = new GameSession(game, new SessionOptions(), fastForwardUnlocked: true);
        var industries = session.Simulation.Industries;
        Assert.True(industries.Count > 0);
        var id = industries.IdAt(0);

        var pick = new PickResult(PickKind.Industry, industries.XAt(0), industries.ZAt(0), 0, VoxelMaterial.Air, -1, id, default, default, 1);
        var window = ObjectInfoWindow.Create(pick, session);

        Assert.Equal($"industry:{id}", window.Key);
        var panel = Assert.IsType<StackPanel>(window.Content);
        Assert.True(panel.ChildCount >= 3);
        Assert.NotEmpty(window.Title.Key ?? string.Empty);
    }

    [Fact]
    public void ClosedIndustryShowsTheGoneMessage()
    {
        var game = GameSetup.Create(new NewGameOptions(ChunksPerSide: 8, VehicleCount: 0, Seed: 7));
        var session = new GameSession(game, new SessionOptions(), fastForwardUnlocked: true);
        var pick = new PickResult(PickKind.Industry, 0, 0, 0, VoxelMaterial.Air, -1, 999_999, default, default, 1);

        var window = ObjectInfoWindow.Create(pick, session);

        var panel = Assert.IsType<StackPanel>(window.Content);
        var label = Assert.IsType<Label>(panel.GetChild(0));
        Assert.Equal("info.industry.gone", label.Text.Key);
    }
}
