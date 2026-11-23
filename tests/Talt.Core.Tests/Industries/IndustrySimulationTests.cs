using Talt.Core.Commands;
using Talt.Core.Content;
using Talt.Core.Determinism;
using Talt.Core.Industries;
using Talt.Core.Maps;
using Talt.Core.Scenarios;
using Talt.Core.Simulation;

namespace Talt.Core.Tests.Industries;

public class IndustrySimulationTests
{
    [Fact]
    public void SimulationWithoutAMapHasNoIndustries()
    {
        var simulation = GameSimulation.Create(new ScenarioSettings(Seed: 1, MapSizeTiles: 256, VehicleCount: 0, LineCount: 1));

        Assert.Equal(0, simulation.Industries.Count);
        Assert.Equal(0, simulation.Industries.SiteCount);
    }

    [Fact]
    public void SimulationWithAMapOwnsItsIndustries()
    {
        var (map, content) = Fixture();
        var simulation = GameSimulation.Create(Settings(), map, content);

        Assert.Equal(1, simulation.Industries.Count);
        Assert.Same(content, simulation.Content);
        Assert.True(simulation.Industries.TryGetIndustryAt(32, 32, out _));
    }

    [Fact]
    public void DayEventAddsOneDayOfProduction()
    {
        var (map, content) = Fixture();
        var simulation = GameSimulation.Create(Settings(), map, content);
        var cargo = content.CargoIndex("test-cargo");

        for (var tick = 0; tick < GameClock.TicksPerDay; tick++)
        {
            simulation.Tick();
        }

        Assert.Equal(3, simulation.Industries.AvailableAt(0, cargo)); // 100 units / 31 days, rounded down.
    }

    [Fact]
    public void MonthEventRotatesStatistics()
    {
        var (map, content) = Fixture();
        var simulation = GameSimulation.Create(Settings(), map, content);
        var id = simulation.Industries.IdAt(0);
        var cargo = content.CargoIndex("test-cargo");

        for (var tick = 0; tick < GameClock.TicksPerDay * 31; tick++)
        {
            simulation.Tick();
        }

        Assert.True(simulation.Industries.TryGetIndustry(id, out var info));
        Assert.Equal(100, simulation.Industries.AvailableAt(0, cargo));
        Assert.All(info.LastMonth, entry => Assert.Equal(0, entry.Accepted));
    }

    [Fact]
    public void ParallelAndSequentialTicksAreIdentical()
    {
        var (map, content) = Fixture();
        var parallel = GameSimulation.Create(Settings(), map, content);
        var sequential = GameSimulation.Create(Settings(), map, content);

        for (var tick = 0; tick < GameClock.TicksPerDay * 40; tick++)
        {
            parallel.Tick(parallel: true);
            sequential.Tick(parallel: false);
        }

        Assert.Equal(parallel.ComputeFingerprint(), sequential.ComputeFingerprint());
    }

    [Fact]
    public void IndustryStateIsPartOfTheFingerprint()
    {
        var (map, content) = Fixture();
        var simulation = GameSimulation.Create(Settings(), map, content);
        var initial = simulation.ComputeFingerprint();

        for (var tick = 0; tick < GameClock.TicksPerDay; tick++)
        {
            simulation.Tick();
        }

        Assert.NotEqual(initial, simulation.ComputeFingerprint());
    }

    [Fact]
    public void RecordedSessionWithAMapReplaysToTheSameIndustryState()
    {
        var settings = new ScenarioSettings(
            Seed: 4,
            MapSizeTiles: 128,
            VehicleCount: 0,
            LineCount: 1,
            Map: new MapSettings(128, 128, Seed: 4));
        var simulation = GameSimulation.Create(settings);
        var recorder = new SessionRecorder(new RecordedSession(settings));
        simulation.AttachRecorder(recorder);
        for (var tick = 0; tick < GameClock.TicksPerDay * 40; tick++)
        {
            simulation.Tick();
        }

        recorder.AddCheckpoint(simulation);
        var recorded = RecordedSession.FromJson(recorder.Session.ToJson());
        var replay = SessionReplayer.Replay(recorded, GameClock.TicksPerDay * 40, checkpointEvery: 0, parallel: true);

        Assert.True(replay.Succeeded);
        Assert.Equal(simulation.Industries.Count, replay.Simulation.Industries.Count);
        Assert.Equal(simulation.ComputeFingerprint(), replay.Simulation.ComputeFingerprint());
    }

    private static ScenarioSettings Settings() =>
        new(Seed: 3, MapSizeTiles: 256, VehicleCount: 0, LineCount: 1);

    private static (MapData Map, GameContent Content) Fixture()
    {
        var industry = IndustryTestData.MinimalIndustry.Replace(
            "\"weight\": 1,",
            "\"weight\": 1, \"production\": [{ \"cargo\": \"test-cargo\", \"perMonth\": 100 }], \"stock\": [{ \"cargo\": \"test-cargo\", \"capacity\": 10000 }],",
            StringComparison.Ordinal);
        var content = IndustryTestData.Content(
            IndustryTestData.CargoFile(IndustryTestData.RawCargo),
            IndustryTestData.IndustryFile(industry, """{"initialOccupancyPercent": 100}"""));
        var map = IndustryTestData.Map(256, 256, new Site(32, 32, SiteKind.IndustryPlain, 2, 128));
        return (map, content);
    }
}
