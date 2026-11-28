namespace Talt.Core.Commands;

/// <summary>A command waiting for the next tick.</summary>
public readonly record struct PendingCommand(long SubmissionId, int IssuerId, ISimulationCommand Command);

/// <summary>
/// First-in, first-out inbox of submitted commands (named Inbox because analyzers reserve the Queue suffix), drained completely at the start of each tick.
/// </summary>
public sealed partial class CommandInbox
{
    private readonly Queue<PendingCommand> _pending = new();
    private long _nextSubmissionId = 1;

    public int Count => _pending.Count;

    public long Enqueue(ISimulationCommand command, int issuerId)
    {
        ArgumentNullException.ThrowIfNull(command);
        var id = _nextSubmissionId++;
        _pending.Enqueue(new PendingCommand(id, issuerId, command));
        return id;
    }

    public bool TryDequeue(out PendingCommand command) => _pending.TryDequeue(out command);
}
