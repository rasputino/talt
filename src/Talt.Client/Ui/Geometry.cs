using System.Numerics;

namespace Talt.Client.Ui;

/// <summary>Axis-aligned rectangle in framebuffer pixels (origin top left, Y down).</summary>
internal readonly record struct RectF(float X, float Y, float Width, float Height)
{
    public float Right => X + Width;

    public float Bottom => Y + Height;

    public Vector2 Position => new(X, Y);

    public Vector2 Size => new(Width, Height);

    public Vector2 Center => new(X + Width / 2f, Y + Height / 2f);

    public bool Contains(Vector2 point) => point.X >= X && point.X < Right && point.Y >= Y && point.Y < Bottom;

    public RectF Deflate(Thickness t) =>
        new(X + t.Left, Y + t.Top, MathF.Max(0f, Width - t.Left - t.Right), MathF.Max(0f, Height - t.Top - t.Bottom));

    public RectF Intersect(RectF other)
    {
        var x = MathF.Max(X, other.X);
        var y = MathF.Max(Y, other.Y);
        var right = MathF.Min(Right, other.Right);
        var bottom = MathF.Min(Bottom, other.Bottom);
        return new RectF(x, y, MathF.Max(0f, right - x), MathF.Max(0f, bottom - y));
    }
}

/// <summary>Space around or inside a widget, in logical pixels (scaled at layout time).</summary>
internal readonly record struct Thickness(float Left, float Top, float Right, float Bottom)
{
    public Thickness(float all)
        : this(all, all, all, all)
    {
    }

    public Thickness(float horizontal, float vertical)
        : this(horizontal, vertical, horizontal, vertical)
    {
    }

    public float Horizontal => Left + Right;

    public float Vertical => Top + Bottom;

    public Thickness Scale(float scale) => new(Left * scale, Top * scale, Right * scale, Bottom * scale);
}

/// <summary>How a widget is placed inside the space its parent gives it.</summary>
internal enum Alignment
{
    Stretch,
    Start,
    Center,
    End,
}

internal enum Orientation
{
    Vertical,
    Horizontal,
}

/// <summary>Colour as sRGB bytes packed RGBA (the shader converts to linear).</summary>
internal readonly record struct UiColor(uint Packed)
{
    public static readonly UiColor White = FromBytes(255, 255, 255, 255);

    public static readonly UiColor Transparent = new(0);

    public byte R => (byte)Packed;

    public byte G => (byte)(Packed >> 8);

    public byte B => (byte)(Packed >> 16);

    public byte A => (byte)(Packed >> 24);

    public static UiColor FromBytes(byte r, byte g, byte b, byte a) =>
        new(r | ((uint)g << 8) | ((uint)b << 16) | ((uint)a << 24));

    /// <summary>Parses <c>#RRGGBB</c> or <c>#RRGGBBAA</c>.</summary>
    public static UiColor Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (!TryParse(text, out var color))
        {
            throw new FormatException($"'{text}' is not a colour; use #RRGGBB or #RRGGBBAA.");
        }

        return color;
    }

    public static bool TryParse(string text, out UiColor color)
    {
        color = default;
        if (text is null || text.Length is not (7 or 9) || text[0] != '#'
            || !uint.TryParse(text.AsSpan(1), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        if (text.Length == 7)
        {
            value = (value << 8) | 0xFF;
        }

        color = FromBytes((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value);
        return true;
    }

    public UiColor WithAlpha(float alpha) => FromBytes(R, G, B, (byte)Math.Clamp(A * alpha, 0f, 255f));
}
