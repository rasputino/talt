using Talt.Core.Construction;

namespace Talt.Core.Rails;

/// <summary>
/// What construction asks the train system (feature 015, FR-005, FR-008): plans that change track, signals, platforms or
/// depots that a train stands on or has reserved are refused.
/// </summary>
public interface IRailOccupancy
{
    /// <summary>True when a train covers or has reserved the tile, or a train is inside a depot on it.</summary>
    bool IsTileUsed(int tile);

    /// <summary>True when the plan changes the rail piece of a used tile.</summary>
    bool Blocks(ConstructionPlan plan);
}
