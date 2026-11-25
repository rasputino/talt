namespace Talt.Core.Construction;

/// <summary>An inclusive rectangle of tiles, normalized so X0 ≤ X1 and Z0 ≤ Z1.</summary>
public readonly record struct TileRect
{
    public TileRect(int x0, int z0, int x1, int z1)
    {
        X0 = Math.Min(x0, x1);
        X1 = Math.Max(x0, x1);
        Z0 = Math.Min(z0, z1);
        Z1 = Math.Max(z0, z1);
    }

    public int X0 { get; }

    public int Z0 { get; }

    public int X1 { get; }

    public int Z1 { get; }

    public int Width => X1 - X0 + 1;

    public int Depth => Z1 - Z0 + 1;

    public long TileCount => (long)Width * Depth;

    public bool Contains(int x, int z) => x >= X0 && x <= X1 && z >= Z0 && z <= Z1;

    /// <summary>The part inside a map of the given size; false when nothing is left.</summary>
    public bool TryClip(int width, int depth, out TileRect clipped)
    {
        var x0 = Math.Max(X0, 0);
        var z0 = Math.Max(Z0, 0);
        var x1 = Math.Min(X1, width - 1);
        var z1 = Math.Min(Z1, depth - 1);
        clipped = new TileRect(x0, z0, x1, z1);
        return x0 <= x1 && z0 <= z1;
    }
}

/// <summary>
/// One tile the planned action changes (data-model.md ConstructionPlan.Changes): height, scenery and
/// object before and after, and for road actions (feature 007) the road piece before and after. Only
/// changes with <paramref name="RoadChanged"/> write the road piece, so terraform and clear never touch roads.
/// </summary>
public readonly record struct TileChange(
    int Index,
    byte OldHeight,
    byte NewHeight,
    byte OldScenery,
    byte NewScenery,
    TileObjectKind OldObject,
    TileObjectKind NewObject,
    TileOwner OldOwner,
    TileOwner NewOwner,
    Roads.RoadPiece OldRoad = default,
    Roads.RoadPiece NewRoad = default,
    bool RoadChanged = false);

/// <summary>
/// A tile where the action is not allowed, with the reason code (data-model.md ConstructionPlan.Problems).
/// A blocking problem refuses the whole action; a non-blocking one only leaves that tile out (the clear
/// tool skips objects it may not remove).
/// </summary>
public readonly record struct TileProblem(int Index, string Reason, bool Blocking = true);

/// <summary>
/// What an action would do (data-model.md ConstructionPlan): the changed tiles, the problem tiles, the
/// total cost and the first reason it is refused. Filled by <see cref="ConstructionPlanner"/>; reused
/// between plans, so planning allocates nothing once the buffers have grown (research R8).
/// </summary>
public sealed class ConstructionPlan
{
    private TileChange[] _changes = new TileChange[256];
    private TileProblem[] _problems = new TileProblem[64];

    public int ChangeCount { get; private set; }

    public int ProblemCount { get; private set; }

    public ReadOnlySpan<TileChange> Changes => _changes.AsSpan(0, ChangeCount);

    public ReadOnlySpan<TileProblem> Problems => _problems.AsSpan(0, ProblemCount);

    /// <summary>Total cost in internal money units (at today's price level once checked, feature 011).</summary>
    public long Cost { get; internal set; }

    /// <summary>True once <see cref="ConstructionCommand.Check"/> applied the price level to <see cref="Cost"/>.</summary>
    internal bool CostInflated { get; set; }

    /// <summary>The first problem's reason, or a whole-action reason (<c>too-large</c>, <c>nothing-to-do</c>...).</summary>
    public string? Reason { get; internal set; }

    /// <summary>
    /// The change of the company's town ratings the action would cause, summed over towns (feature 008; display
    /// only, set by <see cref="ConstructionCommand.Preview"/> or a tool preview; not part of a command's recorded form).
    /// </summary>
    public int RatingChange { get; set; }

    /// <summary>The clipped rectangle the player aimed at (empty plans keep the last one).</summary>
    public TileRect Area { get; internal set; }

    /// <summary>True when the rectangle had at least one tile inside the map.</summary>
    public bool HasArea { get; internal set; }

    /// <summary>Something would change and nothing forbids it (money is checked separately).</summary>
    public bool IsValid => Reason is null && ChangeCount > 0;

    public void Reset()
    {
        ChangeCount = 0;
        ProblemCount = 0;
        Cost = 0;
        CostInflated = false;
        RatingChange = 0;
        Reason = null;
        HasArea = false;
    }

    internal void EnsureCapacity(int changes)
    {
        if (_changes.Length < changes)
        {
            Array.Resize(ref _changes, changes);
        }
    }

    internal void AddChange(in TileChange change)
    {
        if (ChangeCount == _changes.Length)
        {
            Array.Resize(ref _changes, _changes.Length * 2);
        }

        _changes[ChangeCount++] = change;
    }

    internal void AddProblem(int index, string reason, bool blocking = true)
    {
        if (ProblemCount == _problems.Length)
        {
            Array.Resize(ref _problems, _problems.Length * 2);
        }

        _problems[ProblemCount++] = new TileProblem(index, reason, blocking);
        if (blocking)
        {
            Reason ??= reason;
        }
    }

    /// <summary>Copies another plan (used to keep the validated plan for the command's application).</summary>
    internal void CopyFrom(ConstructionPlan other)
    {
        Reset();
        EnsureCapacity(other.ChangeCount);
        other.Changes.CopyTo(_changes);
        ChangeCount = other.ChangeCount;
        foreach (var problem in other.Problems)
        {
            AddProblem(problem.Index, problem.Reason, problem.Blocking);
        }

        Cost = other.Cost;
        CostInflated = other.CostInflated;
        RatingChange = other.RatingChange;
        Reason = other.Reason;
        Area = other.Area;
        HasArea = other.HasArea;
    }
}
