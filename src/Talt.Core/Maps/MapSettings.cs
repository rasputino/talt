using System.Globalization;

namespace Talt.Core.Maps;

/// <summary>
/// The player's choices for a new map (data-model.md, MapSettings). The same settings always
/// produce the same map; <see cref="ToText"/> turns them into one short text players can share.
/// </summary>
/// <param name="Width">Map width in tiles: a multiple of 32 from 64 to 16 384 (players: 256 and up).</param>
/// <param name="Depth">Map depth in tiles, same rules as <paramref name="Width"/>.</param>
/// <param name="Seed">Random seed; any value is valid.</param>
/// <param name="Roughness">0 to 100: amount of small-scale detail and local relief.</param>
/// <param name="MaxHeight">8 to 127: highest terrain surface, in voxels.</param>
/// <param name="WaterAmount">0 to 90: percentage of the map under the sea.</param>
/// <param name="SeaLevel">2 to <c>MaxHeight - 2</c>: height of the sea surface, in voxels.</param>
public sealed record MapSettings(
    int Width = 1024,
    int Depth = 1024,
    long Seed = 1,
    int Roughness = 50,
    int MaxHeight = 28,
    int WaterAmount = 25,
    int SeaLevel = 7)
{
    /// <summary>Bumped whenever a change alters the map produced by the same settings.</summary>
    public const int GeneratorVersion = 2;

    /// <summary>Smallest side the player interface offers; the generator itself accepts <see cref="MinSide"/>.</summary>
    public const int PlayerMinimumSize = 256;

    public const int MinSide = 64;
    public const int MaxSide = 16_384;
    public const int MinMaxHeight = 8;
    public const int MaxMaxHeight = 127;
    public const int MaxWaterAmount = 90;
    public const int MinSeaLevel = 2;

    /// <summary>Throws <see cref="ArgumentException"/> naming the first invalid setting and its allowed range.</summary>
    public void Validate()
    {
        CheckSide(Width, nameof(Width));
        CheckSide(Depth, nameof(Depth));
        CheckRange(Roughness, 0, 100, nameof(Roughness));
        CheckRange(MaxHeight, MinMaxHeight, MaxMaxHeight, nameof(MaxHeight));
        CheckRange(WaterAmount, 0, MaxWaterAmount, nameof(WaterAmount));
        CheckRange(SeaLevel, MinSeaLevel, MaxHeight - 2, nameof(SeaLevel));
    }

    /// <summary>Shareable text, e.g. <c>T2-1024x512-s2026-r50-h28-w25-l7</c>.</summary>
    public string ToText() =>
        string.Create(CultureInfo.InvariantCulture, $"T{GeneratorVersion}-{Width}x{Depth}-s{Seed}-r{Roughness}-h{MaxHeight}-w{WaterAmount}-l{SeaLevel}");

    /// <summary>Parses text written by <see cref="ToText"/>; the result is validated.</summary>
    public static bool TryParse(string? text, out MapSettings? settings, out string? error)
    {
        settings = null;
        error = null;

        // A negative seed adds a second '-' ("s-5"): mark it so the split below keeps it in one part.
        var normalized = (text ?? string.Empty).Trim().Replace("-s-", "-s~", StringComparison.Ordinal);
        var parts = normalized.Split('-');
        if (parts.Length != 7 || parts[0].Length < 2 || parts[0][0] != 'T'
            || !int.TryParse(parts[0].AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var version))
        {
            error = "Map text must look like T2-1024x512-s2026-r50-h28-w25-l7.";
            return false;
        }

        if (version != GeneratorVersion)
        {
            error = $"Map text is for generator version {version}, this game implements version {GeneratorVersion}.";
            return false;
        }

        var size = parts[1].Split('x');
        if (size.Length != 2
            || !int.TryParse(size[0], NumberStyles.None, CultureInfo.InvariantCulture, out var width)
            || !int.TryParse(size[1], NumberStyles.None, CultureInfo.InvariantCulture, out var depth)
            || !TryField(parts[2], 's', true, out var seed)
            || !TryField(parts[3], 'r', false, out var roughness)
            || !TryField(parts[4], 'h', false, out var maxHeight)
            || !TryField(parts[5], 'w', false, out var water)
            || !TryField(parts[6], 'l', false, out var sea))
        {
            error = "Map text must look like T2-1024x512-s2026-r50-h28-w25-l7.";
            return false;
        }

        var parsed = new MapSettings(width, depth, seed, (int)roughness, (int)maxHeight, (int)water, (int)sea);
        try
        {
            parsed.Validate();
        }
        catch (ArgumentException ex)
        {
            error = ex.Message;
            return false;
        }

        settings = parsed;
        return true;
    }

    private static bool TryField(string part, char prefix, bool allowNegative, out long value)
    {
        value = 0;
        if (part.Length < 2 || part[0] != prefix)
        {
            return false;
        }

        var digits = part[1..].Replace('~', '-');
        var style = allowNegative ? NumberStyles.AllowLeadingSign : NumberStyles.None;
        return long.TryParse(digits, style, CultureInfo.InvariantCulture, out value);
    }

    private static void CheckSide(int value, string name)
    {
        if (value is < MinSide or > MaxSide || value % 32 != 0)
        {
            throw new ArgumentException($"{name} must be a multiple of 32 between {MinSide} and {MaxSide} tiles (was {value}).");
        }
    }

    private static void CheckRange(int value, int min, int max, string name)
    {
        if (value < min || value > max)
        {
            throw new ArgumentException($"{name} must be between {min} and {max} (was {value}).");
        }
    }
}
