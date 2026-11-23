# Tasks: Construction and Terraforming Framework

**Input**: Design documents from `specs/006-construction-framework/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: required by the constitution (principle IV): Core logic gets xUnit tests with or before
the code.

**Format**: `- [ ] [ID] [P?] [Story] Description` — [P] = can run in parallel (different files, no
dependency on unfinished tasks).

## Phase 1: Setup

- [X] T001 Create folders `src/Talt.Core/Construction/` (with `Data/`), `src/Talt.Client/Construction/`, `tests/Talt.Core.Tests/Construction/`, `tests/Talt.Client.Tests/Construction/` and embed `Construction/Data/*.json` as resources in `src/Talt.Core/Talt.Core.csproj`
- [X] T002 [P] Add `src/Talt.Core/Construction/Data/construction.json` with the values of contracts/data-files.md

## Phase 2: Foundational (blocks every story)

- [X] T003 [P] Write `tests/Talt.Core.Tests/Construction/ConstructionRulesTests.cs`: default file loads; each field of data-model.md ConstructionRules (StartingBalance, TerraformStepCost, ClearTreeCost, DemolishCosts, UndoWindowTicks, MaxHeight, MaxTilesPerAction, MaxActionCost) out of range or unknown gives an error naming the field
- [X] T004 Implement `src/Talt.Core/Construction/ConstructionRules.cs` (load, validate, `LoadDefault()`, `Parse(json)`) per data-model.md ConstructionRules.StartingBalance, ConstructionRules.TerraformStepCost, ConstructionRules.ClearTreeCost, ConstructionRules.DemolishCosts, ConstructionRules.UndoWindowTicks, ConstructionRules.MaxHeight, ConstructionRules.MaxTilesPerAction and ConstructionRules.MaxActionCost, and research R13
- [X] T005 [P] Implement `TileObjectKind`, `TileOwner` and the dense `TileObjects` layer in `src/Talt.Core/Construction/TileObjects.cs` per data-model.md TileObjects.Kind and TileObjects.Owner (Kind None ⇔ Owner None)
- [X] T006 [P] Implement `Company` in `src/Talt.Core/Construction/Company.cs` per data-model.md Company.Id and Company.Balance
- [X] T007 [P] Implement reason codes in `src/Talt.Core/Construction/ConstructionReasons.cs` (contracts/commands.md table)
- [X] T008 Add `MapSettings? Map` to `src/Talt.Core/Scenarios/ScenarioSettings.cs` (validated with `MapSettings.Validate`), and the optional `settings.map` text in `src/Talt.Core/Commands/RecordedSession.cs` (write when present, parse with `MapSettings.TryParse`); tests in `tests/Talt.Core.Tests/Commands/RecordedSessionTests.cs`
- [X] T009 Add internal mutators to `src/Talt.Core/Maps/MapData.cs` (set height, terrain, flags, scenery; `RecomputeTraits(x, z)` for Cliff/Buildable per research R5) and `AppendStateTo(StateHasher)` covering all tile arrays
- [X] T010 Implement `ConstructionState` in `src/Talt.Core/Construction/ConstructionState.cs`: rules, map, objects (prototype roads → `PublicRoad`/Town 0 using the tile walk of `NetworkTerrainPainter`), player company, undo record placeholder, `Version`, changed-tile log, `AppendTo(StateHasher)`
- [X] T011 Change `src/Talt.Core/Simulation/GameSimulation.cs`: `Construction` property, `Create(settings, map = null)` (generate map when not given; reject a map whose settings differ), hash `Construction` when present, `ProcessPendingCommands()` (research R3); `SessionReplayer` unchanged except through `Create`
- [X] T012 [P] Tests in `tests/Talt.Core.Tests/Construction/ConstructionStateTests.cs`: a map scenario has construction state with the starting balance, roads are `PublicRoad`/Town 0, fingerprint changes when a tile changes, map-less scenario has none; `ProcessPendingCommands` applies without advancing the tick
- [X] T013 Register the new state types in `tests/Talt.Core.Tests/Determinism/NoFloatingPointStateTests.cs` and confirm `GoldenFingerprintTests` still pass unchanged
- [X] T014 Make `src/Talt.Core/Voxels/VoxelWorld.cs` safe for creating chunks while mesh workers read (ConcurrentDictionary, research R6); test in `tests/Talt.Core.Tests/Voxels/VoxelWorldTests.cs`
- [X] T015 Add `WorldBuilder.RebuildColumn(map, world, x, z)` in `src/Talt.Core/Maps/WorldBuilder.cs` and `Heightmap.Refresh(MapData, x, z)` in `src/Talt.Core/Terrain/Heightmap.cs`; tests: rebuilding an unchanged column changes nothing, a raised/lowered tile matches a freshly built world (`tests/Talt.Core.Tests/Maps/WorldBuilderTests.cs`)
- [X] T016 Implement `TileSet` (open addressing with generation stamps) in `src/Talt.Core/Construction/TileSet.cs` with tests `tests/Talt.Core.Tests/Construction/TileSetTests.cs`
- [X] T017 Implement `ConstructionPlan` (reusable changes/problems/cost/reason, `TileRect`) in `src/Talt.Core/Construction/ConstructionPlan.cs` per data-model.md ConstructionPlan
- [X] T018 Implement the shared `ConstructionCommand` base in `src/Talt.Core/Construction/ConstructionCommand.cs`: validation order of contracts/commands.md, `Preview(state, planner, plan)`, `Apply` committing the plan (map, objects, scenery, spend, change log, `Version++`, undo record hook)
- [X] T019 Move the HUD money to the simulation: `src/Talt.Client/Screens/InGameScreen.cs` shows `Construction.Player.Balance`; delete `src/Talt.Client/Ui/Data/company.json` and its loader (`CompanyData`), fix references in `tests/Talt.Client.Tests`
- [X] T020 `src/Talt.Client/GameSetup.cs`: generate the map first, then `GameSimulation.Create(settings with Map, map)`; `src/Talt.Client/GameSession.cs`: call `ProcessPendingCommands()` while paused

**Checkpoint**: the game starts, money comes from the simulation, existing goldens unchanged.

## Phase 3: User Story 2 — Raise, lower and level land (P1)

**Goal**: terraform commands with the cascade, costs and rules. **Independent test**: terraform
commands on a test map change heights, costs and flags exactly as research R5 says.

- [X] T021 [P] [US2] Write `tests/Talt.Core.Tests/Construction/TerraformPlannerTests.cs`: raise/lower/level single tile and area; cascade counts and costs; order independence (rectangle given in four corner orders gives the same plan); water → `water`; flood → `would-flood`; height limits; `PublicRoad` → `owned-by-other`; own `Test` object → `object-in-the-way`; trees removed and charged; Rock → Grass; slope invariant after random actions on a generated map (SC-004); `too-large`
- [X] T022 [US2] Implement terraform planning in `src/Talt.Core/Construction/ConstructionPlanner.cs` (research R5, R8)
- [X] T023 [US2] Implement `TerraformCommand` in `src/Talt.Core/Construction/TerraformCommand.cs` per data-model.md ConstructionAction.TerraformCommand (CompanyId, Mode, X0, Z0, X1, Z1, StartX, StartZ; JSON per contracts/commands.md) and register it in `src/Talt.Core/Commands/CommandSerializer.cs`
- [X] T024 [P] [US2] Write `tests/Talt.Core.Tests/Construction/TerraformCommandTests.cs`: applied through `Tick`, money spent == preview cost (SC-002), `not-enough-money` leaves state unchanged (SC-003), JSON round trip, all-or-nothing
- [X] T025 [US2] Client terrain sync in `src/Talt.Client/Construction/TerrainSync.cs`: drain changed tiles, `RebuildColumn`, refresh heightmap, notify the renderer with the edit timestamp so the existing "edit visible after … ms" log measures terraform edits (SC-005); `src/Talt.Client/Rendering/WorldRenderer.cs` registers chunks created after setup
- [X] T026 [US2] Remove the debug edit path: delete `src/Talt.Core/Terrain/TerrainEditor.cs` and `tests/Talt.Core.Tests/Terrain/TerrainEditorTests.cs`, `GameSession.EditTerrain`, the `raiseTerrain`/`lowerTerrain` actions and their strings

## Phase 4: User Story 1 — Preview with validity, reason and cost (P1)

**Goal**: tools in the toolbar, ghost preview green/red, cost and reason by the cursor.
**Independent test**: `ToolController` tests plus the quickstart hand step 1.

- [X] T027 [P] [US1] Define tools in `src/Talt.Client/Construction/ConstructionTool.cs` (Raise, Lower, Level, Clear; shape Tile/Line/Area; command factory)
- [X] T028 [P] [US1] Write `tests/Talt.Client.Tests/Construction/ToolControllerTests.cs`: hover previews one tile; preview cost equals the cost after applying; invalid tile shows the reason code; re-plans only when the key changes; selecting another tool clears the preview; 1 000 repeated previews of a 25 × 20 drag allocate 0 B (SC-007)
- [X] T029 [US1] Implement `src/Talt.Client/Construction/ToolController.cs` per contracts/construction-api.md
- [X] T030 [US1] Instanced boxes: `src/Talt.Client/Rendering/BoxRenderer.cs` and `src/Talt.Client/Rendering/Shaders/boxes.wgsl` (opaque and translucent pipelines), drawn by `src/Talt.Client/Rendering/WebGpuRenderer.cs`
- [X] T031 [US1] `src/Talt.Client/Construction/PreviewBoxes.cs`: plan → tile boxes at the new height, green/red from the theme (`src/Talt.Client/Ui/Data/theme.json` new colours), reused arrays
- [X] T032 [US1] `src/Talt.Client/Screens/InGameScreen.cs`: tool buttons and icons (`src/Talt.Client/Ui/Data/icons.json`), tool key actions (`src/Talt.Client/Input/Data/actions.json`), cursor label with cost/reason, rejected-command messages; strings in `src/Talt.Client/Localization/Strings.en.json` and `Strings.es.json`
- [X] T033 [US1] Wire preview boxes and terrain sync in `src/Talt.Client/GameWindow.cs` / `GameSession.cs` (focused edits)

## Phase 5: User Story 3 — Demolish own objects and clear trees (P1)

**Goal**: clear tool. **Independent test**: clear commands on a test map with trees and objects.

- [X] T034 [P] [US3] Write `tests/Talt.Core.Tests/Construction/ClearAreaTests.cs`: trees removed and charged per tree; own `Test` object demolished with its cost; `PublicRoad` → `owned-by-other` and nothing removed; empty area → `nothing-to-do` and no charge
- [X] T035 [US3] Implement clear planning in `src/Talt.Core/Construction/ConstructionPlanner.cs` and `ClearAreaCommand` in `src/Talt.Core/Construction/ClearAreaCommand.cs` per data-model.md ConstructionAction.ClearAreaCommand (CompanyId, X0, Z0, X1, Z1); register it
- [X] T036 [US3] Trees on screen: `src/Talt.Client/Rendering/TreeInstances.cs` (scenery near the camera target, rebuilt on move/scenery change, research R12) drawn with `BoxRenderer`; tests for gathering in `tests/Talt.Client.Tests/Rendering/TreeInstancesTests.cs`

## Phase 6: User Story 4 — Drag and cancel (P2)

**Goal**: drag rectangles/lines, release confirms, cancel by key or right button.

- [X] T037 [P] [US4] Extend `tests/Talt.Client.Tests/Construction/ToolControllerTests.cs`: area drag rectangle in all directions, line shape snaps to the dominant axis, release submits one command with the rectangle, Esc/right button cancel without submitting, `back` with no drag deselects the tool
- [X] T038 [US4] Implement drag, line shape, release and cancel in `src/Talt.Client/Construction/ToolController.cs`; route left drag to the tool (no camera rotation) and right button cancel in `src/Talt.Client/Screens/InGameScreen.cs`

## Phase 7: User Story 5 — Undo (P3)

- [X] T039 [P] [US5] Write `tests/Talt.Core.Tests/Construction/UndoTests.cs`: undo restores exact tiles and refunds; expired after `UndoWindowTicks`; `changed-since` when a tile changed; one level only; a later action replaces the record; undo while paused does not expire
- [X] T040 [US5] Implement `UndoRecord` (`src/Talt.Core/Construction/UndoRecord.cs`, data-model.md UndoRecord) and `UndoConstructionCommand` (`src/Talt.Core/Construction/UndoConstructionCommand.cs`, data-model.md ConstructionAction.UndoConstructionCommand CompanyId); register it
- [X] T041 [US5] Undo action (Backspace) and toolbar button in `src/Talt.Client/Screens/InGameScreen.cs`, `actions.json`, strings

## Phase 8: Polish & cross-cutting

- [X] T042 [P] Replay determinism: `tests/Talt.Core.Tests/Determinism/ConstructionReplayTests.cs` (record a map scenario with terraform, clear, undo and paused commands; replay equal; parallel = sequential) and a new golden fingerprint for that scenario in `tests/Talt.Core.Tests/Determinism/GoldenFingerprintTests.cs` (SC-006)
- [X] T043 [P] Benchmark `benchmarks/Talt.Benchmarks/ConstructionBenchmarks.cs` (research R14); re-run `ChunkMeshingBenchmarks`
- [X] T044 Localization coverage: every reason code has strings in both languages (`tests/Talt.Client.Tests/Localization`)
- [X] T045 Update `docs/ARCHITECTURE.md` (construction section, simulation owns the map, controls table, benchmark numbers), `README.md` controls, `docs/SPECKIT_GUIDE.md` status if it tracks it; the client smoke fingerprint where documented
- [X] T046 Run `dotnet build`, `dotnet test --filter "Category!=Gpu"`, `pwsh ci/check-fingerprints.ps1`
- [X] T047 Hand test with the developer (quickstart.md), fix findings with regression tests, record the verdict in `docs/ARCHITECTURE.md`

## Dependencies

- Phase 1 → Phase 2 → stories. US2 (terraform) comes first because US1's preview needs a real
  command to preview; US1 and US3 can then run in parallel; US4 builds on US1's controller; US5 needs
  `ConstructionCommand` only. Polish last.

## Parallel examples

- Phase 2: T003, T005, T006, T007 together; T012 with T014–T016.
- US2: T021 and T024 (tests) while T022 is written. US1: T027, T028 in parallel; US3's T034 with US1.

## Implementation strategy

MVP = Phase 2 + US2 + US1 (terraform with preview and cost). Then US3 (clear and trees), US4 (drag
polish), US5 (undo), polish and the hand test.
