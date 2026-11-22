using Talt.Core.Simulation;

namespace Talt.Core.Commands;

/// <summary>
/// Records every processed command of a simulation into a <see cref="RecordedSession"/>.
/// Attach it before the first tick with <see cref="GameSimulation.AttachRecorder"/>.
/// </summary>
public sealed class SessionRecorder
{
    public SessionRecorder(RecordedSession session)
    {
        Session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public RecordedSession Session { get; }

    internal void OnCommandProcessed(CommandProcessed processed)
    {
        Session.Entries.Add(new RecordedCommand(
            processed.Tick,
            processed.Command,
            processed.Result.Succeeded ? CommandOutcome.Applied : CommandOutcome.Rejected,
            processed.Result.ReasonCode));
    }

    /// <summary>Adds the current fingerprint of <paramref name="simulation"/> as a checkpoint.</summary>
    public void AddCheckpoint(GameSimulation simulation)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        Session.Checkpoints.Add(new RecordedCheckpoint(simulation.CurrentTick, simulation.ComputeFingerprint()));
    }
}
