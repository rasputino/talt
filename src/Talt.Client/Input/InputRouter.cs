using System.Numerics;
using Silk.NET.Input;
using Talt.Client.Ui;

namespace Talt.Client.Input;

/// <summary>What the world (camera, picking) does with mouse input the interface did not take.</summary>
internal interface IWorldInput
{
    void MouseDown(MouseButton button, Vector2 position);

    /// <summary>Mouse moved while a button pressed on the world is held.</summary>
    void Drag(Vector2 position, Vector2 delta);

    /// <summary>Button released; <paramref name="isClick"/> when the mouse barely moved since the press.</summary>
    void MouseUp(MouseButton button, Vector2 position, bool isClick);

    void Scroll(float delta);
}

/// <summary>
/// Single place that decides who receives each input event (research R6, FR-018): the owner of a
/// drag first, then the interface (front to back), then the focused text input for keys, then key
/// bindings, then the world.
/// </summary>
internal sealed class InputRouter
{
    /// <summary>Movement, in logical pixels, below which a press and release on the map is a click (FR-012a).</summary>
    public const float ClickThreshold = 4f;

    private readonly UiContext _ui;
    private readonly KeyBindings _bindings;
    private readonly IWorldInput _world;
    private readonly Action<string, bool> _onAction;
    private Owner _owner;
    private int _buttonsDown;
    private Vector2 _pressPosition;
    private Vector2 _lastPosition;

    public InputRouter(UiContext ui, KeyBindings bindings, IWorldInput world, Action<string, bool> onAction)
    {
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        _bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _onAction = onAction ?? throw new ArgumentNullException(nameof(onAction));
    }

    private enum Owner
    {
        None,
        Ui,
        World,
    }

    /// <summary>Whether the cursor is over the map and no interface drag is in progress (picking runs).</summary>
    public bool IsCursorOverWorld => _owner != Owner.Ui && !_ui.IsMouseOverUi;

    /// <summary>Whether keyboard camera control is allowed (no text input focused, FR-018).</summary>
    public bool IsWorldKeyboardActive => !_ui.HasKeyboardFocus;

    /// <summary>Whether a drag that started on the map is in progress.</summary>
    public bool IsDraggingWorld => _owner == Owner.World;

    public Vector2 CursorPosition => _lastPosition;

    public void MouseMove(Vector2 position)
    {
        var delta = position - _lastPosition;
        _lastPosition = position;
        _ui.MouseMove(position);
        if (_owner == Owner.World)
        {
            _world.Drag(position, delta);
        }
    }

    public void MouseDown(MouseButton button, Vector2 position)
    {
        _lastPosition = position;
        _buttonsDown++;
        if (_buttonsDown > 1 && _owner != Owner.None)
        {
            if (_owner == Owner.World)
            {
                _world.MouseDown(button, position); // Second button during a map drag stays with the map.
            }

            return;
        }

        _pressPosition = position;
        if (_ui.MouseDown(button, position))
        {
            _owner = Owner.Ui;
            return;
        }

        _owner = Owner.World;
        _world.MouseDown(button, position);
    }

    public void MouseUp(MouseButton button, Vector2 position)
    {
        _lastPosition = position;
        _buttonsDown = Math.Max(0, _buttonsDown - 1);
        switch (_owner)
        {
            case Owner.Ui:
                _ui.MouseUp(button, position); // A drag that started on the map never clicks the interface.
                break;
            case Owner.World:
                var isClick = Vector2.Distance(position, _pressPosition) <= ClickThreshold * _ui.Scale;
                _world.MouseUp(button, position, isClick);
                break;
        }

        if (_buttonsDown == 0)
        {
            _owner = Owner.None;
        }
    }

    public void Scroll(float delta)
    {
        if (!_ui.Scroll(delta))
        {
            _world.Scroll(delta);
        }
    }

    public void KeyDown(Key key, bool shift)
    {
        if (_ui.KeyDown(key, shift))
        {
            return; // A focused widget (text input, key capture) takes every key.
        }

        if (_bindings.TryGetAction(key, shift, out var action))
        {
            _onAction(action, shift);
        }
    }

    public void Char(char character) => _ui.Char(character);
}
