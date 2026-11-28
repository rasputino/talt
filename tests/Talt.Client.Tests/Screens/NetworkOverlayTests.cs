using System.Numerics;
using MouseButton = Silk.NET.Input.MouseButton;
using Talt.Client.Rendering;
using Talt.Client.Screens;
using Talt.Client.Tests.Ui;
using Talt.Client.Ui;
using Talt.Client.Ui.Widgets;
using Talt.Core.Stations;
using Talt.Tests.Support;

namespace Talt.Client.Tests.Screens;

public sealed class NetworkOverlayTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EveryPartOfAPlannedLegFollowsAnExistingPhysicalLink(bool rail)
    {
        var game = new GameSession(GameSetup.Create(new NewGameOptions(ChunksPerSide: 8, VehicleCount: 0,
            Seed: rail ? 3 : 2, SeedVehicles: rail ? 0 : 4, SeedTrains: rail ? 2 : 0)), new SessionOptions(), true);
        game.Simulation.ProcessPendingCommands();
        var vehicles = game.Simulation.RoadVehicles!;
        var lines = vehicles.Lines;
        var line = rail ? game.Simulation.Trains!.TrainAt(0).Line : vehicles.LineAt(0);
        var origin = lines.StopAt(line, 0).StationId;
        var destination = lines.StopAt(line, 1).StationId;
        var router = new LineOverlayRouter(game.Construction, game.Simulation.Stations!);
        var path = new List<int>();
        var found = rail ? router.FindRailLeg(origin, destination, game.Construction.Player.Id, false, path)
            : router.FindRoadLeg(origin, destination, StationPieceKind.Bus, path);
        Assert.True(found);
        Assert.True(path.Count >= 2);
        for (var i = 1; i < path.Count; i++)
        {
            var from = path[i - 1];
            var links = rail ? game.Construction.Rails.LinksAt(from) : game.Construction.Roads.ExitsAt(from);
            var valid = false;
            for (var d = 0; d < 8; d++)
            {
                if ((links & (1 << d)) != 0)
                {
                    var to = rail ? game.Construction.Rails.FollowLink(from, d) : game.Construction.Roads.FollowLink(from, d);
                    valid |= path[i] == to;
                }
            }

            Assert.True(valid);
        }

        if (!rail)
        {
            var stations = game.Simulation.Stations!;
            var roads = game.Construction.Roads;
            foreach (var tile in stations.TilesAt(stations.SlotOf(origin)))
            {
                // Keep both stations and the physical road in place, with no legal exit from the first.
                roads.Set(tile, roads.PieceAt(tile) with { Exits = 0 });
            }

            Assert.False(router.FindRoadLeg(origin, destination, StationPieceKind.Bus, path));
            Assert.Empty(path);
        }

        Assert.False(router.FindRoadLeg(origin, -1, StationPieceKind.Bus, path));
        Assert.Empty(path);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void LayerControlsExplainEmptyDataAndLocateRenderedSegments(int buses)
    {
        var session = new GameSession(GameSetup.Create(new NewGameOptions(ChunksPerSide: 8, VehicleCount: 0,
            Seed: 2, SeedVehicles: buses)), new SessionOptions(), true);
        var game = session.Simulation;
        game.ProcessPendingCommands();
        var camera = new OrbitCamera { Target = new(-500, 0, -500), Distance = 30 };
        var ui = UiTestKit.CreateContext("es");
        var screen = new InGameScreen(new FakeHost(ui), session, camera, _ => false);
        ui.Screen = screen.Root;
        var draw = new UiDrawList();
        var overlay = UiTestKit.Walk(screen.Root).OfType<NetworkOverlay>().Single();
        void Frame()
        {
            screen.UpdatePicking(Vector2.Zero, ui.ViewportSize, cursorOverWorld: false);
            UiTestKit.Frame(ui, draw);
        }

        void Click(string key)
        {
            var button = UiTestKit.Walk(screen.Root).OfType<Button>().Single(b => b.Text?.Key == key);
            Assert.True(button.Visible);
            Assert.True(button.Bounds.Width > 0 && button.Bounds.Height > 0);
            Assert.Same(button, ui.HitTest(button.Bounds.Center));
            Assert.True(ui.MouseDown(MouseButton.Left, button.Bounds.Center));
            Assert.True(ui.MouseUp(MouseButton.Left, button.Bounds.Center));
            Frame();
        }

        bool Message(string key) => UiTestKit.Walk(screen.Root).OfType<Label>().Single(l => l.Text.Key == key).Visible;

        Frame();
        Click("map.layers");
        Click("map.routes");
        for (var i = 0; i < 100 && overlay.Routing; i++)
        {
            Frame();
        }

        Frame();
        Assert.False(overlay.Routing);
        Assert.Equal(buses == 0, Message("map.routes.empty"));
        Assert.Equal(buses > 0, Message("map.routes.offscreen"));
        if (buses > 0)
        {
            Click("map.routes.locate");
            Frame();
            Assert.False(Message("map.routes.offscreen"));
            Assert.True(overlay.RoutesInView);
            Assert.Contains(draw.Vertices.ToArray(), v => v.Color == UiColor.Parse("#00B8D4").Packed && v.Local == Vector2.Zero);
        }

        Click("map.traffic");
        Assert.Contains(UiTestKit.Walk(screen.Root).OfType<Label>(), l => l.Visible && l.CurrentText(ui) == ui.Strings.Get("map.traffic.unavailable"));
        for (var i = 0; i < 31 * Talt.Core.Simulation.GameClock.TicksPerDay; i++)
        {
            game.Tick(false);
        }

        camera.Target = new(-500, 0, -500);
        Frame();
        Frame();
        Assert.Equal(buses == 0, Message("map.traffic.empty"));
        Assert.Equal(buses > 0, Message("map.traffic.offscreen"));
        var period = ui.Strings.Format("map.traffic.period", ("month", ui.Strings.Get("month.1")), ("year", "1950"));
        Assert.Contains(UiTestKit.Walk(screen.Root).OfType<Label>(), label => label.Visible && label.CurrentText(ui) == period);
        if (buses > 0)
        {
            Click("map.traffic.locate");
            Frame();
            Assert.True(overlay.TrafficInView);
            Assert.True(overlay.RoutesInView);
            Assert.False(Message("map.traffic.offscreen"));
            Assert.Contains(draw.Vertices.ToArray(), v => NetworkOverlay.HeatColors.Contains(new(v.Color)) && v.Local == Vector2.Zero);
        }
    }

    [Fact]
    public void MapLayersDrawWithoutAllocatingOnceVisible()
    {
        var session = new GameSession(GameSetup.Create(new NewGameOptions(ChunksPerSide: 8, VehicleCount: 0,
            Seed: 2, SeedVehicles: 4)), new SessionOptions(), true);
        var game = session.Simulation;
        game.ProcessPendingCommands();
        for (var i = 0; i < 31 * Talt.Core.Simulation.GameClock.TicksPerDay; i++)
        {
            game.Tick(false);
        }

        var overlay = new NetworkOverlay(game) { Routes = true, Areas = true, Traffic = true };
        var ui = UiTestKit.CreateContext();
        var viewport = new Vector2(1000, 700);
        var camera = new OrbitCamera { Target = new(128, 20, 128), Distance = 550 };
        var projection = camera.ViewProjection(viewport.X / viewport.Y);
        overlay.Measure(ui, viewport);
        overlay.Arrange(ui, new(0, 0, viewport.X, viewport.Y));
        var draw = new UiDrawList();
        void Frame()
        {
            overlay.Update(projection, viewport);
            draw.Begin(viewport.X, viewport.Y);
            overlay.Draw(ui, draw);
            draw.End();
        }

        Frame();
        Assert.True(draw.QuadCount > 0);
        Assert.False(overlay.HasGaps);
        AllocationAssert.NoSteadyStateAllocations(Frame, 12, "network overlays");
        overlay.Routes = overlay.Areas = overlay.Traffic = false;
        Frame();
        Assert.Equal(0, draw.QuadCount);
    }
}
