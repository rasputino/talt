using Talt.Client.Picking;
using Talt.Client.Screens;
using Talt.Client.Tests.Ui;
using Talt.Core.Simulation;

namespace Talt.Client.Tests.Screens;

public class NewGameOptionsTests
{
    [Fact]
    public void Defaults_GiveA512MapWithTheMapGeneratorDefaults()
    {
        var source = new DefaultNewGameOptionsSource();
        var options = source.Create(source.Options.ToDictionary(o => o.Id, o => o.Default));
        var library = new Talt.Core.Maps.MapSettings();
        Assert.Equal(new Talt.Core.Maps.MapSettings(512, 512, 2026, library.Roughness, library.MaxHeight, library.WaterAmount, library.SeaLevel), options.Map);
        Assert.Equal((16, 20_000, 2026, 1950), (options.ChunksPerSide, options.VehicleCount, options.Seed, options.StartYear));
    }

    [Fact]
    public void ChosenValues_ReachTheMapSettings()
    {
        // Task T069: the screen is bound to feature 004's MapSettings.
        var source = new DefaultNewGameOptionsSource();
        var options = source.Create(new Dictionary<string, int>
        {
            ["width"] = 1024, ["depth"] = 256, ["seed"] = 42, ["roughness"] = 80,
            ["maxHeight"] = 60, ["water"] = 40, ["seaLevel"] = 12, ["vehicles"] = 1000,
        });
        Assert.Equal("T1-1024x256-s42-r80-h60-w40-l12", options.MapSettings.ToText());
        Assert.Equal(256, options.MapSizeTiles); // The road network covers the square part.
        Assert.Equal(1000, options.VehicleCount);
    }

    [Fact]
    public void InvalidValues_FallBackToDefaults()
    {
        var source = new DefaultNewGameOptionsSource();
        var options = source.Create(new Dictionary<string, int> { ["width"] = 777, ["roughness"] = 500, ["seed"] = 42 });
        Assert.Equal(512, options.Map!.Width);
        Assert.Equal(new Talt.Core.Maps.MapSettings().Roughness, options.Map.Roughness);
        Assert.Equal(42, options.Map.Seed);
    }

    [Fact]
    public void SeaLevel_StaysBelowTheMaximumHeight()
    {
        var host = new FakeHost(UiTestKit.CreateContext());
        var screen = new NewGameScreen(host, new DefaultNewGameOptionsSource());
        screen.SetValue("seaLevel", 50);
        screen.SetValue("maxHeight", 20);
        Assert.Equal(18, screen.Values["seaLevel"]);
        screen.Start();
        host.Started!.MapSettings.Validate();
    }

    [Fact]
    public void NewGameScreen_StartsWithTheChosenValues()
    {
        var host = new FakeHost(UiTestKit.CreateContext());
        var screen = new NewGameScreen(host, new DefaultNewGameOptionsSource());
        screen.SetValue("width", 2048);
        screen.Start();
        Assert.Equal(2048, host.Started!.Map!.Width);
        Assert.True(screen.HandleAction("back", false));
        Assert.Contains("mainMenu", host.Calls);
    }

    [Fact]
    public void GeneratingScreen_ShowsTheStageAndPercentage()
    {
        var ui = UiTestKit.CreateContext("es");
        var screen = new NewGameScreen(new FakeHost(ui), new DefaultNewGameOptionsSource());
        var progress = new GenerationProgress();
        screen.SetGenerating(true, progress: progress);
        var status = UiTestKit.Walk(screen.Root).OfType<Talt.Client.Ui.Widgets.Label>().First(l => l.Visible && l.Wrap);
        Assert.Equal("Preparando carreteras y tráfico…", status.Text.Resolve(ui));
        progress.Report(new Talt.Core.Maps.MapGenerationProgress(Talt.Core.Maps.MapGenerationStage.Rivers, 0.42f));
        Assert.Equal("Generando el mapa: Excavando ríos (42 %)", status.Text.Resolve(ui));
    }

    [Fact]
    public void Progress_NeverGoesBackwards_WhenReportsArriveOutOfOrder()
    {
        var progress = new GenerationProgress();
        progress.Report(new Talt.Core.Maps.MapGenerationProgress(Talt.Core.Maps.MapGenerationStage.World, 0.8f));
        progress.Report(new Talt.Core.Maps.MapGenerationProgress(Talt.Core.Maps.MapGenerationStage.World, 0.6f));
        progress.Report(new Talt.Core.Maps.MapGenerationProgress(Talt.Core.Maps.MapGenerationStage.Sites, 0.5f));
        Assert.Equal(((int)Talt.Core.Maps.MapGenerationStage.World, 0.8f), (progress.Stage, progress.Fraction));
    }

    [Fact]
    public void MapCode_FollowsTheChosenValues()
    {
        var ui = UiTestKit.CreateContext();
        var screen = new NewGameScreen(new FakeHost(ui), new DefaultNewGameOptionsSource());
        var code = UiTestKit.Walk(screen.Root).OfType<Talt.Client.Ui.Widgets.Label>().First(l => l.Text.Resolve(ui).StartsWith("Map code", StringComparison.Ordinal));
        Assert.Contains("512x512-s2026", code.Text.Resolve(ui), StringComparison.Ordinal);
        screen.SetValue("seed", 7);
        Assert.Contains("512x512-s7-", code.Text.Resolve(ui), StringComparison.Ordinal);
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
        var screen = new InGameScreen(host, session, new Talt.Client.Rendering.OrbitCamera(), _ => false);
        ui.Screen = screen.Root;
        screen.ApplySettings(host.Settings);
        return (screen, session, host);
    }

    [Fact]
    public void Notifications_HaveAPanelBehindTheText()
    {
        // Hand test of feature 006: "Not possible: ..." was hard to read straight over the terrain.
        var (screen, _, _) = Create();

        screen.ShowMessage(Talt.Client.Ui.TextRef.Of("command.rejected.unknown"));

        var panel = Assert.IsType<Talt.Client.Ui.Widgets.Panel>(screen.Messages.GetChild(screen.Messages.ChildCount - 1));
        Assert.Equal(Talt.Client.Ui.Widgets.PanelStyle.Panel, panel.Style);
        Assert.IsType<Talt.Client.Ui.Widgets.Label>(panel.GetChild(0));
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
        var tile = new PickResult(PickKind.Tile, 1, 2, 5, Core.Voxels.VoxelMaterial.Grass, -1, -1, default, default, 1);
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
