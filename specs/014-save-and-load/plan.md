# Implementation Plan: Save and load

**Branch**: `014-save-and-load` (worktree branch `worktree-014-save-and-load`) | **Date**: 2026-11-28 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/014-save-and-load/spec.md`

## Summary

Players save a game under a name, autosave every 10 real minutes of unpaused play (keeping 3), quick
save/load with F5/F9, see each save's details and a map overview thumbnail, and load to continue
**exactly** where they stopped, including after updates. Technically: a binary `.talt` container
(fixed header, JSON details, compressed thumbnail, per-subsystem sections split into independently
compressed and checksummed 4 MiB blocks, section table at the end), written by explicit per-system
`Write`/`Read` code that stores a **verbatim image of all persistent state** — including the hidden
state that is not fingerprinted but drives future ticks (version stamps, budgeted caches, router jobs
in progress, pool free lists). Capture happens on the main thread at a tick boundary into pooled
buffers (≤ 100 ms); compression and disk writes run in the background. Loading verifies every checksum
first, decompresses in parallel, rebuilds a wired empty `GameSimulation` and overwrites it section by
section. Identical continuation is proven by fingerprint equality, byte-identical re-saves and
10 000-tick continuation tests on a rich fixture that uses every system (rail and destination cargo
included). A section contract with per-section versions, migrations, fixture saves and a field
manifest lets features 016 and 017 add their state (`CATL`, `LSTA`) safely.

## Technical Context

**Language/Version**: C# latest on .NET 10, nullable, warnings as errors.

**Primary Dependencies**: no new packages. `System.IO.Compression` (Deflate, Brotli; in the base
library), `System.IO.Hashing` (XxHash3; already referenced by Core), `System.Text.Json` (details
block, pending commands; already used).

**Storage**: local files `<ApplicationData>/TALT/saves/*.talt`; atomic replace through a `.tmp` file.

**Testing**: xUnit in `tests/Talt.Core.Tests/Saves/` (round trip, continuation, corruption, fixtures
and migrations, content remap, field manifest, allocation with `AllocationAssert`),
`tests/Talt.Client.Tests` (save store names and rotation, autosave scheduler, screens without GPU),
`tests/Talt.Headless.Tests` (save/load commands). Fixture saves in `tests/Talt.Core.Tests/Saves/Fixtures/`.

**Target Platform**: Windows, Linux, macOS (x64, arm64); saves are byte-portable (little-endian,
explicit layout).

**Project Type**: desktop game (Core library + client + headless tool).

**Performance Goals**: reference scenario (4096², 500 towns, 1 000 industries, 50 000 vehicles,
5 000 stations): save ≤ 3 s, load ≤ 5 s, autosave main-thread stall ≤ 100 ms, load screen with 200
saves ≤ 0.5 s (research R19).

**Constraints**: no allocation in capture once warm, in the autosave scheduler per frame, or in ticks
after load; existing golden fingerprints unchanged (SC-007); Core never reads the clock (the real save
time comes from the client).

**Scale/Scope**: ~35 Core types get `Write`/`Read` (about 700 fields classified); 13 sections; 3 new
client screens/dialogs; 2 headless commands; reference save about 340 MB uncompressed (estimate),
mostly zero tile layers.

## Constitution Check

*GATE: checked before Phase 0 and again after Phase 1 design.*

| Principle | Status | How |
|---|---|---|
| I. Core/Client separation | ✅ | Format, sections, thumbnail rendering (CPU) and verification in `Talt.Core.Saves`; files folder, scheduler, screens, camera section in the client. Core never touches the clock: save time is an input. |
| II. Performance | ✅ | Budgets in research R19 as acceptance criteria; `SaveLoadBenchmarks` on the reference game; capture allocation-free (`AllocationAssert`); numbers in `docs/ARCHITECTURE.md`; tick numbers after load re-measured. |
| III. Deterministic simulation | ✅ | Saving reads state at a tick boundary and never changes it (fingerprint before/after test); loaded games continue identically, parallel = sequential after load; no new simulation rules, so goldens do not change. |
| IV. Tested increments | ✅ | Tests written with each section; each user story independently testable (see tasks); bug fixes found by continuation tests get regression tests. |
| V. Readable C# | ✅ | Explicit `Write`/`Read` per type in `*.Save.cs` partial files next to the type; one primitive API; XML docs on public save API. No unsafe code (span copies via `MemoryMarshal`). |
| VI. Original content | ✅ | Own format and code; no third-party code. |
| VII. Data-driven content | ✅ | Overview colours in a data file; autosave choices are player settings; content referenced by stable ids (R10). |
| VIII. Living documentation | ✅ | `docs/ARCHITECTURE.md` "Save and load (feature 014)" section (format, sections, budgets, numbers); `docs/VISION.md` decisions row (done in clarify); README options (`--load`, headless commands). |

Post-design re-check (after Phase 1): ✅ no violations. The one added complexity — saving caches and
in-progress jobs instead of rebuilding them — is justified in research R1 and in Complexity Tracking.

## Project Structure

### Documentation (this feature)

```text
specs/014-save-and-load/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── save-format.md      # byte layout of .talt v1
│   ├── save-sections.md    # section registry and rules for 016/017 and later features
│   └── save-api.md         # Core API, client pieces, headless commands, localisation keys
├── checklists/requirements.md
└── tasks.md                # /speckit-tasks
```

### Source Code (repository root)

```text
src/Talt.Core/
├── Saves/                         # new
│   ├── SaveGame.cs                # Capture / WriteAsync / Save / Peek / Load
│   ├── SaveFile.cs                # header, details, thumbnail, table, block codec, checksums
│   ├── SaveBuffer.cs, SaveBufferPool.cs
│   ├── SaveWriter.cs, SaveReader.cs, SaveFormatException.cs
│   ├── SaveSections.cs            # registry, restore order, ISaveSection
│   ├── Sections/*.cs              # one small adapter per section id → the owners' Write/Read
│   ├── ContentIdentity.cs         # CNTN, ContentMap
│   ├── MapOverview.cs             # thumbnail (CPU)
│   ├── SaveDetails.cs, SaveLoadResult.cs, SavePeek.cs
│   └── Data/overview-colors.json
├── Simulation/GameSimulation.cs   # restore construction path (R9)
├── Simulation/GameSimulation.Save.cs, VehicleFleet.Save.cs
├── Maps/MapData.Save.cs
├── Construction/ConstructionState.Save.cs, TileObjects.Save.cs, UndoRecord.Save.cs
├── Roads/RoadLayer.Save.cs; Network/RoadNetwork.Save.cs, RoadRouter.Save.cs, TransportNetwork.Save.cs
├── Rails/RailLayer.Save.cs, RailBlocks.Save.cs, RailDepotRegistry.Save.cs, TrainSystem.Save.cs, Train.Save.cs
├── Towns/TownRegistry.Save.cs, TownBuildings.Save.cs
├── Industries/IndustryFleet.Save.cs
├── Stations/StationRegistry.Save.cs, CargoHold.Save.cs
├── Finance/FinanceSystem.Save.cs, Ledger.Save.cs, RunningCosts.Save.cs (+ company finance)
├── Vehicles/RoadVehicleSystem.Save.cs, DepotRegistry.Save.cs, LineRegistry.Save.cs, LaneIndexMap.Save.cs
└── Demand/DestinationTables.Save.cs, CargoRouter.Save.cs, RouteTable.Save.cs, RoutingProblem.Save.cs

src/Talt.Client/
├── Saves/SaveStore.cs, AutosaveScheduler.cs, SaveController.cs (capture → background write, status)
├── Screens/SaveScreen.cs, LoadScreen.cs, ConfirmDialog.cs; InGameScreen (pause menu), Screens.cs (main menu)
├── GameSetup.cs (FromSave), GameSession.cs (unsaved progress, camera section), GameWindow.cs (routing)
├── PlayerSettings.cs (autosave settings, version bump), Input bindings (quickSave, quickLoad)
└── Localization/Strings.en.json, Strings.es.json

tools/Talt.Headless/   # run --save, load command
benchmarks/Talt.Benchmarks/SaveLoadBenchmarks.cs
tests/Talt.Core.Tests/Saves/   # SaveTestGames (rich fixture), round trip, continuation, corruption,
                               # fixtures + migration, content remap, manifest, allocation
tests/Talt.Client.Tests/Saves/, tests/Talt.Headless.Tests/
ci/check-fingerprints.ps1, ci/golden-fingerprints.json  # add fixture-save load fingerprints
```

**Structure Decision**: the save code of each system lives in a partial file next to the system
(`X.Save.cs`), because the verbatim image needs private fields and keeping it next to the fields makes
"did I save the new field?" obvious in review. The container, primitives, registry and orchestration
live in the new `Talt.Core/Saves/` folder. Types that are not yet `partial` become `partial`.

## Implementation phases (for /speckit-tasks)

1. **Foundation**: primitives, buffers, container read/write with checksums and codecs, corruption
   handling, content identity, section registry, restore construction path, field manifest test.
   Codec measurement (R5).
2. **US1 (P1) save and load**: sections for every system in dependency order, each with its round-trip
   test as it lands (map-less `CORE` first, then map/construction, roads, rail, towns, industries,
   stations, finance, road vehicles, trains, demand); rich fixture; continuation tests; headless
   `--save` / `load`; client save/load screens, pause menu, main menu, `--load`, camera section,
   unsaved-progress confirmation, quick save/load keys.
3. **US2 (P1) autosave**: background write, scheduler, rotation, settings UI, stall measurement.
4. **US3 (P2) load screen details**: details block, map overview thumbnail, peek-only listing, delete.
5. **US4 (P2) compatibility**: migration chain infrastructure with a test-only section version, the
   committed `rich-v1.talt` fixture, missing-section defaults, too-new refusal, content remap with
   reordered content, the 016/017 contract in ARCHITECTURE.md.
6. **US5 (P3) corruption**: exhaustive corruption tests (every block, header, table, truncations,
   empty, unrelated file), damaged rows in the list, interrupted write safety.
7. **Polish**: benchmarks and numbers, CI fingerprint gate with the fixture save, docs, README,
   hand test with the developer.

## Complexity Tracking

| Added complexity | Why needed | Simpler alternative rejected because |
|---|---|---|
| Saving caches, version stamps and in-progress router jobs verbatim | They are not fingerprinted but decide future ticks (research R1); FR-003 requires identical continuation | Rebuilding them on load would make loaded games diverge some ticks later |
| Field manifest test | Forces every new field (016, 017 and later) to be classified for saving | Relying only on fingerprints misses non-hashed state |
| Per-block parallel compression | Save ≤ 3 s and load ≤ 5 s on ~340 MB | A single stream is single-threaded and several times slower |
| Second construction path in `GameSimulation` | Restore needs the exact same wiring and event order without generating a world | A separate loader would duplicate and drift from the wiring |
