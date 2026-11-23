# Data Model: User Interface Foundation (005)

All types live in `Talt.Client`. None is simulation state; none affects fingerprints.

## Screens

| Screen | Simulation | Shows | Transitions |
|---|---|---|---|
| `MainMenu` | none | New game, Load game (disabled), Settings, Quit | New game → `NewGame`; Quit → exit |
| `NewGame` | none | option rows from `INewGameOptionsSource`, Start, Back | Start → generating → `InGame`; Back → `MainMenu` |
| `InGame` | runs | top bar, toolbar, windows, notifications, highlight | Esc (no window) → pause menu |
| Pause menu (modal in `InGame`) | paused | Resume, Settings, Main menu, Quit | Resume → previous speed; Main menu → releases game → `MainMenu` |

Settings and key bindings are windows usable on any screen.

## Widget

| Field | Type | Notes |
|---|---|---|
| `Bounds` | rect (logical px) | set by arrange |
| `DesiredSize` | size | set by measure |
| `Margin`, `Padding` | thickness | logical px |
| `Width`, `Height` | float? | fixed size, otherwise content / stretch |
| `HorizontalAlignment`, `VerticalAlignment` | enum | Stretch, Start, Center, End |
| `Visible`, `Enabled` | bool | disabled widgets still show tooltips |
| `Tooltip` | `TextRef?` | FR-017; shortcut appended from the bound action |
| `Parent`, `Children` | tree | |

States: `Normal → Hovered → Pressed → (click) → Hovered`; `Disabled`; `Focused` (text input).

## UiWindow

| Field | Type | Rules |
|---|---|---|
| `Kind` | string | e.g. `settings`, `object-info`; cascade offset per kind |
| `Key` | string? | identity for "one window per object" (`tile:812,455`, `vehicle:3021`) |
| `SingleInstance` | bool | reopening focuses the existing one |
| `Title` | `TextRef` | |
| `Position` | point | kept so ≥ 32 px of the title bar stays on screen |
| `Pinned` | bool | survives close all |
| `Closable`, `Pinnable` | bool | |
| `ZOrder` | int | index in `WindowManager` list |

Limit: 64 windows; opening the 65th closes the oldest unpinned (edge case).

## TextRef

Either `Key + Arguments` (localized, re-resolved on language change) or `Dynamic` (a delegate
evaluated when its version changes, used for numbers, dates and money composed from localized
formats). Literal strings are not accepted by widgets (SC-003).

## Theme (data)

| Field | Type | Rules |
|---|---|---|
| `Colors` | name → sRGB colour | panel, panelBorder, titleBar, text, textMuted, accent, button, buttonHover, buttonPressed, buttonDisabled, tooltip, overlay, highlightHover, highlightSelection; all required |
| `CornerRadius`, `BorderWidth`, `Spacing`, `Padding` | float | ≥ 0, logical px |
| `FontSizes` | small, body, title, heading | > 0, logical px |
| `TooltipDelay` | seconds | 0.5 by default |
| `TopBarHeight`, `ToolbarWidth` | float | > 0, logical px |

## Pick result

| Field | Type |
|---|---|
| `Kind` | `None`, `Tile`, `Road`, `Vehicle` (later: `Station`, `Building`...) |
| `Tile` | (x, z) |
| `Height` | int |
| `Material` | `VoxelMaterial` (top voxel) |
| `VehicleId` | int (vehicles) |
| `Bounds` | world box for the highlight |
| `Distance` | along the ray |

`IPickLayer`: `Priority`, `TryPick(in Ray, in PickContext, out PickResult)`. The `MapPicker` asks
every layer and keeps the highest priority hit; ties go to the nearest.

## PlayerSettings (persisted)

| Field | Type | Default | Validation |
|---|---|---|---|
| `Version` | int | 2 | |
| `Language` | string? | null → system language | must be `en` or `es` |
| `Currency` | string? | null → by language (`USD`/`EUR`) | must exist in currencies.json |
| `UiScale` | float? | null → suggested | 0.75–2.0, step 0.25 |
| `Fullscreen` | bool | false | |
| `WindowWidth`, `WindowHeight` | int | 1600 × 900 | ≥ 800 × 600 |
| `VSync` | bool | true | |
| `FastForwardUnlocked` | bool | false | (existing, feature 001) |
| `KeyBindings` | map actionId → key names | empty (defaults) | unknown ids/keys ignored |

## InputAction (data)

`Id`, `LabelKey`, `Category` (camera, game, interface, debug), `Kind` (`Press`, `Held`),
`DefaultKeys` (0–2 key names, optional modifier `Shift+`). Bindings: action → up to two keys; a key
maps to at most one action per modifier state.

## Currency (data)

`Code` (ISO 4217), `Symbol`, `Rate` (display units per internal unit), `Decimals`, `NameKey`.

## NewGameOptionDescriptor

`Id`, `LabelKey`, `Kind` (`Choice`, `Integer`, `Seed`), `Choices` (value + label key) or
`Min`/`Max`/`Step`, `Default`. `INewGameOptionsSource.Options` and
`CreateOptions(values) → NewGameOptions` (map size in tiles, seed; extended by 004's record later).
