# Tasks: Destination demand model

**Input**: Design documents from `specs/013-destination-demand/` (plan, research R1–R14, data-model, contracts,
quickstart). Tests are required by the constitution (principle IV) and are written with the code.

## Phase 1: Setup

- [X] T001 Create `src/Talt.Core/Demand/Data/demand.json` per contracts/data-files.md and embed it in `src/Talt.Core/Talt.Core.csproj`
- [X] T002 Implement `DemandRules` (parse, validate with file/field names, `Default`, `Fingerprint`) in `src/Talt.Core/Demand/DemandRules.cs` covering data-model.md DemandRules.cargo, maxDestinations, rangeTiles, expiryDays, routing.*, overlay.pairs
- [X] T003 [P] Tests for `DemandRules` (defaults, every validation error) in `tests/Talt.Core.Tests/Demand/DemandRulesTests.cs`

## Phase 2: Foundational

- [X] T004 Implement exact cumulative apportionment (research R10) in `src/Talt.Core/Demand/Apportion.cs` with tests in `tests/Talt.Core.Tests/Demand/ApportionTests.cs` (sums exact, long-run proportions)
- [X] T005 Add `Credit` to `HeldCargo` and proportional credit splitting/merging in `src/Talt.Core/Stations/CargoHold.cs` (data-model.md HeldCargo.Credit), hash it
- [X] T006 Add `ITransferValuer` and `StationRegistry.TransferValuer`; implement it in `src/Talt.Core/Finance/FinanceSystem.cs` (Quote without booking)
- [X] T007 Register new simulation types in `tests/Talt.Core.Tests/Determinism/NoFloatingPointStateTests.cs`

## Phase 3: User Story 1 — towns generate passengers and mail for other towns (P1)

**Goal**: destination tables per town and cargo; supply gives each unit a destination; unreachable units are unserved.
**Independent test**: tables match hand computation; a stop reaching A, B, C receives the right split.

- [X] T008 [US1] Implement `DestinationTables` (town tables for building cargo, industry tables for goods, full build at start, daily slices, statistics rotation, hashing) in `src/Talt.Core/Demand/DestinationTables.cs` (data-model.md DestinationTables.ownShare, dest, share, cum, counter, movedThis/Last, unservedThis/Last)
- [X] T009 [P] [US1] Tests: 10/8/5/2 example, population effect, own share, top-N cut, range, goods tables only towns accepting goods, slices cover all towns in a month, in `tests/Talt.Core.Tests/Demand/DestinationTablesTests.cs`
- [X] T010 [US1] Implement the destination stock (buckets keyed by group, cargo, hop; packet pool; AddPacket merge; hash only when used; group rebuild moves stock) in `src/Talt.Core/Stations/StationRegistry.Destinations.cs` (data-model.md Destination packet fields)
- [X] T011 [US1] Route destination cargo in supply: gather building amounts per (group, town, cargo, origin) per day, split with `Apportion`, route lookup per unit group, unserved counting; industries return unrouted goods (`TakeProduction`); groups on no line keep 010 behaviour, in `src/Talt.Core/Stations/StationRegistry.Supply.cs`
- [X] T012 [US1] Tests: split at a stop with routes to A (other stop), B, C; nothing moved without routes; goods returned to the industry; debug service unchanged, in `tests/Talt.Core.Tests/Demand/DestinationSupplyTests.cs`

## Phase 4: User Story 2 — cargo travels to its destination with transfers (P1)

**Goal**: link graph, routes, vehicles load/unload by route, payment once, transfer credits.
**Independent test**: A–B and B–C lines; A→C passengers transfer at B and are paid once at C.

- [X] T013 [US2] Implement `ILinkSource`/`LinkCollector` and `LinkGraphSnapshot` (nodes = group keys, CSR edges with ticks and capacity, serving towns, flow estimate, destination set with radius) in `src/Talt.Core/Demand/ILinkSource.cs` and `src/Talt.Core/Demand/LinkGraphSnapshot.cs` (data-model.md LinkGraphSnapshot)
- [X] T014 [US2] Implement `RouteAssignment` (two-label backward Dijkstra, pruning, MSA iterations, hops and shares) in `src/Talt.Core/Demand/RouteAssignment.cs` and `RouteTable` in `src/Talt.Core/Demand/RouteTable.cs` (data-model.md RouteTable fields)
- [X] T015 [P] [US2] Assignment tests on hand-made graphs: chain, two parallel routes with saturation, own-town away hop, zero-capacity edge, unreachable, loop-free, in `tests/Talt.Core.Tests/Demand/RouteAssignmentTests.cs`
- [X] T016 [US2] Implement `CargoRouter` (sources, version, schedule, snapshot build into reused buffers, background work item, fixed apply tick, inline when sequential, carry copy, re-route of waiting packets, hashing) in `src/Talt.Core/Demand/CargoRouter.cs` (data-model.md CargoRouter schedule)
- [X] T017 [US2] `RoadVehicleSystem` implements `ILinkSource` (lines, stop rules, capacity from vehicles on line, speed, `LinkVersion`) in `src/Talt.Core/Vehicles/RoadVehicleSystem.Links.cs`
- [X] T018 [US2] `Load`/`Unload` with `nextStationId`: buckets by next hop, delivery only at groups serving the destination town (buildings of that town) and not the origin group, keep aboard when next hop is the next stop, transfer otherwise with leg credit, `UnloadResult` credits, in `src/Talt.Core/Stations/StationRegistry.Cargo.cs`
- [X] T019 [US2] Vehicles pass their next stop and book profit with credits in `src/Talt.Core/Vehicles/RoadVehicleSystem.Events.cs`
- [X] T020 [US2] Wire `DestinationTables` and `CargoRouter` into `src/Talt.Core/Simulation/GameSimulation.cs` (creation, day events, tick hook, fingerprint)
- [X] T021 [US2] Integration tests: three towns two lines transfer paid once with distance A–C; same-line multi-stop stays aboard; vehicle of a wrong line leaves cargo; UnloadOnly transfers by route; credits sum, in `tests/Talt.Core.Tests/Demand/TransferTests.cs`

## Phase 5: User Story 3 — goods are wanted by specific towns (P1)

**Goal**: goods get destination towns; undirected cargo unchanged.
**Independent test**: factory stop reaching a large and a small town splits by weight; goods not paid elsewhere.

- [X] T022 [US3] Goods supply through industry tables (uses T008/T011) and delivery only at the destination town; tests for split, refusal elsewhere and coal unchanged in `tests/Talt.Core.Tests/Demand/GoodsDestinationTests.cs`

## Phase 6: User Story 5 — demand redistributes when the network changes (P2)

**Goal**: routes follow line changes within the update period, deterministic with background jobs.
**Independent test**: adding a direct line moves new cargo within the period; parallel = sequential = inline.

- [X] T023 [US5] Tests: new line used within period; line deleted → re-route or expire; saturated corridor spreads; job applies at the fixed tick; parallel/sequential/inline fingerprints equal over 2 000 ticks, in `tests/Talt.Core.Tests/Demand/RouterDeterminismTests.cs`
- [X] T024 [US5] Expiry after `expiryDays` and conservation totals (generated = delivered + waiting + on board + lost), with a one-year conservation test, in `src/Talt.Core/Stations/StationRegistry.Destinations.cs` and `tests/Talt.Core.Tests/Demand/ConservationTests.cs`
- [X] T025 [US5] Allocation tests (steady-state ticks with routing, supply day, apply tick) with `AllocationAssert` in `tests/Talt.Core.Tests/Demand/DemandAllocationTests.cs`

## Phase 7: User Story 4 — see destinations and demand (P2)

**Goal**: station, vehicle and town windows show destinations, demand and served share.
**Independent test**: window figures equal the simulation queries.

- [X] T026 [US4] Query APIs (`DestinationWaiting`, table views, monthly demand, served share, held by destination) in `src/Talt.Core/Stations/StationRegistry.Destinations.cs` and `src/Talt.Core/Demand/DestinationTables.cs`, with tests in `tests/Talt.Core.Tests/Demand/DemandQueryTests.cs`
- [X] T027 [P] [US4] Station window destinations section in `src/Talt.Client/Screens/StationWindow.cs`
- [X] T028 [P] [US4] Vehicle window cargo by destination in `src/Talt.Client/Screens/VehicleWindows.cs`
- [X] T029 [P] [US4] Town window top destinations, demand, served share, goods demand in `src/Talt.Client/Screens/TownWindow.cs`
- [X] T030 [US4] Strings in `src/Talt.Client/Localization/Strings.en.json` and `Strings.es.json`

## Phase 8: User Story 6 — unserved demand overlay (P3)

- [X] T031 [US6] Core query of the top unserved town pairs in `src/Talt.Core/Demand/DestinationTables.cs` with a test
- [X] T032 [US6] `DemandOverlay` (dotted screen-space lines between projected town centres) and toggle binding in `src/Talt.Client/Screens/DemandOverlay.cs`, `src/Talt.Client/Screens/InGameScreen.cs`, `src/Talt.Client/Input/Data/actions.json`

## Phase 9: Polish

- [X] T033 Update goldens changed on purpose (RoadVehicleReplayTests and any map golden with lines); confirm map-less, `ci/golden-fingerprints.json` and `ci/sessions/windows-reference.json` unchanged with `pwsh ci/check-fingerprints.ps1`
- [X] T034 [P] `DemandBenchmarks` (tables month, snapshot build, assignment, supply day, re-route, full tick) on the 012 reference scenario in `benchmarks/Talt.Benchmarks/DemandBenchmarks.cs`
- [X] T035 Record numbers and the design in `docs/ARCHITECTURE.md`; decision log in `docs/VISION.md`
- [X] T036 Headless summary shows directed generated/delivered/expired/unserved in `tools/Talt.Headless`
- [ ] T037 Full `dotnet build` and `dotnet test --filter "Category!=Gpu"`; check SC-002 (RoadVehicleBenchmarks tick within the ×20 budget, client 60 FPS on 4096 with `--stats`); launch the client for the developer's hand check (quickstart.md); note the prune-radius limitation (research R5) in ARCHITECTURE.md

## Dependencies

Setup → Foundational → US1 → US2 (needs US1's stock and tables) → US3, US5 (need US2) → US4 → US6 → Polish.
US4 windows (T027–T029) are parallel with each other.

## Implementation strategy

MVP = US1 + US2 (destinations and transfers); then US3, US5, US4, US6; benchmarks and docs last.
