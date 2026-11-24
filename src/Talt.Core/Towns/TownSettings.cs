namespace Talt.Core.Towns;

/// <summary>Which name lists a map's towns use (clarification Q2).</summary>
public enum TownNameStyle
{
    Mixed,
    English,
    Spanish,
}

/// <summary>Scale of the starting town sizes (clarification Q4).</summary>
public enum TownSize
{
    Small,
    Normal,
    Large,
}

/// <summary>
/// Town options of a scenario (data-model.md TownSettings): new-game choices plus the debug service share
/// (clarification Q5). Integers and enums only, so settings are exact on every platform.
/// </summary>
/// <param name="NameStyle">Name lists used for the map's towns.</param>
/// <param name="Size">Scale of the starting populations.</param>
/// <param name="DebugServicePercent">0–100: share of every town's passengers and mail counted as transported each month.</param>
public sealed record TownSettings(
    TownNameStyle NameStyle = TownNameStyle.Mixed,
    TownSize Size = TownSize.Normal,
    int DebugServicePercent = 0)
{
    public static TownSettings Default { get; } = new();

    /// <summary>Throws <see cref="ArgumentException"/> describing the first invalid value.</summary>
    public void Validate()
    {
        if (!Enum.IsDefined(NameStyle))
        {
            throw new ArgumentException($"Unknown town name style {NameStyle}.");
        }

        if (!Enum.IsDefined(Size))
        {
            throw new ArgumentException($"Unknown town size {Size}.");
        }

        if (DebugServicePercent is < 0 or > 100)
        {
            throw new ArgumentException($"Town service must be between 0 and 100 percent (was {DebugServicePercent}).");
        }
    }

    /// <summary>Lower-case text of a name style ("mixed", "english", "spanish").</summary>
    public static string ToText(TownNameStyle style) => style.ToString().ToLowerInvariant();

    /// <summary>Lower-case text of a size ("small", "normal", "large").</summary>
    public static string ToText(TownSize size) => size.ToString().ToLowerInvariant();

    public static bool TryParseNameStyle(string? text, out TownNameStyle style) =>
        Enum.TryParse(text, ignoreCase: true, out style) && Enum.IsDefined(style) && !int.TryParse(text, out _);

    public static bool TryParseSize(string? text, out TownSize size) =>
        Enum.TryParse(text, ignoreCase: true, out size) && Enum.IsDefined(size) && !int.TryParse(text, out _);
}
