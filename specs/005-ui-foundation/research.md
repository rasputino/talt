# Research: User Interface Foundation (005)

## R1 — UI technology

**Decision**: (a) a custom retained-mode UI library inside `Talt.Client` (`Talt.Client.Ui`), drawn
by one extra WebGPU pipeline on top of the world, with a signed-distance-field (SDF) font atlas.

**Rationale**:

- *Rendering through our renderer*: the UI becomes one textured, alpha-blended triangle list per
  frame (a few draw calls split only by scissor rectangles) appended as a second render pass to the
  frame `WebGpuRenderer` already builds. No second graphics context, no extra native library.
- *Look*: flat rounded panels are computed exactly in the fragment shader from a rectangle SDF, so
  corners stay smooth at every scale; the theme is data (FR-019). Full control of style is what the
  spec asks for ("clear, attractive, matches the voxel style").
- *Performance with many windows*: retained widgets keep their measured text layout; a frame only
  walks the tree and writes vertices into a pre-allocated array (no allocations, FR-029). Twenty
  windows of ten widgets are a few thousand quads: tens of microseconds of CPU, one upload.
- *Testability*: layout, input routing, the window manager and the draw-list builder are plain C#
  without GPU types, so xUnit covers them (constitution IV).
- *Maintenance and license*: our code, PolyForm Strict; only the font rasterizer is a dependency.

**Alternatives considered**:

- *(b) Dear ImGui via ImGui.NET*: quick to build tool windows, but immediate-mode styling is hard to
  make polished and localized layouts (long Spanish texts) are manual; it needs `cimgui` native
  binaries for all five CI platforms (win-x64, linux-x64, linux-arm64, osx-arm64) and a WebGPU
  backend we would have to write anyway. Kept as an option for future debug overlays only; not added
  now (no debug overlay needs it yet).
- *(c) Existing .NET UI libraries with custom backends*: Myra (MIT, needs MonoGame/FNA or a custom
  renderer, styling via its own XML), Avalonia (MIT, its own Skia compositor; embedding into a WebGPU
  swap chain is not supported), Noesis (commercial licence, native), RmlUi (C++, native bindings
  not maintained for .NET 10), Gum (MIT, tied to MonoGame/Skia). None renders through a plain
  WebGPU backend without a sizeable adapter, and each brings a styling model we would fight to get
  the requested look. Not clearly better than (a).

## R2 — Text rendering

**Decision**: Noto Sans Regular (SIL Open Font License 1.1, 569 KB) embedded as a resource; at
startup `StbTrueTypeSharp` 1.26.13 (MIT OR Unlicense, pure managed, no dependencies, 89 KB package,
targets net8.0/netstandard2.0) generates SDF bitmaps for a fixed glyph set into one 1024 × 1024
atlas. The shader reconstructs crisp edges at any size with `smoothstep` around the 0.5 level using
screen-space derivatives (`fwidth`).

**Rationale**: one atlas serves every UI scale (75–200 %, FR-024) and every font size without
re-rasterizing; SDF text stays sharp when scaled up to about 4× the base size. The glyph set is
ASCII, Latin-1 Supplement, Latin Extended-A, general punctuation (quotes, dashes, ellipsis) and the
currency symbols of the supported currencies (€ £ ¥ ₹ ₩ ₽ ₺ ₪ ₱ ₫), all verified present in the font
(FR-023). ≈ 420 glyphs at a 32 px base size with 6 px padding fit in 1024 × 1024. Kerning comes from
the font's kern/GPOS pair data via `stbtt_GetCodepointKernAdvance`.

**Alternatives considered**: plain coverage bitmap atlas per scale (simple, but needs re-baking on
every scale change and blurs between sizes); multi-channel SDF (sharper corners, but needs msdfgen,
a native tool); writing our own TrueType parser (≈600 lines of risky code for no gain); FreeType
(native library per platform). The bundled font is OFL: commercial distribution allowed if the
licence text ships with it and the font is not sold alone — `OFL.txt` is embedded next to it and
listed in the README's third-party section.

**Startup cost**: measured when implemented; target < 300 ms, generated in parallel per glyph.

## R3 — Icons

**Decision**: original pixel-art icons (16 × 16) defined as text grids in `Ui/Data/icons.json`
(one character per pixel, a small palette per icon), rasterized into the same atlas as RGBA. Drawn
with nearest filtering they look like voxel/pixel art at every integer-ish scale, matching the
clarify guidance ("voxel-styled icons").

**Alternatives considered**: font icons (the font lacks ▶ ✕ glyphs; icon fonts add another
licence), vector icons in code (harder to edit), PNG files (needs an image decoder dependency).

## R4 — Layout system

**Decision**: a two-pass measure/arrange layout over a widget tree, with a small set of containers:
`StackPanel` (vertical or horizontal, spacing, alignment), `Panel` (padding, background), `Window`
(title bar + content), `ScrollArea` (clips and offsets one child), `TabControl`. Sizes are in
*logical pixels*; the UI scale multiplies them at arrange time, and text uses the scaled font size.
Layout runs only when a widget is marked dirty (text, language, scale or window size changed).

**Rationale**: covers menus, HUD bars, forms and lists; easy to test (pure arithmetic); flexbox or
grid layout engines (Yoga, Taffy) would be native or large and are not needed for the widgets this
feature and the next ones need.

## R5 — Map picking

**Decision**: CPU ray casting. Each frame the cursor ray is built from the inverse view-projection;
the terrain is hit by a 2D DDA walk over tile columns of the `Heightmap` (exact for voxel columns:
side and top faces), vehicles are hit by ray/box tests against the same interpolated instances that
are drawn this frame, and roads are identified by the top voxel of the hit tile. Object kinds are
`IPickLayer` implementations with a priority (FR-011): vehicles (100) > infrastructure (50) >
terrain (0); later features register stations or buildings with their own shapes.

**Rationale**: the result is available in the same frame (SC-002) and exact at every zoom (the DDA
does not sample, it visits every column the ray crosses: at most ≈ 2 × map side ≈ 8 200 columns
on 4096, a few microseconds). The 50 000 vehicle box tests cost ≈ 0.1–0.2 ms on one core,
measured in the benchmark; they run only on frames where the cursor is over the map.

**Alternatives considered**: a GPU ID buffer (render object ids into an extra target and read the
pixel back). WebGPU readback is asynchronous (map-async), so the answer arrives one or two frames
late, which fails "within one frame"; it also adds a full extra pass of the 4096 world (the feature
003 budget is already tight on the integrated GPU) and a second copy of every pipeline. Rejected.

**Highlight**: a small line-list pipeline draws wire boxes (hover and selection) around the picked
object with the world depth buffer (`LessEqual`, boxes inflated by 2 %), in the world pass.

## R6 — Input routing

**Decision**: one `InputRouter` receives raw Silk.NET events and dispatches in this order:
1. *Captured target* (a widget or the world that received the mouse-down keeps the drag until
   mouse-up; FR-012a, edge case "drag released over a window").
2. *UI hit test* front to back (tooltip layer, modal, windows by z-order, HUD); the first widget that
   handles the event consumes it (FR-018).
3. *Focused text input* receives key and character events; shortcuts are suppressed.
4. *Key bindings* map key presses to actions (`InputAction`); held actions (camera pan/rotate) are
   polled per frame through the bindings.
5. *World*: camera drag/zoom and picking clicks. A click = press and release within 4 logical pixels.

**Rationale**: a single place decides who owns each event, which is what the tests exercise.

## R7 — Localization

**Decision**: keep `Strings` (JSON tables per language, embedded, English fallback) and extend it:
- plural forms as `key.one` / `key.other` chosen by a per-language rule (English and Spanish: one
  for |n| = 1, other otherwise), via `Strings.Plural(key, count, ...)`;
- money through `MoneyFormatter` using the language culture's number pattern, the chosen currency's
  symbol, decimals and fixed rate (`Data/currencies.json`);
- dates keep `FormatDate` (month names from the tables);
- live switching: the UI binds labels to keys (`TextRef`), and `UiContext.SetStrings` re-resolves
  every bound label and re-lays out.
The tests assert identical key sets in both languages, that every key used by the UI exists, and
walk every screen's widget tree in both languages to check no label is a literal (SC-003).

**Alternatives considered**: .resx resources (satellite assemblies, poor fit for data-driven files
and modding), gettext/ICU MessageFormat libraries (more than we need; plural rules of en/es are
trivial).

## R8 — Settings persistence

**Decision**: extend `PlayerSettings` (same file: `%AppData%/TALT/settings.json` on Windows,
`~/.config/TALT/settings.json` on Linux, `~/Library/Application Support/TALT` via
`SpecialFolder.ApplicationData` on macOS). Loading reads the file as a `JsonDocument` and takes each
property separately, so one invalid value falls back to its default without losing the rest (FR-026),
and reports what was ignored once. A `Version` field allows future migrations. Saving happens on every
change (clarify: apply immediately), written to a temporary file and moved over the old one so a
crash never leaves a half-written file.

## R9 — Key bindings

**Decision**: `Input/Data/actions.json` lists every action: id, label key, category, kind (press or
held) and up to two default keys (for example pan forward = W and Up). Player overrides are stored in
settings as `actionId → [keys]`; unknown actions or keys in the file are ignored with a log line.
Conflicts: binding a key already used offers to move it (the other action loses it).

## R10 — Screens, game start and coordination with feature 004

**Decision**: the window opens before any world exists and shows the main menu (FR-001). Starting a
game calls `GameSetup.Create(NewGameOptions)` (the code that today lives in `Program.cs`), on a
background task, while the new-game screen shows "Generating…"; when done the renderer receives the
world (`WebGpuRenderer.SetWorld`) and the in-game screen starts. Back to main menu releases the world.
When any game argument is given on the command line (positional map/vehicles/seed, `--record`,
`--print-fingerprint-at`, `--play`) the menu is skipped, so the CI smoke test is unchanged.

The new-game screen is built from `NewGameOptionDescriptor`s (label key, kind, range, default)
provided by an `INewGameOptionsSource`. This branch ships a source with map size and seed (what
`ScenarioSettings` supports today). Feature 004 adds a map generation settings record in
`Talt.Core`; after both are on `main`, a follow-up task binds a source to that record (size, seed,
roughness, maximum height, water, sea level) and shows 004's `IProgress<T>` in the generating
screen. `Program.cs` changes are kept to argument parsing and handing options to `GameWindow`.

## R11 — Display settings

**Decision**: fullscreen uses the Silk.NET window state (`WindowState.Fullscreen`); window size is
remembered. Vertical sync maps to the surface present mode: `Fifo` (on) or `Immediate`/`Mailbox` when
the surface supports it (off); the 2-frames-in-flight limit from 003 keeps the uncapped mode safe.
The suggested first-start scale is the framebuffer-to-window size ratio (OS scaling) rounded to the
nearest 25 %, or 125 %/150 % for 1440p/2160p monitors when the OS reports 1.

## R12 — Measuring UI frame cost

**Decision**: (1) BenchmarkDotNet `UiFrameBenchmarks` builds the draw list of the in-game HUD with 0
and 20 windows (CPU), and `PickingBenchmarks` measures the terrain DDA and the 50 000 vehicle test;
(2) in game, F3 statistics add "UI x.xx ms" (layout + draw list + upload + encode) and a
development option `--ui-test-windows 20` opens 20 windows to compare frame times on the 4096 map.
