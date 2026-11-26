# Tasks: Railways (015)

**Input**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md),
[contracts/](contracts/). Tests are required by the constitution (IV) and the spec's success criteria.

## Phase 1: Setup

- [X] T001 Create `src/Talt.Core/Rails/` and `tests/Talt.Core.Tests/Rails/`; embed `Rails/Data/rail.json` in `src/Talt.Core/Talt.Core.csproj`
- [X] T002 [P] Write `src/Talt.Core/Rails/Data/rail.json` and `RailRules.cs` (load + validation with file/field names, data-model.md "Rolling stock model", contracts/data-files.md); tests in `tests/Talt.Core.Tests/Rails/RailRulesTests.cs`

## Phase 2: Foundational

- [X] T003 `RailPiece` and `RailLayer` (data-model.md RailLayer.links/type/flags/span/longDir/signal, invariants, `Validate`, hashing only once rail existed) in `src/Talt.Core/Rails/RailLayer.cs`; tests `RailLayerTests.cs`
- [X] T004 Transition rule (R2) and signal helpers in `src/Talt.Core/Rails/RailGeometry.cs`; tests
- [X] T005 Extend `TileChange`, `TileSnapshot`, `UndoRecord`, `ConstructionState` (Rails layer, commit/undo, Version) and `TileObjectKind.Rail/RailDepot` in `src/Talt.Core/Construction/`; keep every existing golden
- [X] T006 Register new types in `tests/Talt.Core.Tests/Determinism/NoFloatingPointStateTests.cs`

## Phase 3: User Story 1 — Build track (P1)

**Independent test**: drag track across slopes and a river; bridge, tunnel, junction, removal, level crossing.

- [X] T007 [US1] `RailPlanner` path A* (≤ 45° turns, slopes, joins/junctions) in `src/Talt.Core/Rails/RailPlanner.cs`
- [X] T008 [US1] Bridges, tunnels, removal, conversion (electrify), level crossings in `src/Talt.Core/Rails/RailPlanner.Structures.cs`
- [X] T009 [US1] Commands `buildRail`, `buildRailBridge`, `buildRailTunnel`, `removeRail`, `convertRail` in `src/Talt.Core/Rails/RailCommands.cs`, registered in `CommandSerializer`
- [X] T010 [US1] Tests `tests/Talt.Core.Tests/Rails/RailPlannerTests.cs` (path, slope, bridge, tunnel, junction, removal, crossing, costs, undo)
- [X] T011 [US1] Client: `RailMesher` + generic tile-layer renderer, rail tools (track, bridge, tunnel, remove, electrify) in `src/Talt.Client/`

## Phase 4: User Story 2 — Stations and depots (P1)

- [X] T012 [US2] `rail-platform` piece in `stations.json`, finance maintenance; `StationPlanner.PlanRailStation` and registry support (data-model.md Rail station) in `src/Talt.Core/Stations/`
- [X] T013 [US2] `RailDepotRegistry` (data-model.md RailDepot) and `buildRailDepot`/`removeRailDepot` in `src/Talt.Core/Rails/`
- [X] T014 [US2] Tests `RailStationTests.cs`, `RailDepotTests.cs`
- [X] T015 [US2] Client: station tool (platforms × length, axis), depot tool, placeholders

## Phase 5: User Story 3 — Trains on lines (P1)

- [X] T016 [US3] `TrainSystem` storage (data-model.md Train.*), purchase/sell/clone/start/cargo commands in `src/Talt.Core/Rails/TrainSystem.cs`, `TrainCommands.cs`
- [X] T017 [US3] `RailRouter` (R8, R9) in `src/Talt.Core/Rails/RailRouter.cs`; tests `RailRouterTests.cs`
- [X] T018 [US3] Movement phase (physics R6, braking to authority end) in `TrainSystem.Move.cs`
- [X] T019 [US3] Sequential phase: reservations, stops, loading per wagon, pull-forward (R11), depots, reversing in `TrainSystem.Events.cs`
- [X] T020 [US3] Hook into `GameSimulation` (tick, day/month/year, fingerprint), lines (R13), finance
- [X] T021 [US3] Tests `TrainMovementTests.cs`, `TrainLineTests.cs` (loop earns money), physics on slopes
- [X] T021b [US3] Ageing, reliability, breakdowns, servicing, send to depot (FR-023, FR-011) in `TrainSystem.Daily.cs`; tests `TrainDailyTests.cs`
- [X] T021c [US3] Construction occupancy: removing track, signals, stations or depots under or reserved by a train is refused (FR-005, FR-008); network changes trigger replanning (FR-020); tests
- [X] T022 [US3] Client: train rendering (poses, placeholders, `train:<id>` models), depot window with consist editor, train window, picking, line assignment, sounds

## Phase 6: User Story 4 — Block signals (P1)

- [X] T023 [US4] Blocks (`RailBlocks.cs`) and `setSignal` command
- [X] T024 [US4] Block reservation rule; `CheckInvariants` (one owner per tile)
- [X] T025 [US4] Fixtures `TrainSafetyTests.cs`: double track with 10 trains, unsignalled loop, parallel = sequential
- [X] T026 [US4] Client: signal tool and signal rendering with state

## Phase 7: User Story 5 — Path signals (P2)

- [X] T027 [US5] Path and one-way path rules; fixtures single line with passing loops, junction, terminus (no deadlock)

## Phase 8: User Story 6 — Platform choice (P2)

- [X] T028 [US6] Platform choice through occupancy-aware routing; test three platforms × three trains

## Phase 9: User Story 7 — Electrification (P3)

- [X] T029 [US7] Electric locomotives route only on electrified track; test

## Phase 10: Polish

- [X] T030 Level crossing guard for road vehicles (R12) and test
- [X] T031 `TrainSeeder` + `--seed-trains` (client and headless), `--check-trains`
- [X] T032 Benchmarks `benchmarks/Talt.Benchmarks/TrainBenchmarks.cs` (1 k / 5 k trains with signals, route 1 000 tiles), numbers in `docs/ARCHITECTURE.md`
- [X] T033 Allocation tests with `AllocationAssert`; rail replay golden; `pwsh ci/check-fingerprints.ps1`
- [X] T034 Localization keys en/es; `docs/ARCHITECTURE.md` rail section; README options
- [ ] T035 Hand test with the user (quickstart.md step 5)

## Dependencies

Setup → Foundational → US1 → US2 → US3 → US4 → US5/US6/US7 → Polish. US5–US7 are independent of each other.

## Parallel opportunities

T002 with T003/T004; client tasks (T011, T015, T022, T026) alongside the next Core story; T029 with T027/T028.

## Implementation strategy

MVP = US1–US4 (track, stations, depots, trains on lines, block signals); then path signals and platform choice;
electrification last.
