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
  "visit stamp" avoids clearing them) and is not thread safe: one instance per thread. The heuristic
  of each node is computed once per search and cached (its integer square root was the costliest
  step), and the open set is a small binary heap ordered by (priority, node). Within one search those
  keys are unique, so the expansion order, and therefore every route, is fixed by the algorithm and
  not by the queue implementation.
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
Pairs are drawn in batches no larger than the number of lines still missing, so no batch draws a pair
the sequential loop would not have drawn and the random state afterwards is the same. The routes of
a batch are searched in parallel (one `PathFinder` per worker, each route written to its own slot)
and accepted in attempt order, so the fleet is identical with `parallel: false` (unit test).

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

Scenario setup speed-up (same machine, 2026-11-22). `ScenarioSetupBenchmarks` builds
the reference network and its 5 000 lines (10 000 A* routes) and 50 000 vehicles:

| Measurement | Before | After |
|---|---|---|
| `PathFinderBenchmarks` A* across the whole map (in-process job) | 2.49 ms per search | 1.68 ms per search |
| `ScenarioSetupBenchmarks.CreateSyntheticTraffic`, sequential | — | 22.6 s |
| `ScenarioSetupBenchmarks.CreateSyntheticTraffic`, parallel | — | 2.0 s (92 MB allocated) |
| `ScenarioSetupBenchmarks.GenerateNetwork` | — | 52 ms |
| Headless reference run, `setup-ms` | 38.6 s and 56.7 s (two runs) | 2.4–2.7 s |

Fingerprints are unchanged (`C0C656C5AB2D3302`, `C9F573FD7DE5CBE3`; `ci/check-fingerprints.ps1`
passes, replay included). Timings on this laptop vary a lot between runs (the same A* benchmark gave
1.27 ms in feature 001), so compare only numbers measured in the same session. Before and after the
change were measured back to back. The "before" setup benchmark could not run: BenchmarkDotNet
failed to build its boilerplate from a temporary folder, so the headless `setup-ms` is the "before"
reference.

No regression against the previous float prototype (constitution II): the integer simulation is as
fast or faster. The full tick allocates only what `Parallel.For` itself needs (~2–5 KB per tick,
independent of vehicle count).

**Headless reference run:** `Talt.Headless run --map 4096 --vehicles 50000 --ticks 10000` simulates
10 000 ticks in 2.7 s (0.27 ms per tick) with identical fingerprints in parallel and sequential mode.
Scenario setup takes ~2.5 s (it took 26–57 s before the synthetic lines' 10 000 A* routes were
searched in parallel with a faster A*); real games will build lines from player actions, and cached
hierarchical pathfinding (feature 007) will cut this further. Total run time stays under the 60 s target (SC-008).

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
- **Not covered:** macOS Intel (x64) and any hardware with a real GPU. A downloaded build has to be
  started by hand on Linux and macOS at least once.
- **Merge blocking (gap):** branch protection and rulesets are not available for private repositories
  on the GitHub Free plan (the API answers 403, checked 2026-11-22), so GitHub cannot refuse a merge
  by itself. The checks and the single `ci-result` check are published anyway; merge only when it is
  green. Making the repository public or upgrading the plan lets you require `ci-result`.
- **Benchmark noise:** two runs of the same code on `ubuntu-latest` differed by up to 13% on one
  benchmark (3 rows flagged out of 12 with no code change), which is why regressions only warn and
  benchmarks do not run on every push. Treat a single flagged row as a hint and repeat the run.
- **Verified on GitHub (2026-11-22):** the `publish` jobs produce self-contained builds that pass
  `verify-publish.ps1` for win-x64, linux-x64 and osx-arm64 (about 35-40 MB each), and the release
  step replaces the prerelease assets. Starting the downloaded builds by hand on Linux and macOS is
  still to be done by a person with a display.
