# AGENTS.md

Guidance for AI coding agents working in this repository. `CLAUDE.md` imports this file.

TALT (Transit & Logistics Tycoon) is an original transport management game with a voxel art style
and very large maps, written in C#/.NET 10 with no game engine (Silk.NET windowing/input + WebGPU via
wgpu-native). Product decisions are in `docs/VISION.md`; the code structure is in
`docs/ARCHITECTURE.md`; the feature roadmap and Spec Kit prompts are in `docs/SPECKIT_GUIDE.md`.
The rules that override everything else are in `.specify/memory/constitution.md`.

## Working rules

- Never commit, push, open a PR or merge unless the developer explicitly asks (constitution,
  "Development Workflow").
- All documentation, code, comments, identifiers and commit messages are in English. Talk to the
  developer in Spanish.
- Player-facing text never goes in code: add keys to both `src/Talt.Client/Localization/Strings.en.json`
  and `Strings.es.json` (a test checks the two files have the same keys).
- Game content and balancing values belong in data files validated at load, not in code.
- No code, art, text or names from existing tycoon games; never use the trademark "Transport Tycoon".
  New dependencies must have a licence compatible with a closed commercial game and be justified in
  the feature's plan.
- Every feature that changes structure, data flow or performance updates `docs/ARCHITECTURE.md`.

## Commands

```
dotnet build                                   # warnings are errors (Directory.Build.props)
dotnet test                                    # all test projects
dotnet test tests/Talt.Core.Tests --filter "FullyQualifiedName~GoldenFingerprint"   # one class
dotnet test --filter "Category!=Gpu"           # what CI runs
dotnet run --project src/Talt.Client -c Release                   # main menu
dotnet run --project src/Talt.Client -c Release -- 16 20000       # straight into a 512² map, 20 000 vehicles
dotnet run --project src/Talt.Client -c Release -- --map 1024x512 --seed 7 --water 35
dotnet run --project tools/Talt.Headless -c Release -- run --ticks 10000
dotnet run --project tools/Talt.Headless -c Release -- replay session.json --checkpoint-every 1000
dotnet run --project benchmarks/Talt.Benchmarks -c Release -- --filter "*Tick*" --job short
pwsh ci/check-fingerprints.ps1                 # the cross-platform determinism gate, locally
```

The full client option list is in `README.md`. Useful when verifying changes: `--stats` (render
statistics), `--print-fingerprint-at <tick>`, `--exit-after-fingerprint`, `--record [file]`,
`--unlock-fast-forward` / `--lock-fast-forward` (these two are saved in the player's settings file,
so restore the setting after testing).

Synthetic key presses do not reach the GLFW window, so in-window behaviour (keys, menus, FPS) has to
be checked by the developer: launch the client for them and ask what the title bar or screen shows.
On Ubuntu 26.04 `dotnet run` of the client cannot find GLFW; a self-contained `dotnet publish` works.

## Architecture in brief

- **`Talt.Core`** holds all game logic and must never reference graphics, windowing, audio or
  platform code; everything in it is testable without a GPU. **`Talt.Client`** (window, input,
  rendering, UI) depends on Core. **`tools/Talt.Headless`** references only Core and runs or replays
  simulations from the command line.
- **Deterministic simulation.** `GameSimulation.Tick()` is the only thing that changes simulation
  state: it applies queued commands, advances systems, then fires day/month/year events. State is
  fixed-point integers only (1 tile = 65 536 units, 20 ticks/s, 40 ticks per in-game day); a
  reflection test (`NoFloatingPointStateTests`) fails if a `float`/`double`/vector field appears in
  a simulation type, so register new simulation types there. Randomness comes from forked
  `DeterministicRandom` streams; never the wall clock, `System.Random` or the UI's generator.
  Parallel ticks must equal sequential ones.
- **Player actions are commands** (`ISimulationCommand`: validate, apply, serialize) submitted to
  `GameSimulation` and applied at the start of the next tick. New commands need registering in
  `CommandSerializer` so recorded sessions (`.talt-session.json`) can replay them.
- **Fingerprints.** `ComputeFingerprint()` hashes all state (XxHash3). Golden values live in two
  places: `tests/Talt.Core.Tests/Determinism/GoldenFingerprintTests.cs` and
  `ci/golden-fingerprints.json` (plus `ci/sessions/windows-reference.json`, replayed on every CI
  platform). Change them only when simulation rules change on purpose, and say so in the commit
  message.
- **Real time vs ticks.** `SimulationRunner` (Core) converts the client's elapsed time into ticks:
  speeds ×1/×3/×8 change ticks per second, never tick size; fast forward spends up to 25 ms per frame
  and is locked until unlocked in `PlayerSettings`. The client snapshots vehicle poses before the
  last tick of a frame and interpolates them with `PoseInterpolator` (render-only floats).
- **Hot vehicle state** is a packed `VehicleState` struct array in `VehicleFleet` with stable ids and
  swap-with-last removal (own storage chosen over Arch ECS after benchmarking; see ARCHITECTURE.md).
- **World and rendering.** Voxels are bytes in 32³ chunks (`VoxelWorld`, sparse). Map generation
  (`Talt.Core.Maps`, staged and deterministic) keeps tile-level data separate from render voxels.
  The client meshes chunks with `GreedyMesher` into packed vertices, with LOD, frustum culling and a
  background `ChunkMeshScheduler`; `ChunkMesher` is the naive reference kept for tests.
- **Interface.** A retained-mode UI of its own (`Talt.Client/Ui`: widgets, windows, SDF font from
  bundled Noto Sans) with screens in `Screens/`, data-driven key bindings in `Input/`, and map
  picking (terrain, road, vehicle) in `Picking/`. `GameWindow` owns the lifecycle and routes input;
  `GameSetup` builds a game in the background; `GameSession` is a running game.
- Never call `_wgpu.Dispose()` on exit: it unloads wgpu-native while its threads still run and the
  process crashes.

## Performance and tests

- Hot paths (tick, frame) must not allocate. Features touching simulation or rendering add or update
  a BenchmarkDotNet benchmark and record numbers in `docs/ARCHITECTURE.md`; CI compares nightly
  benchmarks with `benchmarks/baselines/` (warnings only).
- Core logic gets xUnit tests with or before the code, and bug fixes get regression tests. Tests must
  not depend on a GPU, a window or timing; anything needing a GPU carries the trait `Category=Gpu`.
- CI (`.github/workflows/ci.yml`, scripts in `ci/`) builds and tests on Windows, Linux and macOS (x64
  and arm64) and runs the fingerprint gate; `main` requires the `ci-result` check.

## Spec Kit workflow

Features are built one per branch with Spec Kit: specify → clarify → plan → tasks → analyze →
implement, with artifacts in `specs/NNN-name/` (spec, plan, research, data model, contracts, tasks).
Each feature's prompts are in `docs/SPECKIT_GUIDE.md`. When several features are started at the same
time from the same branch, pass an explicit number to `.specify/scripts/powershell/create-new-feature.ps1`
(`-Number N`) so they do not pick the same one. Mark finished tasks `[X]` in `tasks.md`.
