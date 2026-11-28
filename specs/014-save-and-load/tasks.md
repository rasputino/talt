# Tasks: Save and load

**Input**: Design documents from `specs/014-save-and-load/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/ (save-format, save-sections, save-api), quickstart.md

**Tests**: required (constitution IV): Core logic gets xUnit tests with or before the code.

**Organization**: by user story; each story is an independently testable increment.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on unfinished tasks)
- **[Story]**: US1–US5 from spec.md

---

## Phase 1: Setup

- [X] T001 Create `src/Talt.Core/Saves/` and `tests/Talt.Core.Tests/Saves/` folders, add `Saves/Data/*.json` as embedded resource in `src/Talt.Core/Talt.Core.csproj`, and an empty `tests/Talt.Core.Tests/Saves/Fixtures/` copied to output in `tests/Talt.Core.Tests/Talt.Core.Tests.csproj`
- [X] T002 [P] Add `SaveFormatException`, `SaveKind`, `SaveSectionId` (four-char packing, `ToString`) in `src/Talt.Core/Saves/SaveSectionId.cs` and `src/Talt.Core/Saves/SaveFormatException.cs` per data-model.md `SaveSectionId`, `SaveKind`

---

## Phase 2: Foundational (blocks every story)

- [X] T003 [P] Tests for `SaveBuffer`/`SaveWriter`/`SaveReader` primitives (all types, null vs empty strings, arrays across 4 MiB segment boundaries, `ReadInto` length mismatch fails, dictionary helpers keep enumeration order, reading past the end throws `SaveFormatException`) in `tests/Talt.Core.Tests/Saves/SavePrimitivesTests.cs`
- [X] T004 Implement `SaveBuffer` (pooled 4 MiB segments) and `SaveBufferPool` (rent, return, trim) in `src/Talt.Core/Saves/SaveBuffer.cs` and `src/Talt.Core/Saves/SaveBufferPool.cs` (data-model.md `SaveBuffer`)
- [X] T005 Implement `SaveWriter` and `SaveReader` (little-endian primitives, unmanaged arrays via `MemoryMarshal`, strings, `DeterministicRandom` state, dictionary helpers, `Fail`) in `src/Talt.Core/Saves/SaveWriter.cs` and `src/Talt.Core/Saves/SaveReader.cs`; add internal state get/set to `src/Talt.Core/Determinism/DeterministicRandom.cs`
- [X] T006 [P] Tests for the container: header/details/thumbnail/table round trip, multi-block sections, codecs 0/1/2, every checksum verified before any section is decoded, `NotASave`/`Damaged`/`TooNew` statuses, atomic `.tmp` replace leaves the old file on failure, in `tests/Talt.Core.Tests/Saves/SaveFileTests.cs`
- [X] T007 Implement `SaveFile` (write: stream sections block by block with parallel compression, table at the end, `.tmp` + flush + `File.Move` overwrite; read: header, table, verify all checksums, parallel decompression into `SaveBuffer`s) in `src/Talt.Core/Saves/SaveFile.cs` per contracts/save-format.md
- [X] T008 Measure Deflate Fastest vs Brotli q1 on the populated reference snapshot (small console benchmark method in `benchmarks/Talt.Benchmarks/SaveLoadBenchmarks.cs` `[Benchmark] Codec*`) and record the decision in research.md R5 and `docs/ARCHITECTURE.md`
- [X] T009 [P] Implement `SaveDetails` (JSON, camelCase, unknown properties ignored, 64 KiB limit) and `SaveDetailsInput` in `src/Talt.Core/Saves/SaveDetails.cs` (data-model.md `SaveDetails.Name`, `SaveDetails.Kind`, `SaveDetails.Money`, `SaveDetails.MapWidth`, `SaveDetails.MapDepth`, `SaveDetails.Tick`)
- [X] T010 Implement `ISaveSection`, `SaveSections` registry (restore order, version per id, unknown id → `TooNew`, missing known id → `SetDefaults`) in `src/Talt.Core/Saves/SaveSections.cs` (data-model.md `ISaveSection`)
- [X] T011 Add the restore construction path to `GameSimulation` (same wiring and event registration order, no generation work) and internal "for restore" factories `IndustryFleet.CreateForRestore`, `TownRegistry.CreateForRestore`, `MapData` constructor from saved arrays and sites in `src/Talt.Core/Simulation/GameSimulation.cs`, `src/Talt.Core/Industries/IndustryFleet.cs`, `src/Talt.Core/Towns/TownRegistry.cs`, `src/Talt.Core/Maps/MapData.cs` (research R9)
- [X] T012 Implement `SaveGame` orchestration (`Capture`, `WriteAsync`, `Save`, `Load(path|stream)`, `Peek`) and `SaveLoadResult`/`SavePeek` in `src/Talt.Core/Saves/SaveGame.cs`, `src/Talt.Core/Saves/SaveLoadResult.cs` per contracts/save-api.md (data-model.md `SaveLoadResult.Status`)
- [X] T013 [P] Field manifest test (reflection over all saved types: every instance field listed with `saved|derived|scratch|ref`, no stale entries) in `tests/Talt.Core.Tests/Saves/SaveFieldManifestTests.cs` with `tests/Talt.Core.Tests/Saves/SaveFieldManifest.txt` (research R12)
- [X] T014 [P] Rich fixture builder `SaveTestGames` (small map with towns and industries; seeded stations, road vehicles on lines and trains with signals; destination cargo in transit; a loan; a removed station; an undo record; helper to run to router build stages 1–3 and to a pending apply) and the round-trip helper (save → load → fingerprint → re-save byte comparison per section → continue N ticks with scripted commands, sequential and parallel) in `tests/Talt.Core.Tests/Saves/SaveTestGames.cs` and `tests/Talt.Core.Tests/Saves/RoundTrip.cs` (research R13)

**Checkpoint**: an empty game with only `CORE` can be written and read; the harness for every later section exists.

---

## Phase 3: User Story 1 — Save a game and continue it later (P1) 🎯 MVP

**Goal**: a saved game loads with the same fingerprint and continues identically, in Core, the headless tool and the client.

**Independent test**: `SaveTestGames.Rich` round trip (fingerprint at load, identical re-save, 10 000 identical further ticks, parallel = sequential); quickstart §2 and §5 steps 1–2.

### Sections (each with its round-trip test, in restore order)

- [X] T015 [US1] `CORE` section: settings JSON, tick, clock, root random, pending commands via `CommandSerializer` + inbox submission id, `VehicleFleet`, `TransportNetwork`; test map-less scenario round trip in `src/Talt.Core/Saves/Sections/CoreSection.cs`, `src/Talt.Core/Simulation/GameSimulation.Save.cs`, `src/Talt.Core/Simulation/VehicleFleet.Save.cs`, `src/Talt.Core/Network/TransportNetwork.Save.cs`, `src/Talt.Core/Commands/CommandInbox.cs`, test `tests/Talt.Core.Tests/Saves/CoreSectionTests.cs`
- [X] T016 [US1] `CNTN` section and `ContentMap` (id lists for cargo, industry kinds, building types, station pieces, warehouses, road types, rail types, road vehicle models, rolling stock; rules fingerprints; identity fast path) in `src/Talt.Core/Saves/ContentIdentity.cs` (data-model.md `ContentMap`)
- [X] T017 [US1] `MAP_` section: `MapData` tiles, settings, statistics, sites; `ConstructionState` year and companies; `TileObjects`; `UndoRecord` in `src/Talt.Core/Maps/MapData.Save.cs`, `src/Talt.Core/Construction/ConstructionState.Save.cs`, `src/Talt.Core/Construction/TileObjects.Save.cs`, `src/Talt.Core/Construction/UndoRecord.Save.cs`, `src/Talt.Core/Saves/Sections/MapSection.cs`
- [X] T018 [US1] `ROAD` section: `RoadLayer`, `RoadNetwork` verbatim (nodes, edges, numbering, free lists, version), `RoadRouter` cache and eviction queue in `src/Talt.Core/Roads/RoadLayer.Save.cs`, `src/Talt.Core/Network/RoadNetwork.Save.cs`, `src/Talt.Core/Network/RoadRouter.Save.cs`, `src/Talt.Core/Saves/Sections/RoadSection.cs`
- [X] T019 [US1] `RAIL` section: `RailLayer` (+ `EverUsed`), `RailBlocks`, `RailDepotRegistry` in `src/Talt.Core/Rails/RailLayer.Save.cs`, `src/Talt.Core/Rails/RailBlocks.Save.cs`, `src/Talt.Core/Rails/RailDepotRegistry.Save.cs`, `src/Talt.Core/Saves/Sections/RailSection.cs`
- [X] T020 [US1] `TOWN` section: `TownRegistry` (arrays, names, random streams, capacity) and `TownBuildings` (arrays, lookups) in `src/Talt.Core/Towns/TownRegistry.Save.cs`, `src/Talt.Core/Towns/TownBuildings.Save.cs`, `src/Talt.Core/Saves/Sections/TownSection.cs`
- [X] T021 [US1] `INDY` section: `IndustryFleet` arrays, tile owners, slots, free sites, random stream in `src/Talt.Core/Industries/IndustryFleet.Save.cs`, `src/Talt.Core/Saves/Sections/IndustrySection.cs`
- [X] T022 [US1] `STNS` section: `StationRegistry` (stations, pieces, lookups, name counters, removed centres, coverage grid and caches, groups and ratings, packet pools with free lists, destination buckets and pools, pending supply, carry, totals, reroute cursor, debug hold) and `CargoHold` in `src/Talt.Core/Stations/StationRegistry.Save.cs`, `src/Talt.Core/Stations/CargoHold.Save.cs`, `src/Talt.Core/Saves/Sections/StationSection.cs`
- [X] T023 [US1] `FINA` section: `FinanceSystem` levels and totals, per-company finance (loan, ledgers, game over), `Ledger`, `RunningCosts` in `src/Talt.Core/Finance/FinanceSystem.Save.cs`, `src/Talt.Core/Finance/Ledger.Save.cs`, `src/Talt.Core/Finance/RunningCosts.Save.cs`, `src/Talt.Core/Saves/Sections/FinanceSection.cs`
- [X] T024 [US1] `RVEH` section: `RoadVehicleSystem` (hot/next/cold arrays, holds, routes, lanes and `LaneIndexMap`, link tables, holders/requests, route cache, budgets, version stamps, depot regions, components, depot carry, notices, occupied set), `DepotRegistry`, `LineRegistry` with lines and orders in `src/Talt.Core/Vehicles/RoadVehicleSystem.Save.cs`, `src/Talt.Core/Vehicles/DepotRegistry.Save.cs`, `src/Talt.Core/Vehicles/LineRegistry.Save.cs`, `src/Talt.Core/Vehicles/LaneIndexMap.Save.cs`, `src/Talt.Core/Saves/Sections/RoadVehicleSection.cs`
- [X] T025 [US1] `TRAI` section: `TrainSystem` (trains in id order, consists, steps, reservations, holds, block ownership, link tables, maintenance carry, budgets, counters) and `Train` in `src/Talt.Core/Rails/TrainSystem.Save.cs`, `src/Talt.Core/Rails/Train.Save.cs`, `src/Talt.Core/Saves/Sections/TrainSection.cs`
- [X] T026 [US1] `DMND` section: `DestinationTables` (rule tables, cursor), `CargoRouter` schedule and versions, current tables, pending `_nextPool` result (wait for the job first), `RoutingProblem`s and `LinkCollector` while `_buildStage > 0` in `src/Talt.Core/Demand/DestinationTables.Save.cs`, `src/Talt.Core/Demand/CargoRouter.Save.cs`, `src/Talt.Core/Demand/RouteTable.Save.cs`, `src/Talt.Core/Demand/RoutingProblem.Save.cs`, `src/Talt.Core/Saves/Sections/DemandSection.cs`
- [X] T027 [US1] Complete `SaveFieldManifest.txt` for every type above and make the manifest test pass in `tests/Talt.Core.Tests/Saves/SaveFieldManifest.txt`

### Proof of identical continuation

- [X] T028 [US1] Round-trip tests on `SaveTestGames.Rich` at mid-day, day, month boundary, each router build stage and a pending apply: fingerprint at load, per-section byte-identical re-save, identical continuation over 2 000 ticks (fast) and 10 000 ticks (long), sequential = parallel after load, and a game-over game that loads still in game over and still refuses commands, in `tests/Talt.Core.Tests/Saves/ContinuationTests.cs`
- [X] T029 [P] [US1] Dictionary order tests (R3): `_pendingSupply`, `_requests`, `_holders`, `_removedCentre` enumerate identically after load and after removals + inserts in `tests/Talt.Core.Tests/Saves/DictionaryOrderTests.cs`
- [X] T030 [P] [US1] Saving does not change the game: fingerprint and a 500-tick continuation equal with and without a capture; existing goldens unchanged (SC-007) in `tests/Talt.Core.Tests/Saves/CaptureIsPureTests.cs`
- [X] T031 [P] [US1] Allocation tests with `AllocationAssert`: warm capture of the rich game allocates nothing; ticks of a loaded game allocate nothing in `tests/Talt.Core.Tests/Saves/CaptureTests.cs`

### Headless tool

- [X] T032 [US1] `run` gains `--seed-stations/--seed-vehicles/--seed-trains` (same seeders as the client) and `--save <file>`; and `load <file> --ticks --checkpoint-every [--save] [--sequential]` with the contract's output format in `tools/Talt.Headless/HeadlessArguments.cs`, `tools/Talt.Headless/Program.cs`; tests in `tests/Talt.Headless.Tests/SaveLoadCommandTests.cs`

### Client

- [X] T033 [US1] `SaveStore`: saves folder under `ApplicationData/TALT/saves`, safe file names (R15), case-insensitive clash detection, list via `Peek`, delete, quick save path; tests in `src/Talt.Client/Saves/SaveStore.cs`, `tests/Talt.Client.Tests/Saves/SaveStoreTests.cs`
- [X] T034 [US1] `SaveController`: capture on the main thread after the frame's ticks, background `WriteAsync`, one write at a time, result notifications; `GameSession.HasUnsavedProgress`, camera `CAMR` extra section in `src/Talt.Client/Saves/SaveController.cs`, `src/Talt.Client/GameSession.cs`
- [X] T035 [US1] `GameSetup.FromSave` (build `GeneratedGame` from a loaded simulation: options from settings, `WorldBuilder.Build`, heightmap) and window routing for loading in a background task with progress, replacing a running game cleanly in `src/Talt.Client/GameSetup.cs`, `src/Talt.Client/GameWindow.cs`
- [X] T036 [US1] `SaveScreen` (name field with suggested name, existing saves, overwrite confirmation) and `LoadScreen` (list newest first, load, confirmation when unsaved progress) and a reusable confirmation dialog in `src/Talt.Client/Screens/SaveLoadWindows.cs`
- [X] T037 [US1] Pause menu "Save" and "Load", main menu "Load game" enabled, unsaved-progress confirmation on main menu/new game/load, loaded game starts paused at the saved camera in `src/Talt.Client/Screens/InGameScreen.cs`, `src/Talt.Client/Screens/Screens.cs`
- [X] T038 [US1] Quick save F5 / quick load F9 as data-driven bindings `quickSave`, `quickLoad` (rebindable) in `src/Talt.Client/Input/` and `src/Talt.Client/Screens/InGameScreen.cs`
- [X] T039 [US1] `--load <file>` command line option in `src/Talt.Client/Program.cs` and README option list
- [X] T040 [P] [US1] Localisation keys from contracts/save-api.md in `src/Talt.Client/Localization/Strings.en.json` and `src/Talt.Client/Localization/Strings.es.json`
- [X] T041 [P] [US1] Client tests without GPU: save/load screens build and route, overwrite and unsaved confirmations, quick save binding in `tests/Talt.Client.Tests/Saves/SaveScreensTests.cs`

**Checkpoint**: US1 works end to end; run the app (quickstart §5 steps 1–3).

---

## Phase 4: User Story 2 — Autosave (P1)

**Goal**: autosave every N real unpaused minutes, keeping the last K, without visible stalls.

**Independent test**: scheduler tests with simulated time; rotation keeps exactly K newest; capture stall measured ≤ 100 ms on the reference game.

- [X] T042 [P] [US2] `AutosaveScheduler` tests (paused time ignored, interval at every speed, off, reset after capture, skip while a write runs, no allocation per update with `AllocationAssert`) in `tests/Talt.Client.Tests/Saves/SaveStoreTests.cs`
- [X] T043 [US2] Implement `AutosaveScheduler` in `src/Talt.Client/Saves/AutosaveScheduler.cs` and hook it into the frame loop in `src/Talt.Client/GameWindow.cs`
- [X] T044 [US2] Autosave rotation (`autosave-1..N`, oldest by details time replaced, manual saves never deleted) in `src/Talt.Client/Saves/SaveStore.cs` with tests in `tests/Talt.Client.Tests/Saves/SaveStoreTests.cs`
- [X] T045 [US2] Settings `AutosaveMinutes`, `AutosaveKeep` with `PlayerSettings.CurrentVersion` bump and defaults for older files, settings screen rows in `src/Talt.Client/PlayerSettings.cs` and the settings screen (data-model.md `AutosaveSettings.AutosaveMinutes`, `AutosaveSettings.AutosaveKeep`)
- [X] T046 [US2] Save failure handling (disk full, permissions): notification, game continues, no `.tmp` left in `src/Talt.Client/Saves/SaveController.cs`

**Checkpoint**: autosave visible in the load list; no hitch (quickstart §5 step 4).

---

## Phase 5: User Story 3 — Recognise saves on the load screen (P2)

**Goal**: each row shows name, real time, in-game date, company, money, map size and a map overview thumbnail; 200 saves open in ≤ 0.5 s.

**Independent test**: peek-only listing of 200 generated saves within budget; thumbnails for client and headless saves.

- [X] T047 [P] [US3] `MapOverview` tests (size, letterboxing for non-square maps, roads/rail/towns/water coloured, deterministic output) in `tests/Talt.Core.Tests/Saves/MapOverviewTests.cs`
- [X] T048 [US3] Implement `MapOverview.Render` with colours from `src/Talt.Core/Saves/Data/overview-colors.json` (validated at load) in `src/Talt.Core/Saves/MapOverview.cs` (data-model.md `SaveThumbnail`); called from `SaveGame.Capture`
- [X] T049 [US3] Load screen rows with details and lazily created thumbnail textures, autosave/quick save marks, delete with confirmation in `src/Talt.Client/Screens/SaveLoadWindows.cs`
- [X] T050 [P] [US3] Peek performance test: 200 saves listed under 0.5 s (generous CI margin, Category not Gpu) in `tests/Talt.Core.Tests/Saves/SaveStorageTests.cs`

---

## Phase 6: User Story 4 — Older saves still load (P2)

**Goal**: per-section migrations, missing sections get defaults, newer saves refused, content reordering survives.

**Independent test**: committed `rich-v1.talt` fixture loads and continues; a test-only section version 2 migrates from 1; reordered content loads.

- [X] T051 [US4] Migration chain support in `SaveSections` (reader receives the stored version; tests register a test-only section with v1 → v2 migration) in `src/Talt.Core/Saves/SaveSections.cs`, test `tests/Talt.Core.Tests/Saves/CompatibilityTests.cs`
- [X] T052 [US4] Provide fixture `tests/Talt.Core.Tests/Saves/Fixtures/rich-v1.talt` (written by a generator test marked `Skip` unless an environment variable is set) and a test that loads it, checks its recorded fingerprint and continues 1 000 ticks in `tests/Talt.Core.Tests/Saves/CompatibilityTests.cs`
- [X] T053 [US4] Missing-section defaults and too-new refusal (unknown id, higher section version, higher container version) tests in `tests/Talt.Core.Tests/Saves/CompatibilityTests.cs`
- [X] T054 [US4] Content remap: load a save into reordered content (same ids), check entity content ids are preserved; missing id refused naming it; changed rules → `LoadedWithChangedData` in `tests/Talt.Core.Tests/Saves/ContentRemapTests.cs` and remapping in each section reader that stores content indices

---

## Phase 7: User Story 5 — Corrupted saves are reported, never crash (P3)

**Goal**: every damage is detected before state is built; the running game is untouched.

**Independent test**: exhaustive corruption suite.

- [X] T055 [US5] Corruption suite: flip one byte in the header, details, thumbnail, table and every block; truncate at many offsets; empty file; random bytes; another file format; each returns a status, never throws, never builds state in `tests/Talt.Core.Tests/Saves/CorruptionTests.cs`
- [X] T056 [US5] Damaged rows in the load screen and clear localised messages on failed loads, running game untouched in `src/Talt.Client/Screens/SaveLoadWindows.cs`, `src/Talt.Client/GameWindow.cs`
- [X] T057 [US5] Interrupted write test (writer throws midway: target file intact, `.tmp` removed) in `tests/Talt.Core.Tests/Saves/SaveStorageTests.cs`

---

## Phase 8: Polish and cross-cutting

- [X] T058 `SaveLoadBenchmarks` on the reference game (50 000 buses + seeded trains, 4096²): capture, write, read+verify+decompress, restore, file size, allocation; record in `docs/ARCHITECTURE.md` in `benchmarks/Talt.Benchmarks/SaveLoadBenchmarks.cs`
- [X] T059 Re-run tick benchmarks on a loaded reference game (no regression vs a fresh game) and record in `docs/ARCHITECTURE.md`
- [X] T060 CI: load the fixture save on every platform in `ci/check-fingerprints.ps1` with expected values in `ci/golden-fingerprints.json`
- [X] T061 `docs/ARCHITECTURE.md` "Save and load (feature 014)" section (format, sections, restore path, verbatim rule, field manifest, contract for 016/017, budgets and numbers), solution layout update; README options (`--load`, headless `--save`, `load`)
- [X] T062 Full test suite `dotnet test --filter "Category!=Gpu"` and `pwsh ci/check-fingerprints.ps1` pass; goldens unchanged
- [X] T063 Hand test with the developer: launch the client and walk through quickstart §5

---

## Local acceptance record

Windows x64 Release build, non-GPU tests and the local fingerprint gate passed. The gate covers both the pre-016 and current fixtures. A headless run saved at tick 6,000 and matched ten successive checkpoints through tick 16,000 (final fingerprint `A4A2CA0F0371E4C6`). The developer reported the Spanish save/load, quick slots and autosave hand test working. Performance measurements and the paired original/restored tick comparison are in `docs/ARCHITECTURE.md`. The main integration adds feature 017 statistics in `LSTA` and migrates the older ledger and road-vehicle layouts.

## Dependencies & execution order

- Phase 1 → Phase 2 → US1 (Phase 3). US2–US5 depend on US1's Core pieces (T015–T028) but not on each other.
- Within US1, sections T015–T026 go in restore order (each depends on the previous section's systems being restorable); T027–T031 after all sections; client tasks T033–T041 after T012 and can proceed in parallel with sections once T015 exists (they only need `SaveGame`).
- US2 needs T034; US3 needs T009 and T049 needs T036; US4 needs all sections; US5 needs T007/T012 and T036.

## Parallel opportunities

- T003, T006, T009, T013, T014 in parallel after T002.
- T029, T030, T031 in parallel after T028.
- T040, T041 in parallel with T036–T039.
- T042, T047, T050 in parallel with their implementation tasks' predecessors.

## Implementation strategy

- **MVP**: Phases 1–3 (manual save and load of every system, headless and client).
- Then US2 (autosave, also P1), US3, US4, US5, polish with benchmarks and the hand test.
- Stop and validate at each checkpoint; run the app after US1 and US2.
