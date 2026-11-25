using System.Numerics;
using Talt.Client.Ui.Font;

namespace Talt.Client.Ui.Widgets;

/// <summary>Which theme font size a text uses.</summary>
internal enum TextSize
{
    Small,
    Body,
    Title,
    Heading,
}

/// <summary>Which theme colour a text uses.</summary>
internal enum TextColor
{
    Normal,
    Muted,
    Accent,

    /// <summary>Income and positive money (feature 011).</summary>
    Positive,

    /// <summary>Expenses and negative money (feature 011).</summary>
    Negative,
}

/// <summary>Localized text, on one line (cut with an ellipsis) or wrapped.</summary>
internal sealed class Label : Widget
{
    private readonly List<(int Start, int Length)> _lines = [];
    private string? _wrappedText;
    private float _wrappedWidth = -1;
    private float _wrappedSize;

    public Label(TextRef text, TextSize size = TextSize.Body)
    {
        Text = text;
        Size = size;
        VerticalAlignment = Alignment.Center;
    }

    public TextRef Text { get; set; }

    public TextSize Size { get; set; }

    public TextColor Color { get; set; } = TextColor.Normal;

    /// <summary>When set, picks the colour every frame instead of <see cref="Color"/> (red negative cash).</summary>
    public Func<TextColor>? ColorOf { get; set; }

    /// <summary>0 (invisible) to 1 (opaque), for fading messages.</summary>
    public float Opacity { get; set; } = 1f;

    /// <summary>Breaks the text into lines to fit the width instead of cutting it.</summary>
    public bool Wrap { get; set; }

    public Alignment TextAlignment { get; set; } = Alignment.Start;

    /// <summary>The text currently shown.</summary>
    public string CurrentText(UiContext ui) => Text.Resolve(ui);

    protected override Vector2 MeasureCore(UiContext ui, Vector2 available)
    {
        var text = Text.Resolve(ui);
        var size = ui.FontSize(Size);
        var lineHeight = MathF.Ceiling(TextLayout.LineHeight(ui.Font, size));
        if (!Wrap)
        {
            return new Vector2(MathF.Ceiling(TextLayout.Measure(ui.Font, text, size)), lineHeight);
        }

        UpdateLines(ui, text, size, available.X);
        var width = 0f;
        foreach (var (start, length) in _lines)
        {
            width = MathF.Max(width, TextLayout.Measure(ui.Font, text.AsSpan(start, length), size));
        }

        return new Vector2(MathF.Ceiling(width), lineHeight * _lines.Count);
    }

    protected override void DrawSelf(UiContext ui, UiDrawList list)
    {
        var text = Text.Resolve(ui);
        var size = ui.FontSize(Size);
        var color = ui.TextColorOf(ColorOf?.Invoke() ?? Color, IsEnabledInTree).WithAlpha(Opacity);
        var lineHeight = MathF.Ceiling(TextLayout.LineHeight(ui.Font, size));
        if (!Wrap)
        {
            var y = Bounds.Y + MathF.Round((Bounds.Height - lineHeight) / 2f);
            var x = AlignX(ui, text, size, Bounds.Width);
            list.AddTextClipped(ui.Font, text, new Vector2(x, y), size, color, Bounds.Width);
            return;
        }

        UpdateLines(ui, text, size, Bounds.Width);
        var top = Bounds.Y;
        foreach (var (start, length) in _lines)
        {
            var line = text.AsSpan(start, length);
            list.AddText(ui.Font, line, new Vector2(AlignX(ui, line, size, Bounds.Width), top), size, color);
            top += lineHeight;
        }
    }

    private float AlignX(UiContext ui, ReadOnlySpan<char> text, float size, float width)
    {
        if (TextAlignment is Alignment.Start or Alignment.Stretch)
        {
            return Bounds.X;
        }

        var free = MathF.Max(0, width - TextLayout.Measure(ui.Font, text, size));
        return Bounds.X + MathF.Round(TextAlignment == Alignment.Center ? free / 2f : free);
    }

    private void UpdateLines(UiContext ui, string text, float size, float width)
    {
        if (ReferenceEquals(text, _wrappedText) && width == _wrappedWidth && size == _wrappedSize)
        {
            return;
        }

        TextLayout.WrapLines(ui.Font, text, size, MathF.Max(1, width), _lines);
        _wrappedText = text;
        _wrappedWidth = width;
        _wrappedSize = size;
    }
}
