using System.Numerics;
using Talt.Client.Ui.Font;
using Key = Silk.NET.Input.Key;
using MouseButton = Silk.NET.Input.MouseButton;

namespace Talt.Client.Ui.Widgets;

/// <summary>A pixel-art icon from the atlas.</summary>
internal sealed class IconView : Widget
{
    public IconView(string icon)
    {
        Icon = icon;
        HorizontalAlignment = Alignment.Center;
        VerticalAlignment = Alignment.Center;
    }

    public string Icon { get; set; }

    protected override Vector2 MeasureCore(UiContext ui, Vector2 available)
    {
        var size = MathF.Max(1, MathF.Round(ui.Scale)) * ui.Theme.IconSize;
        return new Vector2(size, size);
    }

    protected override void DrawSelf(UiContext ui, UiDrawList list) =>
        list.AddIcon(ui.Icons.Get(Icon), Bounds, IsEnabledInTree ? UiColor.White : UiColor.White.WithAlpha(0.45f));
}

/// <summary>A check box with a localized label.</summary>
internal sealed class Checkbox : Widget
{
    public Checkbox(TextRef text, Func<bool> isChecked, Action<bool> onChanged)
    {
        Text = text;
        IsChecked = isChecked;
        OnChanged = onChanged;
    }

    public TextRef Text { get; }

    public Func<bool> IsChecked { get; }

    public Action<bool> OnChanged { get; }

    public override bool IsHitTestVisible => true;

    public override bool OnMouseDown(UiContext ui, MouseButton button, Vector2 position) => button == MouseButton.Left;

    public override void OnMouseUp(UiContext ui, MouseButton button, Vector2 position, bool inside)
    {
        if (inside && IsEnabledInTree)
        {
            OnChanged(!IsChecked());
        }
    }

    protected override Vector2 MeasureCore(UiContext ui, Vector2 available)
    {
        var box = BoxSize(ui);
        var text = TextLayout.Measure(ui.Font, Text.Resolve(ui), ui.FontSize(TextSize.Body));
        return new Vector2(box + 8 * ui.Scale + text, MathF.Max(box, TextLayout.LineHeight(ui.Font, ui.FontSize(TextSize.Body))));
    }

    protected override void DrawSelf(UiContext ui, UiDrawList list)
    {
        var theme = ui.Theme;
        var box = BoxSize(ui);
        var rect = new RectF(Bounds.X, Bounds.Y + MathF.Round((Bounds.Height - box) / 2), box, box);
        var hovered = ui.Hovered == this && IsEnabledInTree;
        list.AddPanel(rect, hovered ? theme.ButtonHover : theme.Input, theme.PanelBorder, MathF.Max(1, ui.Scale), 3 * ui.Scale);
        if (IsChecked())
        {
            var icon = MathF.Max(1, MathF.Round(ui.Scale)) * theme.IconSize;
            list.AddIcon(ui.Icons.Get("check"), new RectF(rect.Center.X - icon / 2, rect.Center.Y - icon / 2, icon, icon), UiColor.White);
        }

        var size = ui.FontSize(TextSize.Body);
        var y = Bounds.Y + MathF.Round((Bounds.Height - TextLayout.LineHeight(ui.Font, size)) / 2);
        list.AddTextClipped(ui.Font, Text.Resolve(ui), new Vector2(rect.Right + 8 * ui.Scale, y), size,
            ui.TextColorOf(TextColor.Normal, IsEnabledInTree), Bounds.Right - rect.Right - 8 * ui.Scale);
    }

    private static float BoxSize(UiContext ui) => MathF.Round(20 * ui.Scale);
}

/// <summary>One choice of a <see cref="Dropdown"/>.</summary>
internal sealed record DropdownChoice(TextRef Text, object Value);

/// <summary>A button showing the selected choice; clicking opens the list of choices in a popup.</summary>
internal sealed class Dropdown : Widget
{
    /// <summary>Tallest list, in logical pixels, before it scrolls.</summary>
    private const float MaxListHeight = 320f;

    public Dropdown(IReadOnlyList<DropdownChoice> choices, Func<int> selectedIndex, Action<int> onSelected)
    {
        Choices = choices;
        SelectedIndex = selectedIndex;
        OnSelected = onSelected;
        MinWidth = 160;
    }

    public IReadOnlyList<DropdownChoice> Choices { get; }

    public Func<int> SelectedIndex { get; }

    public Action<int> OnSelected { get; }

    public override bool IsHitTestVisible => true;

    public override bool OnMouseDown(UiContext ui, MouseButton button, Vector2 position) => button == MouseButton.Left;

    public override void OnMouseUp(UiContext ui, MouseButton button, Vector2 position, bool inside)
    {
        if (inside && IsEnabledInTree)
        {
            Open(ui);
        }
    }

    /// <summary>Opens the list of choices below the drop-down.</summary>
    public void Open(UiContext ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        var list = new StackPanel(Orientation.Vertical) { Spacing = 0, Padding = new Thickness(2) };
        for (var i = 0; i < Choices.Count; i++)
        {
            var index = i;
            list.Add(new Button(Choices[i].Text, () =>
            {
                ui.ClosePopup();
                OnSelected(index);
            })
            {
                Style = ButtonStyle.Flat,
                IsSelected = () => SelectedIndex() == index,
            });
        }

        var panel = new Panel { Style = PanelStyle.Tooltip, Padding = new Thickness(2) };
        panel.Add(new ScrollArea(list) { MaxHeight = MaxListHeight });
        ui.OpenPopup(new PopupHost(panel, Bounds));
    }

    protected override Vector2 MeasureCore(UiContext ui, Vector2 available)
    {
        var size = ui.FontSize(TextSize.Body);
        var widest = 0f;
        for (var i = 0; i < Choices.Count; i++) // Not foreach: an interface enumerator allocates every frame.
        {
            widest = MathF.Max(widest, TextLayout.Measure(ui.Font, Choices[i].Text.Resolve(ui), size));
        }

        var icon = MathF.Max(1, MathF.Round(ui.Scale)) * ui.Theme.IconSize;
        return new Vector2(widest + icon + 24 * ui.Scale, ui.Theme.ButtonHeight * ui.Scale);
    }

    protected override void DrawSelf(UiContext ui, UiDrawList list)
    {
        var theme = ui.Theme;
        var hovered = ui.Hovered == this && IsEnabledInTree;
        list.AddPanel(Bounds, hovered ? theme.ButtonHover : theme.Input, theme.PanelBorder, MathF.Max(1, ui.Scale), theme.CornerRadius * ui.Scale);
        var icon = MathF.Max(1, MathF.Round(ui.Scale)) * theme.IconSize;
        list.AddIcon(ui.Icons.Get("arrowDown"), new RectF(Bounds.Right - icon - 6 * ui.Scale, Bounds.Y + MathF.Round((Bounds.Height - icon) / 2), icon, icon), UiColor.White);
        var index = SelectedIndex();
        if ((uint)index < Choices.Count)
        {
            var size = ui.FontSize(TextSize.Body);
            var y = Bounds.Y + MathF.Round((Bounds.Height - TextLayout.LineHeight(ui.Font, size)) / 2);
            list.AddTextClipped(ui.Font, Choices[index].Text.Resolve(ui), new Vector2(Bounds.X + 8 * ui.Scale, y), size,
                ui.TextColorOf(TextColor.Normal, IsEnabledInTree), Bounds.Width - icon - 20 * ui.Scale);
        }
    }
}

/// <summary>Places a popup below an anchor rectangle, kept on screen. Only its content takes clicks.</summary>
internal sealed class PopupHost : Widget
{
    private readonly Widget _content;

    public PopupHost(Widget content, RectF anchor)
    {
        _content = Add(content);
        Anchor = anchor;
    }

    public RectF Anchor { get; }

    protected override void ArrangeCore(UiContext ui, RectF area)
    {
        var size = _content.Measure(ui, area.Size);
        var width = MathF.Max(size.X, Anchor.Width);
        var x = Math.Clamp(Anchor.X, 0, MathF.Max(0, area.Width - width));
        var y = Anchor.Bottom + 2;
        if (y + size.Y > area.Height)
        {
            y = MathF.Max(0, Anchor.Y - size.Y - 2); // Open upwards near the bottom of the screen.
        }

        _content.Arrange(ui, new RectF(x, y, width, MathF.Min(size.Y, area.Height)));
    }
}

/// <summary>A one-line text box (for example the map seed). Typed text is player data, not interface text.</summary>
internal sealed class TextInput : Widget
{
    private string _text;
    private int _version;

    public TextInput(string initial, int maxLength = 32, Func<char, bool>? accepts = null)
    {
        _text = initial ?? string.Empty;
        MaxLength = maxLength;
        Accepts = accepts ?? (c => !char.IsControl(c));
        MinWidth = 160;
        DisplayText = TextRef.Dynamic(() => _version, _ => _text);
    }

    public string Text
    {
        get => _text;
        set
        {
            _text = value ?? string.Empty;
            _version++;
        }
    }

    public int MaxLength { get; }

    public Func<char, bool> Accepts { get; }

    public Action<string>? OnChanged { get; set; }

    private TextRef DisplayText { get; }

    public override bool IsHitTestVisible => true;

    public override bool IsFocusable => true;

    public override bool OnMouseDown(UiContext ui, MouseButton button, Vector2 position) => button == MouseButton.Left;

    public override bool OnChar(UiContext ui, char character)
    {
        if (_text.Length < MaxLength && Accepts(character))
        {
            Text = _text + character;
            OnChanged?.Invoke(_text);
        }

        return true;
    }

    public override bool OnKeyDown(UiContext ui, Key key, bool shift)
    {
        ArgumentNullException.ThrowIfNull(ui);
        switch (key)
        {
            case Key.Backspace when _text.Length > 0:
                Text = _text[..^1];
                OnChanged?.Invoke(_text);
                return true;
            case Key.Enter or Key.KeypadEnter or Key.Escape or Key.Tab:
                ui.SetFocus(null);
                return true;
            default:
                return true; // Every other key is swallowed so shortcuts do not fire while typing (FR-018).
        }
    }

    protected override Vector2 MeasureCore(UiContext ui, Vector2 available) =>
        new(160 * ui.Scale, ui.Theme.ButtonHeight * ui.Scale);

    protected override void DrawSelf(UiContext ui, UiDrawList list)
    {
        var theme = ui.Theme;
        var focused = ui.Focused == this;
        list.AddPanel(Bounds, theme.Input, focused ? theme.Accent : theme.PanelBorder, MathF.Max(1, ui.Scale), theme.CornerRadius * ui.Scale);
        var size = ui.FontSize(TextSize.Body);
        var y = Bounds.Y + MathF.Round((Bounds.Height - TextLayout.LineHeight(ui.Font, size)) / 2);
        var text = DisplayText.Resolve(ui);
        var x = Bounds.X + 8 * ui.Scale;
        var width = list.AddText(ui.Font, text, new Vector2(x, y), size, theme.Text);
        if (focused && (int)(ui.Time * 2) % 2 == 0)
        {
            list.AddRect(new RectF(x + width + 1, y + 2 * ui.Scale, MathF.Max(1, ui.Scale), TextLayout.LineHeight(ui.Font, size) - 4 * ui.Scale), theme.Text);
        }
    }
}

/// <summary>A horizontal slider between <see cref="Minimum"/> and <see cref="Maximum"/> in steps.</summary>
internal sealed class Slider : Widget
{
    public Slider(float minimum, float maximum, float step, Func<float> value, Action<float> onChanged)
    {
        Minimum = minimum;
        Maximum = maximum;
        Step = step;
        Value = value;
        OnChanged = onChanged;
        MinWidth = 160;
    }

    public float Minimum { get; }

    public float Maximum { get; }

    public float Step { get; }

    public Func<float> Value { get; }

    public Action<float> OnChanged { get; }

    public override bool IsHitTestVisible => true;

    public override bool IsFocusable => true;

    public override bool OnMouseDown(UiContext ui, MouseButton button, Vector2 position)
    {
        if (button != MouseButton.Left)
        {
            return false;
        }

        SetFrom(position);
        return true;
    }

    public override void OnDrag(UiContext ui, Vector2 position, Vector2 delta) => SetFrom(position);

    public override bool OnKeyDown(UiContext ui, Key key, bool shift)
    {
        ArgumentNullException.ThrowIfNull(ui);
        var step = Step > 0 ? Step : (Maximum - Minimum) / 20f;
        if (shift)
        {
            step *= 5f;
        }

        var value = key switch
        {
            Key.Left or Key.Down => Value() - step,
            Key.Right or Key.Up => Value() + step,
            Key.Home => Minimum,
            Key.End => Maximum,
            _ => Value(),
        };
        if (value == Value())
        {
            return key is Key.Left or Key.Down or Key.Right or Key.Up or Key.Home or Key.End;
        }

        SetValue(value);
        return true;
    }

    /// <summary>The value for a cursor position, snapped to the step.</summary>
    public float ValueAt(float x)
    {
        var t = Bounds.Width > 0 ? Math.Clamp((x - Bounds.X) / Bounds.Width, 0, 1) : 0;
        var raw = Minimum + t * (Maximum - Minimum);
        return Step > 0 ? Math.Clamp(Minimum + MathF.Round((raw - Minimum) / Step) * Step, Minimum, Maximum) : raw;
    }

    protected override Vector2 MeasureCore(UiContext ui, Vector2 available) => new(160 * ui.Scale, 22 * ui.Scale);

    protected override void DrawSelf(UiContext ui, UiDrawList list)
    {
        var theme = ui.Theme;
        var track = new RectF(Bounds.X, Bounds.Center.Y - 3 * ui.Scale, Bounds.Width, 6 * ui.Scale);
        list.AddRect(track, theme.Input, 3 * ui.Scale);
        var t = Maximum > Minimum ? (Value() - Minimum) / (Maximum - Minimum) : 0;
        list.AddRect(new RectF(track.X, track.Y, track.Width * t, track.Height), theme.Accent, 3 * ui.Scale);
        var knob = 16 * ui.Scale;
        var hovered = ui.Hovered == this || ui.IsPressed(this);
        var focused = ui.Focused == this;
        list.AddPanel(new RectF(track.X + track.Width * t - knob / 2, Bounds.Center.Y - knob / 2, knob, knob),
            hovered ? theme.ButtonHover : theme.Button, focused ? theme.Accent : theme.PanelBorder, MathF.Max(1, ui.Scale), knob / 2);
    }

    private void SetFrom(Vector2 position)
    {
        var value = ValueAt(position.X);
        SetValue(value);
    }

    private void SetValue(float value)
    {
        value = Math.Clamp(value, Minimum, Maximum);
        if (Step > 0)
        {
            value = Math.Clamp(Minimum + MathF.Round((value - Minimum) / Step) * Step, Minimum, Maximum);
        }

        if (value != Value())
        {
            OnChanged(value);
        }
    }
}

/// <summary>Shows part of a taller child and scrolls it with the mouse wheel; clips what is outside.</summary>
internal sealed class ScrollArea : Widget
{
    private readonly Widget _content;
    private float _offset;
    private float _contentHeight;

    public ScrollArea(Widget content)
    {
        _content = Add(content);
    }

    /// <summary>Tallest the area grows before scrolling, in logical pixels; null = what the parent gives.</summary>
    public float? MaxHeight { get; set; }

    /// <summary>Scrolled distance in framebuffer pixels.</summary>
    public float Offset => _offset;

    public override bool IsHitTestVisible => true;

    public override bool OnScroll(UiContext ui, float delta)
    {
        ArgumentNullException.ThrowIfNull(ui);
        var max = MathF.Max(0, _contentHeight - Bounds.Height);
        if (max <= 0)
        {
            return false;
        }

        _offset = Math.Clamp(_offset - delta * 40 * ui.Scale, 0, max);
        return true;
    }

    public override Widget? HitTest(Vector2 point) =>
        Visible && Bounds.Contains(point) ? _content.HitTest(point) ?? this : null;

    protected override Vector2 MeasureCore(UiContext ui, Vector2 available)
    {
        var size = _content.Measure(ui, new Vector2(available.X, float.PositiveInfinity));
        _contentHeight = size.Y;
        var max = MaxHeight is { } m ? MathF.Min(m * ui.Scale, available.Y) : available.Y;
        return new Vector2(size.X, MathF.Min(size.Y, max));
    }

    protected override void ArrangeCore(UiContext ui, RectF area)
    {
        _offset = Math.Clamp(_offset, 0, MathF.Max(0, _contentHeight - area.Height));
        _content.Arrange(ui, new RectF(area.X, area.Y - _offset, area.Width - (_contentHeight > area.Height ? 6 * ui.Scale : 0), _contentHeight));
    }

    protected override void DrawChildren(UiContext ui, UiDrawList list)
    {
        list.PushClip(Bounds);
        _content.Draw(ui, list);
        list.PopClip();
        if (_contentHeight > Bounds.Height && _contentHeight > 0)
        {
            var height = Bounds.Height * Bounds.Height / _contentHeight;
            var y = Bounds.Y + (_offset / _contentHeight) * Bounds.Height;
            list.AddRect(new RectF(Bounds.Right - 4 * ui.Scale, y, 4 * ui.Scale, height), ui.Theme.TextMuted.WithAlpha(0.6f), 2 * ui.Scale);
        }
    }
}

/// <summary>A scrollable list of selectable rows.</summary>
internal sealed class ListView : Widget
{
    private readonly StackPanel _rows = new(Orientation.Vertical) { Spacing = 1 };

    public ListView(float maxHeight)
    {
        Add(new ScrollArea(_rows) { MaxHeight = maxHeight });
    }

    public int SelectedIndex { get; set; } = -1;

    public Action<int>? OnSelected { get; set; }

    public int Count => _rows.ChildCount;

    public void AddRow(TextRef text)
    {
        var index = _rows.ChildCount;
        _rows.Add(new Button(text, () =>
        {
            SelectedIndex = index;
            OnSelected?.Invoke(index);
        })
        {
            Style = ButtonStyle.Flat,
            IsSelected = () => SelectedIndex == index,
        });
    }

    public void Clear()
    {
        _rows.ClearChildren();
        SelectedIndex = -1;
    }
}

/// <summary>Tabs across the top; only the selected tab's content is shown.</summary>
internal sealed class TabControl : StackPanel
{
    private readonly StackPanel _headers = new(Orientation.Horizontal) { Spacing = 2 };
    private readonly Panel _body = new() { Style = PanelStyle.None };
    private readonly List<Widget> _pages = [];

    public TabControl()
        : base(Orientation.Vertical)
    {
        Add(_headers);
        Add(_body);
    }

    public int SelectedIndex { get; private set; }

    public void AddTab(TextRef title, Widget content)
    {
        var index = _pages.Count;
        _pages.Add(content);
        _headers.Add(new Button(title, () => Select(index)) { IsSelected = () => SelectedIndex == index });
        if (index == 0)
        {
            _body.Add(content);
        }
    }

    public void Select(int index)
    {
        if ((uint)index >= _pages.Count || index == SelectedIndex)
        {
            return;
        }

        _body.ClearChildren();
        _body.Add(_pages[index]);
        SelectedIndex = index;
    }
}

/// <summary>A horizontal bar filled from 0 to 1 (map generation progress).</summary>
internal sealed class ProgressBar : Widget
{
    public ProgressBar(Func<float> value)
    {
        Value = value;
    }

    public Func<float> Value { get; }

    protected override Vector2 MeasureCore(UiContext ui, Vector2 available) => new(160 * ui.Scale, 10 * ui.Scale);

    protected override void DrawSelf(UiContext ui, UiDrawList list)
    {
        var radius = Bounds.Height / 2;
        list.AddRect(Bounds, ui.Theme.Input, radius);
        var filled = Bounds.Width * Math.Clamp(Value(), 0f, 1f);
        if (filled >= 1)
        {
            list.AddRect(new RectF(Bounds.X, Bounds.Y, MathF.Max(filled, Bounds.Height), Bounds.Height), ui.Theme.Accent, radius);
        }
    }
}
