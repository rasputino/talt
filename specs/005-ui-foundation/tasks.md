# Tasks: User Interface Foundation

**Input**: Design documents from `specs/005-ui-foundation/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: required by the constitution (IV) for all GPU-free logic; GPU and window behaviour is
checked by hand (quickstart.md).

**Organization**: by user story; US1–US3 are P1, US4–US5 P2, US6 P3.

## Format: `[ID] [P?] [Story] Description`

## Phase 1: Setup

- [X] T001 Add `StbTrueTypeSharp` 1.26.13 to `Directory.Packages.props` and `src/Talt.Client/Talt.Client.csproj`; embed `Ui/Font/NotoSans-Regular.ttf`, `Ui/Font/OFL.txt` and every `**/Data/*.json` file with stable logical names
- [X] T002 [P] Add the Noto Sans OFL notice to the README third-party section in `README.md`
- [X] T003 [P] Reference `Talt.Client` from `benchmarks/Talt.Benchmarks/Talt.Benchmarks.csproj` and add `InternalsVisibleTo Talt.Benchmarks` in `src/Talt.Client/Talt.Client.csproj`

## Phase 2: Foundational (blocks all stories)

- [X] T004 [P] Embedded data loading helper with clear validation errors in `src/Talt.Client/DataFiles.cs`
- [X] T005 [P] Theme record and loader (data-model.md Theme.Colors, Theme.CornerRadius, Theme.Spacing, Theme.FontSizes, Theme.TooltipDelay, Theme.TopBarHeight, Theme.ToolbarWidth; colors `#RRGGBB[AA]`) in `src/Talt.Client/Ui/Theme.cs` and `src/Talt.Client/Ui/Data/theme.json`
- [X] T006 [P] SDF font atlas generation (glyph set of research R2, parallel per glyph, advances, kerning) in `src/Talt.Client/Ui/Font/FontAtlas.cs`
- [X] T007 Text layout (measure, glyph quads, ellipsis clipping, wrapping) in `src/Talt.Client/Ui/Font/TextLayout.cs`
- [X] T008 [P] Pixel-art icons parser and atlas packing in `src/Talt.Client/Ui/Icons.cs` and `src/Talt.Client/Ui/Data/icons.json`
- [X] T009 Draw list (vertices, scissor commands, rounded rects, text, icons; no allocations after warm-up) in `src/Talt.Client/Ui/UiDrawList.cs`
- [X] T010 `TextRef` (key + arguments or dynamic) in `src/Talt.Client/Ui/TextRef.cs`
- [X] T011 Widget base with measure/arrange, states, tooltip, invalidation (data-model.md Widget) in `src/Talt.Client/Ui/Widgets/Widget.cs`
- [X] T012 [P] Containers `Panel`, `StackPanel`, `Spacer` in `src/Talt.Client/Ui/Widgets/`
- [X] T013 [P] `Label`, `Button`, `IconButton` in `src/Talt.Client/Ui/Widgets/`
- [X] T014 `UiContext` (layers, scale, hit testing, hover, capture, focus, tooltip timer, layout and draw passes; a zero or minimized framebuffer skips layout and drawing without negative sizes) in `src/Talt.Client/Ui/UiContext.cs`
- [X] T015 UI pipeline: `src/Talt.Client/Rendering/Shaders/ui.wgsl` and `src/Talt.Client/Rendering/UiRenderer.cs` (atlas texture, dynamic vertex buffer, scissor draws), called from `WebGpuRenderer.Draw` as a second pass
- [X] T016 `WebGpuRenderer` without a world (`SetWorld`/`ClearWorld`, drawing only sky + UI) in `src/Talt.Client/Rendering/WebGpuRenderer.cs`
- [X] T017 [P] Input actions data and `KeyBindings` with defaults equal to today's controls (data-model.md InputAction.Id, InputAction.LabelKey, InputAction.Category, InputAction.Kind, InputAction.DefaultKeys) in `src/Talt.Client/Input/InputAction.cs`, `src/Talt.Client/Input/KeyBindings.cs`, `src/Talt.Client/Input/Data/actions.json`
- [X] T018 `InputRouter` (research R6 order, click threshold, drag capture) in `src/Talt.Client/Input/InputRouter.cs`
- [X] T019 [P] Tests: font atlas covers all Spanish and currency glyphs, text measure in `tests/Talt.Client.Tests/Ui/FontAtlasTests.cs`
- [X] T020 [P] Tests: layout of stacks, padding, alignment, scale, zero-size viewport in `tests/Talt.Client.Tests/Ui/LayoutTests.cs`
- [X] T021 [P] Tests: routing order, consumption, drag released over UI, text focus suppresses shortcuts in `tests/Talt.Client.Tests/Ui/InputRouterTests.cs`
- [X] T022 [P] Tests: draw list does not allocate in steady state in `tests/Talt.Client.Tests/Ui/DrawListTests.cs`

**Checkpoint**: an empty window draws themed UI text over the sky.

## Phase 3: User Story 1 — Main menu and in-game HUD (P1) MVP

**Goal**: main menu → new game → map with top bar, toolbar, notifications and pause menu.

**Independent test**: quickstart steps 1–2.

- [X] T023 [US1] Move world/simulation creation from `Program.cs` into `src/Talt.Client/GameSetup.cs` (`NewGameOptions` → scene), keep `Program.cs` to argument parsing (contracts/command-line.md) in `src/Talt.Client/Program.cs`
- [X] T024 [US1] `GameSession` (simulation, runner, poses, vehicle instances, recorder) extracted from `GameWindow` in `src/Talt.Client/GameSession.cs`
- [X] T025 [US1] Screen manager and screen interface in `src/Talt.Client/Screens/ScreenManager.cs`, `src/Talt.Client/Screens/IScreen.cs`
- [X] T026 [P] [US1] New-game option descriptors and `INewGameOptionsSource` with map size and seed (data-model.md NewGameOptionDescriptor.Id, NewGameOptionDescriptor.LabelKey, NewGameOptionDescriptor.Kind, NewGameOptionDescriptor.Choices, NewGameOptionDescriptor.Default) in `src/Talt.Client/Screens/NewGameOptions.cs`
- [X] T027 [P] [US1] `Checkbox`, `Dropdown`, `TextInput` (numeric seed) widgets in `src/Talt.Client/Ui/Widgets/`
- [X] T028 [US1] Main menu screen (Load game disabled with tooltip) in `src/Talt.Client/Screens/MainMenuScreen.cs`
- [X] T029 [US1] New-game screen built from descriptors, background generation with "Generating…" in `src/Talt.Client/Screens/NewGameScreen.cs`
- [X] T030 [US1] Money display source from `src/Talt.Client/Ui/Data/company.json` and `MoneyFormatter` with currencies (data-model.md Currency.Code, Currency.Symbol, Currency.Rate, Currency.Decimals, Currency.NameKey) in `src/Talt.Client/Localization/MoneyFormatter.cs`, `src/Talt.Client/Localization/Data/currencies.json`
- [X] T031 [US1] In-game screen: top bar (money, date, speed buttons reflecting state, fast forward only when unlocked), toolbar (settings, close all, statistics, add/remove vehicle) and notifications in `src/Talt.Client/Screens/InGameScreen.cs`, `src/Talt.Client/Screens/Notifications.cs`
- [X] T032 [US1] Pause menu (pauses, restores previous speed; Esc closes front window first) in `src/Talt.Client/Screens/PauseMenu.cs`
- [X] T033 [US1] Rewire `GameWindow` to screens, router and bindings (keep every existing control) in `src/Talt.Client/GameWindow.cs`
- [X] T034 [P] [US1] Tests: money formatting and conversion, new-game descriptors → options in `tests/Talt.Client.Tests/Localization/MoneyFormatterTests.cs`, `tests/Talt.Client.Tests/Screens/NewGameOptionsTests.cs`
- [X] T035 [US1] Hand check by the user (quickstart 1–2); smoke test still passes with positional arguments

## Phase 4: User Story 2 — Hovering and clicking on the map (P1)

**Goal**: exact hover highlight and click information windows for tile, road and vehicle.

**Independent test**: quickstart step 3.

- [X] T036 [P] [US2] Ray from cursor and inverse view-projection in `src/Talt.Client/Picking/Ray.cs`
- [X] T037 [P] [US2] `PickResult`, `IPickLayer`, `PickContext` (data-model.md Pick result) in `src/Talt.Client/Picking/PickResult.cs`, `src/Talt.Client/Picking/IPickLayer.cs`
- [X] T038 [US2] Terrain DDA layer, road layer and vehicle box layer in `src/Talt.Client/Picking/TerrainPickLayer.cs`, `src/Talt.Client/Picking/RoadPickLayer.cs`, `src/Talt.Client/Picking/VehiclePickLayer.cs`
- [X] T039 [US2] `MapPicker` (priority, then distance; registration for later kinds) in `src/Talt.Client/Picking/MapPicker.cs`
- [X] T040 [US2] Highlight wire boxes: `src/Talt.Client/Rendering/Shaders/highlight.wgsl`, `src/Talt.Client/Rendering/HighlightRenderer.cs`, drawn in the world pass
- [X] T041 [US2] Object information window, one per object (data-model.md UiWindow.Key) in `src/Talt.Client/Screens/ObjectInfoWindow.cs`
- [X] T042 [US2] Picking per frame when the cursor is over the map, click vs drag, selection kept until Esc in `src/Talt.Client/Screens/InGameScreen.cs`
- [X] T043 [P] [US2] Tests: ray/terrain on flat, slope and edited terrain at near and far distances, vehicle priority over road and tile, sky gives none in `tests/Talt.Client.Tests/Picking/PickingTests.cs`
- [X] T044 [US2] Hand check by the user (quickstart 3)

## Phase 5: User Story 3 — Windows and tooltips (P1)

**Goal**: move, stack, pin, close, close all; tooltips everywhere.

**Independent test**: quickstart step 4.

- [X] T045 [US3] `UiWindow` with title bar drag, close and pin buttons (data-model.md UiWindow.Position, UiWindow.Pinned, UiWindow.Closable) in `src/Talt.Client/Ui/Windows/UiWindow.cs`
- [X] T046 [US3] `WindowManager` (z-order, focus, cascade per kind, single instance, Key identity, close all unpinned, clamp to screen, 64-window limit; data-model.md UiWindow.Kind, UiWindow.SingleInstance, UiWindow.ZOrder) in `src/Talt.Client/Ui/Windows/WindowManager.cs`
- [X] T047 [P] [US3] `ListView`, `ScrollArea` (scissor clipping, wheel), `TabControl`, `Slider` widgets in `src/Talt.Client/Ui/Widgets/`
- [X] T048 [US3] Tooltip layer (delay from theme, shortcut from bindings, kept on screen) in `src/Talt.Client/Ui/UiContext.cs`
- [X] T049 [US3] `--ui-test-windows <n>` sample windows using every widget in `src/Talt.Client/Screens/SampleWindows.cs`
- [X] T050 [P] [US3] Tests: window manager behaviours and tooltip placement in `tests/Talt.Client.Tests/Ui/WindowManagerTests.cs`
- [X] T051 [US3] Hand check by the user (quickstart 4)

## Phase 6: User Story 4 — English and Spanish (P2)

**Goal**: complete, switchable localization with plurals and culture formatting.

**Independent test**: quickstart step 5 (language and currency).

- [X] T052 [US4] Plurals and live language switching in `src/Talt.Client/Localization/Strings.cs`; `UiContext.SetStrings` re-resolves labels
- [X] T053 [US4] Complete `src/Talt.Client/Localization/Strings.en.json` and `Strings.es.json` (all screens, actions, currencies, tooltips)
- [X] T054 [P] [US4] Tests: same keys in both languages, every key used in code exists, every screen's widget tree has no literal text in both languages, the longest Spanish and English texts fit their buttons in the default layout, plurals in `tests/Talt.Client.Tests/Localization/LocalizationCoverageTests.cs`, `tests/Talt.Client.Tests/Localization/PluralTests.cs`
- [X] T055 [US4] Hand check by the user (quickstart 5, language and currency)

## Phase 7: User Story 5 — Interface scale and display settings (P2)

**Goal**: settings window, scale 75–200 %, fullscreen, VSync, persistence.

**Independent test**: quickstart step 5 (scale, display) and restart.

- [X] T056 [US5] Extend `PlayerSettings` with robust per-property load and atomic save (data-model.md PlayerSettings.Language, PlayerSettings.Currency, PlayerSettings.UiScale, PlayerSettings.Fullscreen, PlayerSettings.WindowWidth, PlayerSettings.WindowHeight, PlayerSettings.VSync, PlayerSettings.KeyBindings, PlayerSettings.FastForwardUnlocked, PlayerSettings.Version; values that cannot be read are reported once as an on-screen notification) in `src/Talt.Client/PlayerSettings.cs`
- [X] T057 [US5] Settings window (language, currency, scale, fullscreen, VSync, fast-forward unlock, key bindings button; apply and save immediately) in `src/Talt.Client/Screens/SettingsWindow.cs`
- [X] T058 [US5] Present mode switch for VSync and fullscreen/window size handling in `src/Talt.Client/Rendering/WebGpuRenderer.cs`, `src/Talt.Client/GameWindow.cs`; suggested first-start scale
- [X] T059 [P] [US5] Tests: settings round trip, partial invalid file, old version file in `tests/Talt.Client.Tests/PlayerSettingsTests.cs`
- [X] T060 [US5] Hand check by the user (quickstart 5, scale and display)

## Phase 8: User Story 6 — Key bindings (P3)

**Goal**: rebind, conflicts, restore defaults, persistence.

**Independent test**: quickstart step 6.

- [X] T061 [US6] Key bindings window (list by category, capture next key, conflict prompt, restore defaults) in `src/Talt.Client/Screens/KeyBindingsWindow.cs`
- [X] T062 [P] [US6] Tests: overrides, conflicts, unknown ids/keys ignored, defaults equal today's controls in `tests/Talt.Client.Tests/Input/KeyBindingsTests.cs`
- [X] T063 [US6] Hand check by the user (quickstart 6)

## Phase 9: Polish & cross-cutting

- [X] T064 [P] `UiFrameBenchmarks` (HUD with 0 and 20 windows) and `PickingBenchmarks` (terrain DDA, 50 000 vehicles) in `benchmarks/Talt.Benchmarks/`
- [X] T065 UI time in the F3 statistics and measured 20-window cost on 4096 in `src/Talt.Client/GameWindow.cs`
- [X] T066 [P] Update `docs/ARCHITECTURE.md` (UI layer, picking, input, settings, numbers) and `docs/VISION.md` (currency and UI decisions)
- [X] T067 [P] Update the controls documentation in `README.md`
- [X] T068 Run the full test suite and the smoke test script `ci/smoke-client.ps1` locally
- [X] T069 Follow-up after feature 004 is merged into `main` (not in this branch): bind an `INewGameOptionsSource` to 004's map generation settings record (size, seed, roughness, maximum height, water, sea level) and show its `IProgress<T>` on the generating screen — record in `docs/ARCHITECTURE.md` as pending

## Dependencies & execution order

- Phase 1 → Phase 2 → US1 → (US2, US3 in any order) → US4 → US5 → US6 → Polish.
- US2 and US3 both need the in-game screen from US1; US3's window manager is used by US2's info
  windows, so US2 may use a minimal window until T046 lands.
- US4 needs the screens to exist to check their texts; US5's settings window needs US4's switching.

## Parallel examples

- Phase 2: T005, T006, T008, T017 in parallel; tests T019–T022 in parallel after their code.
- US2: T036 and T037 in parallel, then T038.
- Polish: T064, T066, T067 in parallel.

## Implementation strategy

MVP = Phases 1–3 (menu, HUD, pause). Then picking and windows (the other P1 stories), then
localization completeness, settings and bindings. The user checks each story by hand.
