namespace Talt.Core.Stations;

/// <summary>
/// Station options of a scenario (feature 010, research R10): the debug service share that shows cargo flow
/// before vehicles exist (clarification Q3). Part of the scenario settings, so recorded sessions replay it.
/// </summary>
/// <param name="DebugServicePercent">
/// 0–100: every station's handled cargo is served and this share of waiting cargo is picked up every
/// <see cref="StationRules.DebugIntervalDays"/> days (<c>--station-service</c>).
/// </param>
public sealed record StationSettings(int DebugServicePercent = 0)
{
    public static StationSettings Default { get; } = new();

    /// <summary>Throws <see cref="ArgumentException"/> describing the first invalid value.</summary>
    public void Validate()
    {
        if (DebugServicePercent is < 0 or > 100)
        {
            throw new ArgumentException($"Station service must be between 0 and 100 percent (was {DebugServicePercent}).");
        }
    }
}
