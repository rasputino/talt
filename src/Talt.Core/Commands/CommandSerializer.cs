using System.Text.Json;
using Talt.Core.Construction;

namespace Talt.Core.Commands;

/// <summary>
/// Writes and reads commands as JSON objects with a <c>type</c> discriminator
/// (contracts/recorded-session.md). Uses explicit readers registered per type, no reflection.
/// </summary>
public static class CommandSerializer
{
    private static readonly Dictionary<string, Func<JsonElement, ISimulationCommand>> Readers = new(StringComparer.Ordinal)
    {
        [AddVehicleCommand.Type] = AddVehicleCommand.Read,
        [RemoveVehicleCommand.Type] = RemoveVehicleCommand.Read,
        [TerraformCommand.Type] = TerraformCommand.Read,
        [ClearAreaCommand.Type] = ClearAreaCommand.Read,
        [UndoConstructionCommand.Type] = UndoConstructionCommand.Read,
    };

    private static readonly Lock Gate = new();

    /// <summary>
    /// Registers an extra command type (used by tests for <c>test.*</c> commands, and later features
    /// for their own commands).
    /// </summary>
    public static void Register(string typeName, Func<JsonElement, ISimulationCommand> reader)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);
        ArgumentNullException.ThrowIfNull(reader);
        lock (Gate)
        {
            Readers[typeName] = reader;
        }
    }

    public static void Write(Utf8JsonWriter writer, ISimulationCommand command)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(command);
        writer.WriteStartObject();
        writer.WriteString("type", command.TypeName);
        command.WriteProperties(writer);
        writer.WriteEndObject();
    }

    public static ISimulationCommand Read(JsonElement element)
    {
        var type = element.GetProperty("type").GetString()
            ?? throw new JsonException("Command type is missing.");

        Func<JsonElement, ISimulationCommand>? reader;
        lock (Gate)
        {
            Readers.TryGetValue(type, out reader);
        }

        return reader is null
            ? throw new JsonException($"Unknown command type '{type}'.")
            : reader(element);
    }
}
