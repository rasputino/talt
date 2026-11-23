using Talt.Client.Picking;
using Talt.Client.Screens;
using Talt.Client.Tests.Ui;
using Talt.Core.Simulation;

namespace Talt.Client.Tests.Screens;

public class NewGameOptionsTests
{
    [Fact]
    public void Defaults_GiveTheUsualGame()
    {
        var source = new DefaultNewGameOptionsSource();
        var options = source.Create(source.Options.ToDictionary(o => o.Id, o => o.Default));
        Assert.Equal(new NewGameOptions(16, 20_000, 2026, 1950), options);
    }

    [Fact]
    public void InvalidValues_FallBackToDefaults()
    {
        var source = new DefaultNewGameOptionsSource();
        var options = source.Create(new Dictionary<string, int> { ["mapSize"] = 777, ["seed"] = 42, ["vehicles"] = 1000 });
        Assert.Equal(512, options.MapSizeTiles);
        Assert.Equal(42, options.Seed);
        Assert.Equal(1000, options.VehicleCount);
    }

    [Fact]
    public void NewGameScreen_StartsWithTheChosenValues()
    {
        var host = new FakeHost(UiTestKit.CreateContext());
        var screen = new NewGameScreen(host, new DefaultNewGameOptionsSource());
        screen.Start();
        Assert.Equal(512, host.Started!.MapSizeTiles);
        Assert.True(screen.HandleAction("back", false));
        Assert.Contains("mainMenu", host.Calls);
    }
}

public class MainMenuTests
{
    [Fact]
    public void Menu_OffersTheFourEntries_LoadIsDisabledWithTooltip()
    {
        var ui = UiTestKit.CreateContext();
        var host = new FakeHost(ui);
        var menu = new MainMenuScreen(host);
        Assert.Equal(4, menu.Buttons.Count);
        Assert.False(menu.Buttons[1].Enabled);
        Assert.NotNull(menu.Buttons[1].Tooltip);
        menu.Buttons[0].Click();
        menu.Buttons[1].Click();
        menu.Buttons[3].Click();
        Assert.Equal(["newGame", "quit"], host.Calls);
    }
}

public class MenuLayoutTests
{
    [Theory]
    [InlineData("en", 1f)]
    [InlineData("es", 1f)]
    [InlineData("es", 2f)]
    public void MenuTexts_AreNeverCut(string language, float scale)
    {
        // Found in the hand test: the title and the Spanish subtitle were cut with an ellipsis.
        var ui = UiTestKit.CreateContext(language, scale, 1600, 900);
        ui.Screen = new MainMenuScreen(new FakeHost(ui)).Root;
        ui.Layout();
        foreach (var label in UiTestKit.Walk(ui.Screen).OfType<Talt.Client.Ui.Widgets.Label>())
        {
            var text = label.Text.Resolve(ui);
            var width = Talt.Client.Ui.Font.TextLayout.Measure(ui.Font, text, ui.FontSize(label.Size));
            Assert.True(label.Bounds.Width + 0.5f >= width, $"'{text}' needs {width:F0} px, has {label.Bounds.Width}");
        }
    }
}

public class InGameScreenTests
{
    private static (InGameScreen Screen, GameSession Session, FakeHost Host) Create()
    {
        var ui = UiTestKit.CreateContext();
        var host = new FakeHost(ui);
        var game = GameSetup.Create(new NewGameOptions(ChunksPerSide: 4, VehicleCount: 50, Seed: 5));
        var session = new GameSession(game, new SessionOptions(), fastForwardUnlocked: false);
        var screen = new InGameScreen(host, session, new Talt.Client.Rendering.OrbitCamera(), _ => { }, _ => false);
        ui.Screen = screen.Root;
        screen.ApplySettings(host.Settings);
        return (screen, session, host);
    }

    [Fact]
    public void SpeedButtonsAndKeys_ChangeTheSameRunner()
    {
        var (screen, session, _) = Create();
        screen.HandleAction("speedFast", false);
        Assert.Equal(GameSpeed.Fast, session.Runner.Speed);
        screen.HudButtons.First(b => b.ShortcutAction == "speedVeryFast").Click();
        Assert.Equal(GameSpeed.VeryFast, session.Runner.Speed);
        screen.HandleAction("speedNormal", false);
        Assert.Equal(GameSpeed.Normal, session.Runner.Speed);
    }

    [Fact]
    public void FastForward_IsHiddenWhileLocked()
    {
        var (screen, _, host) = Create();
        Assert.False(screen.FastForwardButton.Visible);
        host.Settings.FastForwardUnlocked = true;
        screen.ApplySettings(host.Settings);
        Assert.True(screen.FastForwardButton.Visible);
    }

    [Fact]
    public void Escape_ClosesWindowsFirst_ThenPausesWithTheMenu()
    {
        var (screen, session, host) = Create();
        host.Ui.Windows.Open(SampleWindows.Create(1));
        screen.HandleAction("back", false);
        Assert.Equal(0, host.Ui.Windows.Count);
        Assert.False(screen.IsPauseMenuOpen);
        screen.HandleAction("back", false);
        Assert.True(screen.IsPauseMenuOpen);
        Assert.True(session.Runner.IsPaused);
        screen.HandleAction("back", false);
        Assert.False(screen.IsPauseMenuOpen);
        Assert.Equal(GameSpeed.Normal, session.Runner.Speed);
    }

    [Fact]
    public void Selecting_OpensOneWindowPerObject()
    {
        var (screen, _, host) = Create();
        var tile = new PickResult(PickKind.Tile, 1, 2, 5, Core.Voxels.VoxelMaterial.Grass, -1, default, default, 1);
        var other = tile with { TileX = 9 };
        screen.Select(tile);
        screen.Select(other);
        screen.Select(tile);
        Assert.Equal(2, host.Ui.Windows.Count);
        Assert.Equal(tile.Key, host.Ui.Windows.Front!.Key);
    }

    [Fact]
    public void ClickOnTheMap_OpensTheInformationWindowOfTheHoveredObject()
    {
        // Regression (hand test): picking pauses while the button is held, so the click used to see
        // "nothing" on release and only cleared the hover.
        var (screen, session, host) = Create();
        var camera = (Talt.Client.Rendering.OrbitCamera)typeof(InGameScreen)
            .GetField("_camera", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(screen)!;
        camera.Target = new System.Numerics.Vector3(40.5f, session.Heightmap.Get(40, 40), 40.5f);
        camera.Distance = 60;
        var viewport = new System.Numerics.Vector2(1600, 900);
        session.BuildInstances();
        screen.UpdatePicking(viewport / 2, viewport, cursorOverWorld: true);
        Assert.NotEqual(PickKind.None, screen.Hovered.Kind);
        var world = (Talt.Client.Input.IWorldInput)screen;
        world.MouseDown(Silk.NET.Input.MouseButton.Left, viewport / 2);
        screen.UpdatePicking(viewport / 2, viewport, cursorOverWorld: false); // As while the button is held.
        world.MouseUp(Silk.NET.Input.MouseButton.Left, viewport / 2, isClick: true);
        Assert.Equal(1, host.Ui.Windows.Count);
        Assert.Equal(ObjectInfoWindow.Kind, host.Ui.Windows.Front!.Kind);
        Assert.NotEqual(PickKind.None, screen.Selected.Kind);
    }

    [Fact]
    public void HudButtons_AllHaveTooltips()
    {
        var (screen, _, _) = Create();
        Assert.All(screen.HudButtons, b => Assert.NotNull(b.Tooltip));
    }
}
