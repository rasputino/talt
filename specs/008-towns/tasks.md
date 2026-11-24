# Tasks: Towns (feature 008)

**Input**: design documents in `specs/008-towns/` (plan, spec, research, data-model, contracts, quickstart)

Tests are required (Constitution IV): Core logic gets xUnit tests with or before the code.

## Phase 1: Setup

- [X] T001 Add the embedded resource item `Towns\Data\*.json` (logical name `Talt.Core.Towns.Data.<file>`) in src/Talt.Core/Talt.Core.csproj and create the folder src/Talt.Core/Towns/

## Phase 2: Foundational (blocks every story)

- [X] T002 [P] Write src/Talt.Core/Towns/Data/town-buildings.json (14 original types, 5 categories, per data-model.md BuildingType fields) and src/Talt.Core/Towns/Data/towns.json (growth, sizes, ratings per contracts/data-files.md)
- [X] T003 [P] Write src/Talt.Core/Towns/Data/town-names.json with original English-style and Spanish-style parts (data-model.md TownNameParts)
- [X] T004 Implement BuildingType and TownRules parsing/validation (unknown keys, ranges, ids, zones, era, thresholds increasing; data-model.md BuildingType.* and TownRules) in src/Talt.Core/Towns/BuildingType.cs, src/Talt.Core/Towns/TownRules.cs and src/Talt.Core/Towns/TownJson.cs
- [X] T005 Tests for data validation (default files load; each broken field reports file, id and field) in tests/Talt.Core.Tests/Towns/TownDataTests.cs
- [X] T006 Implement TownSettings (data-model.md TownSettings.NameStyle, .Size, .DebugServicePercent) in src/Talt.Core/Towns/TownSettings.cs; add `Towns` to src/Talt.Core/Scenarios/ScenarioSettings.cs with validation; read/write the `towns` object in src/Talt.Core/Commands/RecordedSession.cs only when the scenario has a map (contracts/cli.md)
- [X] T007 Add `TileObjectKind.TownBuilding = 2` in src/Talt.Core/Construction/TileObjects.cs, `demolishCosts.townBuilding` in src/Talt.Core/Construction/Data/construction.json (and the rules parser), `ConstructionReasons.TownRefuses` in src/Talt.Core/Construction/ConstructionReasons.cs
- [X] T008 Implement TownBuildings SoA store (data-model.md TownBuilding.*: stable ids, swap-remove, tile → id map, changed tiles, hashing) in src/Talt.Core/Towns/TownBuildings.cs

## Phase 3: User Story 1 — named towns with buildings along roads (P1) 🎯 MVP

**Goal**: every town site holds a named town grown to its initial size. **Independent test**: generate
maps with several seeds; check names unique, population = residents sum, placement rules hold, same
seed → same towns.

- [X] T009 [P] [US1] Tests for names (unique, deterministic, style option respected, only allowed characters) in tests/Talt.Core.Tests/Towns/TownDataTests.cs
- [X] T010 [US1] Implement TownNames generator (research R4) in src/Talt.Core/Towns/TownNames.cs
- [X] T011 [US1] Implement plot validation, zone and type choice, building placement with tree removal and foundation height (research R5) in src/Talt.Core/Towns/TownRegistry.Growth.cs
- [X] T012 [US1] Implement street extension (grid / organic, ramp and terrain rules, town ownership, batched network update; research R6) in src/Talt.Core/Towns/TownRegistry.Streets.cs, sharing the link rule with src/Talt.Core/Roads/RoadSeeder.cs
- [X] T013 [US1] Implement TownRegistry founding (data-model.md Town.*, sizes and scale option, per-town streams, name table), queries of contracts/towns-api.md and hashing in src/Talt.Core/Towns/TownRegistry.cs (TownInfo included)
- [X] T014 [US1] Wire the registry into src/Talt.Core/Simulation/GameSimulation.cs (create after RoadSeeder, before RebuildNetwork; attach to ConstructionState; append to fingerprint when present) and src/Talt.Core/Construction/ConstructionState.cs (`Towns`, `AttachTowns`)
- [X] T015 [US1] Tests for founding: every site has a town, names unique, placement invariants (no overlap, land, height step, next to own road, owner tiles), population sum, size option, determinism across two creations, in tests/Talt.Core.Tests/Towns/TownFoundingTests.cs
- [X] T016 [US1] Register Town/TownBuildings/TownRules/TownSettings types in tests/Talt.Core.Tests/Determinism/NoFloatingPointStateTests.cs
- [X] T017 [P] [US1] Client: TownInstance + towns.wgsl (walls with windows, roof) and TownRenderer (per-area buffers, versioned rebuild with a time budget, frustum/fog culling, far LOD) in src/Talt.Client/Rendering/TownRenderer.cs, src/Talt.Client/Rendering/Shaders/towns.wgsl; wire in src/Talt.Client/Rendering/WebGpuRenderer.cs and src/Talt.Client/GameWindow.cs; render stats line
- [X] T018 [P] [US1] Client: instance building from buildings (pure function, testable) in src/Talt.Client/Rendering/TownInstances.cs with tests in tests/Talt.Client.Tests/Screens/TownClientTests.cs
- [X] T019 [US1] Client: TownLabels (project centres, nearest/largest first, at most 64, no overlap) in src/Talt.Client/Screens/TownLabels.cs drawn by src/Talt.Client/Screens/InGameScreen.cs; tests for layout in tests/Talt.Client.Tests/Screens/TownLabelsTests.cs
- [X] T020 [US1] Client options `--town-names`, `--town-size` and new-game screen choosers in src/Talt.Client/Program.cs, src/Talt.Client/Screens/NewGameOptions.cs, src/Talt.Client/Screens/Screens.cs; strings in src/Talt.Client/Localization/Strings.en.json and Strings.es.json

## Phase 4: User Story 2 — towns grow over time (P1)

**Goal**: served towns add and replace buildings and extend streets monthly; unserved towns do not
grow. **Independent test**: headless years with service 0 / 60 / 100; invariants; parallel = sequential.

- [X] T021 [US2] Implement the monthly update (statistics rotation, debug service, service, growth points, sequential growth actions, replacements; research R7, R8) and `RecordTransported` in src/Talt.Core/Towns/TownRegistry.cs and src/Talt.Core/Towns/TownRegistry.Growth.cs; hook the month event in src/Talt.Core/Simulation/GameSimulation.cs passing `parallel`
- [X] T022 [US2] Tests: no growth without service; growth with service; faster at 100 than 60 (SC-003); invariants after years; never removes or retypes non-own objects and never cuts trees outside its plots/streets (FR-013); parallel equals sequential; replay equality, in tests/Talt.Core.Tests/Towns/TownGrowthTests.cs
- [X] T023 [US2] Headless options `--town-names`, `--town-size`, `--town-service` and town summary in tools/Talt.Headless/HeadlessArguments.cs and tools/Talt.Headless/Program.cs; tests in tests/Talt.Headless.Tests/HeadlessArgumentsTests.cs
- [X] T024 [US2] Client `--town-service` option in src/Talt.Client/Program.cs (scenario settings)

## Phase 5: User Story 3 — town window (P2)

**Goal**: click a building or label to open the town window. **Independent test**: window texts match state.

- [X] T025 [US3] Picking: TownPickLayer (ray over building boxes) and label hit test in src/Talt.Client/Picking/Picking.cs and src/Talt.Client/Screens/InGameScreen.cs; tests in tests/Talt.Client.Tests/Screens/TownClientTests.cs
- [X] T026 [US3] Town window branch (name, population, buildings, generated/transported passengers and mail, accepted cargo, rating band and value, growth status) in src/Talt.Client/Screens/TownWindow.cs and src/Talt.Client/Screens/Windows.cs; road window shows the town name; strings en/es; tests in tests/Talt.Client.Tests/Screens/TownClientTests.cs

## Phase 6: User Story 4 — ratings (P2)

**Goal**: penalties for demolition, town roads and trees; monthly recovery; thresholds. **Independent
test**: construction commands change ratings by the data amounts; refused below the threshold.

- [X] T027 [US4] Ratings: monthly movement toward the service target, bands, `MayDemolish`, `MayBuildStation`, `TownZoneAt`, `ApplyConstruction`, `PlanRatingChange` (research R9, R10) in src/Talt.Core/Towns/TownRegistry.cs
- [X] T028 [US4] Construction: clear tool demolishes whole town buildings with cost and rating check, road removal rating check, commit reports to the registry and clears undo when buildings were removed, `ConstructionPlan.RatingChange`, in src/Talt.Core/Construction/ConstructionPlanner.cs, src/Talt.Core/Construction/ConstructionPlan.cs, src/Talt.Core/Construction/ConstructionState.cs, src/Talt.Core/Roads/RoadPlanner.Structures.cs
- [X] T029 [US4] Tests for ratings and construction integration in tests/Talt.Core.Tests/Towns/TownRatingTests.cs
- [X] T030 [US4] Client: preview shows town buildings as blockers with their town, and the rating change in the cost line (FR-024); reason text `construction.reason.town-refuses` in src/Talt.Client/Screens/InGameScreen.cs and Strings en/es

## Phase 7: User Story 5 — bigger buildings and denser centre (P3)

- [X] T031 [US5] Tests: large town centre has higher average level/height than outskirts; era and minimum population respected; replacements increase level, in tests/Talt.Core.Tests/Towns/TownShapeTests.cs (tune data if needed)

## Phase 8: Polish

- [X] T032 Benchmarks MonthlyUpdate500Towns and Founding on a 4096 map in benchmarks/Talt.Benchmarks/TownBenchmarks.cs; record numbers in docs/ARCHITECTURE.md
- [X] T033 Update goldens that change on purpose (ConstructionReplayTests, RoadReplayTests); confirm map-less goldens and `pwsh ci/check-fingerprints.ps1` unchanged
- [X] T034 Docs: docs/ARCHITECTURE.md (Towns section, data flow, rendering), README.md options, docs/VISION.md decisions
- [X] T035 Full `dotnet build` and `dotnet test --filter "Category!=Gpu"`; launch the client for the developer (quickstart) and record the result

## Dependencies

Setup → Foundational → US1 → US2 → (US3, US4 in parallel) → US5 → Polish. US3 needs only US1;
US4 needs US1 (and US2's monthly update for recovery).

## Parallel examples

- Phase 2: T002, T003 together; then T004/T006/T007/T008.
- US1: T009 with T011; T017/T018 (client) alongside T013–T016 (core) once the query API exists.

## Implementation strategy

MVP = US1 (towns visible with names and buildings). Then growth (US2), then the window and ratings,
then shape tuning and polish.
