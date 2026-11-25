using System.Numerics;
using Silk.NET.Input;

namespace Talt.Client.Ui.Widgets;

/// <summary>
/// Base of every interface element (data-model.md Widget): a node of the retained widget tree with
/// a two-pass layout (measure, then arrange), drawing and input handlers. Sizes given in properties
/// are logical pixels; <see cref="Bounds"/> is in framebuffer pixels after arrange.
/// </summary>
internal abstract class Widget
{
    private readonly List<Widget> _children = [];

    public Widget? Parent { get; private set; }

    public int ChildCount => _children.Count;

    /// <summary>Position and size after the last arrange, in framebuffer pixels.</summary>
    public RectF Bounds { get; private set; }

    /// <summary>Size wanted by the last measure, including the margin, in framebuffer pixels.</summary>
    public Vector2 DesiredSize { get; private set; }

    public Thickness Margin { get; set; }

    /// <summary>Fixed width in logical pixels, or null to size to content (or stretch).</summary>
    public float? Width { get; set; }

    public float? Height { get; set; }

    public float MinWidth { get; set; }

    /// <summary>Inside a <see cref="StackPanel"/>: takes a share of the space left by the other children.</summary>
    public bool Fill { get; set; }

    public Alignment HorizontalAlignment { get; set; } = Alignment.Stretch;

    public Alignment VerticalAlignment { get; set; } = Alignment.Stretch;

    public bool Visible { get; set; } = true;

    /// <summary>When set, decides <see cref="Visible"/> at every layout (for example buttons hidden once a mode is on).</summary>
    public Func<bool>? VisibleWhen { get; set; }

    /// <summary>Disabled widgets ignore clicks but still show their tooltip.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Explanation shown when the cursor rests on the widget (FR-017).</summary>
    public TextRef? Tooltip { get; set; }

    /// <summary>Bindable action whose key is appended to the tooltip.</summary>
    public string? ShortcutAction { get; set; }

    /// <summary>Whether the widget takes clicks (and so hides what is below it).</summary>
    public virtual bool IsHitTestVisible => false;

    /// <summary>Whether the widget can receive keyboard focus (text inputs).</summary>
    public virtual bool IsFocusable => false;

    public bool IsEnabledInTree => Enabled && (Parent?.IsEnabledInTree ?? true);

    public Widget GetChild(int index) => _children[index];

    public T Add<T>(T child)
        where T : Widget
    {
        ArgumentNullException.ThrowIfNull(child);
        child.Parent?.Remove(child);
        child.Parent = this;
        _children.Add(child);
        return child;
    }

    public void Remove(Widget child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (_children.Remove(child))
        {
            child.Parent = null;
        }
    }

    public void ClearChildren()
    {
        foreach (var child in _children)
        {
            child.Parent = null;
        }

        _children.Clear();
    }

    /// <summary>Whether <paramref name="widget"/> is this widget or one of its descendants.</summary>
    public bool Contains(Widget? widget)
    {
        for (var w = widget; w is not null; w = w.Parent)
        {
            if (w == this)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Computes <see cref="DesiredSize"/> for the given available space (framebuffer pixels).</summary>
    public Vector2 Measure(UiContext ui, Vector2 available)
    {
        ArgumentNullException.ThrowIfNull(ui);
        if (VisibleWhen is { } visibleWhen)
        {
            Visible = visibleWhen();
        }

        if (!Visible)
        {
            DesiredSize = Vector2.Zero;
            return Vector2.Zero;
        }

        var margin = Margin.Scale(ui.Scale);
        var inner = new Vector2(
            Width is { } w ? w * ui.Scale : MathF.Max(0f, available.X - margin.Horizontal),
            Height is { } h ? h * ui.Scale : MathF.Max(0f, available.Y - margin.Vertical));
        var size = MeasureCore(ui, inner);
        if (Width is { } width)
        {
            size.X = width * ui.Scale;
        }

        if (Height is { } height)
        {
            size.Y = height * ui.Scale;
        }

        size.X = MathF.Max(size.X, MinWidth * ui.Scale);
        DesiredSize = new Vector2(MathF.Ceiling(size.X + margin.Horizontal), MathF.Ceiling(size.Y + margin.Vertical));
        return DesiredSize;
    }

    /// <summary>Places the widget inside <paramref name="slot"/> according to its alignment.</summary>
    public void Arrange(UiContext ui, RectF slot)
    {
        ArgumentNullException.ThrowIfNull(ui);
        if (!Visible)
        {
            Bounds = new RectF(slot.X, slot.Y, 0, 0);
            return;
        }

        var area = slot.Deflate(Margin.Scale(ui.Scale));
        var desired = DesiredSize - new Vector2(Margin.Horizontal, Margin.Vertical) * ui.Scale;
        var (x, width) = Place(HorizontalAlignment, area.X, area.Width, desired.X, Width.HasValue);
        var (y, height) = Place(VerticalAlignment, area.Y, area.Height, desired.Y, Height.HasValue);
        Bounds = new RectF(MathF.Round(x), MathF.Round(y), MathF.Round(width), MathF.Round(height));
        ArrangeCore(ui, Bounds);
    }

    /// <summary>Draws the widget and its children.</summary>
    public void Draw(UiContext ui, UiDrawList list)
    {
        if (!Visible || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        DrawSelf(ui, list);
        DrawChildren(ui, list);
    }

    /// <summary>The front-most hit-test-visible widget under <paramref name="point"/>, or null.</summary>
    public virtual Widget? HitTest(Vector2 point)
    {
        if (!Visible || !Bounds.Contains(point))
        {
            return null;
        }

        for (var i = _children.Count - 1; i >= 0; i--)
        {
            if (_children[i].HitTest(point) is { } hit)
            {
                return hit;
            }
        }

        return IsHitTestVisible ? this : null;
    }

    /// <summary>Mouse button pressed on this widget (or a child that did not handle it).</summary>
    public virtual bool OnMouseDown(UiContext ui, MouseButton button, Vector2 position) => false;

    /// <summary>Mouse button released while this widget had the capture.</summary>
    public virtual void OnMouseUp(UiContext ui, MouseButton button, Vector2 position, bool inside)
    {
    }

    /// <summary>Mouse moved while this widget had the capture (dragging).</summary>
    public virtual void OnDrag(UiContext ui, Vector2 position, Vector2 delta)
    {
    }

    public virtual bool OnScroll(UiContext ui, float delta) => false;

    public virtual bool OnKeyDown(UiContext ui, Key key, bool shift) => false;

    public virtual bool OnChar(UiContext ui, char character) => false;

    public virtual void OnFocusChanged(UiContext ui, bool focused)
    {
    }

    /// <summary>Default: the largest child (children overlap).</summary>
    protected virtual Vector2 MeasureCore(UiContext ui, Vector2 available)
    {
        var size = Vector2.Zero;
        foreach (var child in _children)
        {
            size = Vector2.Max(size, child.Measure(ui, available));
        }

        return size;
    }

    /// <summary>Default: every child gets the whole area.</summary>
    protected virtual void ArrangeCore(UiContext ui, RectF area)
    {
        foreach (var child in _children)
        {
            child.Arrange(ui, area);
        }
    }

    protected virtual void DrawSelf(UiContext ui, UiDrawList list)
    {
    }

    protected virtual void DrawChildren(UiContext ui, UiDrawList list)
    {
        foreach (var child in _children)
        {
            child.Draw(ui, list);
        }
    }

    private static (float Start, float Size) Place(Alignment alignment, float start, float available, float desired, bool fixedSize)
    {
        if (alignment == Alignment.Stretch && !fixedSize)
        {
            return (start, available);
        }

        var size = MathF.Min(desired, available);
        return alignment switch
        {
            Alignment.Center => (start + (available - size) / 2f, size),
            Alignment.End => (start + available - size, size),
            _ => (start, size),
        };
    }
}
