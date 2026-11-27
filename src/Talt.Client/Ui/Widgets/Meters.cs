using System.Numerics;
using Talt.Client.Ui.Font;

namespace Talt.Client.Ui.Widgets;

/// <summary>How a <see cref="Meter"/> picks its fill colour.</summary>
internal enum MeterColoring
{
    /// <summary>The theme accent.</summary>
    Accent,

    /// <summary>Red when low, yellow in the middle, green when high (ratings, service).</summary>
    Ramp,

    /// <summary>Green when low, red when high (age against life).</summary>
    InverseRamp,

    /// <summary><see cref="Meter.FixedColor"/> (a cargo colour).</summary>
    Fixed,
}

/// <summary>Colour helpers of the graphical info windows.</summary>
internal static class MeterColors
{
    /// <summary>The theme's negative → warning → positive colour for a value from 0 to 1.</summary>
    public static UiColor Ramp(Theme theme, float value)
    {
        ArgumentNullException.ThrowIfNull(theme);
        value = Math.Clamp(value, 0f, 1f);
        return value < 0.5f
            ? Lerp(theme.Negative, theme.Warning, value * 2f)
            : Lerp(theme.Warning, theme.Positive, (value - 0.5f) * 2f);
    }

    public static UiColor Lerp(UiColor a, UiColor b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        static byte Mix(byte x, byte y, float t) => (byte)MathF.Round(x + ((y - x) * t));
        return UiColor.FromBytes(Mix(a.R, b.R, t), Mix(a.G, b.G, t), Mix(a.B, b.B, t), Mix(a.A, b.A, t));
    }

    /// <summary>A cargo's packed <c>0xRRGGBB</c> colour from <c>cargo.json</c>.</summary>
    public static UiColor FromRgb(uint rgb) => UiColor.FromBytes((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);

    /// <summary>A lighter or darker shade of <paramref name="color"/> (segments of one cargo by destination).</summary>
    public static UiColor Shade(UiColor color, int index) =>
        index == 0 ? color : Lerp(color, index % 2 == 1 ? UiColor.White : UiColor.FromBytes(0, 0, 0, 255), 0.18f * ((index + 1) / 2));
}

/// <summary>
/// A horizontal gauge filled from 0 to 1 (ratings, served share, speed, reliability). The value is read every frame
/// from <see cref="Value"/>; draws only rectangles and allocates nothing.
/// </summary>
internal sealed class Meter : Widget
{
    public Meter(Func<float> value, MeterColoring coloring = MeterColoring.Ramp)
    {
        Value = value ?? throw new ArgumentNullException(nameof(value));
        Coloring = coloring;
        Width = 140;
        VerticalAlignment = Alignment.Center;
    }

    /// <summary>Takes the cursor only to show its tooltip.</summary>
    public override bool IsHitTestVisible => Tooltip is not null;

    public Func<float> Value { get; }

    public MeterColoring Coloring { get; set; }

    public UiColor FixedColor { get; set; } = UiColor.White;

    /// <summary>When set, a thin mark at this value (0–1), for example a threshold.</summary>
    public Func<float>? Marker { get; set; }

    /// <summary>The fill colour for the current value.</summary>
    public UiColor FillColor(Theme theme, float value) => Coloring switch
    {
        MeterColoring.Ramp => MeterColors.Ramp(theme, value),
        MeterColoring.InverseRamp => MeterColors.Ramp(theme, 1f - value),
        MeterColoring.Fixed => FixedColor,
        _ => theme.Accent,
    };

    protected override Vector2 MeasureCore(UiContext ui, Vector2 available) => new(140 * ui.Scale, 10 * ui.Scale);

    protected override void DrawSelf(UiContext ui, UiDrawList list)
    {
        var bounds = Bounds;
        var radius = MathF.Floor(bounds.Height / 2f);
        list.AddPanel(bounds, ui.Theme.Input, ui.Theme.PanelBorder, MathF.Max(1f, ui.Scale), radius);
        var value = Math.Clamp(Value(), 0f, 1f);
        var filled = MathF.Round((bounds.Width - 2f) * value);
        if (filled >= 1f)
        {
            list.AddRect(new RectF(bounds.X + 1, bounds.Y + 1, MathF.Max(filled, MathF.Min(bounds.Height - 2f, bounds.Width - 2f)), bounds.Height - 2f),
                FillColor(ui.Theme, value), MathF.Max(0f, radius - 1f));
        }

        if (Marker is { } marker)
        {
            var x = MathF.Round(bounds.X + 1 + ((bounds.Width - 2f) * Math.Clamp(marker(), 0f, 1f)));
            list.AddRect(new RectF(x, bounds.Y - 2, MathF.Max(1f, ui.Scale), bounds.Height + 4), ui.Theme.Text);
        }
    }
}

/// <summary>
/// A bar split into coloured segments against a total (cargo on board by cargo or destination against capacity).
/// <see cref="Segment"/> is asked for every index below <see cref="SegmentCount"/> every frame; allocates nothing.
/// </summary>
internal sealed class SegmentBar : Widget
{
    public SegmentBar(Func<int> total, Func<int> segmentCount, Func<int, (int Amount, UiColor Color)> segment)
    {
        Total = total ?? throw new ArgumentNullException(nameof(total));
        SegmentCount = segmentCount ?? throw new ArgumentNullException(nameof(segmentCount));
        Segment = segment ?? throw new ArgumentNullException(nameof(segment));
        Width = 220;
        VerticalAlignment = Alignment.Center;
    }

    public Func<int> Total { get; }

    public Func<int> SegmentCount { get; }

    public Func<int, (int Amount, UiColor Color)> Segment { get; }

    protected override Vector2 MeasureCore(UiContext ui, Vector2 available) => new(220 * ui.Scale, 14 * ui.Scale);

    protected override void DrawSelf(UiContext ui, UiDrawList list)
    {
        var bounds = Bounds;
        list.AddPanel(bounds, ui.Theme.Input, ui.Theme.PanelBorder, MathF.Max(1f, ui.Scale), 3f * ui.Scale);
        var total = Math.Max(1, Total());
        var inner = bounds.Width - 2f;
        var x = bounds.X + 1f;
        var used = 0;
        var count = SegmentCount();
        for (var i = 0; i < count; i++)
        {
            var (amount, color) = Segment(i);
            if (amount <= 0)
            {
                continue;
            }

            used += amount;
            var end = bounds.X + 1f + MathF.Round(inner * Math.Min(1f, used / (float)total));
            if (end - x >= 1f)
            {
                list.AddRect(new RectF(x, bounds.Y + 1, end - x, bounds.Height - 2f), color);
                x = end;
            }
        }
    }
}

/// <summary>
/// A cargo's mark: its pixel-art icon from <c>icons.json</c> (<c>cargo.&lt;id&gt;</c>) on a tile tinted with its
/// <c>cargo.json</c> colour, or a plain colour square when the cargo has no icon.
/// </summary>
internal sealed class CargoBadge : Widget
{
    public CargoBadge(string cargoId, UiColor color)
    {
        ArgumentException.ThrowIfNullOrEmpty(cargoId);
        Icon = IconName(cargoId);
        Color = color;
        HorizontalAlignment = Alignment.Center;
        VerticalAlignment = Alignment.Center;
    }

    /// <summary>Takes the cursor only to show its tooltip (the cargo name).</summary>
    public override bool IsHitTestVisible => Tooltip is not null;

    public string Icon { get; }

    public UiColor Color { get; }

    public static string IconName(string cargoId) => "cargo." + cargoId;

    protected override Vector2 MeasureCore(UiContext ui, Vector2 available)
    {
        var size = (MathF.Max(1, MathF.Round(ui.Scale)) * ui.Theme.IconSize) + (4 * ui.Scale);
        return new Vector2(size, size);
    }

    protected override void DrawSelf(UiContext ui, UiDrawList list)
    {
        var bounds = Bounds;
        var radius = 3f * ui.Scale;
        if (!ui.Icons.Contains(Icon))
        {
            list.AddRect(bounds.Deflate(new Thickness(3, 3, 3, 3).Scale(ui.Scale)), Color, radius);
            return;
        }

        list.AddPanel(bounds, Color.WithAlpha(0.30f), Color, MathF.Max(1f, ui.Scale), radius);
        var icon = MathF.Max(1, MathF.Round(ui.Scale)) * ui.Theme.IconSize;
        var x = MathF.Round(bounds.X + ((bounds.Width - icon) / 2f));
        var y = MathF.Round(bounds.Y + ((bounds.Height - icon) / 2f));
        list.AddIcon(ui.Icons.Get(Icon), new RectF(x, y, icon, icon), UiColor.White);
    }
}

/// <summary>
/// A line's stops as numbered dots joined by a track, with the first stop repeated faded at the end (lines are
/// circular), and a mark per vehicle: on a dot while it loads there, between two dots while it drives to the second.
/// Marker positions come from <see cref="Markers"/>, refreshed only when <see cref="Version"/> changes, into a list
/// kept between frames, so steady frames allocate nothing.
/// </summary>
internal sealed class StopStrip : Widget
{
    private static readonly string[] Numbers = CreateNumbers(99);
    private readonly List<float> _markers = [];
    private long _seen = long.MinValue;

    public StopStrip(Func<int> stopCount, Func<long> version, Action<List<float>> markers)
    {
        StopCount = stopCount ?? throw new ArgumentNullException(nameof(stopCount));
        Version = version ?? throw new ArgumentNullException(nameof(version));
        Markers = markers ?? throw new ArgumentNullException(nameof(markers));
    }

    /// <summary>Takes the cursor only to show its tooltip.</summary>
    public override bool IsHitTestVisible => Tooltip is not null;

    public Func<int> StopCount { get; }

    public Func<long> Version { get; }

    /// <summary>Fills the list with one position per vehicle: <c>i</c> at stop i, <c>i − 0.5</c> driving to stop i.</summary>
    public Action<List<float>> Markers { get; }

    /// <summary>The marker positions of the last refresh (tests).</summary>
    public IReadOnlyList<float> MarkerPositions => _markers;

    /// <summary>Re-reads the markers when the version changed.</summary>
    public void Refresh()
    {
        var version = Version();
        if (version == _seen)
        {
            return;
        }

        _seen = version;
        _markers.Clear();
        Markers(_markers);
    }

    /// <summary>Where a vehicle heading to (or at) stop <paramref name="stopIndex"/> is drawn, in stop units.</summary>
    public static float Position(int stopIndex, int stopCount, bool atStop)
    {
        if (stopCount <= 0)
        {
            return 0f;
        }

        stopIndex = ((stopIndex % stopCount) + stopCount) % stopCount;
        return atStop ? stopIndex : stopIndex == 0 ? stopCount - 0.5f : stopIndex - 0.5f;
    }

    protected override Vector2 MeasureCore(UiContext ui, Vector2 available)
    {
        var count = Math.Max(1, StopCount());
        return new Vector2(MathF.Max(240f * ui.Scale, (count + 1) * 34f * ui.Scale), 44f * ui.Scale);
    }

    protected override void DrawSelf(UiContext ui, UiDrawList list)
    {
        Refresh();
        var theme = ui.Theme;
        var bounds = Bounds;
        var count = StopCount();
        list.AddPanel(bounds, theme.Input, theme.PanelBorder, MathF.Max(1f, ui.Scale), 3f * ui.Scale);
        if (count <= 0)
        {
            return;
        }

        var dot = MathF.Round(18f * ui.Scale);
        var left = bounds.X + (14f * ui.Scale) + (dot / 2f);
        var right = bounds.Right - (14f * ui.Scale) - (dot / 2f);
        var step = (right - left) / count;
        var middle = MathF.Round(bounds.Y + (bounds.Height * 0.62f));
        var track = MathF.Max(2f, MathF.Round(3f * ui.Scale));
        list.AddRect(new RectF(left, middle - MathF.Floor(track / 2f), right - left, track), theme.TextMuted.WithAlpha(0.6f));

        var size = ui.FontSize(TextSize.Small);
        var lineHeight = TextLayout.LineHeight(ui.Font, size);
        for (var i = 0; i <= count; i++)
        {
            var center = MathF.Round(left + (i * step));
            var rect = new RectF(center - (dot / 2f), middle - (dot / 2f), dot, dot);
            var last = i == count;
            list.AddPanel(rect, last ? theme.Button.WithAlpha(0.6f) : theme.Button, last ? theme.TextMuted.WithAlpha(0.6f) : theme.Accent, MathF.Max(1f, ui.Scale), dot / 2f);
            var number = Numbers[Math.Min(i == count ? 0 : i, Numbers.Length - 1)];
            var width = TextLayout.Measure(ui.Font, number, size);
            list.AddText(ui.Font, number, new Vector2(MathF.Round(center - (width / 2f)), MathF.Round(middle - (lineHeight / 2f))), size,
                last ? theme.TextMuted : theme.Text);
        }

        var mark = MathF.Round(8f * ui.Scale);
        var top = MathF.Round(bounds.Y + (4f * ui.Scale));
        foreach (var position in _markers)
        {
            var x = MathF.Round(left + (Math.Clamp(position, 0f, count) * step) - (mark / 2f));
            list.AddRect(new RectF(x, top, mark, mark), theme.Accent, 2f * ui.Scale);
            list.AddRect(new RectF(x + MathF.Floor(mark / 2f) - MathF.Floor(ui.Scale / 2f), top + mark, MathF.Max(1f, ui.Scale), middle - (dot / 2f) - top - mark), theme.Accent.WithAlpha(0.7f));
        }
    }

    private static string[] CreateNumbers(int max)
    {
        var numbers = new string[max + 1];
        for (var i = 0; i <= max; i++)
        {
            numbers[i] = (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return numbers;
    }
}
