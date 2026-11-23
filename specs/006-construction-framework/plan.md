# Implementation Plan: Construction and Terraforming Framework

**Branch**: `006-construction-framework` | **Date**: 2026-11-23 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/006-construction-framework/spec.md`

## Summary

Make the map part of the deterministic simulation and give it one way to change: construction
commands. `GameSimulation` gains a `ConstructionState` (the player's company balance, the mutable
tile data of feature 004, a tile object/owner layer and a one-level undo record) when the scenario
has a map. Every action is an `ISimulationCommand` built on a shared `ConstructionCommand` that
**plans** the action (tile changes, per-tile problems, total cost) with an allocation-free planner,
checks money, and applies the plan as one unit. Terraforming (raise, lower, level) uses a monotone
cascade that keeps the one-step slope rule; clearing removes trees and demolishes company objects;
undo restores the exact previous tiles and refunds. The client's `ToolController` calls the same
planner every frame for the ghost preview (green/red translucent tile boxes, cost and reason next to
the cursor), confirms on mouse release, and turns applied tile changes into voxel column rebuilds
that `ChunkMeshScheduler` re-meshes. Trees are drawn as instanced voxel boxes near the camera.
Commands apply while paused (research R1–R14).

## Technical Context

**Language/Version**: C# latest on .NET 10, nullable, warnings as errors

**Primary Dependencies**: existing only (Silk.NET 2.23 windowing/input/WebGPU, System.Text.Json,
xUnit, BenchmarkDotNet). No new packages.

**Storage**: embedded JSON data file `Talt.Core/Construction/Data/construction.json` (costs, starting
balance, undo window, limits) validated at load; recorded sessions gain an optional `map` setting
(map text of feature 004), format version unchanged.

**Testing**: xUnit in `Talt.Core.Tests/Construction` (planner rules, costs, cascade, water and
ownership, money, undo, serialization, replay determinism, golden fingerprint of a map scenario) and
`Talt.Client.Tests/Construction` (tool controller drag shapes, cancel, confirm, preview text);
BenchmarkDotNet `ConstructionBenchmarks` (500-tile previews) plus re-run of `ChunkMeshingBenchmarks`.

**Target Platform**: Windows x64, Linux x64/arm64, macOS arm64 (CI from feature 002)

**Project Type**: desktop game (client + core library + headless tool)

**Performance Goals**: preview of a 500-tile drag (plan + money check) < 1 ms, 0 B allocated
(SC-001, SC-007); 60 FPS while dragging on the 4096 reference map; terraform edit visible < 100 ms
(SC-005, existing edit path measured at ~30 ms).

**Constraints**: simulation state integers only (`NoFloatingPointStateTests`); parallel = sequential;
no per-tick/per-frame allocations; heights stay 1–127 so every column fits one 128-voxel region;
golden fingerprints of the existing map-less scenarios unchanged.

**Scale/Scope**: maps up to 4096 × 4096 in play (16 384 supported by the generator); actions up to
`maxTilesPerAction` (data, default 16 384 changed tiles); ~3 commands + undo, 4 tools, ~40 new texts.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | How |
|---|---|---|
| I. Core/Client separation | Pass | Planner, commands, rules, company, tile objects, undo and voxel column rebuild live in `Talt.Core` (no graphics). Tool controller, preview boxes, tree instances and HUD texts live in `Talt.Client`. |
| II. Performance | Pass | Allocation-free planner with reusable scratch (open-addressing tile set with generation stamps), previews re-planned only when input or state changes; instanced boxes for previews and trees; new `ConstructionBenchmarks`; meshing benchmark re-run after `VoxelWorld` becomes safe for concurrent chunk creation; numbers in `docs/ARCHITECTURE.md`. |
| III. Determinism | Pass | Map tile data becomes simulation state, changed only by commands inside `Tick()` or `ProcessPendingCommands()` (paused); integers only; planner order is fixed (row-major seeds, FIFO cascade, monotone fixpoint independent of order); state hashed into the fingerprint when a map exists; recorded sessions carry the map settings; golden for a map + construction scenario added, existing goldens unchanged. |
| IV. Tested increments | Pass | Core tests written with the planner/commands; each user story has independent tests and a quickstart hand check. |
| V. Readable C# | Pass | Small types per concern (rules, planner, plan, commands, company, objects, undo); unsafe code only in the new client renderer `BoxRenderer`. |
| VI. Original content | Pass | Rules designed from the spec (per-tile column heights, one-step rule), no code or data from other games; tree shapes are original. |
| VII. Data-driven | Pass | Costs, starting balance, undo window and limits in `construction.json`, validated with messages naming the field; tools' keys in `actions.json`; texts in `Strings.*.json`. |
| VIII. English docs, localizable text | Pass | Reasons are codes mapped to `construction.reason.*` keys in English and Spanish; `docs/ARCHITECTURE.md` and README controls updated. |

**Post-design re-check**: Pass. One documented deviation from the architecture notes (not the
constitution): `GameSimulation.ProcessPendingCommands()` changes state outside `Tick()` while paused
(R3); replays stay exact because commands are recorded at the tick they were processed at and are
processed before that tick's systems.

## Project Structure

### Documentation (this feature)

```text
specs/006-construction-framework/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── commands.md          # Construction commands, JSON form, reasons, validation rules
│   ├── construction-api.md  # Planner, plan, rules, state and client tool contracts
│   └── data-files.md        # construction.json, actions, strings, recorded session "map"
├── checklists/requirements.md
└── tasks.md                 # /speckit-tasks
```

### Source Code (repository root)

```text
src/Talt.Core/
├── Construction/                     # NEW
│   ├── Data/construction.json        # costs, starting balance, undo window, limits (embedded)
│   ├── ConstructionRules.cs          # load + validate the data file
│   ├── ConstructionState.cs          # company, map, objects, undo; hashing; change log
│   ├── Company.cs                    # balance (long, internal unit = 1 USD)
│   ├── TileObjects.cs                # TileObjectKind, TileOwner, dense object/owner layer
│   ├── ConstructionPlan.cs           # tile changes, problems, cost (reusable buffers)
│   ├── ConstructionPlanner.cs        # terraform cascade, clear/demolish planning, scratch set
│   ├── TileSet.cs                    # open-addressing tile index set with generation stamps
│   ├── ConstructionCommand.cs        # shared validate → money → apply → undo record
│   ├── TerraformCommand.cs, ClearAreaCommand.cs, UndoConstructionCommand.cs
│   ├── UndoRecord.cs
│   └── ConstructionReasons.cs        # stable reason codes
├── Maps/MapData.cs                   # internal mutators + flag recompute for edited tiles
├── Maps/WorldBuilder.cs              # RebuildColumn(map, world, x, z) for edits
├── Voxels/VoxelWorld.cs              # concurrent-safe chunk creation
├── Scenarios/ScenarioSettings.cs     # optional MapSettings Map
├── Simulation/GameSimulation.cs      # Construction, Create(settings, map), ProcessPendingCommands
├── Commands/CommandSerializer.cs     # register the new commands
├── Commands/RecordedSession.cs       # optional "map" setting
└── Terrain/TerrainEditor.cs          # REMOVED (debug edits replaced, FR-015)

src/Talt.Client/
├── Construction/                     # NEW
│   ├── ConstructionTool.cs           # tool definitions (id, icon, shape, command factory)
│   ├── ToolController.cs             # select, drag, cancel, confirm, preview planning
│   ├── PreviewBoxes.cs               # plan → coloured tile boxes (reused arrays)
│   └── TerrainSync.cs                # applied tile changes → voxels, heightmap, renderer
├── Rendering/BoxRenderer.cs + Shaders/boxes.wgsl   # NEW instanced boxes (trees opaque, preview translucent)
├── Rendering/TreeInstances.cs        # NEW trees near the camera from scenery data
├── Rendering/WorldRenderer.cs        # accept chunks created after setup
├── Rendering/WebGpuRenderer.cs       # draw trees and preview boxes
├── Screens/InGameScreen.cs           # tool buttons, cursor label, input while a tool is active, money from company
├── GameSetup.cs / GameSession.cs     # map → simulation; process commands while paused; terrain sync
├── GameWindow.cs                     # small: pass renderer hooks (preview, trees)
├── Input/Data/actions.json           # tool actions, undo; debug raise/lower removed
├── Localization/Strings.{en,es}.json # tools, tooltips, reasons, messages
└── Ui/Data/company.json + CompanyData   # REMOVED (starting balance moves to Core data)

tests/Talt.Core.Tests/Construction/   # NEW
tests/Talt.Client.Tests/Construction/ # NEW
benchmarks/Talt.Benchmarks/ConstructionBenchmarks.cs   # NEW
```

**Structure Decision**: the existing projects; one new folder per side (`Talt.Core/Construction`,
`Talt.Client/Construction`). The headless tool needs no change beyond what `GameSimulation.Create`
does with the scenario's optional map (replays of sessions recorded with a map regenerate it).

## Coordination with parallel features

- 009 (cargo and industries, Core) may also add state to `GameSimulation` and hashing; this feature
  keeps its additions in `ConstructionState` and touches `GameSimulation` in three small places
  (property, `Create` overload, `ProcessPendingCommands`). Industries can reuse `TileObjects` for
  their tiles (owner kind `Industry` is reserved).
- 019 (audio, client): no shared files expected beyond `GameWindow.cs` and the localization files;
  edits there stay small.

## Complexity Tracking

No constitution violations. Design choices that add complexity, justified in research: concurrent
chunk dictionary (R6, needed so raised columns can get a new chunk while meshing), open-addressing
scratch set (R8, needed for allocation-free previews on 4096 maps without a per-map scratch array).
