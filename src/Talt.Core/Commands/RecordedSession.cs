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

        var settings = new ScenarioSettings(
            Seed: s.GetProperty("seed").GetInt32(),
            StartYear: s.GetProperty("startYear").GetInt32(),
            MapSizeTiles: s.GetProperty("mapSizeTiles").GetInt32(),
            RoadSpacing: s.GetProperty("roadSpacing").GetInt32(),
            RoadRemovalChancePerMille: s.GetProperty("roadRemovalChancePerMille").GetInt32(),
            VehicleCount: s.GetProperty("vehicleCount").GetInt32(),
            LineCount: s.GetProperty("lineCount").GetInt32(),
            Map: map);

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
