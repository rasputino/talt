# Architecture

This document describes how the code is organized today. Update it whenever the structure changes.

## Solution layout

```
Talt.slnx
├── src/
│   ├── Talt.Core/          Game logic. No graphics, no window, no platform code.
│   │   ├── Voxels/         Chunk, ChunkCoord, RegionCoord, VoxelWorld, VoxelMaterial
│   │   ├── Terrain/        ValueNoise, TerrainGenerator, Heightmap, TerrainEditor (debug edits)
│   │   ├── Meshing/        GreedyMesher, LodVolume, PackedVertex, PackedMesh, FaceDirections,
│   │   │                   MaterialPalette; ChunkMesher/ChunkMesh/VoxelVertex (naive reference)
│   │   ├── Network/        TransportNetwork (+ builder), GridPoint, PathFinder (A*),
│   │   │                   GridNetworkGenerator, NetworkTerrainPainter
│   │   ├── Simulation/     GameSimulation, GameClock/GameDate, SimulationEvents, SimulationRunner,
│   │   │                   VehicleFleet, SyntheticTraffic, PoseInterpolator (render-only)
│   │   ├── Commands/       ISimulationCommand, CommandInbox, Add/RemoveVehicleCommand,
│   │   │                   RecordedSession, SessionRecorder, SessionReplayer, CommandSerializer
│   │   ├── Determinism/    FixedMath, DeterministicRandom (xoshiro256**), StateHasher (XxHash3)
│   │   └── Scenarios/      ScenarioSettings, ScenarioBuilder (shared by client and headless runner)
│   └── Talt.Client/        Executable: window, input, rendering, interface, runs the simulation.
│       ├── GameWindow.cs   Window lifecycle, screens, input routing, settings, UI pass, title status
│       ├── GameSetup.cs    Builds simulation + world + heightmap from NewGameOptions (background task)
│       ├── GameSession.cs  A running game: runner, pose interpolation, vehicle instances, commands
│       ├── PlayerSettings.cs  Persisted settings (language, currency, scale, display, bindings...)
│       ├── DataFiles.cs    Embedded JSON data files with validation errors
│       ├── Ui/             Retained-mode interface: UiContext, widgets, windows, SDF font, icons, theme
│       ├── Screens/        Main menu, new game, in-game HUD and pause menu, settings, key bindings,
│       │                   object information and sample windows; INewGameOptionsSource
│       ├── Input/          KeyBindings (data-driven actions), InputRouter
│       ├── Picking/        Ray, MapPicker and pick layers (terrain, road, vehicle)
│       ├── Localization/   Strings (en/es, plurals), MoneyFormatter and currencies
│       └── Rendering/      WebGpuRenderer, WorldRenderer, UiRenderer, HighlightRenderer,
│                           ChunkMeshScheduler, Frustum, LodSelector, RenderStatistics, OrbitCamera,
│                           UnitCube, Shaders/*.wgsl
├── tools/
│   └── Talt.Headless/      Console runner: run and replay simulations without a window
├── tests/
│   ├── Talt.Core.Tests/    xUnit tests for Talt.Core (determinism, calendar, runner, commands...)
│   ├── Talt.Client.Tests/  Strings and localization coverage, money, layout, draw list, input
│   │                       routing, windows, tooltips, key bindings, settings, picking, screens,
│   │                       frustum, LOD selection, camera, frame-time tests
│   └── Talt.Headless.Tests/ Command-line parsing tests
└── benchmarks/
    └── Talt.Benchmarks/    BenchmarkDotNet: full tick, vehicle tick, pathfinding, SoA vs Arch ECS,
                            chunk meshing (naive vs greedy, LOD), UI frame cost, picking
```

**Rule:** `Talt.Core` must never reference graphics or windowing libraries. Everything in it must be
testable without a GPU. The client depends on the core, never the other way around.

## Build configuration

- `Directory.Build.props`: shared settings for every project (.NET 10, nullable, warnings as errors,
  recommended analyzers).
- `Directory.Packages.props`: NuGet versions are managed centrally. Projects reference packages
  without a version.

## World model

- The world is made of **voxels**, one byte each (`VoxelMaterial`).
- Voxels are grouped into **chunks** of 32 × 32 × 32, stored as a flat array
  (`index = x + z * 32 + y * 32²`). Y is the vertical axis.
- `VoxelWorld` is a sparse dictionary of chunks. Missing chunks are air.
- `ChunkCoord.FromVoxel` uses floor division, so negative coordinates work.

## Terrain generation

`TerrainGenerator` builds a heightmap from fractal value noise (`ValueNoise`) and fills columns:
grass, snow or sand on top, dirt below, stone underneath, water up to the water level.
Same seed, same terrain. This is prototype quality and will be replaced by a proper map generator.

## Meshing

Feature 003 replaced the naive mesher in the game with `GreedyMesher`
(`specs/003-large-map-rendering/research.md` R1–R5). `ChunkMesher` stays as the reference that the
tests and benchmarks compare against.

### Reference mesher (`ChunkMesher`)

`ChunkMesher` turns a chunk into triangles:

1. Copies the chunk plus a one-voxel border from neighbours into a padded 34³ buffer, so
   neighbour lookups are plain array reads and faces on chunk borders are culled correctly.
2. Emits a quad only where a solid voxel touches air (face culling).
3. Computes per-vertex **ambient occlusion** from the three voxels around each corner and splits
   each quad along its brighter diagonal to avoid artifacts.
4. Applies a deterministic ±5% brightness variation per voxel for the voxel art look.

Vertices are in world space (`VoxelVertex`: position, normal, color = 36 bytes), plus 32-bit indices:
168 bytes per face.

### Greedy mesher (`GreedyMesher`)

- **Same surface, fewer quads.** Faces exist exactly where the reference mesher puts them, with the
  same AO and diagonal rule, except faces pointing down at the bottom of the world (below the world
  is a marker that hides faces without darkening AO). Per face direction and slice, a 2D mask of
  faces keyed by material + four corner AO levels is merged into rectangles, but only where linear
  AO interpolation stays exact (uniform AO: both directions; AO constant along one axis: that axis
  only). Tests check that greedy and naive cover the same unit faces with the same AO, and that the
  naive packed mesh equals the reference mesher face for face.
- **Colour variation in the shader.** The ±5 % per-voxel variation (`MaterialPalette.Variation`) is
  recomputed in `voxel.wgsl` from the voxel under each fragment with the same integer hash, so it
  does not stop faces from merging.
- **Packed vertices.** `PackedVertex` is 8 bytes: position relative to the region origin (8 bits
  per axis), face direction, material, AO level. All quads use the index pattern 0-1-2 / 0-2-3 (the
  mesher rotates the corners to pick the diagonal), so one shared index buffer serves every draw:
  32 bytes per quad instead of 168.
- **Levels of detail.** `LodVolume` downsamples a 128³ region by 2, 4 or 8: a block is solid when at
  least half its voxels are; surface blocks take the material of the real surface, and roads win
  when at least half a block width of columns is road, so the road network stays continuous. Outside
  the region is air, so simplified meshes carry their own side walls ("skirts") that close cracks to
  neighbours drawn at another level.
- **Threading.** Scratch buffers are per thread; meshing only reads the world. Voxel writes inside
  existing chunks while meshing only make a mesh stale (it is rebuilt); creating chunks is not
  allowed while the renderer runs (see `VoxelWorld`).

## Transport network

- `TransportNetwork` is an immutable directed graph. Nodes are junctions or stations with a ground
  position (`GridPoint`, integer units); edges are one-way straight segments whose lengths are
  computed once with an exact integer square root. A two-way road is two edges.
- Adjacency is stored in compressed sparse row (CSR) form: flat arrays, no per-node objects, so
  traversals are cache friendly and allocation free. `TransportNetworkBuilder` assembles it.
- `PathFinder` runs A* with a straight-line heuristic. It reuses its buffers between searches (a
  "visit stamp" avoids clearing them) and is not thread safe: one instance per thread.
- `GridNetworkGenerator` builds a synthetic road grid with some roads removed, for tests,
  benchmarks and the prototype. `NetworkTerrainPainter` turns the top voxel under each road into
  road material so the terrain mesher draws it.

## Simulation loop

`GameSimulation` owns all simulated state and advances it with `Tick()`, the only method that
changes state after creation. Each tick:

1. applies the queued player **commands** in submission order (validate → apply or reject);
2. advances the systems (today, `VehicleFleet`);
3. increments `CurrentTick`;
4. fires **day, month and year events** (in that order) when a boundary was crossed.

- **Time:** 40 ticks per in-game day, 20 ticks per second at normal speed (one day ≈ 2 s).
  `GameClock` derives a Gregorian `GameDate` from the tick counter and the start year (default 1950).
- **Runner:** `SimulationRunner` (in Core, fed with real elapsed time by the client) decides how many
  ticks to run per frame. Standard speeds ×1, ×3 and ×8 multiply ticks per second, never the tick
  size. After a long frame, catch-up is capped at 0.25 s of normal-speed time per frame, so the game
  never freezes. Fast forward runs ticks until a 25 ms budget per frame is spent; it is **locked by
  default**, unlocked through `PlayerSettings`, and toggled with its own key.
- **Threading:** the simulation runs on the main thread; each tick uses worker threads internally
  for vehicle batches. A dedicated simulation thread is deferred until profiling needs it.
- **Smooth motion:** the client snapshots vehicle poses right before the last tick of each frame and
  interpolates previous → current with `PoseInterpolator` (rendering-only floats). Slots whose
  vehicle changed between snapshots are drawn without interpolation.

## Determinism

The same settings and the same commands produce the same state at every tick, on every platform.

- **No floating point in simulation state** (`FixedMath`): distances in units (1 tile = 65 536),
  speeds in units per tick, accelerations in units per tick². A reflection test fails if a `float`,
  `double` or vector field appears in simulation types. Floats are allowed only for rendering and
  for one-off conversion of constants.
- **Randomness:** `DeterministicRandom` (xoshiro256**, seeded with SplitMix64). Systems use forked
  streams; the UI never uses the simulation's generator.
- **Commands:** player actions are `ISimulationCommand` records applied at the start of the next
  tick. First commands: `AddVehicleCommand`, `RemoveVehicleCommand`.
- **Fingerprint:** `GameSimulation.ComputeFingerprint()` hashes the whole state with XxHash3.
- **Recorded sessions:** `SessionRecorder` writes settings + commands + checkpoints as JSON
  (`.talt-session.json`); `SessionReplayer` replays them and reports the first diverging tick.
- **Golden fingerprints:** `GoldenFingerprintTests` pins the fingerprints of a fixed scenario,
  generated on Windows x64. Linux and macOS must produce exactly the same values. Regenerate only
  when simulation rules change on purpose and say so in the commit message.

## Headless runner

`tools/Talt.Headless` references only `Talt.Core`:

```
dotnet run -c Release --project tools/Talt.Headless -- run --map 4096 --vehicles 50000 --ticks 10000
dotnet run -c Release --project tools/Talt.Headless -- run --ticks 10000 --record session.json
dotnet run -c Release --project tools/Talt.Headless -- replay session.json --checkpoint-every 1000
```

It prints stable `key: value` lines (`date`, `fingerprint`, `avg-tick-ms`, ...). Exit codes: 0 success,
1 determinism failure, 2 invalid arguments or file, 3 unexpected error.

## Vehicle simulation

`VehicleFleet` simulates vehicles running on **lines**. A line is a closed loop of edges with two
stops: the start of the route and the start of the way back. Vehicles accelerate, cruise, brake so
they stop exactly at the next stop (the remaining distance to each stop is precomputed per line
edge), wait three seconds and continue.

Design choices for performance:

- **Structure of arrays:** one integer array per field (id, line, route index, distance, speed,
  max speed, dwell ticks) instead of one object per vehicle. Vehicles have stable ids; removal moves
  the last vehicle into the free slot.
- **No allocations per tick:** verified by a unit test and by the benchmark's memory diagnoser
  (only `Parallel.For` itself allocates a few kilobytes per tick, independent of vehicle count).
- **Parallel batches:** vehicles are updated in batches of 4096 with `Parallel.For`. Parallel and
  sequential ticks give identical results (unit test).

**Not modeled yet:** vehicles do not interact. They overlap on roads, ignore each other at
junctions and there are no signals or blocks. Interactions are the next big step and the main
threat to parallelism, because neighbouring vehicles depend on each other.

`SyntheticTraffic` creates random lines between junctions and spreads vehicles evenly along them.

## Benchmarks

`benchmarks/Talt.Benchmarks` uses BenchmarkDotNet on a 4096 × 4096 tile network
(257 × 257 junctions, one every 16 tiles, ~220 000 one-way edges, 2 000 lines).

```
dotnet run -c Release --project benchmarks/Talt.Benchmarks -- --filter * --job short
```

Results on the development machine (AMD Ryzen 7 160, 8 cores / 16 threads), feature 001:

| Benchmark (short job) | Sequential | Parallel | Before 001 (parallel) |
|---|---|---|---|
| Full `GameSimulation.Tick`, 10 000 vehicles | 0.16 ms | 0.09 ms | — |
| Full `GameSimulation.Tick`, 50 000 vehicles (reference load) | 0.96 ms | 0.19 ms | — |
| Full `GameSimulation.Tick`, 100 000 vehicles | 2.8 ms | 0.33 ms | 0.35 ms (vehicles only) |
| Full `GameSimulation.Tick`, 1 000 000 vehicles | 23 ms | 5.5 ms | 6.0 ms (vehicles only) |
| A* across the whole map (random pairs) | 1.27 ms per search | — | — |

No regression against the previous float prototype (constitution II): the integer simulation is as
fast or faster. The full tick allocates only what `Parallel.For` itself needs (~2–5 KB per tick,
independent of vehicle count).

**Headless reference run:** `Talt.Headless run --map 4096 --vehicles 50000 --ticks 10000` simulates
10 000 ticks in 2.7 s (0.27 ms per tick) with identical fingerprints in parallel and sequential mode.
Scenario setup takes ~31 s, dominated by computing 5 000 long A* routes for the synthetic lines; real
games will build lines from player actions, and cached hierarchical pathfinding (feature 007) will cut
this further. Total run time stays under the 60 s target (SC-008).

### Entity storage decision (research R5)

The same vehicle tick was implemented on the Arch ECS (`ArchComparisonBenchmarks`, single-threaded,
default statistical job):

| Vehicles | Own storage | Arch ECS |
|---|---|---|
| 100 000 | 2.49 ms ± 0.31 | 2.05 ms ± 0.17 |
| 1 000 000 | 19.2 ms ± 2.2 | 18.4 ms ± 2.3 |

- The first comparison (seven separate arrays) showed Arch ~25 % faster. The cause was data layout:
  Arch keeps a vehicle's fields together. Packing the hot fields into one `VehicleState` struct
  closed most of the gap (the reference tick at 50 000 vehicles went from 0.26 to 0.19 ms).
- With 1 000 000 vehicles both are equal within noise; with 100 000 Arch is still ~16 % faster
  single-threaded.
- **Decision: keep our own storage** (flat arrays + packed hot structs) for now, because the game runs
  the tick in parallel (5× faster than sequential, measured, deterministic and allocation-free),
  Arch's parallel queries need its job scheduler and were not measured, and the single-threaded gap
  does not justify a new dependency in `Talt.Core`. **Pattern for later features:** one dense array
  of packed hot-state structs per system, plus separate arrays for cold data.
- Re-evaluate with a parallel Arch benchmark when vehicle interactions arrive (feature 012).

## Rendering

- **Silk.NET** creates the window (GLFW) and provides the WebGPU bindings.
- **WebGPU** through wgpu-native, which uses Vulkan, Metal or DirectX 12 depending on the platform.
  On the development machine (Windows, AMD) it runs on Vulkan.
- `WebGpuRenderer` owns the device, the terrain and vehicle pipelines and one uniform buffer
  (view-projection, light, camera, sky colour, material palette) shared by both, and runs the frame.
- `voxel.wgsl`: unpacks `PackedVertex`, adds the region origin (per-instance attribute), applies
  palette colour × per-voxel variation × interpolated AO, then directional light plus sky light and
  distance fog (unchanged look).

### World rendering (feature 003)

- **Regions.** `WorldRenderer` groups chunks into regions of 4 × 4 × 4 chunks (128³ voxels): 1 024
  regions on a 4096 map, 16 on a 512 map. Each region has one GPU vertex buffer per level of detail;
  each visible region is one `DrawIndexed` (`firstInstance` = region slot selects its origin).
- **Full-detail buffers** are the concatenation of the region's chunk meshes. When chunks change, a
  new buffer is assembled on the GPU: unchanged chunk ranges are copied buffer-to-buffer from the
  old one and only rebuilt chunks are uploaded, so no mesh copy stays in main memory.
- **Culling** per frame on the CPU: regions beyond the full-fog distance are skipped (they would be
  drawn in sky colour), then a frustum test on the region box and on the mesh bounds.
- **Level of detail** per region from the screen-space error of its nearest point
  (`LodSelector`: the coarsest level whose cells stay under 2 px, 10 % hysteresis). While a level is
  not built yet the nearest built level is drawn (coarser first); a region with nothing built is
  not drawn (the map fills in at start).
- **Background meshing.** `ChunkMeshScheduler` runs dedicated below-normal-priority workers
  (processor count − 2). Each frame the render thread lists missing or stale meshes for regions
  within the fog distance, sorts them by distance (visible first; simplified levels first because
  they are cheap and fill the view), and keeps at most two jobs per worker in flight. Finished meshes
  are uploaded within 4 MB or 2 ms per frame. Nothing is meshed before the window opens.
- **Edits.** `NotifyVoxelsChanged` bumps the version of every chunk within one voxel of a change
  (`ChunkCoord.TouchedBy`) and of their regions' simplified levels; stale meshes are drawn until
  their replacement arrives. The client reports when an edit is on screen (PageUp/PageDown debug
  edits print "Edit visible after … ms").
- **Statistics.** F3 (or `--stats`) shows frame time average/worst, FPS, draw calls, triangles,
  regions per level, world GPU memory, pending mesh jobs and managed bytes allocated per frame by
  the draw path (0 in steady state) in the title bar and the console.
- `vehicles.wgsl`: **instancing**. A single unit cube is drawn once per vehicle in one draw call;
  each instance carries position, heading and color, uploaded every frame.
- Colors are linear. The renderer prefers an sRGB surface format so the GPU converts them.
- GPU resources are released after the window loop has finished, never inside window events.
- **At most two frames in flight.** Each frame is submitted with `wgpuQueueSubmitForIndex`, and before
  submitting a new frame the renderer waits (`wgpuDevicePoll`) for the frame two submissions back.
  Vertical sync alone does not throttle when the window is hidden behind others: during the first
  hand test of feature 003 the client ran at ~480 FPS, queued uploads faster than the integrated GPU
  finished them, and exhausted shared memory until wgpu-native aborted. Vehicle instances are also
  uploaded only in frames that are drawn (never while minimized).

## Camera and controls

`OrbitCamera` orbits a target point on the ground. The target follows the terrain height and stays
inside the map, and the camera tilts up automatically so it never goes inside a hill.

Default controls (feature 005 makes every key rebindable in Settings → Key bindings; the defaults
live in `src/Talt.Client/Input/Data/actions.json`):

| Input | Action |
|---|---|
| Left mouse drag on the map | Rotate |
| Right mouse drag, WASD, arrows | Pan |
| Mouse wheel, `=` / `-` | Zoom (the farthest zoom shows the whole map: up to 2 × the map side) |
| Q / E | Rotate |
| Left click on the map (no drag) | Select the object under the cursor and open its information window |
| Space | Pause / resume |
| 1 / 2 / 3 | Speed ×1 / ×3 / ×8 |
| F | Fast forward on / off (only when unlocked in Settings or with `--unlock-fast-forward`) |
| N | Add a vehicle to a random line |
| Delete | Remove the last vehicle you added |
| Shift+Delete | Close all windows except pinned ones |
| F10 | Settings |
| F3 | Render statistics in the title bar and console (on / off) |
| PageUp / PageDown | Raise / lower the tile under the camera target (debug edit; Shift: 32 × 32 area) |
| Esc | Close the front window; then clear the selection; then open / close the pause menu |

Options for measurements: `--stats` shows the statistics from the start, `--zoom <distance|max>` sets
the initial camera distance, `--ui-test-windows <n>` opens n sample windows.

The date, money and speed are in the top bar (feature 005); the window title shows the game name, or
the render statistics with `--stats`/F3, including the interface time per frame and the window count.

## User interface (feature 005)

The client draws its own interface on top of the world (research in `specs/005-ui-foundation/research.md`).

- **Framework (`Ui/`)**: a retained widget tree with two-pass layout (measure, arrange) in logical
  pixels multiplied by the interface scale (75–200 %). `UiContext` holds the layers drawn back to front
  (screen, overlay such as the pause menu, windows, popup such as an open drop-down, tooltip), hover,
  mouse capture, keyboard focus and the tooltip timer. Widgets: panel, stack, spacer, label, button,
  icon, check box, drop-down, text input, slider, scroll area, list, tabs, window. Widgets only take
  `TextRef` (a localization key, or a dynamic text composed from localized formats), never literals.
  `WindowManager` keeps z-order, cascades windows of the same kind, focuses an open window with the same
  key (one window per map object), pins, closes all unpinned, keeps title bars on screen, at most 64.
- **Drawing**: `UiContext.Draw` fills a `UiDrawList` (44-byte vertices, four per quad, a draw command
  per clip rectangle) with no allocations in steady frames. `UiRenderer` uploads it and draws it in a
  second render pass (`LoadOp.Load`, premultiplied alpha) with one pipeline and one 1024 × 1024 atlas:
  rounded rectangles are exact signed-distance shapes in the shader, text is an SDF font atlas generated
  at startup from the bundled Noto Sans (OFL) with StbTrueTypeSharp (395 glyphs in ~140 ms: Latin-1,
  Latin Extended-A, typography and currency symbols), icons are original 16 × 16 pixel art from
  `Ui/Data/icons.json`. Theme colours and sizes are in `Ui/Data/theme.json`.
- **Input (`Input/`)**: `InputRouter` gives each event to the owner of the current drag, then the
  interface (front to back), then the focused text input, then the key bindings, then the world (camera
  and picking). A press and release within 4 logical pixels on the map is a click; a drag that started
  on the map never clicks the interface.
- **Picking (`Picking/`)**: CPU ray from the cursor each frame. The terrain is a 2D walk over the tile
  columns the ray crosses (exact for voxel columns at any zoom), roads are tiles whose top voxel is road,
  and vehicles are ray/box tests against the instances drawn this frame, pre-filtered by their distance
  to the ray's ground footprint. `IPickLayer` lets later features add stations or buildings with a
  priority. Hits behind the terrain are ignored. `HighlightRenderer` outlines the hovered and selected
  objects with wire boxes in the world pass.
- **Screens (`Screens/`)**: the window opens on the main menu with no world; a new game is generated on
  a background task while the new-game screen shows "Generating…", then the renderer receives the world
  (`SetWorld`; `ClearWorld` when going back to the menu). Game arguments on the command line skip the
  menu, so the CI smoke test is unchanged.
- **Localization**: `Strings` (en/es tables, English fallback, `key.one`/`key.other` plurals, culture
  numbers and dates) and `MoneyFormatter` (21 display currencies with fixed rates from
  `Localization/Data/currencies.json`; the internal unit is one US dollar; default USD in English, EUR in
  Spanish). Until the finances feature exists, the top bar shows `startingCapital` from
  `Ui/Data/company.json`. Tests check both languages have the same keys, every key in the code exists,
  every text of every screen resolves in both languages and fixed-width labels fit.
- **Settings**: `PlayerSettings` reads each property on its own (invalid values fall back alone and are
  reported once), saves after every change through a temporary file, and keeps the feature 001 file.

**Pending follow-up (T069):** once feature 004 is on `main`, bind an `INewGameOptionsSource` to its map
generation settings record (size, seed, roughness, maximum height, water, sea level) and show its
`IProgress<T>` on the generating screen. `Program.cs` will need a small merge with 004.

**Hand test (developer, Windows, 2026-11-23):** main menu, settings in both languages (live switch, currency
following the language, settings kept after restarting), new game, top bar and toolbar, hover outlines,
one information window per tile, road and vehicle, pinning and closing all windows, tooltips with
shortcuts, pause menu and rebinding a key all work. Found and fixed during the test, each with a
regression test: the menu title and the Spanish subtitle were cut with an ellipsis; the menu background
was almost black because this driver stores clear colours without the sRGB conversion (menus now draw
their background as an interface rectangle); clicking the map selected nothing, because picking pauses
while a button is held and the click looked at the cursor on release instead of at the press.

### Interface cost (feature 005)

BenchmarkDotNet, short job, AMD Ryzen 7 160, 2026-11-23 (`UiFrameBenchmarks`, `PickingBenchmarks`):

| Benchmark | Time | Allocated |
|---|---|---|
| Interface frame (layout + draw list), in-game HUD, 0 windows | 0.002 ms | 0 B |
| Interface frame, HUD + 20 sample windows (every widget kind) | 0.076 ms | 0 B |
| Terrain pick, grazing ray across a 4096 map | 0.030 ms | 0 B |
| Full pick with 50 000 vehicles, ray straight down | 0.073 ms | 0 B |
| Full pick with 50 000 vehicles, default camera view | 0.076 ms (0.45 ms before the footprint rectangle test) | 0 B |

The vehicle test first rejects vehicles outside the bounding rectangle of the ray's ground footprint
with four comparisons combined with a non-short-circuit `&` (one well-predicted branch; with `||` the
random positions mispredicted and cost 0.2 ms), then measures the distance to the footprint, and only
then tests the box.

**In the client** (`--stats`, 4096 × 4096 map, 50 000 vehicles, normal speed, 1600 × 900, 2026-11-23),
"interface" = picking + layout + draw list + GPU upload and encoding:

| Case | FPS | Worst frame per second | Interface per frame |
|---|---|---|---|
| Cursor over the map, 0 windows | 60 | 17.5–18.7 ms | 0.17–0.20 ms |
| 20 sample windows open (`--ui-test-windows 20`) | 60 | 17.9–19.4 ms | 0.25–0.34 ms |

SC-001 (20 windows add less than 1 ms) and SC-005 (4096 at 60 FPS with the interface, 0 B allocated per
frame in steady state) hold. The main menu costs 0.1 ms per frame. The first in-game frame takes about
9 ms of interface time once (texts and layouts are created).

## Rendering performance (feature 003)

Windows, AMD Ryzen 7 160 with integrated Radeon GPU over Vulkan, 1600 × 900 window, vertical sync
(60 Hz), 50 000 vehicles at normal speed, Release build, 2026-11-22. Numbers from the client's render
statistics (`--stats`): FPS and worst frame over one second, world GPU memory = vertex, index and
origin buffers of the terrain.

| Map | Zoom (camera distance) | FPS | Worst frame | Draws | Triangles | Regions by LOD 0/1/2/3 | World memory |
|---|---|---|---|---|---|---|---|
| 512 × 512 (`-- 16`) | 350 (default) | 60 | 20 ms | 15 | 268 k | 15/0/0/0 | 4 MB |
| 512 × 512 | 2 000 (max) | 60 | 20 ms | 16 | 78 k | 0/16/0/0 | 1 MB |
| 2048 × 2048 (`-- 64`) | 350 | 60 | 22 ms | 21 | 386 k | 21/0/0/0 | 66 MB |
| 2048 × 2048 | 4 096 (max) | 60 | 21 ms | 256 | 366 k | 0/0/256/0 | 5 MB |
| 4096 × 4096 (`-- 128`) | 350 | 60 | 20 ms | 21 | 379 k | 21/0/0/0 | 96 MB |
| 4096 × 4096 | 1 000 | 60 | 21 ms | 94 | 1.6 M | 92/2/0/0 | 157 MB |
| 4096 × 4096 | 2 500 | 60 | 22 ms | 472 | 1.7 M | 0/313/159/0 | 56 MB |
| 4096 × 4096 | 4 500 | 60 | 21 ms | 906 | 1.3 M | 0/0/897/9 | 22 MB |
| 4096 × 4096 | 8 192 (max, whole map) | 60 | 22 ms | 1 023 | 509 k | 0/0/0/1 023 | 8 MB |

Before feature 003: 1024 map 4.8 M triangles at 36 FPS in fast forward, 2048 map 19.1 M triangles at
14 FPS, 4096 map blank window (6.4 GB of buffers, out of GPU memory).

**Hand test on the 4096 map** (developer, ~8 minutes of panning, rotating and zooming between the
closest and the farthest zoom, with edits and fast forward; console log of the statistics):

- Normal speed: 490 one-second samples, average FPS 56–60 in all of them (60 in 486). In 475 of them
  no frame exceeded 33 ms; 15 had one longer frame (34–56 ms, typically 36–42 ms), also while the
  camera was still and no meshing was pending, so they are not caused by mesh uploads. Up to
  2.75 M triangles and 430 draws at medium zoom; world memory peaked at 292 MB after visiting much of
  the map (all built levels are kept; no eviction yet). SC-001 is met on average frame rate; the
  occasional single long frame is a follow-up (see below).
- Edits (PageUp/PageDown): every single-tile edit was on screen after 29–34 ms (two frames; 1 or 4
  chunks rebuilt), well under the 100 ms target (SC-002). 32 × 32 area edits also showed within a few
  frames.
- Startup: the window opens right after terrain generation (~4.7 s) and the view fills in within
  ~3 s, nearest first. Creating the synthetic traffic before that still takes ~26–38 s (feature 001).
- Managed allocations of the draw path: 0 B per frame in steady state.

Meshing benchmark (`ChunkMeshingBenchmarks`, BenchmarkDotNet default job, terrain with roads, per
chunk of 32³ voxels):

| Benchmark | Mean | Allocated |
|---|---|---|
| Reference naive mesher (`ChunkMesher`) | 1.25 ms | 1 933 KB |
| Greedy mesher, full detail | 0.62 ms | 19 KB |
| Region LOD 1 (per chunk of the region) | 0.22 ms | 154 KB |
| Region LOD 2 | 0.12 ms | 20 KB |
| Region LOD 3 | 0.10 ms | 3 KB |

Memory per face: 168 bytes before (36-byte vertices + 32-bit indices) vs 32 bytes per merged quad now
(8-byte vertices, shared index buffer), and far fewer quads after merging.

**Long frames.** While statistics are shown, every frame over 33.4 ms prints where its time went:
`Long frame … ms: update (simulation, ticks), vehicles, acquire, prepare, encode, wait GPU, submit; GC
gen0/gen1/gen2 (pause)`. First diagnosis (2026-11-22), measured while two other Claude sessions were
running builds, tests and 4096 headless runs on the same machine: the render phases (prepare,
encode, wait GPU, submit) stayed under 1 ms almost always; the long frames came from the simulation
tick (normally ~1.6 ms, here 20–290 ms) and from building vehicle instances (normally ~2–4 ms, here
up to 85 ms), both `Parallel.For` work on the shared thread pool that is starved when other
processes load the CPU. No garbage collection happened in any long frame. The hand test above also
ran while another session was benchmarking, so the isolated 34–56 ms frames are most likely the same
CPU contention.

**Confirmed on an idle machine** (same day, no other session running, 4096 map, 50 000 vehicles,
default zoom, 226 one-second samples): 60 FPS in 222 samples and 56–59 in 4; worst frame ≤ 29 ms in
223 samples. Simulation ticks took ~1.5 ms and vehicle instances < 2 ms. The only three long frames
(~210 ms, about 200 s into the run) were spent entirely inside `wgpuSurfaceGetCurrentTexture`
(waiting for a swap-chain image from the driver/compositor) with everything else under 2 ms: a
presentation stall outside the game's code, not reproduced in the other runs. Conclusion: simulation
and drawing stay within the frame budget; long frames seen during testing came from other processes
loading the CPU, plus rare presentation stalls.

**Follow-ups:** evicting full-detail buffers of regions far from the camera if memory ever matters
(292 MB peak today); watch for presentation stalls on other machines (the long-frame log names them).

## Current numbers (prototype before feature 003, Windows, AMD Radeon integrated GPU)

| Map | Chunks | Generation | Meshing | Triangles | Vehicles | Tick | FPS |
|---|---|---|---|---|---|---|---|
| 512 × 512 | 256 | ~500 ms | ~200 ms | ~1.25 M | 20 000 | ~0.7 ms | 60 (VSync) |
| 1024 × 1024 | 1 024 | ~900 ms | ~600 ms | ~4.8 M | 50 000 | ~1.6 ms | 46–55 |

In the client the tick is slower than in the benchmark (1.6 ms instead of 0.19 ms at 50 000 vehicles)
because at normal speed ticks are 50 ms apart and worker threads go idle between them. Copying poses
for interpolation and building GPU instances run in parallel batches; the pose buffers are swapped
instead of copied when possible.

Known issues reported while testing the 2048 × 2048 map: panning is a bit jerky (about 16 M
terrain triangles are drawn every frame with no culling or level of detail yet).

## Verification status (feature 001)

- Windows x64: all tests pass; headless run and replay verified; the client's fingerprint at a given
  tick equals the headless one; the client exits cleanly.
- Linux x64 (2026-11-22, Ubuntu 26.04 under WSL 2, .NET SDK 10.0.404): all 151 tests pass,
  golden fingerprints included. Headless fingerprints equal the Windows ones (default scenario at
  5000 ticks `C9F573FD7DE5CBE3`, parallel and sequential; reference load at 10 000 ticks
  `C0C656C5AB2D3302`), and a session recorded on Windows replays on Linux with every checkpoint OK.
- Continuous integration (feature 002, GitHub Actions run of 2026-11-22; images ubuntu-26.04,
  windows-2025 and macos-26 arm64; .NET 10 SDK): on **Windows x64, Linux x64 and macOS arm64** all
  151 tests pass and the headless fingerprints equal the golden values (default scenario at 5000
  ticks `C9F573FD7DE5CBE3`, parallel and sequential; reference load `C0C656C5AB2D3302`), and the
  session recorded on Windows replays with every checkpoint OK. The whole matrix took under 3 minutes.
- ARM64 (same run, hosted `ubuntu-24.04-arm` and `windows-11-arm` runners, which are available to this
  private repository): **Linux arm64 and Windows arm64** also pass all 151 tests with identical
  fingerprints and replay. macOS Intel (x64) is not covered.
- Keyboard controls (speeds, fast forward, pause, add/remove vehicle): checked by hand in the window
  on Windows (2026-11-22, `-- 32 50000 2026 --unlock-fast-forward`), on top of the unit tests of the
  runner and commands.
- Fast-forward rate (SC-003, ≥ ×20 at ≥ 30 FPS with 50 000 vehicles): the client cannot draw the
  4096 × 4096 reference map yet. Its naive terrain mesh (38 million faces, about 6.4 GB of vertex
  and index buffers) runs the integrated reference GPU out of memory and the window stays blank.
  Measured by hand on Windows (2026-11-22, integrated AMD Radeon GPU over Vulkan, 50 000 vehicles):

  | Map | Terrain triangles | Fast forward | FPS | Result |
  |---|---|---|---|---|
  | 1024 × 1024 (`-- 32`) | 4.8 M | ×205 | 36 | passes |
  | 2048 × 2048 (`-- 64`) | 19.1 M | ×48 | 16 | limited by terrain drawing (14 FPS even at ×8) |
  | 4096 × 4096 (`-- 128`) | 76.1 M | — | — | blank window, out of GPU memory |

  The simulation meets SC-003 with margin. In fast forward the runner spends up to 25 ms of each
  frame simulating, so 30 FPS also needs drawing to take under about 8 ms per frame. The 2048 and
  4096 maps fail on drawing, not on simulation; those checks move to feature 003 (large-map
  rendering), whose budget is 2 GB of rendering memory.

  **Re-checked after feature 003** (2026-11-22, hand test, `-- 128 50000 --unlock-fast-forward
  --stats`): on the 4096 map fast forward ran at **×140–×228 effective speed at 35–36 FPS** while the
  camera moved (frames ~28 ms: up to 25 ms of simulation plus drawing). Seconds in which fast forward
  was switched on or off, or many regions were being loaded at once, dipped to ×34–×110 at 26–31 FPS.
  **SC-003 passes on the 4096 reference map.**

## Continuous integration

GitHub Actions (repository `rasputino/talt`, private). Workflows are thin; the checks are PowerShell
scripts in `ci/` that also run locally (`pwsh ci/check-fingerprints.ps1`).

| Workflow | When | What |
|---|---|---|
| `ci.yml` job `build-and-test` | every push and pull request | On `windows-latest`, `ubuntu-latest` (x64), `macos-latest`, `ubuntu-24.04-arm` and `windows-11-arm` (arm64): restore, build Release with warnings as errors, `dotnet test` (TRX files uploaded), then `ci/check-fingerprints.ps1` |
| `ci.yml` job `ci-result` | after the matrix | Single aggregate check; require this one in branch protection |
| `ci.yml` jobs `publish`, `nightly-release` | push to `main` | Self-contained builds of `src/Talt.Client` for win-x64, linux-x64, osx-arm64, each checked for the wgpu-native and GLFW native libraries (`ci/verify-publish.ps1`); uploaded as artifacts and attached to the prerelease `nightly` (replaced on every run, unsigned, testing only) |
| `benchmarks.yml` | manual and nightly | BenchmarkDotNet on `ubuntu-latest`, compared with `benchmarks/baselines/ubuntu-latest.json`; regressions above 10% are flagged in the run summary and as warnings but never fail the run |

- **Determinism gate:** `ci/golden-fingerprints.json` holds the expected fingerprints (default scenario
  at 5000 ticks, sequential and parallel; reference load 4096 map, 50 000 vehicles, 10 000 ticks).
  `ci/sessions/windows-reference.json` was recorded on Windows and is replayed on every platform, so
  every checkpoint must match. Change the golden values only when simulation rules change on purpose.
- **GPU:** runners have no GPU, so the client is only built. Any future GPU test must carry the trait
  `Category=Gpu`; CI excludes it.
- **Baselines:** updated by hand (see `benchmarks/baselines/README.md`), never by the workflow.
- **Client smoke test:** job `client-smoke` publishes the client for the runner's RID, starts the
  published executable (`ci/smoke-client.ps1`, option `--exit-after-fingerprint`), lets it simulate 20
  ticks and requires exit code 0. On Linux it uses a virtual display (xvfb) and the software Vulkan
  driver llvmpipe; on macOS the runner's Metal device. It passed on Linux x64, Linux arm64 and macOS
  arm64 with the same fingerprint as Windows (`1475835573081C3A`, tick 20 of `4 1000 1`), so the
  downloaded builds start there. It is informational (not part of `ci-result`). Windows runners have no
  usable GPU (the renderer crashes creating its pipeline), so Windows is checked by hand.
  Finding: on Ubuntu 26.04, `dotnet run` of the client (not self-contained) cannot find the GLFW
  library under `runtimes/`; the published build, which carries it next to the executable, works.
- **Not covered:** macOS Intel (x64), the Windows client under CI, and any real GPU (all rendering in
  CI is software or virtualised).
- **Merge blocking:** the repository is public (since 2026-11-22), so branch protection on `main`
  requires the `ci-result` check. The owner can still push directly (administrators are not
  enforced), which is how direct merges are done.
- **Benchmark noise:** two runs of the same code on `ubuntu-latest` with the quick job differed by up to
  13% on one benchmark (3 rows flagged out of 12 with no code change), which is why regressions only
  warn and benchmarks do not run on every push. The baseline was recorded with the precise `default`
  job (about 14 minutes); a later `default` run showed no regression over it. Treat a single flagged
  row as a hint and repeat the run.
- **Verified on GitHub (2026-11-22):** the `publish` jobs produce self-contained builds that pass
  `verify-publish.ps1` for win-x64, linux-x64 and osx-arm64 (about 35-40 MB each), and the release
  step replaces the prerelease `nightly` assets.
