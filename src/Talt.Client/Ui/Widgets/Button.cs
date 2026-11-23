using System.Numerics;
using Silk.NET.Input;
using Talt.Client.Ui.Font;

namespace Talt.Client.Ui.Widgets;

/// <summary>Look of a <see cref="Button"/>.</summary>
internal enum ButtonStyle
{
    /// <summary>Themed rounded button.</summary>
    Normal,

    /// <summary>Large main-menu button.</summary>
    Menu,

    /// <summary>No background until hovered (title bar and toolbar icons).</summary>
    Flat,
}

/// <summary>A clickable button with an optional icon and an optional localized label.</summary>
internal class Button : Widget
{
    public Button(TextRef? text, Action? onClick = null, string? icon = null)
    {
        Text = text;
        OnClick = onClick;
        Icon = icon;
    }

    public TextRef? Text { get; set; }

    public string? Icon { get; set; }

    public Action? OnClick { get; set; }

    public ButtonStyle Style { get; set; } = ButtonStyle.Normal;

    /// <summary>Shown as selected (for example the current speed).</summary>
    public Func<bool>? IsSelected { get; set; }

    public TextSize TextSize { get; set; } = TextSize.Body;

    public override bool IsHitTestVisible => true;

    /// <summary>Activates the button as if clicked (ignored when disabled).</summary>
    public void Click()
    {
        if (IsEnabledInTree)
        {
            OnClick?.Invoke();
        }
    }

    public override bool OnMouseDown(UiContext ui, MouseButton button, Vector2 position) =>
        button == MouseButton.Left;

    public override void OnMouseUp(UiContext ui, MouseButton button, Vector2 position, bool inside)
    {
        if (button == MouseButton.Left && inside)
        {
            if (IsEnabledInTree)
            {
                ui.Activated?.Invoke();
            }

            Click();
        }
    }

    protected override Vector2 MeasureCore(UiContext ui, Vector2 available)
    {
        var scale = ui.Scale;
        var padding = (Style == ButtonStyle.Menu ? 16f : 8f) * scale;
        var height = (Style == ButtonStyle.Menu ? ui.Theme.ButtonHeight * 1.5f : ui.Theme.ButtonHeight) * scale;
        var width = 0f;
        if (Icon is not null)
        {
            width += IconSize(ui);
        }

        if (Text is not null)
        {
            var text = Text.Resolve(ui);
            width += TextLayout.Measure(ui.Font, text, ui.FontSize(TextSize)) + (Icon is not null ? 6 * scale : 0);
            width += 2 * padding;
        }
        else
        {
            width = MathF.Max(width + 2 * 6 * scale, height);
        }

        return new Vector2(MathF.Ceiling(width), MathF.Ceiling(height));
    }

    protected override void DrawSelf(UiContext ui, UiDrawList list)
    {
        var theme = ui.Theme;
        var enabled = IsEnabledInTree;
        var hovered = enabled && ui.Hovered == this;
        var pressed = enabled && ui.IsPressed(this);
        var selected = IsSelected?.Invoke() ?? false;
        var fill = !enabled ? theme.ButtonDisabled
            : pressed ? theme.ButtonPressed
            : selected ? theme.ButtonSelected
            : hovered ? theme.ButtonHover
            : theme.Button;
        if (Style != ButtonStyle.Flat || hovered || pressed || selected)
        {
            list.AddPanel(Bounds, fill, Style == ButtonStyle.Flat ? UiColor.Transparent : theme.PanelBorder,
                MathF.Max(1, theme.BorderWidth * ui.Scale), theme.CornerRadius * ui.Scale);
        }

        var color = enabled ? theme.Text : theme.TextMuted;
        var iconSize = IconSize(ui);
        var textSize = ui.FontSize(TextSize);
        var text = Text?.Resolve(ui);
        var contentWidth = (Icon is not null ? iconSize : 0)
            + (text is not null ? TextLayout.Measure(ui.Font, text, textSize) + (Icon is not null ? 6 * ui.Scale : 0) : 0);
        var x = Bounds.X + MathF.Round(MathF.Max(0, (Bounds.Width - contentWidth) / 2f));
        if (Icon is not null)
        {
            var y = Bounds.Y + MathF.Round((Bounds.Height - iconSize) / 2f);
            list.AddIcon(ui.Icons.Get(Icon), new RectF(x, y, iconSize, iconSize), enabled ? UiColor.White : UiColor.White.WithAlpha(0.45f));
            x += iconSize + 6 * ui.Scale;
        }

        if (text is not null)
        {
            var lineHeight = TextLayout.LineHeight(ui.Font, textSize);
            var y = Bounds.Y + MathF.Round((Bounds.Height - lineHeight) / 2f);
            list.AddTextClipped(ui.Font, text, new Vector2(x, y), textSize, color, Bounds.Right - x - 4 * ui.Scale);
        }
    }

    /// <summary>Icons are drawn at whole multiples of their pixel size so pixel art stays crisp.</summary>
    private static float IconSize(UiContext ui) => MathF.Max(1, MathF.Round(ui.Scale)) * ui.Theme.IconSize;
}
