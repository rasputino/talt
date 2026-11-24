using Talt.Core.Construction;

namespace Talt.Client.Construction;

/// <summary>
/// The road type chosen for the road, bridge and tunnel tools (US5, FR-040). Interface state, not simulation
/// state: the chosen type travels in each command by its id. When the chosen type is not available in the game
/// year, the tools use the best available ordinary type.
/// </summary>
internal static class RoadToolOptions
{
    private static string _typeId = "paved";

    /// <summary>Incremented when the choice changes (previews are planned again).</summary>
    public static int Version { get; private set; }

    /// <summary>The id the next command carries (the effective type of the last plan).</summary>
    public static string TypeId => _typeId;

    /// <summary>The chosen type when available in the state's year, otherwise the best ordinary type; remembers it.</summary>
    public static int TypeFor(ConstructionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var rules = state.RoadRules;
        var index = rules.FindType(_typeId);
        if (index < 0 || rules.Types[index].Motorway || !rules.IsAvailable(index, state.Year))
        {
            index = Math.Max(0, rules.BestOrdinaryType(state.Year));
            _typeId = rules.Types[index].Id;
        }

        return index;
    }

    /// <summary>Selects the next ordinary type available in the year (wrapping around).</summary>
    public static void Cycle(ConstructionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var rules = state.RoadRules;
        var current = TypeFor(state);
        for (var step = 1; step <= rules.Types.Count; step++)
        {
            var candidate = (current + step) % rules.Types.Count;
            if (!rules.Types[candidate].Motorway && rules.IsAvailable(candidate, state.Year))
            {
                _typeId = rules.Types[candidate].Id;
                break;
            }
        }

        Version++;
    }

    /// <summary>Tests: back to the default choice.</summary>
    internal static void Reset()
    {
        _typeId = "paved";
        Version++;
    }
}
