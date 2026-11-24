# Implementation Plan: Towns

**Branch**: `008-towns` (worktree branch `claude/008-towns-6157e8`) | **Date**: 2026-11-24 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/008-towns/spec.md`

## Summary

Add towns to `Talt.Core` as a new deterministic system, `TownRegistry` (namespace `Talt.Core.Towns`),
owned by `GameSimulation` when the game has a map. At game creation every town site gets a town with a
generated original name (English, Spanish or mixed style, a new-game option) and is grown to an initial
size (Small/Normal/Large option) by the same growth code that runs later. Each month the registry
records passengers and mail generated and transported, moves every company's rating toward its
service target, and grows the served towns: buildings on free plots along the town's own roads,
larger buildings toward the centre of larger towns, replacements of small buildings, and new street
pieces following the town's grid or organic pattern when plots run out. Town buildings are tile
objects (`TileObjectKind.TownBuilding`, owner `Town(id)`), so feature 006's blocking and ownership
rules apply unchanged; the clear tool and road removal gain rating checks and penalties. Building
types, growth and rating balancing, size distribution and name parts are three embedded data files
validated at load. A debug scenario setting simulates service (`--town-service`) until stations
(010) report pickups through the same entry point.

The client draws buildings as instanced procedural boxes (walls with shader-drawn windows plus a roof
box) per 128-tile area with frustum/fog culling and a far LOD, shows name/population labels, adds a
town picking layer and the town window, and two options on the new-game screen. Benchmarks cover the
monthly update of 500 towns on a 4096 map.

## Technical Context

**Language/Version**: C# (latest) on .NET 10

**Primary Dependencies**: none new. `System.Text.Json` (in-box, `JsonDocument` like `roads.json`) for
data files; existing Silk.NET/WebGPU instanced box path for rendering.

**Storage**: embedded resources `Talt.Core.Towns.Data.town-buildings.json`, `towns.json` (growth,
ratings, sizes) and `town-names.json`; recorded sessions gain an optional `towns` settings object.

**Testing**: xUnit in `tests/Talt.Core.Tests/Towns` (data validation, names, founding, placement
validity, growth with and without service, parallel = sequential, ratings and construction
integration, fingerprints); client tests for labels, picking, window texts and localization; updated
map goldens; BenchmarkDotNet `TownBenchmarks`.

**Target Platform**: Windows, Linux x64/arm64, macOS x64/arm64; identical results on all.

**Project Type**: Desktop game (Core library + client + headless tool + benchmarks).

**Performance Goals**: monthly update of 500 towns < 50 ms total (SC-002), 0 B allocated in steady
state; game creation on a 4096 map with towns founded adds < 1.5 s; 60 FPS on the 4096 map with all
towns drawn (SC-004); tick budget unchanged (towns run only on month boundaries).

**Constraints**: integer-only state; per-town forked `DeterministicRandom` streams; parallel ticks
equal sequential; `Talt.Core` graphics-free; towns never change objects they do not own, never
terraform, never build bridges or tunnels; map-less fingerprints unchanged.

**Scale/Scope**: about 400–550 town sites on a 4096² map (feature 004: one per 25 000 land tiles),
roughly 50 000–150 000 buildings in total; 14 building types in 5 categories; up to 16 384² maps
(more towns, same per-town cost).

## Constitution Check

*Initial gate and post-design re-check: PASS. One justified design choice in Complexity Tracking.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I Core/Client separation | PASS | names, growth, ratings, statistics in `Talt.Core.Towns`; client renders, picks and formats text |
| II Performance | PASS | `TownBenchmarks` (monthly update 500 towns, founding), numbers in ARCHITECTURE.md; preallocated buffers, no per-month allocation; renderer culls per area with a far LOD |
| III Deterministic, data-oriented | PASS | SoA town and building arrays; per-town forked streams; growth phase sequential in town-id order, statistics/rating phase parallel per town and tested equal to sequential; changes only on the month event |
| IV Tested increments | PASS | tests per user story; regression goldens updated on purpose |
| V Readable C# | PASS | small types (`TownRegistry`, `TownBuildings`, `TownGrowth`, `TownStreets`, `TownNames`, `TownRules`) with XML docs |
| VI Original content | PASS | name parts and building models are invented for TALT; rating band names are our own |
| VII Data-driven | PASS | building types, growth/rating balancing, size distribution and name parts in validated JSON |
| VIII Living docs | PASS | ARCHITECTURE.md (Towns section, data flow, numbers), README options, VISION decisions |

## Project Structure

### Documentation (this feature)

```text
specs/008-towns/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── data-files.md      # town-buildings.json, towns.json, town-names.json
│   ├── towns-api.md       # TownRegistry public API (window, 010 entry points, construction hooks)
│   └── cli.md             # client/headless options and recorded-session settings
└── tasks.md
```

### Source Code (repository root)

```text
src/Talt.Core/
├── Towns/
│   ├── Data/town-buildings.json, towns.json, town-names.json
│   ├── TownRules.cs          # parsed towns.json + building types (validated)
│   ├── BuildingType.cs       # one building definition
│   ├── TownNames.cs          # name parts + deterministic unique generator
│   ├── TownSettings.cs       # NameStyle, TownSize, DebugServicePercent (scenario settings)
│   ├── TownBuildings.cs      # SoA building store, stable ids, swap-remove, tile → building
│   ├── TownRegistry.cs       # towns SoA, monthly update, ratings, statistics, hashing
│   ├── TownRegistry.Growth.cs    # plot search, type choice, replacement (sequential phase)
│   ├── TownRegistry.Streets.cs   # street extension (grid / organic) with network updates
│   ├── TownRegistry.Construction.cs # rating penalties and demolition from construction plans
│   ├── TownJson.cs           # strict JSON reading helpers
│   └── (TownInfo in TownRegistry.cs)   # read-only snapshot for the window
├── Construction/ (TileObjects: TownBuilding kind; ConstructionState.Towns hook; planner rating checks;
│                  construction.json demolish cost; ConstructionReasons.TownRefuses)
├── Roads/ (RoadPlanner removal rating check; RoadSeeder shares link helper with TownStreets)
├── Scenarios/ScenarioSettings.cs (Towns settings), Commands/RecordedSession.cs ("towns" object)
└── Simulation/GameSimulation.cs (create, month event, hashing)

src/Talt.Client/
├── Rendering/TownRenderer.cs, TownInstances.cs, Shaders/towns.wgsl
├── Screens/TownWindow.cs (+ ObjectInfoWindow branch), TownLabels.cs, NewGameOptions (2 options)
├── Picking/Picking.cs (TownPickLayer), GameSession/GameWindow wiring, Program options
└── Localization/Strings.en.json, Strings.es.json

tools/Talt.Headless/ (town options, town summary in `run`)
tests/Talt.Core.Tests/Towns/*, tests/Talt.Client.Tests/{Rendering,Screens,Picking}/Town*
benchmarks/Talt.Benchmarks/TownBenchmarks.cs
docs/ARCHITECTURE.md, README.md, docs/VISION.md
```

**Structure Decision**: one new Core folder `Towns/` beside `Industries/` and `Roads/`, following the
same SoA + data-file pattern; client additions mirror feature 009's industry rendering, picking and
window, and feature 007's per-area road renderer.

## Complexity Tracking

| Choice | Why Needed | Simpler Alternative Rejected Because |
|--------|------------|-------------------------------------|
| Monthly update split into a parallel per-town phase and a sequential growth phase | Growth writes shared tile, road and network state, so it must run in a fixed order; statistics and ratings are independent per town | All-parallel growth would need tile locking or conflict resolution and could differ from sequential runs; all-sequential wastes the easy parallel part (still within budget, so the parallel phase is kept small) |
| Actions that demolish town buildings clear the undo record | Undo restores tiles but not the town's building list | Teaching undo to restore buildings and ratings couples feature 006's generic record to towns for a rare case |
