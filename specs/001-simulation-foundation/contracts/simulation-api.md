# Contract: Simulation API (Talt.Core public surface)

The public types other projects (client, headless runner, tests, later features) rely on. Names are
binding; signatures may gain optional parameters without breaking callers.

```csharp
public sealed class GameSimulation
{
    public static GameSimulation Create(ScenarioSettings settings);
    public long CurrentTick { get; }   // completed ticks; named to avoid clashing with Tick()
    public GameDate Date { get; }
    public ScenarioSettings Settings { get; }
    public VehicleFleet Vehicles { get; }
    public TransportNetwork Network { get; }
    public SimulationEvents Events { get; }

    public long Submit(ISimulationCommand command, int issuerId = 0);   // returns submission id
    public event Action<CommandProcessed>? CommandProcessed;              // applied or rejected
    public void Tick(bool parallel = true);
    public ulong ComputeFingerprint();
    public void AttachRecorder(SessionRecorder recorder);
}

public interface ISimulationCommand
{
    string TypeName { get; }                       // discriminator in recorded sessions
    CommandResult Validate(GameSimulation simulation);
    void Apply(GameSimulation simulation);
    void WriteProperties(Utf8JsonWriter writer);   // explicit JSON, no reflection
}

public sealed class SimulationEvents
{
    public void OnDayEnded(Action<GameSimulation> handler);
    public void OnMonthEnded(Action<GameSimulation> handler);
    public void OnYearEnded(Action<GameSimulation> handler);
}

public enum GameSpeed { Paused, Normal, Fast, VeryFast, FastForward }

public sealed class SimulationRunner
{
    public GameSpeed Speed { get; }
    public bool FastForwardUnlocked { get; }
    public void SetStandardSpeed(GameSpeed speed);      // Normal, Fast or VeryFast only
    public void TogglePause();
    public bool ToggleFastForward();                    // false (no change) when locked
    public void SetFastForwardUnlocked(bool unlocked);  // locking while active restores last speed
    public float InterpolationFactor { get; }
    public RunnerFrameStats Advance(double elapsedRealSeconds, GameSimulation simulation,
                                    ITimeSource timeSource, Action? beforeLastTick = null,
                                    Action<GameSimulation>? afterEachTick = null, bool parallel = true);
}

public interface ITimeSource { long GetTimestamp(); long Frequency { get; } }
```

## Guarantees

- `Tick()` is the only method that changes simulation state after creation.
- `Submit` never changes state immediately; the command is processed at the start of the next tick.
- `CommandProcessed` is raised synchronously during `Tick()`, in processing order.
- Event handlers run after the tick's systems, in the order daily → monthly → yearly, each list in
  registration order.
- `ComputeFingerprint()` is pure (no state change) and equal for equal states on all platforms.
- `SimulationRunner` never reads the system clock directly; time comes from the arguments.
