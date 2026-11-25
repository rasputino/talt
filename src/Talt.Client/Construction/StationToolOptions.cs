using Talt.Core.Stations;

namespace Talt.Client.Construction;

/// <summary>
/// The warehouse size chosen for the warehouse tool (feature 010). Interface state, not simulation state: the
/// size travels in each command by its id.
/// </summary>
internal static class StationToolOptions
{
    private static int _size;

    /// <summary>Incremented when the choice changes (previews are planned again).</summary>
    public static int Version { get; private set; }

    /// <summary>Index of the chosen warehouse size in <see cref="StationRules.Warehouses"/>.</summary>
    public static int SizeIndex(StationRules rules) => Math.Clamp(_size, 0, rules.Warehouses.Count - 1);

    /// <summary>Id of the chosen warehouse size.</summary>
    public static string SizeId(StationRules rules) => rules.Warehouses[SizeIndex(rules)].Id;

    /// <summary>Selects the next size (wrapping around).</summary>
    public static void Cycle(StationRules rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        _size = (SizeIndex(rules) + 1) % rules.Warehouses.Count;
        Version++;
    }

    /// <summary>Tests: back to the default choice.</summary>
    internal static void Reset()
    {
        _size = 0;
        Version++;
    }
}
