using System.Text.Json;
using Talt.Core.Simulation;

namespace Talt.Core.Commands;

/// <summary>
/// A player action (research R7). Commands are submitted to <see cref="GameSimulation.Submit"/> and
/// processed at the start of the next tick: validated against the current state, then applied or
/// rejected. Implementations are immutable records with integer fields only.
/// </summary>
public interface ISimulationCommand
{
    /// <summary>Stable type name used in recorded sessions (for example <c>addVehicle</c>).</summary>
    string TypeName { get; }

    /// <summary>Checks the command against the current state. Must not change anything.</summary>
    CommandResult Validate(GameSimulation simulation);

    /// <summary>Applies the command. Only called right after a successful validation, same tick.</summary>
    void Apply(GameSimulation simulation);

    /// <summary>Writes the command's own properties (not the type) as JSON.</summary>
    void WriteProperties(Utf8JsonWriter writer);
}

/// <summary>Outcome of validating a command.</summary>
public readonly record struct CommandResult(bool Succeeded, string? ReasonCode, string? Message)
{
    public static CommandResult Success { get; } = new(true, null, null);

    public static CommandResult Rejected(string reasonCode, string message) => new(false, reasonCode, message);
}

/// <summary>Raised for every processed command, applied or rejected, in processing order.</summary>
public readonly record struct CommandProcessed(
    long SubmissionId,
    int IssuerId,
    long Tick,
    ISimulationCommand Command,
    CommandResult Result);
