using System.Text;
using System.Text.Json;
using Talt.Core.Determinism;
using Talt.Core.Maps;
using Talt.Core.Scenarios;

namespace Talt.Core.Commands;

/// <summary>Whether a recorded command was applied or rejected.</summary>
public enum CommandOutcome
{
    Applied,
    Rejected,
}

/// <summary>One processed command with the tick at whose start it was processed.</summary>
public sealed record RecordedCommand(long Tick, ISimulationCommand Command, CommandOutcome Outcome, string? Reason = null);

/// <summary>Expected fingerprint after a given number of ticks.</summary>
public readonly record struct RecordedCheckpoint(long Tick, ulong Fingerprint);

/// <summary>
/// Everything needed to reproduce a game exactly: settings plus ordered commands
/// (contracts/recorded-session.md, format version 1).
/// </summary>
public sealed class RecordedSession
{
    public const int CurrentFormatVersion = 1;

    public RecordedSession(ScenarioSettings settings, string gameVersion = "0.1.0")
    {
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        GameVersion = gameVersion;
    }

    public ScenarioSettings Settings { get; }

    public string GameVersion { get; }

    public List<RecordedCommand> Entries { get; } = [];

    public List<RecordedCheckpoint> Checkpoints { get; } = [];

    public string ToJson()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("formatVersion", CurrentFormatVersion);
            writer.WriteString("gameVersion", GameVersion);

            writer.WriteStartObject("settings");
            writer.WriteNumber("seed", Settings.Seed);
            writer.WriteNumber("startYear", Settings.StartYear);
            writer.WriteNumber("mapSizeTiles", Settings.MapSizeTiles);
            writer.WriteNumber("roadSpacing", Settings.RoadSpacing);
            writer.WriteNumber("roadRemovalChancePerMille", Settings.RoadRemovalChancePerMille);
            writer.WriteNumber("lineCount", Settings.EffectiveLineCount);
            writer.WriteNumber("vehicleCount", Settings.VehicleCount);
            if (Settings.Map is not null)
            {
                writer.WriteString("map", Settings.Map.ToText());
                var towns = Settings.Towns ?? Towns.TownSettings.Default;
                writer.WriteStartObject("towns");
                writer.WriteString("names", Towns.TownSettings.ToText(towns.NameStyle));
                writer.WriteString("size", Towns.TownSettings.ToText(towns.Size));
                writer.WriteNumber("servicePercent", towns.DebugServicePercent);
                writer.WriteEndObject();
                if (Settings.Stations is { } stations && stations != Stations.StationSettings.Default)
                {
                    // Feature 010: written only when set, so earlier sessions keep their exact form.
                    writer.WriteStartObject("stations");
                    writer.WriteNumber("servicePercent", stations.DebugServicePercent);
                    writer.WriteEndObject();
                }

                if (Settings.Finance is { } finance && finance != Finance.FinanceSettings.Default)
                {
                    // Feature 011: written only when set, so earlier sessions keep their exact form.
                    writer.WriteStartObject("finance");
                    writer.WriteBoolean("sandbox", finance.Sandbox);
                    writer.WriteBoolean("inflation", finance.Inflation);
                    writer.WriteBoolean("servicePays", finance.DebugServicePays);
                    writer.WriteBoolean("infiniteMoney", finance.InfiniteMoney);
                    writer.WriteEndObject();
                }
            }

            writer.WriteEndObject();

            writer.WriteStartArray("entries");
            foreach (var entry in Entries)
            {
                writer.WriteStartObject();
                writer.WriteNumber("tick", entry.Tick);
                writer.WriteString("outcome", entry.Outcome == CommandOutcome.Applied ? "applied" : "rejected");
                if (entry.Reason is not null)
                {
                    writer.WriteString("reason", entry.Reason);
                }

                writer.WritePropertyName("command");
                CommandSerializer.Write(writer, entry.Command);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();

            writer.WriteStartArray("checkpoints");
            foreach (var checkpoint in Checkpoints)
            {
                writer.WriteStartObject();
                writer.WriteNumber("tick", checkpoint.Tick);
                writer.WriteString("fingerprint", StateHasher.Format(checkpoint.Fingerprint));
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Parses a session. Throws <see cref="JsonException"/> or <see cref="NotSupportedException"/>.</summary>
    public static RecordedSession FromJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var version = root.GetProperty("formatVersion").GetInt32();
        if (version != CurrentFormatVersion)
        {
            throw new NotSupportedException($"Recorded session format version {version} is not supported (expected {CurrentFormatVersion}).");
        }

        var s = root.GetProperty("settings");
        MapSettings? map = null;
        if (s.TryGetProperty("map", out var mapElement))
        {
            var mapText = mapElement.GetString();
            if (!MapSettings.TryParse(mapText, out map, out var mapError))
            {
                throw new JsonException($"Invalid map settings in the session: {mapError}");
            }
        }

        Towns.TownSettings? townSettings = null;
        if (s.TryGetProperty("towns", out var townsElement))
        {
            if (!Towns.TownSettings.TryParseNameStyle(townsElement.GetProperty("names").GetString(), out var style)
                || !Towns.TownSettings.TryParseSize(townsElement.GetProperty("size").GetString(), out var size))
            {
                throw new JsonException("Invalid town settings in the session.");
            }

            townSettings = new Towns.TownSettings(style, size, townsElement.GetProperty("servicePercent").GetInt32());
            if (townSettings == Towns.TownSettings.Default)
            {
                townSettings = null; // Defaults read back as "not set", like settings written before feature 008.
            }
        }

        Stations.StationSettings? stationSettings = null;
        if (s.TryGetProperty("stations", out var stationsElement))
        {
            stationSettings = new Stations.StationSettings(stationsElement.GetProperty("servicePercent").GetInt32());
            if (stationSettings == Stations.StationSettings.Default)
            {
                stationSettings = null;
            }
        }

        Finance.FinanceSettings? financeSettings = null;
        if (s.TryGetProperty("finance", out var financeElement))
        {
            financeSettings = new Finance.FinanceSettings(
                financeElement.GetProperty("sandbox").GetBoolean(),
                financeElement.GetProperty("inflation").GetBoolean(),
                financeElement.GetProperty("servicePays").GetBoolean(),
                financeElement.TryGetProperty("infiniteMoney", out var infinite) && infinite.GetBoolean());
            if (financeSettings == Finance.FinanceSettings.Default)
            {
                financeSettings = null;
            }
        }

        var settings = new ScenarioSettings(
            Seed: s.GetProperty("seed").GetInt32(),
            StartYear: s.GetProperty("startYear").GetInt32(),
            MapSizeTiles: s.GetProperty("mapSizeTiles").GetInt32(),
            RoadSpacing: s.GetProperty("roadSpacing").GetInt32(),
            RoadRemovalChancePerMille: s.GetProperty("roadRemovalChancePerMille").GetInt32(),
            VehicleCount: s.GetProperty("vehicleCount").GetInt32(),
            LineCount: s.GetProperty("lineCount").GetInt32(),
            Map: map,
            Towns: townSettings,
            Stations: stationSettings,
            Finance: financeSettings);

        var gameVersion = root.TryGetProperty("gameVersion", out var gv) ? gv.GetString() ?? "unknown" : "unknown";
        var session = new RecordedSession(settings, gameVersion);

        foreach (var entry in root.GetProperty("entries").EnumerateArray())
        {
            var outcome = entry.GetProperty("outcome").GetString() switch
            {
                "applied" => CommandOutcome.Applied,
                "rejected" => CommandOutcome.Rejected,
                var other => throw new JsonException($"Unknown outcome '{other}'."),
            };
            var reason = entry.TryGetProperty("reason", out var r) ? r.GetString() : null;
            session.Entries.Add(new RecordedCommand(
                entry.GetProperty("tick").GetInt64(),
                CommandSerializer.Read(entry.GetProperty("command")),
                outcome,
                reason));
        }

        if (root.TryGetProperty("checkpoints", out var checkpoints))
        {
            foreach (var checkpoint in checkpoints.EnumerateArray())
            {
                session.Checkpoints.Add(new RecordedCheckpoint(
                    checkpoint.GetProperty("tick").GetInt64(),
                    StateHasher.Parse(checkpoint.GetProperty("fingerprint").GetString() ?? string.Empty)));
            }
        }

        return session;
    }
}
