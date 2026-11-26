using Talt.Core.Roads;

namespace Talt.Core.Rails;

/// <summary>The parallel move phase (research R6, R7): speed from forces and authority, head advance on owned track.</summary>
public sealed partial class TrainSystem
{
    /// <summary>Advances the trains by one tick (called by the simulation after road vehicles).</summary>
    public void Tick(long tick, bool parallel)
    {
        _tick = tick;
        if (_trains.Count == 0)
        {
            return;
        }

        if (SignalBlocks.Refresh())
        {
            RecountBlocks();
        }

        var batches = (_trains.Count + BatchSize - 1) / BatchSize;
        if (parallel && batches > 1)
        {
            Parallel.For(0, batches, _moveBatch);
        }
        else
        {
            for (var b = 0; b < batches; b++)
            {
                MoveBatch(b);
            }
        }

        _searchBudget = Rules.SearchNodesPerTick;
        foreach (var train in _byId)
        {
            Sequential(train);
        }
    }

    private void MoveBatch(int batch)
    {
        var end = Math.Min(_trains.Count, (batch + 1) * BatchSize);
        long moved = 0;
        for (var slot = batch * BatchSize; slot < end; slot++)
        {
            moved += Move(_trains[slot]);
        }

        if (moved != 0)
        {
            Interlocked.Add(ref _distance, moved);
        }
    }

    /// <summary>One train's move: reads the rail layer, the rules and its own state; writes only its speed and head.</summary>
    private int Move(Train train)
    {
        if (train.Mode != TrainMode.Running || train.StepCount == 0)
        {
            train.Speed = 0;
            return 0;
        }

        // Distance left in the authority (to the end of the owned steps).
        long ahead = train.StepLength(train.HeadStep) - train.HeadOffset;
        for (var i = train.HeadStep + 1; i < train.StepCount; i++)
        {
            ahead += train.StepLength(i);
        }

        var v = (long)train.Speed;
        var started = (train.Flags & TrainBits.Started) != 0;
        var limit = (long)SpeedLimit(train, train.HeadStep) * RailRules.SpeedScale;
        var braking = (long)Rules.Braking;

        // The next step's limit, approached no faster than braking allows.
        if (train.HeadStep + 1 < train.StepCount)
        {
            var nextLimit = (long)SpeedLimit(train, train.HeadStep + 1) * RailRules.SpeedScale;
            var toNext = train.StepLength(train.HeadStep) - train.HeadOffset;
            limit = Math.Min(limit, Approach(nextLimit, toNext, braking));
        }

        // Stop at the end of the authority (the next signal, a stop, a depot or a track end).
        limit = Math.Min(limit, Approach(0, ahead, braking));
        if (!started)
        {
            limit = 0;
        }

        if (v > limit)
        {
            v = Math.Max(limit, v - braking);
        }
        else
        {
            v = Math.Min(limit, v + Acceleration(train, v));
        }

        v = Math.Max(0, v);
        var advance = Math.Min(v / RailRules.SpeedScale, ahead);
        if (advance == 0 && v > 0 && ahead > 0)
        {
            advance = 1; // Creep the last units.
        }

        train.Speed = (int)v;
        if (advance <= 0)
        {
            return 0;
        }

        var offset = train.HeadOffset + advance;
        var step = train.HeadStep;
        while (offset > train.StepLength(step) && step + 1 < train.StepCount)
        {
            offset -= train.StepLength(step);
            step++;
        }

        train.HeadStep = step;
        train.HeadOffset = (int)Math.Min(offset, train.StepLength(step));
        if (advance >= ahead)
        {
            train.Speed = 0;
        }

        return (int)advance;
    }

    /// <summary>Highest speed in sub-units (×256) from which the train can brake to <paramref name="target"/> within <paramref name="distance"/> units.</summary>
    private static long Approach(long target, long distance, long braking)
    {
        if (distance <= 0)
        {
            return target;
        }

        // v² = target² + 2·b·d with v and b in sub-units and d in units (×256 to keep sub-units).
        var squared = (target * target) + (2 * braking * distance * RailRules.SpeedScale);
        return (long)Math.Sqrt(squared); // Exact for the values used: integers below 2^52 (deterministic on every platform).
    }

    /// <summary>Speed limit on a step, units per tick: the consist, the track type, curves and breakdowns.</summary>
    private int SpeedLimit(Train train, int step)
    {
        var from = train.StepFrom(step);
        var dir = train.StepDir(step);
        var to = _rails.FollowLink(from, dir);
        var type = Math.Max(0, Math.Min(_rails.TypeAt(from), to >= 0 ? _rails.TypeAt(to) : int.MaxValue));
        var limit = Math.Min(train.MaxSpeed, _typeSpeed[Math.Min(type, _typeSpeed.Length - 1)]);
        if (step > 0 && train.StepDir(step - 1) != dir)
        {
            limit = limit * Rules.CurveSpeedPercent / 100;
        }

        if (train.IsBrokenDown)
        {
            var pulse = (int)((_tick / VehicleRules.BreakdownPulseTicks) & 1);
            limit = pulse == 0 ? limit / 2 : limit / 4;
        }

        return Math.Max(1, limit);
    }

    /// <summary>Acceleration in sub-units per tick from power, tractive effort, mass, slope and rolling resistance.</summary>
    private long Acceleration(Train train, long v)
    {
        if (train.PowerKw <= 0)
        {
            return 0;
        }

        // Speed in mm/s from sub-units per tick: units/tick × 20 ticks/s × mm per unit.
        var mmPerSecond = Math.Max(1L, v * 20L * Rules.TileMillimetres / (Determinism.FixedMath.UnitsPerTile * (long)RailRules.SpeedScale));
        var force = Math.Min((long)train.EffortKn * 1000, (long)train.PowerKw * 1_000_000 / mmPerSecond); // Newtons.
        var mass = (long)train.MassTonnes;
        var step = train.HeadStep;
        var from = train.StepFrom(step);
        var to = _rails.FollowLink(from, train.StepDir(step));
        var climb = to >= 0 ? Math.Sign(_state.Map.Heights[to] - _state.Map.Heights[from]) : 0;
        if (_rails.IsLongLink(from, train.StepDir(step)))
        {
            climb = 0; // Bridges and tunnels are level.
        }

        force -= mass * Rules.GravityMilli * ((climb * Rules.RampGradePermille) + Rules.RollingResistancePermille) / 1000;
        if (force <= 0)
        {
            return climb > 0 ? -(long)Rules.Braking / 8 : 0; // Too heavy for the slope: it slows down.
        }

        // a [mm/s²] = F / m[t]; Δv [sub-units/tick] = a × units-per-tile × 256 / (mm-per-tile × 400).
        var accel = force * Rules.AccelerationScale / mass;
        return Math.Max(1, accel * Determinism.FixedMath.UnitsPerTile * RailRules.SpeedScale
            / ((long)Rules.TileMillimetres * Determinism.FixedMath.TicksPerSecond * Determinism.FixedMath.TicksPerSecond));
    }
}
