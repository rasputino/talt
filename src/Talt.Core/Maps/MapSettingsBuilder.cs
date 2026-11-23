using System.Globalization;

namespace Talt.Core.Maps;

/// <summary>
/// Builds <see cref="MapSettings"/> from command-line options, so the game client and the headless
/// tool accept exactly the same names (contracts/cli.md). Options are applied in order, so a later
/// option overrides an earlier one, including those inside <c>--map-text</c>.
/// </summary>
public sealed class MapSettingsBuilder
{
    public const string MapOption = "--map";
    public const string SeedOption = "--seed";
    public const string RoughnessOption = "--roughness";
    public const string MaxHeightOption = "--max-height";
    public const string WaterOption = "--water";
    public const string SeaLevelOption = "--sea-level";
    public const string TextOption = "--map-text";

    /// <summary>Creates a builder starting from <paramref name="defaults"/> (the library defaults when null).</summary>
    public MapSettingsBuilder(MapSettings? defaults = null)
    {
        Current = defaults ?? new MapSettings();
    }

    /// <summary>The settings so far.</summary>
    public MapSettings Current { get; private set; }

    /// <summary>True once a seed was given (by <c>--seed</c> or inside <c>--map-text</c>).</summary>
    public bool SeedGiven { get; private set; }

    /// <summary>True once a size was given (by <c>--map</c> or inside <c>--map-text</c>).</summary>
    public bool SizeGiven { get; private set; }

    /// <summary>Whether <paramref name="option"/> is one of the map options.</summary>
    public static bool IsMapOption(string option) =>
        option is MapOption or SeedOption or RoughnessOption or MaxHeightOption or WaterOption or SeaLevelOption or TextOption;

    /// <summary>Applies one option. Returns false and an error message when the value cannot be read.</summary>
    public bool TryApply(string option, string value, out string? error)
    {
        error = null;
        switch (option)
        {
            case MapOption:
                if (!TryParseSize(value, out var width, out var depth))
                {
                    error = $"{option} must be a size like 1024 or 1024x512 (was '{value}').";
                    return false;
                }

                Current = Current with { Width = width, Depth = depth };
                SizeGiven = true;
                return true;

            case SeedOption:
                if (!long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var seed))
                {
                    error = $"{option} must be a whole number (was '{value}').";
                    return false;
                }

                Current = Current with { Seed = seed };
                SeedGiven = true;
                return true;

            case RoughnessOption:
                return TryInt(option, value, out var roughness, out error) && Set(Current with { Roughness = roughness });
            case MaxHeightOption:
                return TryInt(option, value, out var maxHeight, out error) && Set(Current with { MaxHeight = maxHeight });
            case WaterOption:
                return TryInt(option, value, out var water, out error) && Set(Current with { WaterAmount = water });
            case SeaLevelOption:
                return TryInt(option, value, out var sea, out error) && Set(Current with { SeaLevel = sea });

            case TextOption:
                if (!MapSettings.TryParse(value, out var parsed, out error))
                {
                    return false;
                }

                Current = parsed!;
                SeedGiven = true;
                SizeGiven = true;
                return true;

            default:
                error = $"Unknown map option '{option}'.";
                return false;
        }
    }

    /// <summary>Reads "1024" (square) or "1024x512" (width x depth).</summary>
    public static bool TryParseSize(string text, out int width, out int depth)
    {
        width = 0;
        depth = 0;
        var parts = (text ?? string.Empty).Split('x', 'X');
        if (parts.Length == 1)
        {
            return int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out width) && (depth = width) >= 0;
        }

        return parts.Length == 2
            && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out width)
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out depth);
    }

    private bool Set(MapSettings settings)
    {
        Current = settings;
        return true;
    }

    private static bool TryInt(string option, string value, out int result, out string? error)
    {
        error = null;
        if (int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out result))
        {
            return true;
        }

        error = $"{option} must be a whole number (was '{value}').";
        return false;
    }
}
