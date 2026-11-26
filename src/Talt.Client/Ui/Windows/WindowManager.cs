using System.Numerics;
using Talt.Client.Ui.Widgets;

namespace Talt.Client.Ui.Windows;

/// <summary>
/// Keeps the open windows in front-to-back order and applies the window rules of FR-013–FR-015:
/// focus to front, cascade per kind, single instance, one window per object key, pin, close all,
/// stay on screen, at most <see cref="MaxWindows"/> windows.
/// </summary>
internal sealed class WindowManager
{
    /// <summary>Opening more windows than this closes the oldest unpinned one (edge case).</summary>
    public const int MaxWindows = 64;

    /// <summary>Logical pixels between cascaded windows.</summary>
    public const float CascadeOffset = 26f;

    /// <summary>Cascade places tried before the cascade starts again from the top left.</summary>
    public const int CascadeSteps = 10;

    /// <summary>Logical pixels of a window that must stay on screen horizontally.</summary>
    public const float KeepVisible = 48f;

    private readonly List<UiWindow> _windows = []; // Back to front.
    private readonly List<UiWindow> _pendingPlacement = [];

    public int Count => _windows.Count;

    /// <summary>The window in front, if any.</summary>
    public UiWindow? Front => _windows.Count > 0 ? _windows[^1] : null;

    /// <summary>Where new windows start, before cascading (logical pixels from the top left).</summary>
    public Vector2 Origin { get; set; } = new(70, 60);

    /// <summary>Window at <paramref name="index"/>, 0 = back.</summary>
    public UiWindow this[int index] => _windows[index];

    /// <summary>Opens a window, or focuses the open one with the same key or single-instance kind.</summary>
    public UiWindow Open(UiWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var existing = Find(window);
        if (existing is not null)
        {
            BringToFront(existing);
            return existing;
        }

        if (_windows.Count >= MaxWindows)
        {
            var oldest = _windows.FirstOrDefault(w => !w.Pinned) ?? _windows[0];
            Close(oldest);
        }

        window.Manager = this;
        _windows.Add(window);
        if (float.IsNaN(window.Position.X))
        {
            _pendingPlacement.Add(window); // Placed at the next layout, once the scale is known.
        }

        return window;
    }

    public bool IsOpen(UiWindow window) => _windows.Contains(window);

    /// <summary>The open window with this key, if any.</summary>
    public UiWindow? FindByKey(string key) => _windows.Find(w => w.Key == key);

    public void Close(UiWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (_windows.Remove(window))
        {
            _pendingPlacement.Remove(window);
            window.Manager = null;
            window.RaiseClosed();
        }
    }

    /// <summary>Closes the front window; false when none is closable.</summary>
    public bool CloseFront()
    {
        for (var i = _windows.Count - 1; i >= 0; i--)
        {
            if (_windows[i].Closable)
            {
                Close(_windows[i]);
                return true;
            }
        }

        return false;
    }

    /// <summary>Closes every unpinned window (FR-014).</summary>
    public void CloseAll()
    {
        for (var i = _windows.Count - 1; i >= 0; i--)
        {
            if (!_windows[i].Pinned && _windows[i].Closable)
            {
                Close(_windows[i]);
            }
        }
    }

    /// <summary>Closes every window, pinned or not (leaving a game).</summary>
    public void CloseEverything()
    {
        for (var i = _windows.Count - 1; i >= 0; i--)
        {
            Close(_windows[i]);
        }
    }

    public void BringToFront(UiWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (_windows.Remove(window))
        {
            _windows.Add(window);
        }
    }

    /// <summary>Measures, places and arranges every window.</summary>
    public void Layout(UiContext ui)
    {
        ArgumentNullException.ThrowIfNull(ui);
        foreach (var window in _windows)
        {
            window.Measure(ui, ui.ViewportSize);
        }

        foreach (var window in _pendingPlacement)
        {
            Place(ui, window);
        }

        _pendingPlacement.Clear();
        foreach (var window in _windows)
        {
            Clamp(ui, window);
            var size = Vector2.Min(window.DesiredSize, ui.ViewportSize);
            window.Arrange(ui, new RectF(window.Position.X, window.Position.Y, size.X, size.Y));
        }
    }

    public void Draw(UiContext ui, UiDrawList list)
    {
        foreach (var window in _windows)
        {
            window.Draw(ui, list);
        }
    }

    /// <summary>Front-most widget of any window under <paramref name="point"/>.</summary>
    public Widget? HitTest(Vector2 point)
    {
        for (var i = _windows.Count - 1; i >= 0; i--)
        {
            if (_windows[i].HitTest(point) is { } hit)
            {
                return hit;
            }
        }

        return null;
    }

    /// <summary>The window containing <paramref name="widget"/>, if any.</summary>
    public UiWindow? WindowOf(Widget? widget)
    {
        for (var w = widget; w is not null; w = w.Parent)
        {
            if (w is UiWindow window && window.Manager == this)
            {
                return window;
            }
        }

        return null;
    }

    /// <summary>Keeps the title bar reachable: never above the top, at least part of it on screen.</summary>
    public static void Clamp(UiContext ui, UiWindow window)
    {
        ArgumentNullException.ThrowIfNull(ui);
        ArgumentNullException.ThrowIfNull(window);
        if (float.IsNaN(window.Position.X))
        {
            return;
        }

        var keep = KeepVisible * ui.Scale;
        var width = MathF.Min(window.DesiredSize.X, ui.ViewportSize.X);
        var minX = MathF.Min(0, keep - width);
        var maxX = MathF.Max(minX, ui.ViewportSize.X - keep);
        var maxY = MathF.Max(0, ui.ViewportSize.Y - UiWindow.TitleBarHeight(ui));
        window.Position = new Vector2(Math.Clamp(window.Position.X, minX, maxX), Math.Clamp(window.Position.Y, 0, maxY));
    }

    private void Place(UiContext ui, UiWindow window)
    {
        // Every new window, of any kind, takes the first cascade place no open window sits on, so a window opened from
        // another never lands exactly on top of it and looks like part of it (found in the hand test of feature 012).
        var origin = Origin * ui.Scale;
        var offset = CascadeOffset * ui.Scale;
        for (var step = 0; step < CascadeSteps; step++)
        {
            var candidate = origin + new Vector2(step * offset);
            if (!IsTaken(window, candidate, offset / 2f))
            {
                window.Position = candidate;
                return;
            }
        }

        // All places taken: one step below the front window (wrapping to the top left past the last place).
        var front = _windows.Count > 1 ? _windows[^2] : null;
        var frontStep = front is null || float.IsNaN(front.Position.X) ? -1 : (int)MathF.Round((front.Position.X - origin.X) / offset);
        window.Position = origin + new Vector2(((frontStep + 1) % CascadeSteps + CascadeSteps) % CascadeSteps * offset);
    }

    private bool IsTaken(UiWindow window, Vector2 position, float tolerance)
    {
        foreach (var open in _windows)
        {
            if (open != window && !float.IsNaN(open.Position.X)
                && MathF.Abs(open.Position.X - position.X) < tolerance && MathF.Abs(open.Position.Y - position.Y) < tolerance)
            {
                return true;
            }
        }

        return false;
    }

    private UiWindow? Find(UiWindow window)
    {
        foreach (var open in _windows)
        {
            if ((window.Key is not null && open.Key == window.Key) || (window.SingleInstance && open.Kind == window.Kind))
            {
                return open;
            }
        }

        return null;
    }
}
