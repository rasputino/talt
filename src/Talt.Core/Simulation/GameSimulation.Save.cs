using Talt.Core.Commands;
using Talt.Core.Saves;

namespace Talt.Core.Simulation;

/// <summary>What capture needs that never changes during a game: the settings JSON, content ids and rules fingerprints.</summary>
internal sealed record SaveCaptureCache(string SettingsJson, string? MapText, string[][] ContentIds, ulong[] Fingerprints);

public sealed partial class GameSimulation
{
    /// <summary>Built at the first capture so later captures do not allocate (feature 014). Not state.</summary>
    internal SaveCaptureCache? SaveCache { get; set; }

    /// <summary>
    /// The <c>CORE</c> section after the settings (feature 014, data-model.md): tick, root random stream, queued commands and
    /// the prototype fleet. The prototype network and the clock are rebuilt from the settings (research R2).
    /// </summary>
    internal void WriteCoreState(SaveWriter writer)
    {
        writer.WriteLong(CurrentTick);
        writer.WriteRandom(Random);
        _commands.WriteState(writer);
        Vehicles.WriteState(writer);
    }

    internal void ReadCoreState(SaveReader reader)
    {
        var tick = reader.ReadLong();
        if (tick < 0)
        {
            throw reader.Fail("negative tick");
        }

        CurrentTick = tick;
        reader.ReadRandomInto(Random);
        _commands.ReadState(reader);
        Vehicles.ReadState(reader);
    }

    /// <summary>The scenario settings as session JSON (the same form recorded sessions use).</summary>
    internal static string SettingsToJson(Scenarios.ScenarioSettings settings) => new RecordedSession(settings).ToJson();

    /// <summary>Parses settings written by <see cref="SettingsToJson"/>; malformed text is a damaged save.</summary>
    internal static Scenarios.ScenarioSettings SettingsFromJsonSafe(string json, SaveReader reader)
    {
        try
        {
            var settings = RecordedSession.FromJson(json).Settings;
            settings.Validate();
            return settings;
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or NotSupportedException or ArgumentException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new SaveFormatException($"Section {reader.Section}: the scenario settings cannot be read.", e);
        }
    }
}
