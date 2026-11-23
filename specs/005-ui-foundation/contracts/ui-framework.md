# Contract: UI framework (internal API of `Talt.Client.Ui`)

Later features build their windows with this API; it must stay stable.

## Building a window

```csharp
var window = new UiWindow(kind: "vehicle", title: TextRef.Key("vehicle.title", ("id", id)))
{
    Key = $"vehicle:{id}",
    Content = new StackPanel(Orientation.Vertical)
    {
        Children =
        {
            new Label(TextRef.Key("vehicle.speed", ("speed", speed))),
            new Button(TextRef.Key("vehicle.stop"), onClick: Stop) { Tooltip = TextRef.Key("vehicle.stop.tooltip"), Action = InputAction.None },
        },
    },
};
ui.Windows.Open(window);            // cascades, focuses; returns the existing one if Key matches
```

## Rules

- Widgets never take literal strings: `TextRef.Key(...)` for localized text, `TextRef.Dynamic(...)`
  for runtime values formatted with `Strings`/`MoneyFormatter`.
- `Widget.Invalidate()` after changing anything that affects size; layout runs once per frame at
  most, only when something is invalid.
- Input handlers return `true` when they consume the event.
- No allocations in `Draw`; text is laid out when it changes, not per frame.
- `UiContext` is GPU-free; `UiRenderer` consumes `UiDrawList` and is the only GPU code.

## Input routing order

Captured target → tooltip/modal → windows (front to back) → HUD → focused text input (keys) →
key bindings → world (camera, picking). See research R6.

## Picking

```csharp
interface IPickLayer { int Priority { get; } bool TryPick(in Ray ray, in PickContext context, out PickResult result); }
mapPicker.Register(new StationPickLayer(...)); // later features
```

## Rendering

`UiRenderer.Upload(UiDrawList)` then `UiRenderer.Encode(pass)` inside a render pass that loads the
world colour target. Vertex format: position (float2, framebuffer px), uv (float2), color (unorm8x4),
shape (float4: half width, half height, radius, mode). Modes: 0 rounded rect, 1 SDF text, 2 icon.
