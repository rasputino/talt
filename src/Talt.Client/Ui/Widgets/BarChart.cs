using System.Numerics;
using MouseButton = Silk.NET.Input.MouseButton;

namespace Talt.Client.Ui.Widgets;

/// <summary>
/// A simple two-sided bar chart (feature 011, finance window): one slot per period, oldest left; a positive bar
/// above the middle line (income) and a negative bar below it (expenses), both scaled to the largest value
/// shown. Values are read every frame from <see cref="Values"/>; draws only rectangles, allocates nothing.
/// </summary>
internal sealed class BarChart : Widget
{
    public BarChart(int slots, Func<int, (long Positive, long Negative)> values)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(slots, 1);
        Slots = slots;
        Values = values ?? throw new ArgumentNullException(nameof(values));
        Height = 110;
    }

    /// <summary>Number of periods shown.</summary>
    public int Slots { get; }

    /// <summary>Slot index (0 = oldest) → (income ≥ 0, expenses ≤ 0).</summary>
    public Func<int, (long Positive, long Negative)> Values { get; }

    /// <summary>Optional period selection, used by historical charts to inspect an exact figure.</summary>
    public Action<int>? SelectSlot { get; init; }

    public override bool IsHitTestVisible => SelectSlot is not null;

    public override bool OnMouseDown(UiContext ui, MouseButton button, Vector2 position) => SelectSlot is not null && button == MouseButton.Left;

    public override void OnMouseUp(UiContext ui, MouseButton button, Vector2 position, bool inside)
    {
        if (inside && button == MouseButton.Left && Bounds.Width > 4)
        {
            SelectSlot?.Invoke(Math.Clamp((int)((position.X - Bounds.X - 2) * Slots / (Bounds.Width - 4)), 0, Slots - 1));
        }
    }

    /// <summary>The largest absolute value of the slots (the chart's scale), for tests.</summary>
    public long Scale()
    {
        long max = 1;
        for (var i = 0; i < Slots; i++)
        {
            var (positive, negative) = Values(i);
            max = Math.Max(max, Math.Max(positive, -negative));
        }

        return max;
    }

    protected override Vector2 MeasureCore(UiContext ui, Vector2 available) => new(MathF.Max(available.X, Slots * 8 * ui.Scale), 110 * ui.Scale);

    protected override void DrawSelf(UiContext ui, UiDrawList list)
    {
        var theme = ui.Theme;
        var bounds = Bounds;
        list.AddPanel(bounds, theme.Input, theme.PanelBorder, MathF.Max(1f, ui.Scale), 3f * ui.Scale);
        var middle = MathF.Round(bounds.Y + (bounds.Height / 2f));
        var half = (bounds.Height / 2f) - (4f * ui.Scale);
        list.AddRect(new RectF(bounds.X + 2, middle, bounds.Width - 4, MathF.Max(1f, ui.Scale)), theme.TextMuted.WithAlpha(0.5f));
        var scale = (float)Scale();
        var slot = (bounds.Width - 4f) / Slots;
        var width = MathF.Max(1f, MathF.Floor(slot * 0.7f));
        for (var i = 0; i < Slots; i++)
        {
            var (positive, negative) = Values(i);
            var x = MathF.Round(bounds.X + 2f + (i * slot) + ((slot - width) / 2f));
            if (positive > 0)
            {
                var h = MathF.Max(1f, MathF.Round(half * (positive / scale)));
                list.AddRect(new RectF(x, middle - h, width, h), theme.Positive);
            }

            if (negative < 0)
            {
                var h = MathF.Max(1f, MathF.Round(half * (-negative / scale)));
                list.AddRect(new RectF(x, middle + 1, width, h), theme.Negative);
            }
        }
    }
}
