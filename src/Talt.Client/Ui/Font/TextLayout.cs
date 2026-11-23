namespace Talt.Client.Ui.Font;

/// <summary>
/// Measures and breaks text with the font atlas. Sizes are font em sizes in framebuffer pixels.
/// Allocation-free except <see cref="WrapLines"/>, which fills a caller-owned list.
/// </summary>
internal static class TextLayout
{
    public const char Ellipsis = '…';

    /// <summary>Height of one line of text.</summary>
    public static float LineHeight(FontAtlas font, float size) =>
        (font.Ascent - font.Descent + font.LineGap) * (size / FontAtlas.BaseSize);

    /// <summary>Distance from the top of a line to its baseline.</summary>
    public static float Baseline(FontAtlas font, float size) =>
        (font.Ascent + font.LineGap / 2f) * (size / FontAtlas.BaseSize);

    /// <summary>Width of a single line of text.</summary>
    public static float Measure(FontAtlas font, ReadOnlySpan<char> text, float size)
    {
        ArgumentNullException.ThrowIfNull(font);
        var scale = size / FontAtlas.BaseSize;
        var width = 0f;
        for (var i = 0; i < text.Length; i++)
        {
            width += font.Get(text[i]).Advance;
            if (i + 1 < text.Length)
            {
                width += font.Kerning(text[i], text[i + 1]);
            }
        }

        return width * scale;
    }

    /// <summary>
    /// How many characters of <paramref name="text"/> fit in <paramref name="maxWidth"/> when followed
    /// by an ellipsis; the whole length when the text fits without one.
    /// </summary>
    public static int FitWithEllipsis(FontAtlas font, ReadOnlySpan<char> text, float size, float maxWidth)
    {
        if (Measure(font, text, size) <= maxWidth)
        {
            return text.Length;
        }

        var scale = size / FontAtlas.BaseSize;
        var available = maxWidth - font.Get(Ellipsis).Advance * scale;
        var width = 0f;
        for (var i = 0; i < text.Length; i++)
        {
            width += font.Get(text[i]).Advance * scale;
            if (width > available)
            {
                return i;
            }
        }

        return text.Length;
    }

    /// <summary>
    /// Breaks text into lines no wider than <paramref name="maxWidth"/> at spaces (or anywhere when a
    /// word is longer than a line) and at '\n'. Adds (start, length) pairs to <paramref name="lines"/>.
    /// </summary>
    public static void WrapLines(FontAtlas font, string text, float size, float maxWidth, List<(int Start, int Length)> lines)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(lines);
        lines.Clear();
        var start = 0;
        while (start <= text.Length)
        {
            var newline = text.IndexOf('\n', start);
            var end = newline < 0 ? text.Length : newline;
            WrapParagraph(font, text, start, end, size, maxWidth, lines);
            if (newline < 0)
            {
                break;
            }

            start = newline + 1;
        }
    }

    private static void WrapParagraph(FontAtlas font, string text, int start, int end, float size, float maxWidth, List<(int, int)> lines)
    {
        if (start == end)
        {
            lines.Add((start, 0));
            return;
        }

        while (start < end)
        {
            var lineEnd = start;
            var lastBreak = -1;
            while (lineEnd < end && Measure(font, text.AsSpan(start, lineEnd - start + 1), size) <= maxWidth)
            {
                if (text[lineEnd] == ' ')
                {
                    lastBreak = lineEnd;
                }

                lineEnd++;
            }

            if (lineEnd >= end)
            {
                lines.Add((start, end - start));
                return;
            }

            if (lastBreak > start)
            {
                lines.Add((start, lastBreak - start));
                start = lastBreak + 1;
            }
            else
            {
                var take = Math.Max(1, lineEnd - start); // A word longer than the line: break inside it.
                lines.Add((start, take));
                start += take;
            }
        }
    }
}
