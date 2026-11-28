using Talt.Core.Determinism;
using Talt.Core.Stations;
using Talt.Core.Vehicles;

namespace Talt.Core.Rails;

/// <summary>What a train is doing (data-model.md Train, state transitions).</summary>
public enum TrainMode : byte
{
    InDepot = 0,
    Running = 1,
    Loading = 2,
}

/// <summary>What the train window shows.</summary>
public enum TrainStatus : byte
{
    InDepot,
    Stopped,
    Running,
    Loading,
    WaitingAtSignal,
    GoingToDepot,
    BrokenDown,
    NoRoute,
    NoOrders,
    Stuck,
}

/// <summary>Flags of a train (data-model.md Train.state flags).</summary>
[Flags]
public enum TrainBits : ushort
{
    None = 0,

    /// <summary>The player started it (it runs when it has orders).</summary>
    Started = 1,

    /// <summary>Its parts run from tail to head (after reversing at a terminus or dead end).</summary>
    Reversed = 2,

    /// <summary>Its route must be planned again.</summary>
    NeedsRoute = 4,

    /// <summary>No route exists to its target (reported once).</summary>
    NoRoute = 8,

    /// <summary>Pulling forward along a short platform: no further reservation until it halts.</summary>
    PullingForward = 16,

    /// <summary>Reported stuck once.</summary>
    StuckNotified = 32,

    /// <summary>Reported that the platform was too short once at this stop.</summary>
    ShortNotified = 64,

    /// <summary>Waiting at a signal (the last reservation failed).</summary>
    Waiting = 128,
}

/// <summary>One locomotive or wagon of a train.</summary>
public sealed partial class TrainPart
{
    internal TrainPart(int model, int cargo, int runningHandle, int purchaseDay, long price)
    {
        Model = model;
        Cargo = cargo;
        RunningHandle = runningHandle;
        PurchaseDay = purchaseDay;
        Price = price;
    }

    public int Model { get; internal set; }

    /// <summary>Cargo index it carries, or −1.</summary>
    public int Cargo { get; internal set; }

    public bool RenewalNotified { get; internal set; }

    public CargoHold Hold { get; } = new();

    internal int RunningHandle { get; set; }

    public int PurchaseDay { get; internal set; }

    /// <summary>Price paid (inflated), the base of its value.</summary>
    public long Price { get; internal set; }
}

/// <summary>
/// One train (feature 015, data-model.md Train): its parts, line, orders and the deque of track steps it owns from its
/// tail to the end of its authority, with the head's position on it. Mutated only by <see cref="TrainSystem"/>: the move
/// phase writes only the train's own head and speed; everything else changes in the sequential phase.
/// </summary>
public sealed partial class Train
{
    private int[] _stepFrom = new int[32];
    private byte[] _stepDir = new byte[32];
    private int[] _stepLen = new int[32];
    private int _stepStart;

    internal Train(int id, int owner, int depot)
    {
        Id = id;
        Owner = owner;
        Depot = depot;
        HomeDepot = depot;
    }

    public int Id { get; }

    public int Owner { get; }

    public List<TrainPart> Parts { get; } = [];

    public int Line { get; internal set; } = -1;

    public int StopIndex { get; internal set; }

    /// <summary>The depot it stands in, or −1.</summary>
    public int Depot { get; internal set; }

    public int HomeDepot { get; internal set; }

    public TrainMode Mode { get; internal set; }

    public TrainBits Flags { get; internal set; }

    public VehicleOrders Orders { get; internal set; }

    public RouteGoalKind TargetKind { get; internal set; }

    /// <summary>Station or depot id of the target.</summary>
    public int TargetId { get; internal set; } = -1;

    /// <summary>Speed in sub-units per tick (× <see cref="RailRules.SpeedScale"/>).</summary>
    public int Speed { get; internal set; }

    /// <summary>Steps owned (from the tail step to the end of the authority).</summary>
    public int StepCount { get; private set; }

    /// <summary>Index of the step the head is on (0 = tail-most step).</summary>
    public int HeadStep { get; internal set; }

    /// <summary>Distance of the head from the start of its step, units.</summary>
    public int HeadOffset { get; internal set; }

    // Scratch overwritten during movement and consumed before the sequential phase trims the tail.
    internal int FirstCrossedStep { get; set; }
    internal int EndCrossedStep { get; set; }

    /// <summary>The planned route beyond the authority and the next step to reserve.</summary>
    internal RailRoute Route { get; } = new();

    internal int RoutePos { get; set; }

    internal int Timer { get; set; }

    internal ulong ServedMask { get; set; }

    internal int WaitTicks { get; set; }

    internal long LastPlanTick { get; set; } = long.MinValue / 2;

    public int AgeDays { get; internal set; }

    // Per-tick movement scratch, consumed before loading or consist changes.
    internal int ServiceDistance { get; set; }

    public int Reliability { get; internal set; }

    public int DaysSinceService { get; internal set; }

    public int ServiceInterval { get; internal set; }

    internal int BreakdownTicks { get; set; }

    public long ProfitThisYear { get; internal set; }

    public long ProfitLastYear { get; internal set; }

    // Derived from the parts (not hashed).
    public int Length { get; internal set; }

    public int MassTonnes { get; internal set; }

    public int PowerKw { get; internal set; }

    public int EffortKn { get; internal set; }

    /// <summary>Highest speed of the consist, units per tick.</summary>
    public int MaxSpeed { get; internal set; }

    public bool Electric { get; internal set; }

    public bool IsBrokenDown => BreakdownTicks > 0;

    public int StepFrom(int index) => _stepFrom[(_stepStart + index) & (_stepFrom.Length - 1)];

    public int StepDir(int index) => _stepDir[(_stepStart + index) & (_stepFrom.Length - 1)];

    public int StepLength(int index) => _stepLen[(_stepStart + index) & (_stepFrom.Length - 1)];

    /// <summary>The tile at the end of the authority (the last step's far end), or −1 with no steps.</summary>
    internal int EndTile { get; private set; } = -1;

    /// <summary>Total length of the steps, units.</summary>
    internal long TotalLength { get; private set; }

    internal void PushStep(int from, int dir, int length, int to)
    {
        if (StepCount == _stepFrom.Length)
        {
            Grow();
        }

        var i = (_stepStart + StepCount) & (_stepFrom.Length - 1);
        _stepFrom[i] = from;
        _stepDir[i] = (byte)dir;
        _stepLen[i] = length;
        StepCount++;
        EndTile = to;
        TotalLength += length;
    }

    internal void PopStep()
    {
        TotalLength -= _stepLen[_stepStart];
        _stepStart = (_stepStart + 1) & (_stepFrom.Length - 1);
        StepCount--;
        HeadStep--;
        if (StepCount == 0)
        {
            EndTile = -1;
        }
    }

    internal void ClearSteps()
    {
        StepCount = 0;
        _stepStart = 0;
        HeadStep = 0;
        HeadOffset = 0;
        EndTile = -1;
        TotalLength = 0;
    }

    /// <summary>Position of the head from the start of the tail step, units.</summary>
    internal long HeadPosition
    {
        get
        {
            long p = HeadOffset;
            for (var i = 0; i < HeadStep; i++)
            {
                p += StepLength(i);
            }

            return p;
        }
    }

    /// <summary>Flips the deque (reversing at a terminus or dead end): the old tail becomes the head. Allocation-free.</summary>
    internal void ReverseSteps()
    {
        var count = StepCount;
        if (count == 0)
        {
            return;
        }

        var tail = HeadPosition - Length;
        var total = TotalLength;
        var oldEnd = EndTile;
        var firstFrom = StepFrom(0);
        Normalize();
        Array.Reverse(_stepDir, 0, count);
        Array.Reverse(_stepLen, 0, count);
        Array.Reverse(_stepFrom, 0, count);
        for (var i = 0; i < count; i++)
        {
            _stepDir[i] = (byte)Roads.Dir8.Opposite(_stepDir[i]);
        }

        // New from-tiles: the old end, then the old from-tiles from last to second.
        for (var i = count - 1; i > 0; i--)
        {
            _stepFrom[i] = _stepFrom[i - 1];
        }

        _stepFrom[0] = oldEnd;
        EndTile = firstFrom;

        // The new head stands where the tail was (never before the start).
        var position = Math.Clamp(total - Math.Max(0, tail), 0, total);
        var index = 0;
        while (index < count - 1 && position > StepLength(index))
        {
            position -= StepLength(index);
            index++;
        }

        HeadStep = index;
        HeadOffset = (int)position;
        Flags ^= TrainBits.Reversed;
    }

    /// <summary>Moves the ring so the tail step is at index 0 of the arrays.</summary>
    private void Normalize()
    {
        if (_stepStart == 0)
        {
            return;
        }

        var size = _stepFrom.Length;
        var start = _stepStart;
        Rotate(_stepFrom, start, size);
        Rotate(_stepDir, start, size);
        Rotate(_stepLen, start, size);
        _stepStart = 0;
    }

    private static void Rotate<T>(T[] array, int start, int size)
    {
        Array.Reverse(array, 0, start);
        Array.Reverse(array, start, size - start);
        Array.Reverse(array, 0, size);
    }

    private void Grow()
    {
        var size = _stepFrom.Length * 2;
        var from = new int[size];
        var dir = new byte[size];
        var len = new int[size];
        for (var i = 0; i < StepCount; i++)
        {
            from[i] = StepFrom(i);
            dir[i] = (byte)StepDir(i);
            len[i] = StepLength(i);
        }

        _stepFrom = from;
        _stepDir = dir;
        _stepLen = len;
        _stepStart = 0;
    }

    internal void AppendTo(StateHasher h)
    {
        h.Append(Id);
        h.Append(Owner);
        h.Append(Parts.Count);
        foreach (var part in Parts)
        {
            h.Append(part.Model);
            h.Append(part.Cargo);
            h.Append(part.PurchaseDay);
            h.Append(part.Price);
            if (part.RenewalNotified)
            {
                h.Append("renewal-notified");
            }
            h.Append(part.Hold);
        }

        h.Append(Line);
        h.Append(StopIndex);
        h.Append(Depot);
        h.Append(HomeDepot);
        h.Append((int)Mode | ((int)Flags << 8) | ((int)Orders << 24));
        h.Append((int)TargetKind);
        h.Append(TargetId);
        h.Append(Speed);
        h.Append(StepCount);
        for (var i = 0; i < StepCount; i++)
        {
            h.Append(StepFrom(i));
            h.Append(StepDir(i));
        }

        h.Append(HeadStep);
        h.Append(HeadOffset);
        h.Append(Route.Count);
        for (var i = 0; i < Route.Count; i++)
        {
            h.Append(Route.FromAt(i));
            h.Append(Route.DirAt(i));
        }

        h.Append(RoutePos);
        h.Append(Timer);
        h.Append(ServedMask);
        h.Append(WaitTicks);
        h.Append(LastPlanTick);
        h.Append(AgeDays);
        h.Append(Reliability);
        h.Append(DaysSinceService);
        h.Append(ServiceInterval);
        h.Append(BreakdownTicks);
        h.Append(ProfitThisYear);
        h.Append(ProfitLastYear);
    }
}
