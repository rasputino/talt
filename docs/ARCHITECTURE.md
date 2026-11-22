# Architecture

This document describes how the code is organized today. Update it whenever the structure changes.

## Solution layout

```
Talt.slnx
├── src/
│   ├── Talt.Core/          Game logic. No graphics, no window, no platform code.
│   │   ├── Voxels/         Chunk, ChunkCoord, VoxelWorld, VoxelMaterial
│   │   ├── Terrain/        ValueNoise, TerrainGenerator, Heightmap
│   │   ├── Meshing/        ChunkMesher, ChunkMesh, VoxelVertex, MaterialPalette
│   │   ├── Network/        TransportNetwork (+ builder), GridPoint, PathFinder (A*),
│   │   │                   GridNetworkGenerator, NetworkTerrainPainter
│   │   ├── Simulation/     GameSimulation, GameClock/GameDate, SimulationEvents, SimulationRunner,
│   │   │                   VehicleFleet, SyntheticTraffic, PoseInterpolator (render-only)
│   │   ├── Commands/       ISimulationCommand, CommandInbox, Add/RemoveVehicleCommand,
│   │   │                   RecordedSession, SessionRecorder, SessionReplayer, CommandSerializer
│   │   ├── Determinism/    FixedMath, DeterministicRandom (xoshiro256**), StateHasher (XxHash3)
│   │   └── Scenarios/      ScenarioSettings, ScenarioBuilder (shared by client and headless runner)
│   └── Talt.Client/        Executable: window, input, rendering, runs the simulation.
│       ├── GameWindow.cs   Window lifecycle, camera, speed keys, pose interpolation, title status
│       ├── PlayerSettings.cs  Persisted player settings (fast-forward unlock)
│       ├── Localization/   Minimal en/es string table (Strings.cs + JSON resources)
│       └── Rendering/      WebGpuRenderer, OrbitCamera, UnitCube, Shaders/*.wgsl
├── tools/
│   └── Talt.Headless/      Console runner: run and replay simulations without a window
├── tests/
│   ├── Talt.Core.Tests/    xUnit tests for Talt.Core (determinism, calendar, runner, commands...)
│   ├── Talt.Client.Tests/  String table tests
│   └── Talt.Headless.Tests/ Command-line parsing tests
└── benchmarks/
    └── Talt.Benchmarks/    BenchmarkDotNet: full tick, vehicle tick, pathfinding, SoA vs Arch ECS
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

`ChunkMesher` turns a chunk into triangles:

1. Copies the chunk plus a one-voxel border from neighbours into a padded 34³ buffer, so
   neighbour lookups are plain array reads and faces on chunk borders are culled correctly.
2. Emits a quad only where a solid voxel touches air (face culling).
3. Computes per-vertex **ambient occlusion** from the three voxels around each corner and splits
   each quad along its brighter diagonal to avoid artifacts.
4. Applies a deterministic ±5% brightness variation per voxel for the voxel art look.

Vertices are in world space (`VoxelVertex`: position, normal, color = 36 bytes). Chunks can be meshed
in parallel because meshing only reads the world.

Possible future optimizations: greedy meshing (merge coplanar faces), chunk-relative positions with
compressed vertices, LOD meshes for distant chunks.

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
- `WebGpuRenderer` owns all GPU objects: a terrain pipeline with one vertex + index buffer per
  chunk, a vehicle pipeline, and one uniform buffer (view-projection, light, camera, sky color)
  shared by both.
- `voxel.wgsl`: directional light plus sky light, baked ambient occlusion, distance fog.
- `vehicles.wgsl`: **instancing**. A single unit cube is drawn once per vehicle in one draw call;
  each instance carries position, heading and color, uploaded every frame.
- Colors are linear. The renderer prefers an sRGB surface format so the GPU converts them.
- GPU resources are released after the window loop has finished, never inside window events.

## Camera and controls

`OrbitCamera` orbits a target point on the ground. The target follows the terrain height and stays
inside the map, and the camera tilts up automatically so it never goes inside a hill.

| Input | Action |
|---|---|
| Left mouse drag | Rotate |
| Right mouse drag, WASD, arrows | Pan |
| Mouse wheel | Zoom |
| Q / E | Rotate |
| Space | Pause / resume |
| 1 / 2 / 3 | Speed ×1 / ×3 / ×8 |
| F | Fast forward on / off (only when unlocked: `--unlock-fast-forward`) |
| N | Add a vehicle to a random line |
| Delete | Remove the last vehicle you added |
| Esc | Quit |

The title bar shows the date, speed, effective speed, FPS, vehicles, average tick time and the
result of the last command, in English or Spanish (`--language en|es`).

## Current numbers (prototype, Windows, AMD Radeon integrated GPU)

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
- Linux and macOS: **not run yet**. Run `dotnet test` (golden fingerprints) and a headless `replay`
  there at least once; continuous integration (feature 002) will automate it.
- Keyboard controls (speeds, fast forward, pause, add/remove vehicle) are covered by unit tests of
  the runner and commands but still need a manual check in the window.
