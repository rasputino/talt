# Tasks: Road vehicles, depots and lines

**Input**: `specs/012-road-vehicles/` (spec, plan, research R1–R14, data-model, contracts, quickstart)

Tests are required by the constitution (IV) and by the spec's success criteria; they are written with the code.

## Phase 1: Setup

- [X] T001 Create `src/Talt.Core/Vehicles/` with `Data/vehicles.json` embedded in `src/Talt.Core/Talt.Core.csproj` (contracts/data-files.md; models per data-model.md VehicleModel.*: 2 buses, bulk/tanker/box trucks, years from 1850)
- [X] T002 Implement `VehicleRules` (load, validate with file/model/field names, fingerprint, unit conversions km/h → units per tick, milli-tiles → units) in `src/Talt.Core/Vehicles/VehicleRules.cs` with tests in `tests/Talt.Core.Tests/Vehicles/VehicleRulesTests.cs` (data-model.md VehicleModel.lengthMilliTiles bound, cargo category coverage)

## Phase 2: Foundational

- [X] T003 Add `TileObjectKind.Depot`, new reason codes (`no-depot`, `unknown-model`, `model-not-available`, `cargo-not-allowed`, `no-vehicle`, `not-in-depot`, `no-depot-reachable`, `hold-not-empty`, `no-line`, `invalid-index`, `too-many-stops`, `last-stop`, `wrong-kind`, `vehicle-in-the-way`, `invalid-value`) in `src/Talt.Core/Construction/TileObjects.cs` and `ConstructionReasons.cs`; demolish/clear rules skip depots like stations
- [X] T004 Add `IVehicleOccupancy` to `ConstructionState` and the refusal in `ConstructionCommand.Check` for plans that change road links, station pieces or depots on occupied tiles (research R12) in `src/Talt.Core/Construction/`
- [X] T005 [P] Add `RunningCosts.SetValue(handle, value)` and `MoneyEvent.VehicleId` (+ `FinanceSystem.CurrentVehicleId` used when writing income events) in `src/Talt.Core/Finance/`
- [X] T006 Implement `DepotRegistry` (data-model.md Depot.*) and `LineRegistry` (data-model.md Line and stops: 1–64 stops, rule values, merge duplicates, name ≤ 32) in `src/Talt.Core/Vehicles/DepotRegistry.cs`, `LineRegistry.cs`, both `IStateHashable`
- [X] T007 Implement `RoadVehicleState` and `RoadVehicleSystem` storage (slots, ids, double buffer, cold arrays per data-model.md RoadVehicle.*, hashing that appends nothing until something exists) in `src/Talt.Core/Vehicles/RoadVehicleState.cs`, `RoadVehicleSystem.cs`; register types in `tests/Talt.Core.Tests/Determinism/NoFloatingPointStateTests.cs`
- [X] T008 Wire `RoadVehicleSystem` into `GameSimulation` (create for map games, `Tick` after the prototype fleet, day/month/year handlers after finance, fingerprint after finance, occupancy attached to construction) in `src/Talt.Core/Simulation/GameSimulation.cs`; test that every existing golden is unchanged (`pwsh ci/check-fingerprints.ps1` + full test run)

## Phase 3: User Story 1 — depot and buying (P1)

**Independent test**: build a depot, buy a bus; money and ledger change; the vehicle is listed in the depot.

- [X] T009 [US1] Implement `DepotPlanner` (bay-like placement and removal plans) and `BuildDepotCommand`/`RemoveDepotCommand` in `src/Talt.Core/Vehicles/DepotPlanner.cs`, `DepotCommands.cs`; tests in `tests/Talt.Core.Tests/Vehicles/DepotTests.cs`
- [X] T010 [US1] Implement `buyVehicle`, `startVehicle`, `setVehicleCargo` in `src/Talt.Core/Vehicles/VehicleCommands.cs` (price × cost level, infinite money, running cost registration, availability years, cargo category) with tests in `VehicleCommandTests.cs`
- [X] T011 [US1] Register the new commands in `src/Talt.Core/Commands/CommandSerializer.cs`; round-trip test in `VehicleCommandTests.cs`
- [X] T012 [US1] Depot maintenance booked monthly (property maintenance) in `RoadVehicleSystem.Daily.cs`; test

## Phase 4: User Story 3 — driving and interactions (P1, before lines because lines need movement)

**Independent test**: vehicles on a road follow, queue, take turns at junctions, never overlap; parallel = sequential.

- [X] T013 [US3] Lane registry and lane lists (sorted insert by S and id, unlink, head/tail) in `src/Talt.Core/Vehicles/RoadVehicleSystem.Lanes.cs` (research R2)
- [X] T014 [US3] `RoutePlanner` (node ahead, drive-through goals through P/Q, nearest depot) over `RoadRouter` in `src/Talt.Core/Vehicles/RoutePlanner.cs`; tests in `RoutePlannerTests.cs` (research R6)
- [X] T015 [US3] Phase 1 drive step: obstacle search (leader, next lanes, stop line, stop place, dead end), speed candidates with hard/soft rules, road speed limits, curve and ramp speed shares (FR-016), crossing into next lanes by geometry/route, junction request/release flags (research R3, R4, R5) in `src/Talt.Core/Vehicles/RoadVehicleSystem.Drive.cs`
- [X] T016 [US3] Phase 2/3: apply lane changes, junction grants (smallest key), dead-end U-turns with room check, depot exits/entries, urgent routes within the search budget, stuck notice and turnaround (research R7, R8) in `RoadVehicleSystem.Events.cs`
- [X] T017 [US3] `CheckInvariants` (sorted lanes, gaps, vehicles on road links, holders consistent) in `RoadVehicleSystem.cs`
- [X] T018 [P] [US3] Tests: following distance, queue behind a stopped vehicle, junction FCFS and ties, crossing and T deadlock month test, overlap property test over random grids, parallel = sequential, stuck turnaround in `tests/Talt.Core.Tests/Vehicles/DrivingTests.cs`, `JunctionTests.cs`, `RoadVehicleDeterminismTests.cs`

## Phase 5: User Story 2 — lines, loading, payments (P1)

**Independent test**: two bus stations, a line, two buses: deliveries paid, buses cycle.

- [X] T019 [US2] Line commands `createLine`, `deleteLine`, `renameLine`, `editLineStops`, `assignLine` (data-model.md Line and stops: name ≤ 32, 1–64 stops) in `src/Talt.Core/Vehicles/LineCommands.cs` with tests `LineTests.cs`
- [X] T020 [US2] Orders: next stop selection, skip unusable stops (kind, removed, unreachable) with a notice, arrival at bays (two places) and drive-through stops, unload → load steps with timer and min dwell, departure with a new route, profit from `TotalPaid` delta, money event with vehicle id, in `RoadVehicleSystem.Events.cs`; tests `StopTests.cs`
- [X] T021 [US2] Golden replay test of a map game with depot, stations, line and buses in `tests/Talt.Core.Tests/Determinism/RoadVehicleReplayTests.cs`

## Phase 6: User Story 4 — loading rules (P2)

- [X] T022 [US4] Full load / unload only / no loading behaviour in `RoadVehicleSystem.Events.cs`; tests in `StopTests.cs`

## Phase 7: User Story 5 — ageing, breakdowns, servicing (P2)

- [X] T023 [US5] Daily ageing, reliability decay, breakdown roll from a stateless hash, breakdown pulses and repair at the nearest depot or after the breakdown time, service due → depot → serviced, monthly value update and yearly profit rotation in `src/Talt.Core/Vehicles/RoadVehicleSystem.Daily.cs`; `setServiceInterval` command (data-model.md RoadVehicle.serviceInterval, 0–1000 days per contracts/commands.md); tests `ReliabilityTests.cs`

## Phase 8: User Story 6 — sell, clone, send to depot (P2)

- [X] T024 [US6] `sellVehicle`, `cloneVehicle`, `sendToDepot` in `VehicleCommands.cs` with tests

## Phase 9: User Story 7 — rerouting (P3)

- [X] T025 [US7] Lazy route validation at junctions, urgent replans, "no route" wait and retry, removal refused while vehicles stand on the tile; tests `RerouteTests.cs`

## Phase 10: Client

- [X] T026 Poses from `RoadVehicleSystem` in `src/Talt.Client/GameSession.cs` (interpolation, lane offset, bridge heights, model per vehicle model `vehicle:<id>` with fallback), audio keeps working
- [X] T027 [P] Bus and truck and depot models in `tools/Talt.ModelGen` and `src/Talt.Client/Rendering/Data/models.json`; depot placeholders appended to station placeholders
- [X] T028 Depot tool (toolbar + key) in `src/Talt.Client/Construction/` and `Input/Data/actions.json`
- [X] T029 DepotWindow, VehicleWindow, LineWindow, LinesWindow, line-building mode, vehicle and depot picking in `src/Talt.Client/Screens/`, `Picking/`
- [X] T030 Money texts above vehicles, notices as messages, breakdown smoke/sparks in `src/Talt.Client/Screens/MoneyTexts.cs`, `GameSession.cs`, `Rendering/`
- [X] T031 [P] Strings in `src/Talt.Client/Localization/Strings.en.json` and `Strings.es.json`
- [X] T032 `--seed-vehicles`, `--check-overlaps` in client `Program.cs`/`GameSetup.cs` and headless `HeadlessArguments.cs` (contracts/cli.md) using `src/Talt.Core/Vehicles/VehicleSeeder.cs`

## Phase 11: Polish

- [X] T033 [P] `RoadVehicleBenchmarks` (10 k / 50 k / 100 k, sequential and parallel) and `RouteBenchmarks` in `benchmarks/Talt.Benchmarks/`; allocation tests with `AllocationAssert`
- [X] T034 Docs: `docs/ARCHITECTURE.md` (road vehicles section, algorithm, numbers, layout; known limit: depots not in company value), `README.md` options, `docs/SPECKIT_GUIDE.md` status note
- [X] T035 Full test run, fingerprint gate, headless parallel/sequential check, client launched for the user's hand test

## Dependencies

Setup → Foundational → US1 → US3 → US2 → (US4, US5, US6, US7 in any order) → Client → Polish. Client tasks T026–T031
can start once US2 exists; T027 and T031 any time.

## Implementation strategy

MVP = US1 + US3 + US2 (buy, drive, earn) with the client's depot tool and windows; then the P2/P3 stories.
