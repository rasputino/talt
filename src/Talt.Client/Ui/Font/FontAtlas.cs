using System.Diagnostics;
using StbTrueTypeSharp;

namespace Talt.Client.Ui.Font;

/// <summary>One glyph of the SDF atlas, with metrics at the atlas base size.</summary>
internal readonly record struct Glyph(
    float Advance,
    float OffsetX,
    float OffsetY,
    float Width,
    float Height,
    float U0,
    float V0,
    float U1,
    float V1)
{
    public bool HasImage => Width > 0;
}

/// <summary>
/// Signed-distance-field font atlas generated at startup from the bundled Noto Sans (research R2).
/// One atlas serves every font size and interface scale: the shader rebuilds sharp edges from the
/// distance values.
/// </summary>
internal sealed unsafe class FontAtlas
{
    /// <summary>Em size, in pixels, at which glyph distances are generated.</summary>
    public const float BaseSize = 32f;

    /// <summary>Pixels of distance stored around each glyph.</summary>
    public const int Padding = 5;

    private const byte OnEdgeValue = 128;
    private const int DirectLookupLimit = 0x2800; // Every requested code point is below this.
    private const char Fallback = '?';

    private readonly Glyph[] _direct = new Glyph[DirectLookupLimit];
    private readonly bool[] _directPresent = new bool[DirectLookupLimit];
    private readonly Dictionary<int, float> _kerning = [];
    private readonly StbTrueType.stbtt_fontinfo _font;
    private readonly float _scale;

    private FontAtlas(UiAtlas atlas, byte[] fontData)
    {
        _font = StbTrueType.CreateFont(fontData, 0)
            ?? throw new InvalidDataException("The interface font could not be read.");
        _scale = StbTrueType.stbtt_ScaleForMappingEmToPixels(_font, BaseSize);

        int ascent, descent, lineGap;
        StbTrueType.stbtt_GetFontVMetrics(_font, &ascent, &descent, &lineGap);
        Ascent = ascent * _scale;
        Descent = descent * _scale;
        LineGap = lineGap * _scale;
        Build(atlas);
    }

    /// <summary>Distance from the baseline to the top of the tallest glyphs, at <see cref="BaseSize"/>.</summary>
    public float Ascent { get; }

    /// <summary>Distance from the baseline to the bottom (negative), at <see cref="BaseSize"/>.</summary>
    public float Descent { get; }

    public float LineGap { get; }

    /// <summary>Number of glyphs in the atlas.</summary>
    public int GlyphCount => _directPresent.Count(present => present);

    /// <summary>Milliseconds spent generating the atlas.</summary>
    public double BuildMilliseconds { get; private set; }

    /// <summary>The code points the interface needs: English, Spanish, typography and currency symbols.</summary>
    public static IEnumerable<int> RequestedCodePoints()
    {
        for (var c = 0x20; c <= 0x7E; c++)
        {
            yield return c;
        }

        for (var c = 0xA0; c <= 0x17F; c++)
        {
            yield return c; // Latin-1 Supplement and Latin Extended-A.
        }

        for (var c = 0x2010; c <= 0x203A; c++)
        {
            yield return c; // Dashes, quotes, bullet, ellipsis, per mille, angle quotes.
        }

        for (var c = 0x20A0; c <= 0x20C0; c++)
        {
            yield return c; // Currency symbols.
        }

        yield return 0x2122; // ™
        yield return 0x2190; // ←
        yield return 0x2191;
        yield return 0x2192;
        yield return 0x2193;
        yield return 0x2212; // −
        yield return 0x221E; // ∞ (infinite money, feature 011)
        yield return 0x2715; // ✕ (when the font has it)
    }

    /// <summary>Loads the embedded font and adds its glyphs to <paramref name="atlas"/>.</summary>
    public static FontAtlas Create(UiAtlas atlas)
    {
        using var stream = DataFiles.Open("font/NotoSans-Regular.ttf");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return new FontAtlas(atlas, memory.ToArray());
    }

    /// <summary>Whether the font has a glyph for <paramref name="codePoint"/>.</summary>
    public bool Contains(int codePoint) => (uint)codePoint < DirectLookupLimit && _directPresent[codePoint];

    /// <summary>The glyph for a character, or '?' when the font lacks it.</summary>
    public ref readonly Glyph Get(int codePoint)
    {
        if ((uint)codePoint < DirectLookupLimit && _directPresent[codePoint])
        {
            return ref _direct[codePoint];
        }

        return ref _direct[Fallback];
    }

    /// <summary>Kerning between two characters at <see cref="BaseSize"/> (pairs within Latin-1 only).</summary>
    public float Kerning(char left, char right) =>
        left < 0x180 && right < 0x180 && _kerning.TryGetValue((left << 16) | right, out var value) ? value : 0f;

    private void Build(UiAtlas atlas)
    {
        var timer = Stopwatch.StartNew();
        var codePoints = RequestedCodePoints()
            .Distinct()
            .Where(c => StbTrueType.stbtt_FindGlyphIndex(_font, c) != 0 || c == ' ')
            .ToArray();

        // Glyph distances are independent, so they are generated in parallel; packing is sequential.
        var images = new (int Width, int Height, int OffsetX, int OffsetY, byte[]? Pixels, float Advance)[codePoints.Length];
        Parallel.For(0, codePoints.Length, i =>
        {
            int width, height, offsetX, offsetY, advance, bearing;
            StbTrueType.stbtt_GetCodepointHMetrics(_font, codePoints[i], &advance, &bearing);
            var bitmap = StbTrueType.stbtt_GetCodepointSDF(_font, _scale, codePoints[i], Padding, OnEdgeValue,
                OnEdgeValue / (float)Padding, &width, &height, &offsetX, &offsetY);
            byte[]? pixels = null;
            if (bitmap != null)
            {
                pixels = new ReadOnlySpan<byte>(bitmap, width * height).ToArray();
                StbTrueType.stbtt_FreeSDF(bitmap, null);
            }

            images[i] = (width, height, offsetX, offsetY, pixels, advance * _scale);
        });

        var order = Enumerable.Range(0, codePoints.Length).OrderByDescending(i => images[i].Height).ToArray();
        foreach (var i in order)
        {
            var image = images[i];
            var glyph = new Glyph(image.Advance, 0, 0, 0, 0, 0, 0, 0, 0);
            if (image.Pixels is not null && image.Width > 0)
            {
                var (x, y) = atlas.Allocate(image.Width, image.Height);
                atlas.WriteAlpha(x, y, image.Width, image.Height, image.Pixels);
                glyph = new Glyph(
                    image.Advance, image.OffsetX, image.OffsetY, image.Width, image.Height,
                    x / (float)atlas.Width, y / (float)atlas.Height,
                    (x + image.Width) / (float)atlas.Width, (y + image.Height) / (float)atlas.Height);
            }

            _direct[codePoints[i]] = glyph;
            _directPresent[codePoints[i]] = true;
        }

        if (!_directPresent[Fallback])
        {
            throw new InvalidDataException("The interface font has no '?' glyph.");
        }

        BuildKerning(codePoints);
        BuildMilliseconds = timer.Elapsed.TotalMilliseconds;
    }

    private void BuildKerning(int[] codePoints)
    {
        var latin = codePoints.Where(c => c is >= 0x20 and < 0x180).ToArray();
        foreach (var left in latin)
        {
            foreach (var right in latin)
            {
                var kern = StbTrueType.stbtt_GetCodepointKernAdvance(_font, left, right);
                if (kern != 0)
                {
                    _kerning[(left << 16) | right] = kern * _scale;
                }
            }
        }
    }
}
