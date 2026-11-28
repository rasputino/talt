using System.Reflection;
using System.Text.Json;
using Talt.Core.Construction;
using Talt.Core.Maps;
using Talt.Core.Simulation;

namespace Talt.Core.Saves;

/// <summary>Colours of the save thumbnail (<c>Saves/Data/overview-colors.json</c>, validated at load).</summary>
public sealed record OverviewColors(
    (byte R, byte G, byte B) Background,
    (byte R, byte G, byte B) Grass,
    (byte R, byte G, byte B) Beach,
    (byte R, byte G, byte B) Rock,
    (byte R, byte G, byte B) Snow,
    (byte R, byte G, byte B) Water,
    (byte R, byte G, byte B) Road,
    (byte R, byte G, byte B) Rail,
    (byte R, byte G, byte B) Town,
    (byte R, byte G, byte B) Industry,
    (byte R, byte G, byte B) Station,
    int ShadeMinPercent,
    int ShadeMaxPercent)
{
    private static readonly Lazy<OverviewColors> LazyDefault = new(() => FromJson(ReadResource()));

    /// <summary>The game's data file.</summary>
    public static OverviewColors Default => LazyDefault.Value;

    /// <summary>Parses and validates the colours; throws <see cref="InvalidDataException"/> with a clear message.</summary>
    public static OverviewColors FromJson(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            (byte, byte, byte) Color(string name)
            {
                if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != 3)
                {
                    throw new InvalidDataException($"overview-colors.json: '{name}' must be an array of three numbers 0-255.");
                }

                var c = value.EnumerateArray().Select(v => v.TryGetInt32(out var n) && n is >= 0 and <= 255 ? (byte)n
                    : throw new InvalidDataException($"overview-colors.json: '{name}' values must be 0-255.")).ToArray();
                return (c[0], c[1], c[2]);
            }

            int Percent(string name) => root.TryGetProperty(name, out var value) && value.TryGetInt32(out var n) && n is >= 10 and <= 300
                ? n
                : throw new InvalidDataException($"overview-colors.json: '{name}' must be a percentage from 10 to 300.");

            var colors = new OverviewColors(Color("background"), Color("grass"), Color("beach"), Color("rock"), Color("snow"), Color("water"),
                Color("road"), Color("rail"), Color("town"), Color("industry"), Color("station"), Percent("shadeMinPercent"), Percent("shadeMaxPercent"));
            return colors.ShadeMinPercent <= colors.ShadeMaxPercent
                ? colors
                : throw new InvalidDataException("overview-colors.json: shadeMinPercent must not exceed shadeMaxPercent.");
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"overview-colors.json is not valid JSON: {e.Message}", e);
        }
    }

    internal static string ReadResource()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Talt.Core.Saves.Data.overview-colors.json")
            ?? throw new InvalidDataException("The embedded overview-colors.json is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

/// <summary>
/// The save thumbnail: a top-down overview of the whole map drawn from the game state without a GPU (research R14,
/// data-model.md SaveThumbnail), so saves written by the headless tool have one too. The larger map side spans
/// <see cref="Size"/> pixels; the other is letterboxed. Each pixel shows the most important thing on the tile at its centre:
/// stations, industries, town buildings, track, roads, water, then terrain shaded by height.
/// </summary>
public static class MapOverview
{
    /// <summary>Thumbnail side in pixels.</summary>
    public const int Size = 256;

    /// <summary>The overview of <paramref name="simulation"/>'s map, or null for map-less scenarios.</summary>
    /// <param name="reuse">Pixels of an earlier thumbnail of the same size to draw into (avoids an allocation per save).</param>
    public static SaveThumbnailData? Render(GameSimulation simulation, byte[]? reuse = null, OverviewColors? colors = null)
    {
        return RenderInto(simulation, reuse is { Length: Size * Size * 3 } ? new SaveThumbnailData(Size, Size, reuse) : null, colors);
    }

    internal static SaveThumbnailData? RenderInto(GameSimulation simulation, SaveThumbnailData? reuse, OverviewColors? colors = null)
    {
        ArgumentNullException.ThrowIfNull(simulation);
        if (simulation.Construction is not { } construction)
        {
            return null;
        }

        colors ??= OverviewColors.Default;
        var map = construction.Map;
        var thumbnail = reuse ?? new SaveThumbnailData(Size, Size, new byte[Size * Size * 3]);
        var pixels = thumbnail.Pixels;
        var side = Math.Max(map.Width, map.Depth);
        var maxHeight = Math.Max(1, map.Settings.MaxHeight);
        var offsetX = (Size - (map.Width * Size / side)) / 2;
        var offsetZ = (Size - (map.Depth * Size / side)) / 2;
        for (var py = 0; py < Size; py++)
        {
            for (var px = 0; px < Size; px++)
            {
                // Pixel centre → tile, in integers: tile = (pixel - offset + 0.5) * side / Size.
                var x = (((2 * (px - offsetX)) + 1) * side) / (2 * Size);
                var z = (((2 * (py - offsetZ)) + 1) * side) / (2 * Size);
                var color = px < offsetX || py < offsetZ || !map.Contains(x, z)
                    ? colors.Background
                    : TileColor(simulation, construction, map, x, z, maxHeight, colors);
                var at = ((py * Size) + px) * 3;
                pixels[at] = color.R;
                pixels[at + 1] = color.G;
                pixels[at + 2] = color.B;
            }
        }

        return thumbnail;
    }

    private static (byte R, byte G, byte B) TileColor(GameSimulation simulation, ConstructionState construction, MapData map, int x, int z, int maxHeight, OverviewColors colors)
    {
        var tile = map.Index(x, z);
        var kind = construction.Objects.KindAt(tile);
        if (kind == TileObjectKind.Station)
        {
            return colors.Station;
        }

        if (simulation.Industries.IsOccupied(x, z))
        {
            return colors.Industry;
        }

        if (kind == TileObjectKind.TownBuilding)
        {
            return colors.Town;
        }

        if (construction.Rails.HasRail(tile))
        {
            return colors.Rail;
        }

        if (construction.Roads.HasRoad(tile))
        {
            return colors.Road;
        }

        if (map.IsWater(tile))
        {
            return colors.Water;
        }

        var baseColor = map.TerrainAt(x, z) switch
        {
            TerrainType.Beach => colors.Beach,
            TerrainType.Rock => colors.Rock,
            TerrainType.Snow => colors.Snow,
            TerrainType.Water => colors.Water,
            _ => colors.Grass,
        };
        var percent = colors.ShadeMinPercent + ((colors.ShadeMaxPercent - colors.ShadeMinPercent) * Math.Clamp(map.HeightAt(x, z), 0, maxHeight) / maxHeight);
        return (Shade(baseColor.R, percent), Shade(baseColor.G, percent), Shade(baseColor.B, percent));
    }

    private static byte Shade(byte value, int percent) => (byte)Math.Min(255, value * percent / 100);
}
