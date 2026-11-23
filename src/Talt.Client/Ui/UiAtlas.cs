namespace Talt.Client.Ui;

/// <summary>
/// The interface's single RGBA texture (CPU copy): SDF glyphs in the alpha channel with white colour,
/// and pixel-art icons as sRGB colours. Images are packed in rows ("shelves").
/// </summary>
internal sealed class UiAtlas
{
    private const int Gap = 1; // Empty pixels between images so linear filtering never bleeds.

    private int _shelfX = Gap;
    private int _shelfY = Gap;
    private int _shelfHeight;

    public UiAtlas(int width, int height)
    {
        Width = width;
        Height = height;
        Pixels = new byte[width * height * 4];

        // A white pixel at (0, 0) for solid shapes that sample the texture.
        Pixels[0] = Pixels[1] = Pixels[2] = Pixels[3] = 255;
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>RGBA8 pixels, row major.</summary>
    public byte[] Pixels { get; }

    /// <summary>Incremented whenever pixels change, so the renderer re-uploads.</summary>
    public int Version { get; private set; }

    /// <summary>Reserves a <paramref name="width"/> × <paramref name="height"/> area; returns its top-left corner.</summary>
    public (int X, int Y) Allocate(int width, int height)
    {
        if (_shelfX + width + Gap > Width)
        {
            _shelfX = Gap;
            _shelfY += _shelfHeight + Gap;
            _shelfHeight = 0;
        }

        if (_shelfY + height + Gap > Height || width + 2 * Gap > Width)
        {
            throw new InvalidOperationException($"The interface atlas ({Width} × {Height}) is full.");
        }

        var position = (_shelfX, _shelfY);
        _shelfX += width + Gap;
        _shelfHeight = Math.Max(_shelfHeight, height);
        Version++;
        return position;
    }

    /// <summary>Writes a single-channel image as white with that alpha (SDF glyphs).</summary>
    public void WriteAlpha(int x, int y, int width, int height, ReadOnlySpan<byte> alpha)
    {
        for (var row = 0; row < height; row++)
        {
            for (var column = 0; column < width; column++)
            {
                var index = ((y + row) * Width + x + column) * 4;
                Pixels[index] = 255;
                Pixels[index + 1] = 255;
                Pixels[index + 2] = 255;
                Pixels[index + 3] = alpha[row * width + column];
            }
        }

        Version++;
    }

    /// <summary>Writes one RGBA pixel.</summary>
    public void WritePixel(int x, int y, UiColor color)
    {
        var index = (y * Width + x) * 4;
        Pixels[index] = color.R;
        Pixels[index + 1] = color.G;
        Pixels[index + 2] = color.B;
        Pixels[index + 3] = color.A;
    }

    public void MarkChanged() => Version++;
}
