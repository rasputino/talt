using Talt.Core.Commands;
using Talt.Core.Determinism;
using Talt.Core.Network;
using Talt.Core.Scenarios;

namespace Talt.Core.Simulation;

/// <summary>
/// Owns the complete simulated game state and advances it one fixed tick at a time
/// (data-model.md, GameSimulation; contracts/simulation-api.md).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Tick"/> is the only method that changes state after creation. Each tick: (1) apply the
/// queued player commands in submission order, (2) advance the systems (vehicles), (3) increment
/// <see cref="CurrentTick"/>, (4) fire day, month and year events when a boundary was crossed.
/// </para>
/// <para>
/// The simulation never reads the wall clock and all randomness comes from seeded
/// <see cref="DeterministicRandom"/> streams, so the same settings and the same commands produce the
/// same <see cref="ComputeFingerprint"/> at every tick, on every platform.
/// </para>
/// </remarks>
public sealed class GameSimulation : IStateHashable
{
    private readonly CommandInbox _commands = new();
    private SessionRecorder? _recorder;

    private GameSimulation(ScenarioSettings settings, TransportNetwork network, VehicleFleet vehicles)
    {
        Settings = settings;
        Network = network;
        Vehicles = vehicles;
        Clock = new GameClock(settings.StartYear);
        Random = new DeterministicRandom(unchecked((ulong)settings.Seed)).Fork(streamId: 0x51_4D);
    }

    /// <summary>Number of completed ticks since the game started (data-model.md GameSimulation.CurrentTick).</summary>
    public long CurrentTick { get; private set; }

    public GameDate Date => Clock.DateAt(CurrentTick);

    public GameClock Clock { get; }

    public ScenarioSettings Settings { get; }

    public TransportNetwork Network { get; }

    public VehicleFleet Vehicles { get; }

    public SimulationEvents Events { get; } = new();

    /// <summary>Root random generator of the simulation; systems should <see cref="DeterministicRandom.Fork"/> it.</summary>
    public DeterministicRandom Random { get; }

    /// <summary>Commands waiting for the next tick.</summary>
    public int PendingCommandCount => _commands.Count;

    /// <summary>Raised during <see cref="Tick"/> for every processed command, in processing order.</summary>
    public event Action<CommandProcessed>? CommandProcessed;

    public static GameSimulation Create(ScenarioSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var (network, fleet) = ScenarioBuilder.Build(settings);
        return new GameSimulation(settings, network, fleet);
    }

    /// <summary>Queues a command; it is processed at the start of the next tick.</summary>
    /// <returns>Submission id.</returns>
    public long Submit(ISimulationCommand command, int issuerId = 0) => _commands.Enqueue(command, issuerId);

    /// <summary>Records every processed command from now on.</summary>
    public void AttachRecorder(SessionRecorder recorder) => _recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));

    /// <summary>Advances the simulation by exactly one tick.</summary>
    /// <param name="parallel">Use all CPU cores where possible. Results are identical either way.</param>
    public void Tick(bool parallel = true)
    {
        ProcessCommands();
        Vehicles.Tick(parallel);
        CurrentTick++;

        if (GameClock.IsDayBoundary(CurrentTick))
        {
            var date = Date;
            var monthEnded = date.Day == 1;
            var yearEnded = monthEnded && date.Month == 1;
            Events.Dispatch(this, dayEnded: true, monthEnded, yearEnded);
        }
    }

    /// <summary>64-bit fingerprint of the full simulation state. Pure: changes nothing.</summary>
    public ulong ComputeFingerprint()
    {
        var hasher = new StateHasher();
        hasher.Append(this);
        return hasher.GetFingerprint();
    }

    public void AppendTo(StateHasher hasher)
    {
        ArgumentNullException.ThrowIfNull(hasher);
        hasher.Append(CurrentTick);
        hasher.Append(Clock.StartDayNumber);
        hasher.Append(Random);
        hasher.Append(Network);
        hasher.Append(Vehicles);
    }

    private void ProcessCommands()
    {
        while (_commands.TryDequeue(out var pending))
        {
            var result = pending.Command.Validate(this);
            if (result.Succeeded)
            {
                pending.Command.Apply(this);
            }

            var processed = new CommandProcessed(pending.SubmissionId, pending.IssuerId, CurrentTick, pending.Command, result);
            _recorder?.OnCommandProcessed(processed);
            CommandProcessed?.Invoke(processed);
        }
    }
}
