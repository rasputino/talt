# Tasks: Stations and cargo handling

**Input**: Design documents from `specs/010-stations-cargo/` (plan.md, spec.md, research.md, data-model.md,
contracts/, quickstart.md)

**Tests**: required by the constitution (principle IV): Core tests are written with or before the code.

**Format**: `[ID] [P?] [Story] Description`

## Phase 1: Setup

- [X] T001 Create `src/Talt.Core/Stations/Data/stations.json` per contracts/data-files.md and embed it in `src/Talt.Core/Talt.Core.csproj` (EmbeddedResource like towns/roads)
- [X] T002 [P] Create test folder `tests/Talt.Core.Tests/Stations/` with `StationTestMaps.cs` helpers (flat test map with roads, a town and industries; uses existing `TestMaps`/`IndustryTestData` patterns)

## Phase 2: Foundational

- [X] T003 Implement `StationRules` (parse + validate stations.json, `Fingerprint`) in `src/Talt.Core/Stations/StationRules.cs` covering every field of data-model.md StationRules; tests in `tests/Talt.Core.Tests/Stations/StationRulesTests.cs` (default file loads; invalid values name the field)
- [X] T004 [P] Add `StationSettings` (`DebugServicePercent` 0–100, `Validate`) in `src/Talt.Core/Stations/StationSettings.cs` and `ScenarioSettings.Stations` + `EffectiveStations` in `src/Talt.Core/Scenarios/ScenarioSettings.cs`
- [X] T005 [P] Add `TileObjectKind.Station = 3` in `src/Talt.Core/Construction/TileObjects.cs`, `"station": 0` demolish cost in `src/Talt.Core/Construction/Data/construction.json` (+ ConstructionRules parsing if it enumerates kinds), new reason codes (`no-road`, `not-straight-road`, `adjacent-two-stations`, `station-too-spread`, `no-station`, `station-road`, `use-station-tool`, `name-too-long`) in `src/Talt.Core/Construction/ConstructionReasons.cs`
- [X] T006 Create `StationRegistry` skeleton (SoA stations, pieces, groups, packet pool, `_pieceAt`, `_slotById`, queries, `Version`, `AppendTo` that appends nothing when `Count == 0`) in `src/Talt.Core/Stations/StationRegistry.cs` per data-model.md Stations, Station pieces, Groups and CargoPacket
- [X] T007 Wire the registry into `GameSimulation` (create for map games after towns, `ConstructionState.AttachStations`, day and month events after industries/towns, hashing after towns) in `src/Talt.Core/Simulation/GameSimulation.cs` and `src/Talt.Core/Construction/ConstructionState.cs`
- [X] T008 Add `ConstructionCommand.Committed(GameSimulation)` protected virtual hook called after `Commit` in `src/Talt.Core/Construction/ConstructionCommand.cs`
- [X] T009 Register every new station type in `tests/Talt.Core.Tests/Determinism/NoFloatingPointStateTests.cs`

**Checkpoint**: build and all existing tests pass with unchanged goldens.

## Phase 3: User Story 1 — Build road stations with a catchment preview (P1) 🎯 MVP

**Goal**: build/remove bus and truck stops (bay and drive-through) with cost, reasons, names and a live catchment preview.

**Independent test**: spec US1.

- [X] T010 [P] [US1] Placement tests (bay beside road in N/E/S/W order, drive-through on straight own/town road, refusals: no road, slope, water, occupied, curve/junction/bridge end, money, town refuses) in `tests/Talt.Core.Tests/Stations/StationPlacementTests.cs`
- [X] T011 [US1] Implement `StationPlanner` (bay: Station object + one-link road piece + neighbour link as `TileChange`s; drive-through: unchanged change; removal plans; costs from rules; town rule via `TownRegistry.MayBuildStation` on the tile's town zone or road owner) in `src/Talt.Core/Stations/StationPlanner.cs`
- [X] T012 [US1] Implement `BuildStationCommand`, `RemoveStationCommand` (Read/Write, `Committed` → registry, undo cleared) in `src/Talt.Core/Stations/StationCommands.cs`; register in `src/Talt.Core/Commands/CommandSerializer.cs`
- [X] T013 [US1] Implement piece add/remove, station creation/removal and automatic names (data-model.md Stations `_town`, `_nameKind`, `_nameNumber`; research R11) in `src/Talt.Core/Stations/StationRegistry.Construction.cs`
- [X] T014 [US1] Protect station roads in `src/Talt.Core/Roads/RoadPlanner.Structures.cs` and `RoadPlanner.Path.cs` (`station-road`), refuse station tiles in `ConstructionPlanner.PlanClear` (`use-station-tool`), and skip station tiles in town street growth `src/Talt.Core/Towns/TownRegistry.Streets.cs`; tests in `tests/Talt.Core.Tests/Stations/StationRoadProtectionTests.cs`
- [X] T015 [US1] Implement catchment and acceptance (research R5) and `PreviewCatchment` in `src/Talt.Core/Stations/StationRegistry.Coverage.cs`; tests in `tests/Talt.Core.Tests/Stations/CatchmentTests.cs` (radius 3/4, union of pieces, industry acceptance 8, town goods/food eighths, passenger/mail derived, receives list)
- [X] T016 [P] [US1] Command round-trip and replay tests in `tests/Talt.Core.Tests/Stations/StationCommandTests.cs`
- [X] T017 [US1] Client tools `busStop`, `truckStop`, `removeStation` (form from tile: straight road → `*-stop`, else `*-bay`) in `src/Talt.Client/Construction/ConstructionTool.cs`; key actions in `src/Talt.Client/Input/Data/actions.json`; icons in `src/Talt.Client/Ui/Data/icons.json`
- [X] T018 [US1] Stations toolbar column, catchment preview boxes and cursor label "Receives … · Accepts …" in `src/Talt.Client/Screens/InGameScreen.cs` and `src/Talt.Client/Construction/PreviewBoxes.cs` (catchment tiles in the area colour)
- [X] T019 [US1] Placeholder station boxes appended to the industry placeholder instances in `src/Talt.Client/GameSession.cs` (rebuild on registry version)
- [X] T020 [P] [US1] Localization keys (tools, reasons, names, cargo lists) in `src/Talt.Client/Localization/Strings.en.json` and `Strings.es.json`
- [X] T021 [P] [US1] Client tests for tool form choice and preview text in `tests/Talt.Client.Tests/Construction/StationToolTests.cs`

## Phase 4: User Story 2 — Cargo appears at served stations and waits (P1)

**Goal**: daily supply from industries and town buildings to served groups, cap and overflow.

**Independent test**: spec US2.

- [X] T022 [P] [US2] Supply tests (unserved → nothing and mine stock grows; served → rating share; two stations share by rating; town passengers; cap and overflow penalty; carry exact over a month) in `tests/Talt.Core.Tests/Stations/SupplyTests.cs`
- [X] T023 [US2] Add `IProductionSink` and the `sink` parameter of `IndustryFleet.AdvanceDay` in `src/Talt.Core/Industries/IndustryFleet.cs` (no change when null); industry tests unchanged
- [X] T024 [US2] Producer table (CSR), dirty tracking, station grid, sim-side changed tiles in `src/Talt.Core/Towns/TownBuildings.cs` and coverage rebuild in `src/Talt.Core/Stations/StationRegistry.Coverage.cs`
- [X] T025 [US2] Daily supply with carry (data-model.md Supply carry), split among best groups, packets (data-model.md CargoPacket) in `src/Talt.Core/Stations/StationRegistry.Supply.cs` and `StationRegistry.Cargo.cs`
- [X] T025b [US2] Monthly rotation of group statistics (data-model.md Groups `_received*`, `_pickedUp*`, `_delivered*`, `_lost*`) in `StationRegistry.AdvanceMonth` (`src/Talt.Core/Stations/StationRegistry.cs`), tested in `SupplyTests.cs`
- [X] T026 [US2] `MarkServed` and the debug service (`--station-service`, research R10) in `src/Talt.Core/Stations/StationRegistry.Supply.cs`; recorded session `stations` object in `src/Talt.Core/Commands/RecordedSession.cs`
- [X] T027 [US2] `StationSeeder` (deterministic bay placements as commands) in `src/Talt.Core/Stations/StationSeeder.cs`; tests in `tests/Talt.Core.Tests/Stations/StationSeederTests.cs`
- [X] T028 [US2] Options `--station-service`, `--seed-stations` in `src/Talt.Client/Program.cs`, `tools/Talt.Headless/HeadlessArguments.cs` and summary in `tools/Talt.Headless/Program.cs`

## Phase 5: User Story 3 — Loading and unloading rules for vehicles (P1)

**Goal**: Load/Unload/Transfer API with deliveries for 011 and conservation.

**Independent test**: spec US3.

- [X] T029 [P] [US3] Tests: load oldest first, unload to power station via Deliver, refused stays, transfer keeps origin/pickup day, origin-group refusal, town transported counted, delivery reports, 1 000-day random conservation (SC-003) in `tests/Talt.Core.Tests/Stations/LoadUnloadTests.cs` and `CargoConservationTests.cs`
- [X] T030 [US3] `CargoHold`, `HeldCargo` in `src/Talt.Core/Stations/CargoHold.cs`; `CargoDelivery`, `IDeliveryListener`, `UnloadResult`, `UnloadMode` in `src/Talt.Core/Stations/CargoDelivery.cs` (data-model.md CargoHold, CargoDelivery)
- [X] T031 [US3] `Load` and `Unload` in `src/Talt.Core/Stations/StationRegistry.Cargo.cs`

## Phase 6: User Story 4 — Station ratings (P2)

- [X] T032 [P] [US4] Rating tests (initial, falls without pickup, rises with fast frequent pickups, clamped, loss below threshold) in `tests/Talt.Core.Tests/Stations/RatingTests.cs`
- [X] T033 [US4] Rating target, step and loss (data-model.md Groups `_rating`, `_lastPickupDay`, `_lastSpeedKmh`; StationRules rating/loss) in `src/Talt.Core/Stations/StationRegistry.Supply.cs`, with parallel group phase; parallel = sequential test in `tests/Talt.Core.Tests/Stations/StationDeterminismTests.cs`

## Phase 7: User Story 5 — Expand stations with adjacent pieces (P2)

- [X] T034 [P] [US5] Join tests (adjacent joins, two stations refused, spread limit, removal never splits) in `tests/Talt.Core.Tests/Stations/StationJoinTests.cs`
- [X] T035 [US5] Joining and spread rules in `src/Talt.Core/Stations/StationPlanner.cs` and `StationRegistry.Construction.cs`

## Phase 8: User Story 6 — Warehouses group nearby stations (P2)

- [X] T036 [P] [US6] Group tests (shared stock, load from any member, acceptance by any member, merge on overlap, split conserves, lone station) in `tests/Talt.Core.Tests/Stations/WarehouseGroupTests.cs`
- [X] T037 [US6] `BuildWarehouseCommand` and warehouse planning (footprint flat/empty/touching road, town rule `MayBuildStation` as for stations) in `src/Talt.Core/Stations/StationCommands.cs`, `StationPlanner.cs`; register in `CommandSerializer`
- [X] T038 [US6] Union–find groups and merge/split of group state (research R4) in `src/Talt.Core/Stations/StationRegistry.Groups.cs`; warehouse names with place suffix
- [X] T039 [US6] Client warehouse tool + size option button (`StationToolOptions`) and warehouse placeholder boxes in `src/Talt.Client/Construction/`, `InGameScreen.cs`, `GameSession.cs`

## Phase 9: User Story 7 — Station window (P3)

- [X] T040 [US7] `StationInfo` snapshot and `TryGetStation` in `src/Talt.Core/Stations/StationInfo.cs`
- [X] T041 [US7] `RenameStationCommand` in `src/Talt.Core/Stations/StationCommands.cs` (+ serializer, tests in `StationCommandTests.cs`)
- [X] T042 [US7] `StationPickLayer` / `PickKind.Station` in `src/Talt.Client/Picking/Picking.cs`
- [X] T043 [US7] `StationWindow` (name, group, cargo table, catchment toggle, rename field) in `src/Talt.Client/Screens/StationWindow.cs` and `ObjectInfoWindow` in `src/Talt.Client/Screens/Windows.cs`; catchment highlight in `GameSession.cs`
- [X] T044 [P] [US7] Client tests for window texts and pick layer in `tests/Talt.Client.Tests/Screens/StationWindowTests.cs`

## Phase 10: Polish & cross-cutting

- [X] T045 Fingerprint goldens: confirm map-less goldens and windows-reference unchanged (`pwsh ci/check-fingerprints.ps1`); add a station golden in `tests/Talt.Core.Tests/Determinism/StationReplayTests.cs`
- [X] T046 [P] `StationBenchmarks` (daily 5 000 stations, with groups, monthly, preview) in `benchmarks/Talt.Benchmarks/StationBenchmarks.cs`; record numbers in `docs/ARCHITECTURE.md`
- [X] T047 [P] Docs: `docs/ARCHITECTURE.md` Stations section and data flow, `README.md` options, `docs/VISION.md` decisions (warehouses, both forms, names), `docs/SPECKIT_GUIDE.md` note
- [ ] T048 Run quickstart.md; launch the client for the developer's hand test and record the result; leave notes in the shared notes folder

## Dependencies

- Phase 2 blocks everything. US1 → US2 → US3 (US3 needs waiting cargo); US4 builds on US2; US5 on US1;
  US6 on US2/US5; US7 on US1–US4.
- Parallel: tasks marked [P] (separate test files, localization, docs, benchmarks).

## Implementation strategy

MVP = Phases 1–3 (stations can be built with a preview). Then US2 + US3 (cargo flow and vehicle API),
then ratings, joining, warehouses, window; finish with goldens, benchmarks, docs and the hand test.
