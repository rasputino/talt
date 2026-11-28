using System.Numerics;
using System.Runtime.InteropServices;
using Talt.Client.Ui.Font;

namespace Talt.Client.Ui;

/// <summary>Vertex of the interface pipeline. Must match <c>VertexIn</c> in ui.wgsl (44 bytes).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct UiVertex
{
    /// <summary>Framebuffer pixels.</summary>
    public Vector2 Position;

    /// <summary>Atlas texture coordinates.</summary>
    public Vector2 Uv;

    /// <summary>Position relative to the shape's centre (rounded rectangles).</summary>
    public Vector2 Local;

    /// <summary>sRGB colour, RGBA bytes.</summary>
    public uint Color;

    /// <summary>x, y: half size; z: corner radius; w: mode (see <see cref="UiDrawList"/>).</summary>
    public Vector4 Shape;

    public const int SizeInBytes = 44;
}

/// <summary>One draw call: a range of quads with a scissor rectangle.</summary>
internal readonly record struct UiDrawCommand(int FirstQuad, int QuadCount, RectF Clip);

/// <summary>
/// The interface's geometry for one frame: quads (4 vertices each, drawn with a shared index buffer)
/// and draw commands split by clip rectangle. GPU-free so it can be built and tested anywhere.
/// After the first frames the arrays are large enough and building a frame allocates nothing (FR-029).
/// </summary>
internal sealed class UiDrawList
{
    public const float ModeRect = 0f;
    public const float ModeText = 1f;
    public const float ModeIcon = 2f;

    private readonly Stack<RectF> _clips = new();
    private UiVertex[] _vertices = new UiVertex[4 * 1024];
    private UiDrawCommand[] _commands = new UiDrawCommand[64];
    private int _quads;
    private int _commandCount;
    private int _commandStart;
    private RectF _clip;
    private RectF _screen;

    public ReadOnlySpan<UiVertex> Vertices => _vertices.AsSpan(0, _quads * 4);

    public ReadOnlySpan<UiDrawCommand> Commands => _commands.AsSpan(0, _commandCount);

    public int QuadCount => _quads;

    /// <summary>Starts a new frame covering the whole framebuffer.</summary>
    public void Begin(float width, float height)
    {
        _quads = 0;
        _commandCount = 0;
        _commandStart = 0;
        _clips.Clear();
        _screen = new RectF(0, 0, width, height);
        _clip = _screen;
    }

    /// <summary>Closes the last draw command.</summary>
    public void End() => FlushCommand();

    /// <summary>Restricts the following shapes to <paramref name="clip"/> (intersected with the current clip).</summary>
    public void PushClip(RectF clip)
    {
        FlushCommand();
        _clips.Push(_clip);
        _clip = _clip.Intersect(clip);
    }

    public void PopClip()
    {
        FlushCommand();
        _clip = _clips.Count > 0 ? _clips.Pop() : _screen;
    }

    /// <summary>A filled rectangle with rounded corners.</summary>
    public void AddRect(RectF rect, UiColor color, float radius = 0f)
    {
        if (rect.Width <= 0 || rect.Height <= 0 || color.A == 0)
        {
            return;
        }

        var half = rect.Size / 2f;
        var shape = new Vector4(half.X, half.Y, MathF.Min(radius, MathF.Min(half.X, half.Y)), ModeRect);
        AddQuad(rect.X, rect.Y, rect.Right, rect.Bottom, 0, 0, 0, 0, color, shape);
    }

    /// <summary>A rounded rectangle with a border of <paramref name="borderWidth"/> pixels.</summary>
    public void AddPanel(RectF rect, UiColor fill, UiColor border, float borderWidth, float radius)
    {
        if (borderWidth > 0 && border.A > 0)
        {
            AddRect(rect, border, radius);
            rect = new RectF(rect.X + borderWidth, rect.Y + borderWidth, rect.Width - 2 * borderWidth, rect.Height - 2 * borderWidth);
            radius = MathF.Max(0, radius - borderWidth);
        }

        AddRect(rect, fill, radius);
    }

    /// <summary>A filled projected tile or line in screen coordinates, using the existing UI geometry pass.</summary>
    public void AddPolygon(Vector2 a, Vector2 b, Vector2 c, Vector2 d, UiColor color)
    {
        var min = Vector2.Min(Vector2.Min(a, b), Vector2.Min(c, d));
        var max = Vector2.Max(Vector2.Max(a, b), Vector2.Max(c, d));
        if (max.X <= _clip.X || min.X >= _clip.Right || max.Y <= _clip.Y || min.Y >= _clip.Bottom)
        {
            return;
        }

        if ((_quads + 1) * 4 > _vertices.Length)
        {
            Array.Resize(ref _vertices, _vertices.Length * 2);
        }

        var i = _quads++ * 4;
        var vertex = new UiVertex { Color = color.Packed, Shape = new Vector4(1, 1, 0, ModeRect), Position = a };
        _vertices[i] = vertex;
        vertex.Position = b;
        _vertices[i + 1] = vertex;
        vertex.Position = c;
        _vertices[i + 2] = vertex;
        vertex.Position = d;
        _vertices[i + 3] = vertex;
    }

    /// <summary>An icon stretched over <paramref name="rect"/>, multiplied by <paramref name="tint"/>.</summary>
    public void AddIcon(IconImage icon, RectF rect, UiColor tint)
    {
        var half = rect.Size / 2f;
        AddQuad(rect.X, rect.Y, rect.Right, rect.Bottom, icon.U0, icon.V0, icon.U1, icon.V1, tint,
            new Vector4(half.X, half.Y, 0, ModeIcon));
    }

    /// <summary>
    /// One line of text with its top-left corner at <paramref name="position"/>; returns its width.
    /// </summary>
    public float AddText(FontAtlas font, ReadOnlySpan<char> text, Vector2 position, float size, UiColor color)
    {
        ArgumentNullException.ThrowIfNull(font);
        var scale = size / FontAtlas.BaseSize;
        var baseline = MathF.Round(position.Y + TextLayout.Baseline(font, size));
        var x = position.X;
        for (var i = 0; i < text.Length; i++)
        {
            ref readonly var glyph = ref font.Get(text[i]);
            if (glyph.HasImage)
            {
                var x0 = x + glyph.OffsetX * scale;
                var y0 = baseline + glyph.OffsetY * scale;
                AddQuad(x0, y0, x0 + glyph.Width * scale, y0 + glyph.Height * scale,
                    glyph.U0, glyph.V0, glyph.U1, glyph.V1, color, new Vector4(0, 0, 0, ModeText));
            }

            x += glyph.Advance * scale;
            if (i + 1 < text.Length)
            {
                x += font.Kerning(text[i], text[i + 1]) * scale;
            }
        }

        return x - position.X;
    }

    /// <summary>The ellipsis as a string kept for the whole run: a <c>[c]</c> span literal allocated on every cut text.</summary>
    private static readonly string EllipsisText = TextLayout.Ellipsis.ToString();

    /// <summary>Text cut with an ellipsis when wider than <paramref name="maxWidth"/>.</summary>
    public void AddTextClipped(FontAtlas font, ReadOnlySpan<char> text, Vector2 position, float size, UiColor color, float maxWidth)
    {
        var fit = TextLayout.FitWithEllipsis(font, text, size, maxWidth);
        var width = AddText(font, text[..fit], position, size, color);
        if (fit < text.Length)
        {
            AddText(font, EllipsisText, position + new Vector2(width, 0), size, color);
        }
    }

    private void AddQuad(float x0, float y0, float x1, float y1, float u0, float v0, float u1, float v1, UiColor color, Vector4 shape)
    {
        // Skip quads entirely outside the clip: cheaper than drawing them clipped.
        if (x1 <= _clip.X || x0 >= _clip.Right || y1 <= _clip.Y || y0 >= _clip.Bottom)
        {
            return;
        }

        if ((_quads + 1) * 4 > _vertices.Length)
        {
            Array.Resize(ref _vertices, _vertices.Length * 2);
        }

        var cx = (x0 + x1) / 2f;
        var cy = (y0 + y1) / 2f;
        var i = _quads * 4;
        _vertices[i] = new UiVertex { Position = new(x0, y0), Uv = new(u0, v0), Local = new(x0 - cx, y0 - cy), Color = color.Packed, Shape = shape };
        _vertices[i + 1] = new UiVertex { Position = new(x1, y0), Uv = new(u1, v0), Local = new(x1 - cx, y0 - cy), Color = color.Packed, Shape = shape };
        _vertices[i + 2] = new UiVertex { Position = new(x1, y1), Uv = new(u1, v1), Local = new(x1 - cx, y1 - cy), Color = color.Packed, Shape = shape };
        _vertices[i + 3] = new UiVertex { Position = new(x0, y1), Uv = new(u0, v1), Local = new(x0 - cx, y1 - cy), Color = color.Packed, Shape = shape };
        _quads++;
    }

    private void FlushCommand()
    {
        if (_quads > _commandStart)
        {
            if (_commandCount == _commands.Length)
            {
                Array.Resize(ref _commands, _commands.Length * 2);
            }

            _commands[_commandCount++] = new UiDrawCommand(_commandStart, _quads - _commandStart, _clip);
        }

        _commandStart = _quads;
    }
}
