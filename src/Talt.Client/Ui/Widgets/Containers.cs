using System.Numerics;

namespace Talt.Client.Ui.Widgets;

/// <summary>Background style of a <see cref="Panel"/>.</summary>
internal enum PanelStyle
{
    None,
    Panel,
    Tooltip,
    Overlay,
    Input,

    /// <summary>Full-screen theme background (menus, where no world is drawn).</summary>
    Background,
}

/// <summary>A rounded, themed background with padding around its children (which overlap).</summary>
internal class Panel : Widget
{
    public PanelStyle Style { get; set; } = PanelStyle.Panel;

    /// <summary>Padding in logical pixels; null uses the theme padding.</summary>
    public Thickness? Padding { get; set; }

    /// <summary>Panels block clicks to what is behind them, except transparent ones.</summary>
    public override bool IsHitTestVisible => Style != PanelStyle.None;

    protected Thickness ScaledPadding(UiContext ui) =>
        (Padding ?? new Thickness(Style == PanelStyle.None ? 0 : ui.Theme.Padding)).Scale(ui.Scale);

    protected override Vector2 MeasureCore(UiContext ui, Vector2 available)
    {
        var padding = ScaledPadding(ui);
        var inner = new Vector2(MathF.Max(0, available.X - padding.Horizontal), MathF.Max(0, available.Y - padding.Vertical));
        return base.MeasureCore(ui, inner) + new Vector2(padding.Horizontal, padding.Vertical);
    }

    protected override void ArrangeCore(UiContext ui, RectF area) => base.ArrangeCore(ui, area.Deflate(ScaledPadding(ui)));

    protected override void DrawSelf(UiContext ui, UiDrawList list)
    {
        var theme = ui.Theme;
        var radius = theme.CornerRadius * ui.Scale;
        switch (Style)
        {
            case PanelStyle.Panel:
                list.AddPanel(Bounds, theme.Panel, theme.PanelBorder, MathF.Max(1, theme.BorderWidth * ui.Scale), radius);
                break;
            case PanelStyle.Tooltip:
                list.AddPanel(Bounds, theme.Tooltip, theme.PanelBorder, MathF.Max(1, theme.BorderWidth * ui.Scale), radius);
                break;
            case PanelStyle.Input:
                list.AddPanel(Bounds, theme.Input, theme.PanelBorder, MathF.Max(1, theme.BorderWidth * ui.Scale), radius);
                break;
            case PanelStyle.Overlay:
                list.AddRect(Bounds, theme.Overlay);
                break;
            case PanelStyle.Background:
                // Drawn by the interface rather than as the GPU clear colour: some drivers store clear
                // colours without the sRGB conversion, which made the menu background almost black.
                list.AddRect(Bounds, theme.Background);
                break;
        }
    }
}

/// <summary>Lays out its children one after another, vertically or horizontally.</summary>
internal class StackPanel : Panel
{
    public StackPanel(Orientation orientation = Orientation.Vertical)
    {
        Orientation = orientation;
        Style = PanelStyle.None;
    }

    public Orientation Orientation { get; set; }

    /// <summary>Space between children in logical pixels; null uses the theme spacing.</summary>
    public float? Spacing { get; set; }

    private float ScaledSpacing(UiContext ui) => (Spacing ?? ui.Theme.Spacing) * ui.Scale;

    protected override Vector2 MeasureCore(UiContext ui, Vector2 available)
    {
        var padding = ScaledPadding(ui);
        var inner = new Vector2(MathF.Max(0, available.X - padding.Horizontal), MathF.Max(0, available.Y - padding.Vertical));
        var spacing = ScaledSpacing(ui);
        var vertical = Orientation == Orientation.Vertical;
        float along = 0, across = 0;
        var visible = 0;
        for (var i = 0; i < ChildCount; i++)
        {
            var child = GetChild(i);
            if (!child.Visible)
            {
                continue;
            }

            var remaining = vertical ? new Vector2(inner.X, MathF.Max(0, inner.Y - along)) : new Vector2(MathF.Max(0, inner.X - along), inner.Y);
            var size = child.Measure(ui, remaining);
            along += (vertical ? size.Y : size.X) + (visible > 0 ? spacing : 0);
            across = MathF.Max(across, vertical ? size.X : size.Y);
            visible++;
        }

        var content = vertical ? new Vector2(across, along) : new Vector2(along, across);
        return content + new Vector2(padding.Horizontal, padding.Vertical);
    }

    protected override void ArrangeCore(UiContext ui, RectF area)
    {
        area = area.Deflate(ScaledPadding(ui));
        var spacing = ScaledSpacing(ui);
        var vertical = Orientation == Orientation.Vertical;

        // Space left for children marked Fill, shared equally.
        float fixedLength = 0;
        int fills = 0, visible = 0;
        for (var i = 0; i < ChildCount; i++)
        {
            var child = GetChild(i);
            if (!child.Visible)
            {
                continue;
            }

            visible++;
            if (child.Fill)
            {
                fills++;
            }
            else
            {
                fixedLength += vertical ? child.DesiredSize.Y : child.DesiredSize.X;
            }
        }

        var total = vertical ? area.Height : area.Width;
        var fillLength = fills > 0 ? MathF.Max(0, (total - fixedLength - spacing * Math.Max(0, visible - 1)) / fills) : 0;
        var position = vertical ? area.Y : area.X;
        for (var i = 0; i < ChildCount; i++)
        {
            var child = GetChild(i);
            if (!child.Visible)
            {
                continue;
            }

            var length = child.Fill ? fillLength : vertical ? child.DesiredSize.Y : child.DesiredSize.X;
            child.Arrange(ui, vertical
                ? new RectF(area.X, position, area.Width, length)
                : new RectF(position, area.Y, length, area.Height));
            position += length + spacing;
        }
    }
}

/// <summary>Empty space; with <see cref="Widget.Fill"/> it pushes the following children to the end.</summary>
internal sealed class Spacer : Widget
{
    protected override Vector2 MeasureCore(UiContext ui, Vector2 available) => Vector2.Zero;
}
