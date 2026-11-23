using System.Numerics;
using Silk.NET.Input;
using Talt.Client.Localization;
using Talt.Client.Ui.Font;
using Talt.Client.Ui.Widgets;
using Talt.Client.Ui.Windows;

namespace Talt.Client.Ui;

/// <summary>
/// Root of the interface: theme, font, strings, scale and the layers drawn from back to front —
/// screen, overlay (pause menu), windows, popup (open drop-down) and tooltip. Owns hover, mouse
/// capture, keyboard focus and the tooltip timer. GPU-free: it produces a <see cref="UiDrawList"/>.
/// </summary>
internal sealed class UiContext
{
    /// <summary>Allowed interface scales (FR-024).</summary>
    public static readonly float[] Scales = [0.75f, 1f, 1.25f, 1.5f, 1.75f, 2f];

    /// <summary>Widest tooltip, in logical pixels.</summary>
    private const float TooltipMaxWidth = 320f;

    private readonly Panel _tooltip;
    private readonly Label _tooltipText;
    private readonly Label _tooltipShortcut;
    private Widget? _tooltipTarget;
    private double _tooltipSince;
    private bool _tooltipVisible;
    private Widget? _hovered;
    private Widget? _focused;
    private Vector2 _lastMouse;

    public UiContext(Theme theme, FontAtlas font, Icons icons, UiAtlas atlas, Strings strings)
    {
        Theme = theme ?? throw new ArgumentNullException(nameof(theme));
        Font = font ?? throw new ArgumentNullException(nameof(font));
        Icons = icons ?? throw new ArgumentNullException(nameof(icons));
        Atlas = atlas ?? throw new ArgumentNullException(nameof(atlas));
        Strings = strings ?? throw new ArgumentNullException(nameof(strings));

        _tooltip = new Panel { Style = PanelStyle.Tooltip, HorizontalAlignment = Alignment.Start, VerticalAlignment = Alignment.Start };
        var stack = _tooltip.Add(new StackPanel(Orientation.Vertical) { Spacing = 2 });
        _tooltipText = stack.Add(new Label(TextRef.Of("app.title"), TextSize.Small) { Wrap = true });
        _tooltipShortcut = stack.Add(new Label(TextRef.Of("app.title"), TextSize.Small) { Color = TextColor.Accent });
    }

    public Theme Theme { get; }

    public FontAtlas Font { get; }

    public Icons Icons { get; }

    public UiAtlas Atlas { get; }

    public Strings Strings { get; private set; }

    /// <summary>Incremented on every language change so texts re-resolve (FR-022).</summary>
    public int LanguageVersion { get; private set; }

    /// <summary>Interface scale: logical pixel → framebuffer pixels (FR-024).</summary>
    public float Scale { get; set; } = 1f;

    /// <summary>Framebuffer size in pixels.</summary>
    public Vector2 ViewportSize { get; set; }

    /// <summary>Seconds since the interface started (for the tooltip delay and fading messages).</summary>
    public double Time { get; private set; }

    /// <summary>Full-screen content of the current screen (menus, HUD).</summary>
    public Widget? Screen { get; set; }

    /// <summary>Full-screen layer above the screen and below windows (pause menu).</summary>
    public Widget? Overlay { get; set; }

    public WindowManager Windows { get; } = new();

    /// <summary>Temporary layer above everything (open drop-down list); closed by any click outside it.</summary>
    public Widget? Popup { get; private set; }

    /// <summary>Returns the key name bound to an action, for tooltips; set by the client.</summary>
    public Func<string, string?>? ShortcutText { get; set; }

    public Vector2 MousePosition => _lastMouse;

    public Widget? Hovered => _hovered;

    /// <summary>Widget that received the last mouse-down and gets the drag and the mouse-up.</summary>
    public Widget? Captured { get; private set; }

    public MouseButton CapturedButton { get; private set; }

    /// <summary>Widget receiving keys and characters (text input, key capture).</summary>
    public Widget? Focused => _focused;

    /// <summary>Whether a widget has keyboard focus, so shortcuts must not fire (FR-018).</summary>
    public bool HasKeyboardFocus => _focused is not null;

    public bool IsTooltipVisible => _tooltipVisible;

    public Widget? TooltipTarget => _tooltipVisible ? _tooltipTarget : null;

    /// <summary>The tooltip panel (for tests: its bounds after layout).</summary>
    internal Panel TooltipPanel => _tooltip;

    /// <summary>Changes the language; every bound text updates in the next frame.</summary>
    public void SetStrings(Strings strings)
    {
        Strings = strings ?? throw new ArgumentNullException(nameof(strings));
        LanguageVersion++;
    }

    public float FontSize(TextSize size) => Scale * size switch
    {
        TextSize.Small => Theme.FontSizes.Small,
        TextSize.Title => Theme.FontSizes.Title,
        TextSize.Heading => Theme.FontSizes.Heading,
        _ => Theme.FontSizes.Body,
    };

    public UiColor TextColorOf(TextColor color, bool enabled) =>
        !enabled ? Theme.TextMuted : color switch
        {
            TextColor.Muted => Theme.TextMuted,
            TextColor.Accent => Theme.Accent,
            _ => Theme.Text,
        };

    /// <summary>Whether <paramref name="widget"/> is being pressed (captured with the cursor inside).</summary>
    public bool IsPressed(Widget widget) =>
        Captured == widget && widget.Bounds.Contains(_lastMouse);

    /// <summary>Whether the cursor is over any interface element.</summary>
    public bool IsMouseOverUi => HitTest(_lastMouse) is not null;

    public void OpenPopup(Widget popup) => Popup = popup ?? throw new ArgumentNullException(nameof(popup));

    public void ClosePopup() => Popup = null;

    public void SetFocus(Widget? widget)
    {
        if (_focused == widget)
        {
            return;
        }

        var previous = _focused;
        _focused = widget;
        previous?.OnFocusChanged(this, false);
        widget?.OnFocusChanged(this, true);
    }

    /// <summary>Front-most interface element under a point, or null when the point is over the world.</summary>
    public Widget? HitTest(Vector2 point)
    {
        if (Popup?.HitTest(point) is { } popup)
        {
            return popup;
        }

        return Windows.HitTest(point) ?? Overlay?.HitTest(point) ?? Screen?.HitTest(point);
    }

    /// <summary>Advances timers (tooltip delay).</summary>
    public void Update(double deltaSeconds)
    {
        Time += deltaSeconds;
        var target = _hovered;
        while (target is not null && target.Tooltip is null)
        {
            target = target.Parent;
        }

        if (target != _tooltipTarget)
        {
            _tooltipTarget = target;
            _tooltipSince = Time;
            _tooltipVisible = false;
            if (target is not null)
            {
                _tooltipText.Text = target.Tooltip!;
                var key = target.ShortcutAction is { } action ? ShortcutText?.Invoke(action) : null;
                _tooltipShortcut.Visible = key is not null;
                if (key is not null)
                {
                    _tooltipShortcut.Text = TextRef.Of("tooltip.shortcut", ("key", key));
                }
            }
        }

        _tooltipVisible = target is not null && Captured is null && Time - _tooltipSince >= Theme.TooltipDelaySeconds;
    }

    /// <summary>Lays out every layer for the current viewport and scale.</summary>
    public void Layout()
    {
        if (ViewportSize.X < 1 || ViewportSize.Y < 1)
        {
            return; // Minimized: nothing to lay out (edge case).
        }

        var screen = new RectF(0, 0, ViewportSize.X, ViewportSize.Y);
        LayoutFull(Screen, screen);
        LayoutFull(Overlay, screen);
        Windows.Layout(this);
        if (Popup is not null)
        {
            Popup.Measure(this, ViewportSize);
            Popup.Arrange(this, screen);
        }

        if (_tooltipVisible)
        {
            _tooltipText.Width = null;
            var size = _tooltip.Measure(this, new Vector2(TooltipMaxWidth * Scale, ViewportSize.Y));
            var offset = 18 * Scale;
            var x = Math.Clamp(_lastMouse.X + offset * 0.5f, 0, MathF.Max(0, ViewportSize.X - size.X));
            var y = _lastMouse.Y + offset;
            if (y + size.Y > ViewportSize.Y)
            {
                y = MathF.Max(0, _lastMouse.Y - size.Y - 4 * Scale); // Above the cursor near the bottom.
            }

            _tooltip.Arrange(this, new RectF(x, y, size.X, size.Y));
        }
    }

    /// <summary>Builds the frame's geometry. Call after <see cref="Layout"/>.</summary>
    public void Draw(UiDrawList list)
    {
        ArgumentNullException.ThrowIfNull(list);
        list.Begin(ViewportSize.X, ViewportSize.Y);
        if (ViewportSize.X >= 1 && ViewportSize.Y >= 1)
        {
            Screen?.Draw(this, list);
            Overlay?.Draw(this, list);
            Windows.Draw(this, list);
            Popup?.Draw(this, list);
            if (_tooltipVisible)
            {
                _tooltip.Draw(this, list);
            }
        }

        list.End();
    }

    /// <summary>Mouse moved; returns whether the interface is under the cursor or dragging.</summary>
    public bool MouseMove(Vector2 position)
    {
        var delta = position - _lastMouse;
        _lastMouse = position;
        Captured?.OnDrag(this, position, delta);
        _hovered = HitTest(position);
        return Captured is not null || _hovered is not null;
    }

    /// <summary>Mouse button pressed; returns whether the interface took it (FR-018).</summary>
    public bool MouseDown(MouseButton button, Vector2 position)
    {
        _lastMouse = position;
        if (Popup is not null && Popup.HitTest(position) is null)
        {
            ClosePopup(); // A click outside an open drop-down closes it; the click is consumed.
            return true;
        }

        var hit = HitTest(position);
        _hovered = hit;
        if (hit is null)
        {
            SetFocus(null);
            return false;
        }

        if (Windows.WindowOf(hit) is { } window)
        {
            Windows.BringToFront(window); // FR-013: click brings to front.
        }

        Widget? handler = null;
        for (var w = hit; w is not null; w = w.Parent)
        {
            if (w.IsEnabledInTree && w.OnMouseDown(this, button, position))
            {
                handler = w;
                break;
            }
        }

        SetFocus(handler is { IsFocusable: true } ? handler : null);
        Captured = handler ?? hit;
        CapturedButton = button;
        return true;
    }

    /// <summary>Mouse button released; returns whether the interface had the press.</summary>
    public bool MouseUp(MouseButton button, Vector2 position)
    {
        _lastMouse = position;
        if (Captured is null || button != CapturedButton)
        {
            return false;
        }

        var captured = Captured;
        Captured = null;
        var inside = captured.Bounds.Contains(position) && captured.Contains(HitTest(position));
        captured.OnMouseUp(this, button, position, inside);
        _hovered = HitTest(position);
        return true;
    }

    /// <summary>Mouse wheel; returns whether the cursor is over the interface.</summary>
    public bool Scroll(float delta)
    {
        var hit = HitTest(_lastMouse);
        for (var w = hit; w is not null; w = w.Parent)
        {
            if (w.OnScroll(this, delta))
            {
                break;
            }
        }

        return hit is not null;
    }

    /// <summary>Key pressed; returns whether a focused widget took it (shortcuts must not fire).</summary>
    public bool KeyDown(Key key, bool shift)
    {
        if (_focused is null)
        {
            return false;
        }

        if (!_focused.OnKeyDown(this, key, shift) && key == Key.Escape)
        {
            SetFocus(null);
        }

        return true;
    }

    public bool Char(char character) => _focused is not null && _focused.OnChar(this, character);

    /// <summary>Closes popups and clears hover, focus and capture (for example when changing screens).</summary>
    public void ResetInteraction()
    {
        ClosePopup();
        SetFocus(null);
        Captured = null;
        _hovered = null;
        _tooltipTarget = null;
        _tooltipVisible = false;
    }

    private void LayoutFull(Widget? widget, RectF area)
    {
        if (widget is null)
        {
            return;
        }

        widget.Measure(this, area.Size);
        widget.Arrange(this, area);
    }
}
