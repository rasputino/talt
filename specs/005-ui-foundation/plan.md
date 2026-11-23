# Implementation Plan: User Interface Foundation

**Branch**: `005-ui-foundation` | **Date**: 2026-11-22 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/005-ui-foundation/spec.md`

## Summary

Build the game's interface layer inside `Talt.Client`: a custom retained-mode UI (widgets, layout,
theme, window manager, tooltips) drawn by one alpha-blended WebGPU pipeline over the world with an
SDF font atlas generated from a bundled OFL font; input routing between UI and world with
data-driven key bindings; CPU ray picking (terrain DDA, vehicle boxes, road tiles) with wire-box
highlights; localization with plurals, culture formatting and display currencies; persisted
settings; and the first screens: main menu, new game, in-game HUD (top bar, toolbar, notifications),
pause menu, settings, key bindings and object information windows (research R1–R12).

## Technical Context

**Language/Version**: C# latest on .NET 10, nullable, warnings as errors

**Primary Dependencies**: Silk.NET.Windowing/Input/WebGPU 2.23 (existing); new: StbTrueTypeSharp
1.26.13 (font rasterizer, MIT OR Unlicense); bundled asset: Noto Sans Regular (SIL OFL 1.1)

**Storage**: JSON settings file in the user profile folder; embedded JSON data files (strings,
theme, icons, actions, currencies, starting capital)

**Testing**: xUnit (`Talt.Client.Tests`: layout, routing, window manager, localization, settings,
bindings, picking, font atlas); BenchmarkDotNet (`UiFrameBenchmarks`, `PickingBenchmarks`)

**Target Platform**: Windows x64, Linux x64/arm64, macOS arm64 (CI from feature 002)

**Project Type**: desktop game (client + core library)

**Performance Goals**: 20 windows add < 1 ms per frame (SC-001); picking within the same frame
(SC-002); 4096 map still 60 FPS at every zoom (SC-005)

**Constraints**: no per-frame allocations in steady state (FR-029); no GPU readback; font atlas
generation < 300 ms at startup

**Scale/Scope**: ~6 screens, ~14 widget types, ~150 localized texts, ~25 bindable actions, 21+
currencies

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | How |
|---|---|---|
| I. Core/Client separation | Pass | All UI, picking, input and settings code is in `Talt.Client`. Picking reads `Heightmap`, `VoxelWorld` and drawn vehicle instances; nothing new in `Talt.Core` references graphics. |
| II. Performance | Pass | Pre-allocated draw list, retained text layout, layout only when dirty; new benchmarks `UiFrameBenchmarks`, `PickingBenchmarks`; UI time in F3 statistics; results in `docs/ARCHITECTURE.md`. |
| III. Determinism | Pass | UI never touches simulation state directly: speed changes go through `SimulationRunner` as today, add/remove vehicle still go through commands. Picking and display currency are read-only. Golden fingerprints unchanged. |
| IV. Tested increments | Pass | UI logic is GPU-free and covered by xUnit; each user story has its own tests and a hand check (quickstart). |
| V. Readable C# | Pass | Small widget classes, XML docs; unsafe code only in `Rendering/UiRenderer.cs` and `Rendering/HighlightRenderer.cs`. |
| VI. Original content | Pass | Own UI code and pixel-art icons; Noto Sans is OFL (commercial distribution allowed, licence shipped); StbTrueTypeSharp MIT/Unlicense. No assets from other games. |
| VII. Data-driven | Pass | Theme, icons, actions/default keys, currencies and starting capital are JSON data files validated at load. |
| VIII. English docs, localizable text | Pass | All player text from `Strings.*.json` in English and Spanish, checked by tests; docs updated. |

**New dependency justification** (Technology Constraints): StbTrueTypeSharp — purpose: read the
TrueType font and generate SDF glyph bitmaps; licence MIT OR Unlicense (compatible with a closed
commercial game); maintenance: StbSharp organisation, port of the widely used stb_truetype, last
release 1.26.13; size: 89 KB, no native code, no transitive dependencies. Asset: Noto Sans Regular
569 KB, OFL 1.1.

**Post-design re-check**: Pass (no violations; no complexity tracking needed).

## Project Structure

### Documentation (this feature)

```text
specs/005-ui-foundation/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── ui-framework.md      # Widget, window manager, input routing and rendering contracts
│   ├── data-files.md        # Strings, theme, icons, actions, currencies, settings formats
│   └── command-line.md      # Client options added or changed
├── checklists/
└── tasks.md
```

### Source Code (repository root)

```text
src/Talt.Client/
├── Program.cs                     # argument parsing only; hands ClientOptions to GameWindow
├── GameWindow.cs                  # window, loop; delegates input to InputRouter, screens to ScreenManager
├── GameSetup.cs                   # builds simulation + world + heightmap from NewGameOptions (moved from Program.cs)
├── GameSession.cs                 # one running game: simulation, world, runner, poses, vehicle instances
├── DataFiles.cs                   # embedded data loading with clear validation errors
├── PlayerSettings.cs              # extended: language, currency, scale, display, bindings; robust load
├── Localization/
│   ├── Strings.cs                 # + plurals, language switch support
│   ├── Strings.en.json / .es.json
│   ├── MoneyFormatter.cs
│   └── Data/currencies.json
├── Input/
│   ├── InputAction.cs             # action ids
│   ├── KeyBindings.cs             # defaults from data + player overrides
│   ├── InputRouter.cs             # dispatch order (research R6)
│   └── Data/actions.json
├── Picking/
│   ├── Ray.cs, PickResult.cs, IPickLayer.cs
│   ├── TerrainPickLayer.cs, VehiclePickLayer.cs, RoadPickLayer.cs
│   └── MapPicker.cs
├── Ui/
│   ├── UiContext.cs               # root: theme, strings, scale, layers, focus, tooltip, layout and draw
│   ├── UiDrawList.cs              # vertices + scissor commands, no GPU types
│   ├── Theme.cs, Data/theme.json
│   ├── Font/FontAtlas.cs, Font/TextLayout.cs, Font/NotoSans-Regular.ttf, Font/OFL.txt
│   ├── Icons.cs, Data/icons.json
│   ├── Widgets/ (Widget, Panel, StackPanel, Label, Button, IconButton, Checkbox, Slider,
│   │            Dropdown, TextInput, ListView, ScrollArea, TabControl, Spacer)
│   ├── Windows/ (UiWindow, WindowManager)
│   └── TextRef.cs                 # localized text binding (key + arguments) or dynamic text
├── Screens/
│   ├── ScreenManager.cs, IScreen.cs
│   ├── MainMenuScreen.cs, NewGameScreen.cs, InGameScreen.cs (HUD), PauseMenu.cs
│   ├── SettingsWindow.cs, KeyBindingsWindow.cs, ObjectInfoWindow.cs, Notifications.cs
│   ├── SampleWindows.cs           # --ui-test-windows: sample windows using every widget
│   └── NewGameOptions.cs          # descriptors + INewGameOptionsSource (bound to 004 later)
└── Rendering/
    ├── WebGpuRenderer.cs          # SetWorld/ClearWorld, UI pass, present mode, highlight boxes
    ├── UiRenderer.cs, Shaders/ui.wgsl
    └── HighlightRenderer.cs, Shaders/highlight.wgsl

tests/Talt.Client.Tests/
├── Ui/ (LayoutTests, WindowManagerTests, InputRouterTests, DrawListTests, TextLayoutTests, FontAtlasTests)
├── Localization/ (StringsTests, PluralTests, MoneyFormatterTests, LocalizationCoverageTests)
├── Screens/NewGameOptionsTests.cs
├── Input/KeyBindingsTests.cs
├── Picking/PickingTests.cs
└── PlayerSettingsTests.cs

benchmarks/Talt.Benchmarks/
├── UiFrameBenchmarks.cs
└── PickingBenchmarks.cs
```

**Structure Decision**: everything stays in the two existing projects (`Talt.Client` and its test
project); the benchmark project gains a reference to `Talt.Client` (internals visible) to measure UI
and picking on the CPU. No new projects.

## Design Notes

- **Frame order**: input events → `InputRouter` → update (camera, simulation, screen updates) →
  picking (when the cursor is over the map) → UI layout (if dirty) → draw-list build → render: world
  pass (terrain, vehicles, highlight boxes) then UI pass (`LoadOp.Load`, no depth, alpha blend,
  scissor per clip command).
- **UI coordinates**: logical pixels × scale = framebuffer pixels; the mouse is converted from
  window to framebuffer coordinates (they differ on high-DPI macOS).
- **Draw list**: `UiVertex { Vector2 Position; Vector2 Uv; uint Color; Vector4 Shape }` where
  `Shape` packs rectangle half-size, corner radius and mode (solid rounded rect, SDF text, icon);
  one texture (font SDF + icons), one pipeline, one dynamic vertex buffer grown by doubling.
- **Retained text**: `Label` keeps glyph positions for its current text and size; changes of text,
  language or scale invalidate it.
- **Window manager**: z-ordered list, focus to front on click, cascade offset per kind, single
  instance kinds, pin, close all unpinned, clamp to screen on resize/scale change, limit 64 windows
  (oldest unpinned replaced).
- **Simulation on menus**: on the main menu there is no simulation; the pause menu pauses the
  runner and restores the previous speed on resume.
- **Money**: `CompanyFunds` display source = starting capital from `Data/company.json` until the
  finances feature provides a balance.
- **Coordination with 004**: see research R10; follow-up task in tasks.md.

## Complexity Tracking

No violations.
