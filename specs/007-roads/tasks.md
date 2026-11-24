# Tasks: Roads (feature 007)

**Input**: `specs/007-roads/` (plan.md, spec.md, research.md R1–R12, data-model.md, contracts/)

Tests are required by the constitution (IV) and the plan prompt: every story has tests with or before
its code. Paths are relative to the repository root.

## Phase 1: Setup

- [X] T001 Create `src/Talt.Core/Roads/` and embed `Roads/Data/roads.json` (EmbeddedResource) in `src/Talt.Core/Talt.Core.csproj`
- [X] T002 [P] Rename `TileObjectKind.PublicRoad` → `Road` in `src/Talt.Core/Construction/TileObjects.cs`, `construction.json` key `road`, and update tests referencing it (data-model.md TileObjects)

## Phase 2: Foundational (blocks every story)

- [X] T003 Dir8 helpers (offsets, opposite, axis test, step length) in `src/Talt.Core/Roads/Dir8.cs` (data-model.md Dir8)
- [X] T004 [P] `RoadPiece`, `RoadFeatures` in `src/Talt.Core/Roads/RoadPiece.cs` (data-model.md RoadLayer.Flags, RoadPiece)
- [X] T005 `RoadLayer` dense arrays, piece get/set, changed-tile queue, hashing, `Validate()` invariants in `src/Talt.Core/Roads/RoadLayer.cs` (data-model.md RoadLayer.Links/Exits/Type/Flags/Span)
- [X] T006 [P] `roads.json` v1 values and `RoadRules`/`RoadType` parsing with field-named errors in `src/Talt.Core/Roads/RoadRules.cs`, `src/Talt.Core/Roads/Data/roads.json` (data-model.md RoadType.*, RoadRules.*)
- [X] T007 [P] Tests for roads.json validation in `tests/Talt.Core.Tests/Roads/RoadRulesTests.cs`
- [X] T008 Geometry checks (straight/ramp, diagonal sides and crossings, straight-only ends, motorway junction rule) as `TerrainAllows`/`ShapeAllows`/`EnterBlocker` in `src/Talt.Core/Roads/RoadPlanner.cs` (research R3); the seeder has its own copy of the ramp rule
- [X] T009 [P] Geometry rules are tested through the planner in `tests/Talt.Core.Tests/Roads/RoadSlopeTests.cs` and `BuildRoadTests.cs`
- [X] T010 Extend `TileChange` (OldRoad, NewRoad, RoadChanged) and `TileSnapshot` (Road) in `src/Talt.Core/Construction/ConstructionPlan.cs`, `UndoRecord.cs` (data-model.md TileChange, TileSnapshot)
- [X] T011 `ConstructionState`: own `RoadLayer`, `RoadRules`, `RoadNetwork`, `Year`, industries; Commit/ApplyUndo apply road pieces and update the network; hash roads; drop prototype public-road marking in `src/Talt.Core/Construction/ConstructionState.cs`
- [X] T012 Terraform guards (tiles under bridges/over tunnels, diagonal side tiles) and clear tool skipping road tiles (`use-road-removal`) in `src/Talt.Core/Construction/ConstructionPlanner.cs`; new reason codes in `ConstructionReasons.cs` (contracts/commands.md)
- [X] T013 `ScenarioBuilder`: empty network and fleet when the scenario has a map; `GameSimulation`: set `Construction.Year`, attach industries, seed town roads, build the network in `src/Talt.Core/Scenarios/ScenarioBuilder.cs`, `src/Talt.Core/Simulation/GameSimulation.cs` (research R1, R12)
- [X] T014 [P] `IndustryFleet` openings skip sites whose footprint has objects in `src/Talt.Core/Industries/IndustryFleet.cs`
- [X] T015 `RoadSeeder` town street grids (and debug full grid) in `src/Talt.Core/Roads/RoadSeeder.cs` (research R9)
- [X] T016 `RoadNetwork` build: nodes, chains, per-region CSR, canonical dump in `src/Talt.Core/Network/RoadNetwork.cs` (research R5, data-model.md RoadNetwork)
- [X] T017 `RoadNetwork.Update` incremental (affected tiles, chain walks before/after, dirty regions) in `src/Talt.Core/Network/RoadNetwork.cs`
- [X] T018 `RoadRouter` (A*, octile heuristic, ties by depth then node tile, version-invalidated cache) in `src/Talt.Core/Network/RoadRouter.cs` (research R6, data-model.md Route). It keeps its own small heap rather than sharing one with `PathFinder`, so the prototype path finder is untouched
- [X] T019 [P] Network tests: connectivity, one-way, loops, incremental = full rebuild over random edits in `tests/Talt.Core.Tests/Network/RoadNetworkTests.cs`
- [X] T020 [P] Router tests (shortest against a brute-force tile Dijkstra after random actions, unreachable pairs, cache) in `tests/Talt.Core.Tests/Network/RoadNetworkTests.cs`
- [X] T021 Register new simulation types in `tests/Talt.Core.Tests/Determinism/NoFloatingPointStateTests.cs`; seeding tests in `tests/Talt.Core.Tests/Determinism/RoadReplayTests.cs`

**Checkpoint**: map games start with town roads and a correct network; map-less goldens unchanged.

## Phase 3: User Story 1 — Build a road by dragging (P1) 🎯 MVP

**Independent test**: L-shaped drag on flat land → preview path and cost → build → money and route same tick.

- [X] T022 [US1] `RoadPlanner.PlanRoad` (weighted A*, states, costs, trees, reuse, upgrades, junction joins, auto-bridge hook) in `src/Talt.Core/Roads/RoadPlanner.cs` (research R4)
- [X] T023 [US1] `BuildRoadCommand` + serializer registration in `src/Talt.Core/Roads/RoadCommands.cs`, `src/Talt.Core/Commands/CommandSerializer.cs` (contracts/commands.md)
- [X] T024 [P] [US1] Planner/command tests (L path, junctions, trees, others' objects, industries, money, undo, route same tick, no allocation after warm-up) in `tests/Talt.Core.Tests/Roads/BuildRoadTests.cs`
- [X] T025 [US1] Client road tool (`Path` shape), `RoadToolOptions`, toolbar button and key in `src/Talt.Client/Construction/ConstructionTool.cs`, `ToolController.cs`, `src/Talt.Client/Screens/InGameScreen.cs`, `src/Talt.Client/Input/Data/actions.json`
- [X] T026 [US1] `RoadMesher` (flat, diagonal, junction hub, smoothed corners, markings) GPU-free in `src/Talt.Client/Rendering/RoadMesher.cs`; tests in `tests/Talt.Client.Tests/Rendering/RoadMesherTests.cs`
- [X] T027 [US1] `RoadRenderer` + `roads.wgsl` (blocks, detail/simplified, culling, rebuild on change) and hook in `WebGpuRenderer`/`GameSession` in `src/Talt.Client/Rendering/RoadRenderer.cs`, `Shaders/roads.wgsl`
- [X] T028 [US1] Remove voxel road painting from map games; road picking from `RoadLayer`; road info window (type, speed, owner, one-way); vehicle option and add/remove-vehicle keys do nothing on map games without lines (FR-061) in `src/Talt.Client/GameSetup.cs`, `GameSession.cs`, `Picking/Picking.cs`, `Screens/Windows.cs`
- [X] T029 [P] [US1] Strings en/es (tools, types, reasons, info window) in `src/Talt.Client/Localization/Strings.en.json`, `Strings.es.json`

## Phase 4: User Story 2 — Slopes and steep terrain (P1)

- [X] T030 [US2] Ramp handling in planner and seeder (arrival-direction states) in `src/Talt.Core/Roads/RoadPlanner.cs`
- [X] T031 [P] [US2] Tests: hill with gentle sides, cliff → too-steep, terraform under road refused in `tests/Talt.Core.Tests/Roads/RoadSlopeTests.cs`
- [X] T032 [US2] Ramp meshes with side wedges in `src/Talt.Client/Rendering/RoadMesher.cs`

## Phase 5: User Story 3 — Bridges and tunnels (P1)

- [X] T033 [US3] `PlanBridge`, `PlanTunnel`, auto-bridge over water in `PlanRoad` in `src/Talt.Core/Roads/RoadPlanner.cs` (research R8)
- [X] T034 [US3] `BuildBridgeCommand`, `BuildTunnelCommand` + serializer in `src/Talt.Core/Roads/RoadCommands.cs`
- [X] T035 [P] [US3] Tests: river bridge, too long by era, over a road (not connected), tunnel through hill, no exit, routes across in `tests/Talt.Core.Tests/Roads/BridgeTunnelTests.cs`
- [X] T036 [US3] Bridge deck/pillars/railings and tunnel portal meshes; bridge and tunnel tools (`Line` shape) in `src/Talt.Client/Rendering/RoadMesher.cs`, `src/Talt.Client/Construction/ConstructionTool.cs`

## Phase 6: User Story 4 — Remove roads (P2)

- [X] T037 [US4] `PlanRemoval` (own pieces, neighbours' links, whole bridges/tunnels, town connectivity rule) in `src/Talt.Core/Roads/RoadPlanner.cs` (research R9)
- [X] T038 [US4] `RemoveRoadCommand` + serializer; removal tool (`Line8`) in `src/Talt.Core/Roads/RoadCommands.cs`, `src/Talt.Client/Construction/ConstructionTool.cs`
- [X] T039 [P] [US4] Tests: gap breaks routes, town road kept/removed by connectivity, junction with town road, bridge removed whole in `tests/Talt.Core.Tests/Roads/RemoveRoadTests.cs`

## Phase 7: User Story 5 — Road types and motorways (P2)

- [X] T040 [US5] Type availability by year, upgrade cost difference, downgrade refusal in `src/Talt.Core/Roads/RoadPlanner.cs`
- [X] T041 [US5] `PlanMotorway` (two carriageways, continuation, elevated sections), `PlanConnector` in `src/Talt.Core/Roads/RoadPlanner.cs` (research R7)
- [X] T042 [US5] `BuildMotorwayCommand`, `BuildMotorwayConnectorCommand` + serializer in `src/Talt.Core/Roads/RoadCommands.cs`
- [X] T043 [P] [US5] Tests: types by year, upgrade, downgrade, motorway lanes and directions, junction refusal, connectors, elevated over roads in `tests/Talt.Core.Tests/Roads/RoadTypeMotorwayTests.cs`
- [X] T044 [US5] Type selector, motorway/exit/entry tools, motorway and connector meshes in `src/Talt.Client/Screens/InGameScreen.cs`, `src/Talt.Client/Construction/ConstructionTool.cs`, `src/Talt.Client/Rendering/RoadMesher.cs`

## Phase 8: User Story 6 — One-way roads (P3)

- [X] T045 [US6] `PlanOneWay`, `SetOneWayCommand` + serializer in `src/Talt.Core/Roads/RoadPlanner.cs`, `RoadCommands.cs`
- [X] T046 [P] [US6] Tests: cycle, routing with/against, junction refused in `tests/Talt.Core.Tests/Roads/OneWayTests.cs`
- [X] T047 [US6] One-way tool and arrow meshes in `src/Talt.Client/Construction/ConstructionTool.cs`, `src/Talt.Client/Rendering/RoadMesher.cs`

## Phase 9: Polish & cross-cutting

- [X] T048 Replay golden with every road command and regenerated map golden in `tests/Talt.Core.Tests/Determinism/RoadReplayTests.cs`, `ConstructionReplayTests.cs`; confirm `pwsh ci/check-fingerprints.ps1` unchanged
- [X] T049 [P] `RoadNetworkBenchmarks` (full build, incremental update, long route, cached route, plan 200) in `benchmarks/Talt.Benchmarks/RoadNetworkBenchmarks.cs`; road block meshing benchmark in `benchmarks/Talt.Benchmarks/RoadMeshBenchmarks.cs` (or a timed GPU-free test if the benchmark project cannot reference the client)
- [X] T050 [P] Client `--road-grid <spacing>` and `--start-year`; headless `road-tiles` summary in `src/Talt.Client/Program.cs`, `tools/Talt.Headless/`
- [X] T051 Update `docs/ARCHITECTURE.md` (roads, network, rendering, numbers), `docs/VISION.md` (clarify decisions), `README.md` options
- [X] T052 Run all tests and benchmarks; launch the client for the developer's hand test (quickstart.md) and record results

## Dependencies

Phase 1 → Phase 2 (T015 before the seeding part of T013) → US1 → (US2, US3, US4, US5, US6 in any order; US3 auto-bridge needs US1's planner)
→ Phase 9. Client tasks of each story need that story's commands.

## Parallel examples

- Phase 2: T004, T006/T007, T009, T014 together; T019 and T020 once T016–T018 exist.
- US3–US6 Core tests (T035, T039, T043, T046) are independent files.

## Implementation strategy

MVP = Phases 1–3 (build roads by dragging, drawn and routed). Then slopes and bridges/tunnels (P1),
removal and types/motorways (P2), one-way (P3), then polish.
