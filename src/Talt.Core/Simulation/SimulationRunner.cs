namespace Talt.Core.Simulation;

/// <summary>Game speed chosen by the player. Not simulation state (FR-008).</summary>
public enum GameSpeed
{
    Paused,
    Normal,
    Fast,
    VeryFast,
    FastForward,
}

/// <summary>Source of real time, injected so the runner never reads the clock itself.</summary>
public interface ITimeSource
{
    long GetTimestamp();

    /// <summary>Timestamp ticks per second.</summary>
    long Frequency { get; }
}

/// <summary>What happened in one call to <see cref="SimulationRunner.Advance"/>.</summary>
public readonly record struct RunnerFrameStats(int TicksRun, double TickMilliseconds, bool FastForward);

/// <summary>
/// Turns real elapsed time into simulation ticks (research R3; data-model.md SimulationRunner).
/// </summary>
/// <remarks>
/// <para>
/// Standard speeds (normal ×1, fast ×3, very fast ×8) are always available. Fast forward runs as many
/// ticks as fit in <see cref="FastForwardBudget"/> per frame, and is only reachable through
/// <see cref="ToggleFastForward"/> once <see cref="FastForwardUnlocked"/> is true (FR-007a–d).
/// </para>
/// <para>
/// Changing speed never touches simulation state: it only changes how many ticks run per second.
/// After a long frame, catch-up is limited to <see cref="MaxCatchUpSeconds"/> of normal-speed time
/// (scaled by the speed), so the game never freezes; the excess time is dropped.
/// </para>
/// </remarks>
public sealed class SimulationRunner
{
    private const double TickSeconds = 1.0 / GameClock.TicksPerSecondAtNormalSpeed;

    private double _accumulatedSeconds;
    private GameSpeed _speedBeforePause = GameSpeed.Normal;

    public GameSpeed Speed { get; private set; } = GameSpeed.Normal;

    /// <summary>Standard speed restored when fast forward is turned off or locked.</summary>
    public GameSpeed LastStandardSpeed { get; private set; } = GameSpeed.Normal;

    /// <summary>Whether the player unlocked fast forward in the settings (default false).</summary>
    public bool FastForwardUnlocked { get; private set; }

    /// <summary>Maximum real time of catch-up per frame, at normal speed (data-model.md SimulationRunner.MaxCatchUpSeconds).</summary>
    public double MaxCatchUpSeconds { get; init; } = 0.25;

    /// <summary>Tick work allowed per frame in fast forward (data-model.md SimulationRunner.FastForwardBudget).</summary>
    public TimeSpan FastForwardBudget { get; init; } = TimeSpan.FromMilliseconds(25);

    /// <summary>Rendering only: how far real time is between the last two ticks (0..1).</summary>
    public float InterpolationFactor { get; private set; }

    public bool IsPaused => Speed == GameSpeed.Paused;

    /// <summary>Speed multiplier of a standard speed; 0 for paused and fast forward.</summary>
    public static int Multiplier(GameSpeed speed) => speed switch
    {
        GameSpeed.Normal => 1,
        GameSpeed.Fast => 3,
        GameSpeed.VeryFast => 8,
        _ => 0,
    };

    /// <summary>Selects normal, fast or very fast (also resumes if paused).</summary>
    public void SetStandardSpeed(GameSpeed speed)
    {
        if (Multiplier(speed) == 0)
        {
            throw new ArgumentException("Only Normal, Fast and VeryFast are standard speeds.", nameof(speed));
        }

        Speed = speed;
        LastStandardSpeed = speed;
    }

    /// <summary>Pauses, or resumes the speed that was active before pausing.</summary>
    public void TogglePause()
    {
        if (Speed == GameSpeed.Paused)
        {
            Speed = _speedBeforePause == GameSpeed.FastForward && !FastForwardUnlocked
                ? LastStandardSpeed
                : _speedBeforePause;
        }
        else
        {
            _speedBeforePause = Speed;
            Speed = GameSpeed.Paused;
            _accumulatedSeconds = 0;
        }
    }

    /// <summary>Turns fast forward on or off. Does nothing while it is locked.</summary>
    /// <returns>False when fast forward is locked.</returns>
    public bool ToggleFastForward()
    {
        if (!FastForwardUnlocked)
        {
            return false;
        }

        var inFastForward = Speed == GameSpeed.FastForward ||
                            (Speed == GameSpeed.Paused && _speedBeforePause == GameSpeed.FastForward);
        Speed = inFastForward ? LastStandardSpeed : GameSpeed.FastForward;
        _accumulatedSeconds = 0;
        return true;
    }

    /// <summary>Unlocks or locks fast forward. Locking while it is active restores the last standard speed.</summary>
    public void SetFastForwardUnlocked(bool unlocked)
    {
        FastForwardUnlocked = unlocked;
        if (unlocked)
        {
            return;
        }

        if (Speed == GameSpeed.FastForward)
        {
            Speed = LastStandardSpeed;
        }

        if (_speedBeforePause == GameSpeed.FastForward)
        {
            _speedBeforePause = LastStandardSpeed;
        }
    }

    /// <summary>Runs the ticks owed for <paramref name="elapsedRealSeconds"/> of real time.</summary>
    /// <param name="elapsedRealSeconds">Real time since the previous call.</param>
    /// <param name="simulation">Simulation to advance.</param>
    /// <param name="timeSource">Used to measure tick cost (fast-forward budget and statistics).</param>
    /// <param name="beforeLastTick">Called right before the last tick of this frame (pose snapshot for interpolation).</param>
    /// <param name="afterEachTick">Called after every tick (tools, exact-tick checks).</param>
    /// <param name="parallel">Passed to <see cref="GameSimulation.Tick"/>.</param>
    public RunnerFrameStats Advance(
        double elapsedRealSeconds,
        GameSimulation simulation,
        ITimeSource timeSource,
        Action? beforeLastTick = null,
        Action<GameSimulation>? afterEachTick = null,
        bool parallel = true)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        ArgumentNullException.ThrowIfNull(timeSource);
        ArgumentOutOfRangeException.ThrowIfNegative(elapsedRealSeconds);

        if (Speed == GameSpeed.Paused)
        {
            _accumulatedSeconds = 0;
            return new RunnerFrameStats(0, 0, false);
        }

        if (Speed == GameSpeed.FastForward)
        {
            return RunFastForward(simulation, timeSource, afterEachTick, parallel);
        }

        var multiplier = Multiplier(Speed);
        _accumulatedSeconds += elapsedRealSeconds * multiplier;

        var maxTicks = Math.Max(1, (int)Math.Round(MaxCatchUpSeconds * multiplier * GameClock.TicksPerSecondAtNormalSpeed));
        var ticksDue = (int)Math.Floor(_accumulatedSeconds / TickSeconds);
        if (ticksDue > maxTicks)
        {
            // Too far behind (long frame): drop the excess instead of freezing to catch up.
            ticksDue = maxTicks;
            _accumulatedSeconds = maxTicks * TickSeconds;
        }

        var start = timeSource.GetTimestamp();
        for (var i = 0; i < ticksDue; i++)
        {
            if (i == ticksDue - 1)
            {
                beforeLastTick?.Invoke();
            }

            simulation.Tick(parallel);
            afterEachTick?.Invoke(simulation);
        }

        _accumulatedSeconds -= ticksDue * TickSeconds;
        InterpolationFactor = (float)Math.Clamp(_accumulatedSeconds / TickSeconds, 0, 1);
        return new RunnerFrameStats(ticksDue, ElapsedMilliseconds(timeSource, start), false);
    }

    private RunnerFrameStats RunFastForward(GameSimulation simulation, ITimeSource timeSource, Action<GameSimulation>? afterEachTick, bool parallel)
    {
        _accumulatedSeconds = 0;
        var start = timeSource.GetTimestamp();
        var budgetTicks = (long)(FastForwardBudget.TotalSeconds * timeSource.Frequency);
        var ticks = 0;

        // At least one tick per frame; then keep going while the budget allows.
        do
        {
            simulation.Tick(parallel);
            afterEachTick?.Invoke(simulation);
            ticks++;
        }
        while (timeSource.GetTimestamp() - start < budgetTicks);

        InterpolationFactor = 1f;
        return new RunnerFrameStats(ticks, ElapsedMilliseconds(timeSource, start), true);
    }

    private static double ElapsedMilliseconds(ITimeSource timeSource, long start) =>
        (timeSource.GetTimestamp() - start) * 1000.0 / timeSource.Frequency;
}
