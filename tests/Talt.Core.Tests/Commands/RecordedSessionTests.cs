using System.Text.Json;
using Talt.Core.Commands;
using Talt.Core.Maps;
using Talt.Core.Scenarios;
using Talt.Core.Tests.TestCommands;

namespace Talt.Core.Tests.Commands;

/// <summary>contracts/recorded-session.md.</summary>
public class RecordedSessionTests
{
    [Fact]
    public void RoundTrip_PreservesEverything()
    {
        TestCommandRegistration.EnsureRegistered();
        var session = new RecordedSession(new ScenarioSettings(Seed: 7, StartYear: 1975, MapSizeTiles: 1024, VehicleCount: 300, LineCount: 30));
        session.Entries.Add(new RecordedCommand(120, new AddVehicleCommand(3, 21845, 2), CommandOutcome.Applied));
        session.Entries.Add(new RecordedCommand(480, new RemoveVehicleCommand(999_999), CommandOutcome.Rejected, "vehicle-not-found"));
        session.Entries.Add(new RecordedCommand(480, new NoopCommand(), CommandOutcome.Applied));
        session.Checkpoints.Add(new RecordedCheckpoint(1000, 0x9F3A51C0D2E4B776));

        var copy = RecordedSession.FromJson(session.ToJson());

        Assert.Equal(session.Settings, copy.Settings);
        Assert.Equal(session.Entries, copy.Entries);
        Assert.Equal(session.Checkpoints, copy.Checkpoints);
    }

    [Fact]
    public void Json_UsesTheContractFieldNamesAndIntegersOnly()
    {
        var session = new RecordedSession(new ScenarioSettings());
        session.Entries.Add(new RecordedCommand(5, new AddVehicleCommand(1, 2), CommandOutcome.Applied));
        session.Checkpoints.Add(new RecordedCheckpoint(10, 0x0C11E2A9F07B3D45));

        var json = session.ToJson();
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal(1, root.GetProperty("formatVersion").GetInt32());
        Assert.Equal(150, root.GetProperty("settings").GetProperty("roadRemovalChancePerMille").GetInt32());
        Assert.Equal("addVehicle", root.GetProperty("entries")[0].GetProperty("command").GetProperty("type").GetString());
        Assert.Equal("applied", root.GetProperty("entries")[0].GetProperty("outcome").GetString());
        Assert.Equal("0C11E2A9F07B3D45", root.GetProperty("checkpoints")[0].GetProperty("fingerprint").GetString());
        Assert.DoesNotContain(".", json.Replace("0.1.0", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal); // No decimals.
    }

    [Fact]
    public void UnknownFormatVersion_IsRejected()
    {
        const string json = """{ "formatVersion": 2, "settings": {}, "entries": [] }""";
        Assert.Throws<NotSupportedException>(() => RecordedSession.FromJson(json));
    }

    [Fact]
    public void UnknownCommandType_IsRejected()
    {
        var json = new RecordedSession(new ScenarioSettings()).ToJson()
            .Replace("\"entries\": []", "\"entries\": [ { \"tick\": 1, \"outcome\": \"applied\", \"command\": { \"type\": \"nope\" } } ]", StringComparison.Ordinal);
        Assert.Throws<JsonException>(() => RecordedSession.FromJson(json));
    }

    [Fact]
    public void MapSettings_RoundTrip()
    {
        var map = new MapSettings(512, 256, 7, 40, 28, 35, 7);
        var session = new RecordedSession(new ScenarioSettings(Seed: 7, MapSizeTiles: 256, VehicleCount: 0, LineCount: 1, Map: map));

        var copy = RecordedSession.FromJson(session.ToJson());

        Assert.Equal(map, copy.Settings.Map);
        Assert.Equal(session.Settings, copy.Settings);
    }

    [Fact]
    public void SessionsWithoutAMapStayValid()
    {
        var session = RecordedSession.FromJson(new RecordedSession(new ScenarioSettings()).ToJson());

        Assert.Null(session.Settings.Map);
    }

    [Fact]
    public void InvalidMapText_IsRejected()
    {
        var json = new RecordedSession(new ScenarioSettings()).ToJson()
            .Replace("\"vehicleCount\": 20000", "\"vehicleCount\": 20000, \"map\": \"nope\"", StringComparison.Ordinal);

        Assert.Throws<JsonException>(() => RecordedSession.FromJson(json));
    }
}
