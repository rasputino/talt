using Talt.Client.Localization;
using Talt.Client.Picking;
using Talt.Client.Screens;
using Talt.Client.Tests.Ui;
using Talt.Client.Ui;
using Talt.Client.Ui.Widgets;
using Talt.Client.Ui.Windows;
using Talt.Core.Rails;
using Talt.Core.Stations;
using Talt.Tests.Support;
using Talt.Core.Voxels;

namespace Talt.Client.Tests.Screens;

/// <summary>The graphical info windows: cargo icons, gauges, the load bar by destination and the line's stop strip.</summary>
public class InfoWindowTests
{
    // One seeder per game: each predicts the ids of what it builds from the registries, so they cannot be combined. Seed 2 gets
    // vehicles and trains on an 8-chunk map (some seeds place no connected bays).
    private static readonly Lazy<GameSession> Game = new(() => Start(new NewGameOptions(ChunksPerSide: 8, VehicleCount: 0, Seed: 2, SeedVehicles: 4)));

    private static readonly Lazy<GameSession> RailGame = new(() =>
    {
        // Seeded trains wait in their depots: start them so they run on their lines.
        var session = Start(new NewGameOptions(ChunksPerSide: 8, VehicleCount: 0, Seed: 3, SeedTrains: 2));
        var trains = session.Simulation.Trains!;
        for (var i = 0; i < trains.Count; i++)
        {
            session.Submit(new StartTrainCommand(session.Construction.Player.Id, trains.TrainAt(i).Id, true));
        }

        for (var i = 0; i < 400; i++)
        {
            session.Simulation.Tick();
        }

        return session;
    });

    private static GameSession Start(NewGameOptions options)
    {
        var session = new GameSession(
            GameSetup.Create(options),
            new SessionOptions(),
            fastForwardUnlocked: true);
        session.Simulation.ProcessPendingCommands();
        for (var i = 0; i < 400; i++)
        {
            session.Simulation.Tick();
        }

        return session;
    }

    [Fact]
    public void EveryCargo_HasAnIcon()
    {
        var content = Game.Value.Simulation.Content;
        for (var c = 0; c < content.CargoCount; c++)
        {
            Assert.True(UiTestKit.Icons.Contains(CargoBadge.IconName(content.Cargo(c).Id)), content.Cargo(c).Id);
        }
    }

    [Fact]
    public void Ramp_GoesFromNegativeThroughWarningToPositive()
    {
        var theme = Theme.Load();
        Assert.Equal(theme.Negative, MeterColors.Ramp(theme, 0f));
        Assert.Equal(theme.Warning, MeterColors.Ramp(theme, 0.5f));
        Assert.Equal(theme.Positive, MeterColors.Ramp(theme, 1f));
        Assert.Equal(theme.Positive, MeterColors.Ramp(theme, 7f));
        Assert.Equal(MeterColors.FromRgb(0x2E77C8), UiColor.FromBytes(0x2E, 0x77, 0xC8, 255));
    }

    [Theory]
    [InlineData(0, 3, true, 0f)]
    [InlineData(2, 3, true, 2f)]
    [InlineData(2, 3, false, 1.5f)]
    [InlineData(0, 3, false, 2.5f)] // Driving back from the last stop to the first.
    [InlineData(4, 3, true, 1f)]
    public void StopStrip_PlacesVehiclesOnOrBetweenStops(int stopIndex, int stops, bool atStop, float expected) =>
        Assert.Equal(expected, StopStrip.Position(stopIndex, stops, atStop));

    [Fact]
    public void HoldSegments_SumsByDestination_LargestFirst()
    {
        var hold = new CargoHold();
        hold.Add(new HeldCargo(0, 5, 0, 0, Destination: 3, 0));
        hold.Add(new HeldCargo(0, 9, 0, 0, Destination: 1, 0));
        hold.Add(new HeldCargo(0, 2, 0, 0, Destination: 3, 0));
        hold.Add(new HeldCargo(0, 4, 0, 0, Destination: -1, 0));
        for (var town = 10; town < 10 + HoldSegments.MaxDestinations; town++)
        {
            hold.Add(new HeldCargo(0, 1, 0, 0, Destination: town, 0));
        }

        var segments = new HoldSegments(() => 1, () => 1, _ => hold, UiColor.White).Refresh();

        Assert.Equal(26, segments.Total);
        Assert.Equal(HoldSegments.MaxDestinations, segments.DestinationCount);
        Assert.Equal((1, 9), (segments.TownAt(0), segments.AmountAt(0)));
        Assert.Equal((3, 7), (segments.TownAt(1), segments.AmountAt(1)));
        Assert.Equal((10, 1), (segments.TownAt(2), segments.AmountAt(2)));
        Assert.Equal(2, segments.OtherAmount); // Towns 14 and 15.
        Assert.Equal(4, segments.UndirectedAmount);
        var sum = 0;
        for (var i = 0; i < segments.SegmentCount; i++)
        {
            sum += segments.Segment(i).Amount;
        }

        Assert.Equal(segments.Total, sum);
        foreach (var language in Strings.SupportedLanguages)
        {
            var text = VehicleGauges.Destinations(Strings.Load(language), null, segments);
            Assert.DoesNotContain("[", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ClippedLabel_DoesNotAllocate()
    {
        // Regression: the ellipsis of a cut text was a span literal that allocated on every frame (the info windows'
        // narrow label column cuts long town names).
        var ui = UiTestKit.CreateContext();
        ui.Screen = new Label(TextRef.Dynamic(() => 0, _ => "A very long town name that does not fit")) { Width = 40 };
        AssertSteady(ui);
    }

    [Fact]
    public void StationAndTownWindows_HaveGauges_AndSteadyFramesDoNotAllocate()
    {
        var session = Game.Value;
        var stations = session.Simulation.Stations!;
        var ui = UiTestKit.CreateContext();
        var station = ObjectInfoWindow.Create(new PickResult(PickKind.Station, 0, 0, 0, VoxelMaterial.Air, -1, -1, default, default, 1, StationId: stations.IdAt(0)), session);
        var town = ObjectInfoWindow.Create(new PickResult(PickKind.Town, 0, 0, 0, VoxelMaterial.Air, -1, -1, default, default, 1, TownId: 0), session);
        ui.Windows.Open(station);
        ui.Windows.Open(town);
        AssertSteady(ui);

        Assert.True(UiTestKit.Walk(town.Content!).OfType<Meter>().Count(m => m.Visible) >= 4); // Passengers, mail, rating, growth.
        Assert.Contains(UiTestKit.Walk(station.Content!), w => w is CargoBadge { Visible: true });
    }

    [Fact]
    public void VehicleTrainAndLineWindows_DrawTheirGauges_WithoutSteadyAllocations()
    {
        var session = Game.Value;
        var fake = new FakeVehicleUi(session);
        var vehicles = session.RoadVehicles!;
        Assert.True(vehicles.Count > 0, $"vehicles {vehicles.Count}, trains {session.Simulation.Trains?.Count}, lines {vehicles.Lines.Count}");
        var vehicleId = vehicles.IdAt(0);
        var lineId = vehicles.LineAt(0);
        Assert.True(lineId >= 0);

        var ui = UiTestKit.CreateContext("es");
        var vehicle = VehicleWindow.Create(fake, vehicleId);
        var line = LineWindow.Create(fake, lineId);
        ui.Windows.Open(vehicle);
        ui.Windows.Open(line);
        AssertSteady(ui);
        Assert.Contains(UiTestKit.Walk(vehicle.Content!), w => w is SegmentBar { Visible: true });
        var strip = Assert.Single(UiTestKit.Walk(line.Content!).OfType<StopStrip>());
        Assert.True(strip.Visible);
        Assert.All(strip.MarkerPositions, p => Assert.InRange(p, 0f, vehicles.Lines.StopCount(lineId)));
    }

    [Fact]
    public void TrainWindowAndItsLine_DrawTheirGauges_WithoutSteadyAllocations()
    {
        var session = RailGame.Value;
        var trains = session.Simulation.Trains!;
        Assert.True(trains.Count > 0);
        var train = trains.TrainAt(0);
        Assert.True(train.Line >= 0);
        var fake = new FakeVehicleUi(session);
        var ui = UiTestKit.CreateContext();
        var window = ui.Windows.Open(TrainWindow.Create(fake, train.Id));
        var line = ui.Windows.Open(LineWindow.Create(fake, train.Line));
        AssertSteady(ui);

        Assert.Contains(UiTestKit.Walk(window.Content!), w => w is SegmentBar { Visible: true });
        var strip = Assert.Single(UiTestKit.Walk(line.Content!).OfType<StopStrip>());
        Assert.True(strip.MarkerPositions.Count > 0, $"{TrainSystem.StatusOf(train)} flags {train.Flags} mode {train.Mode}"); // Trains show on the strip of their line.
        Assert.Contains(UiTestKit.Walk(line.Content!), w => w is Button b && b.Text is { } t && t.Resolve(ui).Contains(TrainTexts.Name(ui.Strings, trains, train.Id), StringComparison.Ordinal));
    }

    [Fact]
    public void DepotCatalogShowsSelectedVoxelModelWithoutFrameAllocations()
    {
        var session = RailGame.Value;
        session.UseModels(Talt.Client.Rendering.ModelLibrary.LoadShipped());
        var train = session.Simulation.Trains!.TrainAt(0);
        var ui = UiTestKit.CreateContext();
        var window = ui.Windows.Open(RailDepotWindow.Create(new FakeVehicleUi(session), train.HomeDepot));
        AssertSteady(ui);
        Assert.Single(UiTestKit.Walk(window.Content!).OfType<CatalogPreview>());
    }

    [Fact]
    public void ChoosingClearviewRevealsTimingInTheOpenDepot()
    {
        var session = Start(new NewGameOptions(ChunksPerSide: 8, VehicleCount: 0, Seed: 3, StartYear: 1985, SeedTrains: 2));
        var ui = UiTestKit.CreateContext("es");
        var depot = session.Simulation.Trains!.TrainAt(0).HomeDepot;
        var window = ui.Windows.Open(RailDepotWindow.Create(new FakeVehicleUi(session), depot));
        UiTestKit.Frame(ui);
        var source = UiTestKit.Walk(window.Content!).OfType<Dropdown>().First();
        source.OnSelected(source.Choices.ToList().FindIndex(c => c.Text.Resolve(ui) == ui.Strings.Get("rail.model.wagon-coach")));
        UiTestKit.Frame(ui);
        var target = UiTestKit.Walk(window.Content!).OfType<Dropdown>().Single(d => d.Choices.Any(c => Equals(c.Value, "wagon-clearview")));
        var timing = UiTestKit.Walk(window.Content!).OfType<Checkbox>().Single(c => c.Text.Resolve(ui) == ui.Strings.Get("catalog.defer"));
        Assert.False(timing.Visible);
        target.OnSelected(target.Choices.ToList().FindIndex(c => Equals(c.Value, "wagon-clearview")));
        session.Simulation.Tick();
        UiTestKit.Frame(ui);
        Assert.True(timing.Visible);
        Assert.False(timing.IsChecked());
        timing.OnChanged(true);
        session.Simulation.Tick();
        Assert.True(timing.IsChecked());
        target.OnSelected(0);
        session.Simulation.Tick();
        UiTestKit.Frame(ui);
        Assert.False(timing.Visible);
    }

    private static void AssertSteady(UiContext ui)
    {
        var list = new UiDrawList();
        for (var i = 0; i < 5; i++)
        {
            ui.Update(0.016);
            UiTestKit.Frame(ui, list);
        }

        AllocationAssert.NoSteadyStateAllocations(
            () =>
            {
                ui.Update(0.016);
                UiTestKit.Frame(ui, list);
            },
            iterationsPerRound: 20,
            "20 info window frames");
    }

    private sealed class FakeVehicleUi(GameSession session) : ITrainUi
    {
        public GameSession Session { get; } = session;

        public string Money(long amount, Strings s) => amount.ToString("N0", s.Culture);

        public void OpenVehicle(int vehicleId)
        {
        }

        public void OpenDepot(int depotId)
        {
        }

        public void OpenLine(int lineId)
        {
        }

        public void StartLineMode(int lineId, int vehicleId)
        {
        }

        public void OpenTrain(int trainId)
        {
        }

        public void OpenRailDepot(int depotId)
        {
        }
    }
}
