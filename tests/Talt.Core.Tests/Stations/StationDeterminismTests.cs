using System.Text.Json;
using Talt.Core.Commands;
using Talt.Core.Construction;
using Talt.Core.Determinism;
using Talt.Core.Stations;

namespace Talt.Core.Tests.Stations;

/// <summary>
/// Conservation (SC-003), determinism (SC-004, FR-029, FR-030), commands and settings round trips, and the
/// station data rules.
/// </summary>
public sealed class StationDeterminismTests
{
    [Fact]
    public void RandomOperationsOver1000Days_ConserveCargo()
    {
        var kit = StationKit.Generated(seed: 12);
        foreach (var command in StationSeeder.Plan(kit.Simulation, 12))
        {
            Assert.True(kit.Run(command).Succeeded, kit.Last.ReasonCode);
        }

        var stations = kit.Stations;
        Assert.True(stations.Count >= 4);
        var ids = Enumerable.Range(0, stations.Count).Select(stations.IdAt).ToArray();
        for (var c = 0; c < kit.Simulation.Content.CargoCount; c++)
        {
            foreach (var id in ids)
            {
                stations.MarkServed(id, c);
            }
        }

        var random = new DeterministicRandom(99);
        var holds = Enumerable.Range(0, 4).Select(_ => new CargoHold()).ToArray();
        for (var day = 0; day < 1000; day++)
        {
            kit.Days(1, parallel: day % 2 == 0);
            foreach (var hold in holds)
            {
                var station = ids[random.NextInt(ids.Length)];
                var cargo = random.NextInt(kit.Simulation.Content.CargoCount);
                if (random.NextInt(2) == 0)
                {
                    stations.Load(station, cargo, random.NextInt(200), hold, 40 + random.NextInt(80));
                }
                else
                {
                    stations.Unload(station, hold, random.NextInt(3) == 0 ? UnloadMode.Transfer : UnloadMode.Normal);
                }
            }

            var held = holds.Sum(h => (long)h.Total);
            Assert.Equal(stations.TotalSupplied, stations.TotalWaiting + held + stations.TotalDelivered + stations.TotalLost);
        }

        Assert.True(stations.TotalSupplied > 0);
        Assert.True(stations.TotalDelivered > 0);
    }

    [Fact]
    public void ParallelAndSequentialRuns_GiveTheSameFingerprints()
    {
        var a = Seeded();
        var b = Seeded();
        a.Stations.ParallelGroupThreshold = 1;
        b.Stations.ParallelGroupThreshold = 1;
        for (var day = 0; day < 90; day++)
        {
            a.Days(1, parallel: true);
            b.Days(1, parallel: false);
            Assert.Equal(a.Simulation.ComputeFingerprint(), b.Simulation.ComputeFingerprint());
        }

        Assert.True(a.Stations.TotalSupplied > 0);
        Assert.True(a.Stations.TotalDebugCollected > 0);
    }

    [Fact]
    public void WithoutStations_TheFingerprintIgnoresTheRegistry()
    {
        var kit = StationKit.Generated();
        var before = kit.Simulation.ComputeFingerprint();
        var hasher = new StateHasher();
        kit.Stations.AppendTo(hasher);
        Assert.Equal(new StateHasher().GetFingerprint(), hasher.GetFingerprint());
        Assert.Equal(before, kit.Simulation.ComputeFingerprint());
    }

    [Fact]
    public void RecordedSession_ReplaysStationsAndTheDebugService()
    {
        var kit = StationKit.Generated(stations: new StationSettings(50), extraMoney: false);
        var recorder = new SessionRecorder(new RecordedSession(kit.Simulation.Settings));
        kit.Simulation.AttachRecorder(recorder);
        foreach (var command in StationSeeder.Plan(kit.Simulation, 6))
        {
            kit.Simulation.Submit(command);
        }

        kit.Days(40);
        var json = recorder.Session.ToJson();
        Assert.Contains("\"stations\"", json, StringComparison.Ordinal);
        var replayed = RecordedSession.FromJson(json);
        Assert.Equal(50, replayed.Settings.Stations!.DebugServicePercent);
        var result = SessionReplayer.Replay(replayed, kit.Simulation.CurrentTick);
        Assert.True(result.Succeeded, result.FailureReason);
        Assert.Equal(kit.Simulation.ComputeFingerprint(), result.Simulation.ComputeFingerprint());
    }

    [Theory]
    [InlineData("{\"type\":\"buildStation\",\"companyId\":0,\"pieceType\":\"bus-bay\",\"x\":4,\"z\":5}")]
    [InlineData("{\"type\":\"buildWarehouse\",\"companyId\":0,\"size\":\"large\",\"x\":4,\"z\":5}")]
    [InlineData("{\"type\":\"removeStation\",\"companyId\":0,\"x\":4,\"z\":5}")]
    [InlineData("{\"type\":\"renameStation\",\"companyId\":0,\"stationId\":3,\"name\":\"Quay\"}")]
    public void Commands_RoundTrip(string json)
    {
        using var document = JsonDocument.Parse(json);
        var command = CommandSerializer.Read(document.RootElement);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            CommandSerializer.Write(writer, command);
        }

        Assert.Equal(json, System.Text.Encoding.UTF8.GetString(stream.ToArray()));
    }

    [Fact]
    public void DefaultRules_Load_AndInvalidValuesNameTheField()
    {
        var rules = StationRules.Default;
        Assert.Equal(3, rules.Pieces[rules.FindPiece("bus-bay")].CatchmentRadius);
        Assert.Equal(4, rules.Pieces[rules.FindPiece("truck-stop")].CatchmentRadius);
        Assert.Equal(4000, rules.WaitingCap);
        Assert.Equal([8, 16, 32], rules.Warehouses.Select(w => w.InfluenceRadius));

        var broken = StationRules.ReadResource().Replace("\"catchmentRadius\": 3", "\"catchmentRadius\": 30", StringComparison.Ordinal);
        var error = Assert.Throws<InvalidDataException>(() => StationRules.FromJson(broken, Talt.Core.Content.GameContent.Default));
        Assert.Contains("catchmentRadius", error.Message, StringComparison.Ordinal);
        var unknown = StationRules.ReadResource().Replace("\"passenger\"]", "\"spaceships\"]", StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => StationRules.FromJson(unknown, Talt.Core.Content.GameContent.Default));
    }

    private static StationKit Seeded()
    {
        var kit = StationKit.Generated(seed: 12, stations: new StationSettings(40));
        foreach (var command in StationSeeder.Plan(kit.Simulation, 10))
        {
            Assert.True(kit.Run(command).Succeeded, kit.Last.ReasonCode);
        }

        Assert.True(kit.Stations.Count >= 4);
        return kit;
    }
}
