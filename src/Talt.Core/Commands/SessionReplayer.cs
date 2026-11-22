using Talt.Core.Simulation;

namespace Talt.Core.Commands;

/// <summary>Result of comparing a replay against a recorded checkpoint.</summary>
public readonly record struct CheckpointResult(long Tick, ulong Expected, ulong Actual)
{
    public bool Matches => Expected == Actual;
}

/// <summary>Result of a replay.</summary>
public sealed record ReplayResult(
    GameSimulation Simulation,
    IReadOnlyList<CheckpointResult> Checkpoints,
    IReadOnlyList<(long Tick, ulong Fingerprint)> ExtraFingerprints,
    long? FirstFailingTick,
    string? FailureReason)
{
    public bool Succeeded => FirstFailingTick is null;
}

/// <summary>
/// Replays a <see cref="RecordedSession"/>: recreates the scenario, submits each command so it is
/// processed at its recorded tick, and verifies outcomes and checkpoints.
/// </summary>
public static class SessionReplayer
{
    /// <param name="session">Session to replay.</param>
    /// <param name="ticks">Ticks to run; defaults to the last entry or checkpoint tick.</param>
    /// <param name="checkpointEvery">Also report fingerprints every N ticks (0 = off).</param>
    /// <param name="parallel">Tick vehicles in parallel. Results are identical either way.</param>
    public static ReplayResult Replay(RecordedSession session, long? ticks = null, int checkpointEvery = 0, bool parallel = true)
    {
        ArgumentNullException.ThrowIfNull(session);
        var total = ticks ?? Math.Max(
            session.Entries.Count == 0 ? 0 : session.Entries[^1].Tick + 1,
            session.Checkpoints.Count == 0 ? 0 : session.Checkpoints.Max(c => c.Tick));

        var simulation = GameSimulation.Create(session.Settings);
        var processed = new List<CommandProcessed>();
        simulation.CommandProcessed += processed.Add;

        var expected = session.Checkpoints.ToDictionary(c => c.Tick, c => c.Fingerprint);
        var checkpointResults = new List<CheckpointResult>();
        var extra = new List<(long, ulong)>();
        long? firstFailingTick = null;
        string? failure = null;
        var entryIndex = 0;

        Check(simulation.CurrentTick);
        while (simulation.CurrentTick < total && firstFailingTick is null)
        {
            var tick = simulation.CurrentTick;
            var submittedFrom = entryIndex;
            while (entryIndex < session.Entries.Count && session.Entries[entryIndex].Tick == tick)
            {
                simulation.Submit(session.Entries[entryIndex].Command);
                entryIndex++;
            }

            processed.Clear();
            simulation.Tick(parallel);

            for (var i = 0; i < processed.Count && firstFailingTick is null; i++)
            {
                var recorded = session.Entries[submittedFrom + i];
                var outcome = processed[i].Result.Succeeded ? CommandOutcome.Applied : CommandOutcome.Rejected;
                if (outcome != recorded.Outcome)
                {
                    firstFailingTick = tick;
                    failure = $"Command {recorded.Command.TypeName} at tick {tick} was {outcome}, recorded as {recorded.Outcome}.";
                }
            }

            Check(simulation.CurrentTick);
        }

        return new ReplayResult(simulation, checkpointResults, extra, firstFailingTick, failure);

        void Check(long tick)
        {
            if (expected.TryGetValue(tick, out var fingerprint))
            {
                var actual = simulation.ComputeFingerprint();
                checkpointResults.Add(new CheckpointResult(tick, fingerprint, actual));
                if (actual != fingerprint && firstFailingTick is null)
                {
                    firstFailingTick = tick;
                    failure = $"Fingerprint mismatch at tick {tick}.";
                }
            }

            if (checkpointEvery > 0 && tick > 0 && tick % checkpointEvery == 0)
            {
                extra.Add((tick, simulation.ComputeFingerprint()));
            }
        }
    }
}
