using System.Numerics;
using MouseButton = Silk.NET.Input.MouseButton;
using Talt.Client.Ui.Widgets;

namespace Talt.Client.Ui.Windows;

/// <summary>
/// A movable window with a title bar, pin and close buttons and content (data-model.md UiWindow).
/// Managed by <see cref="WindowManager"/>.
/// </summary>
internal sealed class UiWindow : Panel
{
    private readonly StackPanel _titleBar;
    private readonly Button _pinButton;
    private readonly Button _closeButton;
    private Widget? _content;
    private bool _dragging;

    public UiWindow(string kind, TextRef title)
    {
        ArgumentException.ThrowIfNullOrEmpty(kind);
        Kind = kind;
        Title = title;
        Style = PanelStyle.Panel;
        Padding = new Thickness(0);
        HorizontalAlignment = Alignment.Start;
        VerticalAlignment = Alignment.Start;
        MinWidth = 220;

        var layout = Add(new StackPanel(Orientation.Vertical) { Spacing = 0 });
        _titleBar = layout.Add(new StackPanel(Orientation.Horizontal) { Spacing = 2, Padding = new Thickness(8, 0, 4, 0) });
        _titleBar.Add(new Label(title, TextSize.Title) { Fill = true });
        _pinButton = _titleBar.Add(new Button(null, TogglePin, "pin")
        {
            Style = ButtonStyle.Flat,
            Tooltip = TextRef.Of("window.pin.tooltip"),
            VerticalAlignment = Alignment.Center,
        });
        _closeButton = _titleBar.Add(new Button(null, () => Manager?.Close(this), "close")
        {
            Style = ButtonStyle.Flat,
            Tooltip = TextRef.Of("window.close.tooltip"),
            ShortcutAction = "closeWindow",
            VerticalAlignment = Alignment.Center,
        });
        ContentHost = layout.Add(new Panel { Style = PanelStyle.None });
    }

    /// <summary>Kind of window, for cascading and single-instance rules (data-model.md UiWindow.Kind).</summary>
    public string Kind { get; }

    /// <summary>Identity of the object shown, for "one window per object" (data-model.md UiWindow.Key).</summary>
    public string? Key { get; init; }

    /// <summary>Reopening a single-instance kind focuses the open one (data-model.md UiWindow.SingleInstance).</summary>
    public bool SingleInstance { get; init; }

    public TextRef Title { get; }

    /// <summary>Top-left corner in framebuffer pixels; NaN until the manager places it (data-model.md UiWindow.Position).</summary>
    public Vector2 Position { get; set; } = new(float.NaN, float.NaN);

    /// <summary>Pinned windows survive "close all windows" (data-model.md UiWindow.Pinned).</summary>
    public bool Pinned
    {
        get;
        set
        {
            field = value;
            _pinButton.Icon = value ? "pinned" : "pin";
        }
    }

    /// <summary>Whether the close button is shown (data-model.md UiWindow.Closable).</summary>
    public bool Closable
    {
        get => _closeButton.Visible;
        init => _closeButton.Visible = value;
    }

    public bool Pinnable
    {
        get => _pinButton.Visible;
        init => _pinButton.Visible = value;
    }

    /// <summary>Called once after the window is closed.</summary>
    public event Action<UiWindow>? Closed;

    internal WindowManager? Manager { get; set; }

    /// <summary>Holds the content (with the theme padding).</summary>
    public Panel ContentHost { get; }

    public Widget? Content
    {
        get => _content;
        set
        {
            if (_content is not null)
            {
                ContentHost.Remove(_content);
            }

            _content = value;
            if (value is not null)
            {
                ContentHost.Add(value);
            }
        }
    }

    public static float TitleBarHeight(UiContext ui) => ui.Theme.ButtonHeight * ui.Scale;

    internal void RaiseClosed() => Closed?.Invoke(this);

    public override bool OnMouseDown(UiContext ui, MouseButton button, Vector2 position)
    {
        if (button == MouseButton.Left && position.Y < Bounds.Y + TitleBarHeight(ui))
        {
            _dragging = true; // FR-013: dragged by the title bar.
        }

        return true; // Clicks on the window body never reach the world.
    }

    public override void OnDrag(UiContext ui, Vector2 position, Vector2 delta)
    {
        if (_dragging)
        {
            Position += delta;
            WindowManager.Clamp(ui, this);
        }
    }

    public override void OnMouseUp(UiContext ui, MouseButton button, Vector2 position, bool inside) => _dragging = false;

    protected override Vector2 MeasureCore(UiContext ui, Vector2 available)
    {
        _titleBar.Height = ui.Theme.ButtonHeight;
        ContentHost.Padding = new Thickness(ui.Theme.Padding);
        return base.MeasureCore(ui, available);
    }

    protected override void DrawSelf(UiContext ui, UiDrawList list)
    {
        base.DrawSelf(ui, list);
        var theme = ui.Theme;
        var border = MathF.Max(1, theme.BorderWidth * ui.Scale);
        var radius = theme.CornerRadius * ui.Scale;
        var bar = new RectF(Bounds.X + border, Bounds.Y + border, Bounds.Width - 2 * border, TitleBarHeight(ui) - border);
        var front = Manager?.Front == this;
        list.AddRect(bar, front ? theme.TitleBar : theme.TitleBar.WithAlpha(0.7f), radius - border);
        // Square off the bottom corners of the title bar.
        list.AddRect(new RectF(bar.X, bar.Bottom - radius, bar.Width, radius), front ? theme.TitleBar : theme.TitleBar.WithAlpha(0.7f));
    }

    private void TogglePin() => Pinned = !Pinned;
}
