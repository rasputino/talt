namespace Talt.Client.Ui;

/// <summary>Where an icon lies in the atlas (texture coordinates).</summary>
internal readonly record struct IconImage(float U0, float V0, float U1, float V1);

/// <summary>
/// Original pixel-art icons from <c>Ui/Data/icons.json</c> (research R3), rasterized into the
/// interface atlas at startup.
/// </summary>
internal sealed class Icons
{
    private const string FileName = "icons.json";
    private readonly Dictionary<string, IconImage> _icons;

    private Icons(Dictionary<string, IconImage> icons)
    {
        _icons = icons;
    }

    /// <summary>Names of all icons.</summary>
    public IEnumerable<string> Names => _icons.Keys;

    /// <summary>Loads and validates the embedded icons into <paramref name="atlas"/>.</summary>
    public static Icons Load(UiAtlas atlas) => FromData(DataFiles.Load<IconsData>(FileName), atlas);

    internal static Icons FromData(IconsData data, UiAtlas atlas)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(atlas);
        if (data.Size <= 0 || data.Icons is null)
        {
            throw DataFiles.Invalid(FileName, "needs a positive 'size' and an 'icons' object.");
        }

        var icons = new Dictionary<string, IconImage>(StringComparer.Ordinal);
        foreach (var (name, icon) in data.Icons)
        {
            if (icon.Rows is null || icon.Rows.Length != data.Size || icon.Rows.Any(row => row.Length != data.Size))
            {
                throw DataFiles.Invalid(FileName, $"icon '{name}' must have {data.Size} rows of {data.Size} characters.");
            }

            var palette = new Dictionary<char, UiColor>();
            foreach (var (key, value) in icon.Palette ?? [])
            {
                if (key.Length != 1 || !UiColor.TryParse(value, out var color))
                {
                    throw DataFiles.Invalid(FileName, $"icon '{name}' has an invalid palette entry '{key}'.");
                }

                palette[key[0]] = color;
            }

            var (x, y) = atlas.Allocate(data.Size, data.Size);
            for (var row = 0; row < data.Size; row++)
            {
                for (var column = 0; column < data.Size; column++)
                {
                    var c = icon.Rows[row][column];
                    if (c == '.')
                    {
                        continue;
                    }

                    if (!palette.TryGetValue(c, out var color))
                    {
                        throw DataFiles.Invalid(FileName, $"icon '{name}' uses '{c}', which is not in its palette.");
                    }

                    atlas.WritePixel(x + column, y + row, color);
                }
            }

            icons[name] = new IconImage(
                x / (float)atlas.Width, y / (float)atlas.Height,
                (x + data.Size) / (float)atlas.Width, (y + data.Size) / (float)atlas.Height);
        }

        atlas.MarkChanged();
        return new Icons(icons);
    }

    /// <summary>The icon called <paramref name="name"/>; throws for unknown names (a programming error).</summary>
    public IconImage Get(string name) =>
        _icons.TryGetValue(name, out var icon) ? icon : throw new KeyNotFoundException($"Unknown icon '{name}'.");

    public bool Contains(string name) => _icons.ContainsKey(name);

    /// <summary>JSON shape of icons.json.</summary>
    internal sealed class IconsData
    {
        public int Size { get; set; }
        public Dictionary<string, IconData>? Icons { get; set; }
    }

    internal sealed class IconData
    {
        public Dictionary<string, string>? Palette { get; set; }
        public string[]? Rows { get; set; }
    }
}
