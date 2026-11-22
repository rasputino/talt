---

description: "Task list for feature 001 - Simulation Foundation"
---

# Tasks: Simulation Foundation

**Input**: Design documents from `specs/001-simulation-foundation/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md),
[data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**: Included. The constitution (principle IV) requires xUnit tests for all `Talt.Core` logic,
written with or before the code; determinism tests are acceptance criteria of this feature.

**Organization**: Tasks are grouped by user story so each story can be implemented and verified on
its own.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: User story the task belongs to (US1–US4)
- Paths are relative to the repository root

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Dependencies and project skeletons used by the rest of the feature

- [X] T001 Add `System.IO.Hashing` 10.0.15 to `Directory.Packages.props` and reference it from `src/Talt.Core/Talt.Core.csproj` (justification in plan.md, research R9)
- [X] T002 [P] Create console project `tools/Talt.Headless/Talt.Headless.csproj` referencing only `src/Talt.Core`, with a placeholder `Program.cs`; add it to `Talt.slnx` under a `/tools/` folder
- [X] T003 [P] Add `Arch` 2.1.0 to `Directory.Packages.props` and reference it only from `benchmarks/Talt.Benchmarks/Talt.Benchmarks.csproj` (research R5)
- [X] T004 [P] Create xUnit project `tests/Talt.Headless.Tests/Talt.Headless.Tests.csproj` referencing `tools/Talt.Headless` (so `Talt.Core.Tests` never depends on an executable); add it to `Talt.slnx` under `/tests/`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Deterministic primitives, integer simulation state and the `GameSimulation` core that every story builds on

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

### Tests for the foundation

- [X] T005 [P] Write tests for fixed-point helpers (unit conversions with explicit rounding, integer square root exact for 0..2^40 samples and perfect squares, overflow-safe `long` braking distance) in `tests/Talt.Core.Tests/Determinism/FixedMathTests.cs`
- [X] T006 [P] Write tests for `DeterministicRandom` (known reference sequence for a fixed seed committed as constants, `NextInt` bounds and unbiasedness smoke test, `Fork` streams independent and reproducible, seeding never yields the all-zero state per data-model.md `DeterministicRandom.s0..s3`) in `tests/Talt.Core.Tests/Determinism/DeterministicRandomTests.cs`
- [X] T007 [P] Write tests for `StateHasher` (equal inputs → equal hash, any byte change → different hash, order sensitivity, little-endian assertion) in `tests/Talt.Core.Tests/Determinism/StateHasherTests.cs`
- [X] T008 [P] Update network tests for integer coordinates and edge lengths (axis-aligned lengths exact, diagonal lengths from integer sqrt) in `tests/Talt.Core.Tests/Network/PathFinderTests.cs`
- [X] T009 [P] Update fleet tests to integer units and stable ids: arrival exactly at stops, dwell of 3 s × 20 ticks, max speed never exceeded, full loop returns to first stop, parallel == sequential, zero allocations per tick, `TotalDistance` exact (`long`) in `tests/Talt.Core.Tests/Simulation/VehicleFleetTests.cs`
- [X] T010 [P] Write tests for `GameSimulation` core (`CurrentTick` starts at 0 and increments by exactly 1 per `Tick()`, fingerprint equal for two simulations from the same settings after N ticks, fingerprint pure) in `tests/Talt.Core.Tests/Simulation/GameSimulationTests.cs`
- [X] T011 [P] Write a reflection guard test that fails if any instance field of simulation state types (`GameSimulation`, `VehicleFleet`, `TransportNetwork` state, `DeterministicRandom`, commands) is `float`, `double`, `Vector2` or `Vector3`, with an explicit allow-list for render-only views, in `tests/Talt.Core.Tests/Determinism/NoFloatingPointStateTests.cs`

### Implementation of the foundation

- [X] T012 [P] Implement `FixedMath` (units per tile = 65 536, tiles/s ↔ units/tick and tiles/s² ↔ units/tick² conversions for 20 ticks/s, `IntegerSqrt(ulong)`, braking distance in `long`) in `src/Talt.Core/Determinism/FixedMath.cs` (research R1)
- [X] T013 [P] Implement `DeterministicRandom` (xoshiro256\*\* with SplitMix64 seeding, `NextUInt64`, unbiased `NextInt(maxExclusive)` and `NextInt(min, maxExclusive)`, `Fork(streamId)`, `AppendTo(ref XxHash3)`) per data-model.md `DeterministicRandom.s0..s3` in `src/Talt.Core/Determinism/DeterministicRandom.cs` (research R6)
- [X] T014 [P] Implement `StateHasher` wrapper over `XxHash3` with helpers to append `long`, `ulong` and live array slices via `MemoryMarshal.AsBytes`, asserting `BitConverter.IsLittleEndian`, plus `IStateHashable` interface, in `src/Talt.Core/Determinism/StateHasher.cs` (research R9)
- [X] T015 Convert `TransportNetwork`/`TransportNetworkBuilder`/`GridNetworkGenerator` to integer node coordinates (units) and integer edge lengths computed with `FixedMath.IntegerSqrt`, keeping a float `NodePositionF` view for rendering only; implement `IStateHashable` (hash once, cache) in `src/Talt.Core/Network/TransportNetwork.cs`, `src/Talt.Core/Network/TransportNetworkBuilder.cs`, `src/Talt.Core/Network/GridNetworkGenerator.cs` (data-model.md `TransportNetwork`)
- [X] T016 Update `NetworkTerrainPainter` and `PathFinder` to the integer network (A* costs in `long` units, heuristic from integer distance) in `src/Talt.Core/Network/NetworkTerrainPainter.cs` and `src/Talt.Core/Network/PathFinder.cs`
- [X] T017 Refactor `VehicleFleet` to fixed point and stable ids per data-model.md `VehicleFleet.Id`, `VehicleFleet.Line`, `VehicleFleet.RouteIndex`, `VehicleFleet.Distance`, `VehicleFleet.Speed`, `VehicleFleet.MaxSpeed`, `VehicleFleet.DwellTicks` (all `int`; `TotalDistance` as `long` summed per batch with `Interlocked.Add`; id→slot map; `NextVehicleId`; constants converted once with `FixedMath`; `Tick(bool parallel)` with no time argument; float `GetPose`/`CopyPoses` for rendering; `IStateHashable`) in `src/Talt.Core/Simulation/VehicleFleet.cs`
- [X] T018 Add `AddVehicle` (returns new `VehicleId`) and `RemoveVehicle(vehicleId)` (swap-with-last, update id→slot map) internal operations to `VehicleFleet`, used later by commands, in `src/Talt.Core/Simulation/VehicleFleet.cs`
- [X] T019 Implement `ScenarioSettings` record with defaults and validation per data-model.md `ScenarioSettings.Seed`, `ScenarioSettings.StartYear`, `ScenarioSettings.MapSizeTiles`, `ScenarioSettings.RoadSpacing`, `ScenarioSettings.RoadRemovalChancePerMille`, `ScenarioSettings.LineCount`, `ScenarioSettings.VehicleCount`, plus `ScenarioBuilder.Build(settings)` producing network and fleet, in `src/Talt.Core/Scenarios/ScenarioSettings.cs` and `src/Talt.Core/Scenarios/ScenarioBuilder.cs`
- [X] T020 [P] Create a minimal localizable string table in `src/Talt.Client/Localization/Strings.cs` with resources `src/Talt.Client/Localization/Strings.en.json` and `src/Talt.Client/Localization/Strings.es.json`: date keys (short month names, `{day} {month} {year}` format), speed keys (`speed.normal`, `speed.fast`, `speed.veryFast`, `speed.fastForward`, `speed.paused`) and command result keys (`command.applied`, `command.rejected.*`); launch option `--language en|es` (default: system language if Spanish, otherwise English); tests for key lookup and that every key exists in both languages in `tests/Talt.Client.Tests/Localization/StringsTests.cs` (create `tests/Talt.Client.Tests/Talt.Client.Tests.csproj` referencing `src/Talt.Client` and add it to `Talt.slnx`) (constitution VIII)
- [X] T021 Switch `SyntheticTraffic` to `DeterministicRandom` and integer speeds (units/tick) in `src/Talt.Core/Simulation/SyntheticTraffic.cs`; also replace `System.Random` in `GridNetworkGenerator` (research R6)
- [X] T022 Implement `GameSimulation` core per data-model.md `GameSimulation.CurrentTick`, `GameSimulation.Random`, `GameSimulation.Network`, `GameSimulation.Vehicles`, `GameSimulation.Settings`: `Create(settings)`, `Tick(parallel)` advancing vehicles then incrementing `CurrentTick`, `ComputeFingerprint()` (tick, RNG, network, fleet) in `src/Talt.Core/Simulation/GameSimulation.cs` (contracts/simulation-api.md)
- [X] T023 Implement a basic fixed-step `SimulationRunner` (normal speed only for now: accumulator, 20 ticks/s, catch-up cap per data-model.md `SimulationRunner.MaxCatchUpSeconds`, `InterpolationFactor`, `beforeLastTick` callback, `ITimeSource`) in `src/Talt.Core/Simulation/SimulationRunner.cs` and `src/Talt.Core/Simulation/ITimeSource.cs`; tests for accumulator, cap and interpolation in `tests/Talt.Core.Tests/Simulation/SimulationRunnerTests.cs`
- [X] T024 Integrate `GameSimulation` + `SimulationRunner` in the client: build the scenario from `ScenarioSettings` in `src/Talt.Client/Program.cs`; in `src/Talt.Client/GameWindow.cs` replace per-frame `fleet.Tick(dt)` with `runner.Advance(...)`, snapshot poses in `beforeLastTick`, interpolate previous → current poses when uploading instances (research R4); add a `StopwatchTimeSource` in `src/Talt.Client/StopwatchTimeSource.cs`

**Checkpoint**: Tests green; the client runs on the fixed-step simulation at normal speed with smooth, interpolated vehicles and the same behaviour as before.

---

## Phase 3: User Story 1 - In-game date advances steadily (Priority: P1) 🎯 MVP

**Goal**: A Gregorian in-game calendar driven by ticks, shown on screen, with daily/monthly/yearly events.

**Independent Test**: Start the client: date begins on 1 January of the configured year (1950 by default) and advances one day ≈ every 2 s, rolling months, years and leap days correctly.

### Tests for User Story 1

- [X] T025 [P] [US1] Write `GameClock`/`GameDate` tests: start date for default and custom years, 40 ticks per day, leap-year February, 31 Dec → 1 Jan, 1 000-year run without overflow, invalid start year rejected per data-model.md `GameClock.StartYear`, in `tests/Talt.Core.Tests/Simulation/GameClockTests.cs`
- [X] T026 [P] [US1] Write `SimulationEvents` tests: daily handlers fire every 40 ticks, monthly on the 1st, yearly on 1 January, order daily → monthly → yearly and registration order within a period, in `tests/Talt.Core.Tests/Simulation/SimulationEventsTests.cs`

### Implementation for User Story 1

- [X] T027 [P] [US1] Implement `GameDate` readonly struct (`Year`, `Month`, `Day`, `DayNumber`, ISO `ToString`) in `src/Talt.Core/Simulation/GameDate.cs`
- [X] T028 [US1] Implement `GameClock` per data-model.md `GameClock.TicksPerDay`, `GameClock.TicksPerSecondAtNormalSpeed`, `GameClock.StartYear`, `GameClock.StartDayNumber` (date from `CurrentTick` via `DateOnly.FromDayNumber`, day/month/year boundary detection) in `src/Talt.Core/Simulation/GameClock.cs` (research R2)
- [X] T029 [US1] Implement `SimulationEvents` (`OnDayEnded`, `OnMonthEnded`, `OnYearEnded`, deterministic dispatch) in `src/Talt.Core/Simulation/SimulationEvents.cs` (research R10)
- [X] T030 [US1] Wire clock and events into `GameSimulation` (`Date` property, fire events after the tick that crosses a boundary) in `src/Talt.Core/Simulation/GameSimulation.cs`
- [X] T031 [US1] Show the in-game date in the window title using `Strings` (T020) for month names and date format (e.g. `1 Jan 1950` / `1 ene 1950`), no hard-coded player-facing text, and accept a `--start-year` launch option in `src/Talt.Client/GameWindow.cs` and `src/Talt.Client/Program.cs`

**Checkpoint**: Date visible and advancing at ~30 days per minute; calendar and event tests pass.

---

## Phase 4: User Story 2 - Pause and change game speed (Priority: P1)

**Goal**: Pause, three standard speeds (×1, ×3, ×8) freely selectable, and an unlockable fast forward with its own separate control; identical results at every speed; smooth motion.

**Independent Test**: In the client, pause/resume and switch ×1/×3/×8; fast-forward key does nothing until unlocked in settings, then toggles fast forward and back to the previous speed; a vehicle reaches the same stops on the same in-game dates at every speed.

### Tests for User Story 2

- [X] T032 [P] [US2] Write runner speed tests with a fake `ITimeSource`: ×1/×3/×8 tick counts per second, pause runs no ticks and resets the accumulator, standard controls never reach fast forward, fast forward locked by default per data-model.md `SimulationRunner.FastForwardUnlocked`, unlock enables the toggle, toggle off and locking restore data-model.md `SimulationRunner.LastStandardSpeed`, pause/resume during fast forward, fast-forward budget per data-model.md `SimulationRunner.FastForwardBudget`, and 10 simulated minutes at ×1 with irregular fake frame times (8–50 ms) yielding 300 in-game days ±2 % (SC-004), in `tests/Talt.Core.Tests/Simulation/SimulationRunnerSpeedTests.cs`
- [X] T033 [P] [US2] Write a speed-independence test: run the same scenario for 2 400 ticks driven by the runner at ×1, ×3, ×8 and fast forward (fake time) and assert identical fingerprints and identical stop-arrival ticks, in `tests/Talt.Core.Tests/Simulation/SpeedIndependenceTests.cs`

### Implementation for User Story 2

- [X] T034 [P] [US2] Implement `GameSpeed` enum per data-model.md `GameSpeed` in `src/Talt.Core/Simulation/GameSpeed.cs`
- [X] T035 [US2] Extend `SimulationRunner` with `Speed`, `SetStandardSpeed`, `TogglePause`, `ToggleFastForward`, `SetFastForwardUnlocked`, `LastStandardSpeed`, per-speed multipliers, catch-up cap scaled by speed, and fast-forward time budget (research R3; contracts/simulation-api.md) in `src/Talt.Core/Simulation/SimulationRunner.cs`; per data-model.md `SimulationRunner.FastForwardBudget`, `SimulationRunner.FastForwardUnlocked`, `SimulationRunner.LastStandardSpeed`
- [X] T036 [P] [US2] Implement `PlayerSettings` per data-model.md `PlayerSettings.FastForwardUnlocked` (JSON file in the user profile folder, load with defaults on missing/corrupt file, save on change) and a `--unlock-fast-forward` / `--lock-fast-forward` launch option that updates it, in `src/Talt.Client/PlayerSettings.cs` and `src/Talt.Client/Program.cs`
- [X] T037 [US2] Client controls in `src/Talt.Client/GameWindow.cs`: Space = pause, `1`/`2`/`3` = ×1/×3/×8, `F` = separate fast-forward toggle (ignored while locked); show speed (and "fast forward" or "paused") plus effective speed multiplier in the title using `Strings` (T020), no hard-coded player-facing text; remove the old `+`/`-` simulation speed keys
- [X] T038 [P] [US2] Extract pose interpolation into a pure function `PoseInterpolator.Interpolate(previous, current, factor, ...)` in `src/Talt.Core/Simulation/PoseInterpolator.cs` (rendering-only floating point, outside simulation state; add it to the allow-list of the guard test T011); tests that a vehicle never moves backwards along its route, never jumps more than its expected per-frame distance, and is not interpolated when added or removed between snapshots, in `tests/Talt.Core.Tests/Simulation/PoseInterpolatorTests.cs` (SC-006)
- [X] T039 [US2] Use `PoseInterpolator` (T038) in the client and verify smooth motion: interpolation factor 1 during fast forward, no backwards motion when a vehicle changes edge, vehicles added/removed between snapshots drawn without interpolation, in `src/Talt.Client/GameWindow.cs` (research R4)

**Checkpoint**: Speeds, pause and unlockable fast forward work in the client; speed-independence and runner tests pass.

---

## Phase 5: User Story 3 - Predictable player actions (Priority: P2)

**Goal**: Player actions as commands applied at the next tick, recordable and replayable with identical results; first real actions add/remove vehicle.

**Independent Test**: Record a session with add/remove commands, replay it twice and in parallel/sequential modes: fingerprints identical at every tick for 10 000 ticks; invalid commands rejected without side effects.

### Tests for User Story 3

- [X] T040 [P] [US3] Write command tests: `AddVehicleCommand` validation per data-model.md `AddVehicleCommand.LineId`, `AddVehicleCommand.MaxSpeed`, `AddVehicleCommand.StartEdgeIndex`; `RemoveVehicleCommand` validation per data-model.md `RemoveVehicleCommand.VehicleId`; rejected commands leave the fingerprint unchanged; commands submitted in one tick apply at the start of the next in submission order; commands submitted while paused apply on the first tick after resuming; in `tests/Talt.Core.Tests/Commands/VehicleCommandsTests.cs`
- [X] T041 [P] [US3] Write determinism tests: same settings + same recorded commands → identical fingerprints at each of 10 000 ticks across two runs, and parallel == sequential, in `tests/Talt.Core.Tests/Determinism/ReplayDeterminismTests.cs`
- [X] T042 [P] [US3] Write session serialization tests: round trip of `RecordedSession` JSON per contracts/recorded-session.md (format version, integer-only numbers, `applied`/`rejected` outcomes, checkpoints as 16-hex-digit strings, unknown version rejected) in `tests/Talt.Core.Tests/Commands/RecordedSessionTests.cs`
- [X] T043 [P] [US3] Add test-only commands (`test.noop`, `test.consumeRandom`) for exercising the queue and RNG in `tests/Talt.Core.Tests/TestCommands/TestCommands.cs`

### Implementation for User Story 3

- [X] T044 [P] [US3] Implement `ISimulationCommand`, `CommandResult` (success / rejected with reason code and message) and `CommandProcessed` event args in `src/Talt.Core/Commands/ISimulationCommand.cs` and `src/Talt.Core/Commands/CommandResult.cs` (research R7)
- [X] T045 [P] [US3] Implement `CommandQueue` (FIFO of submission id, issuer id, command; drained fully per tick) in `src/Talt.Core/Commands/CommandQueue.cs`
- [X] T046 [US3] Implement `AddVehicleCommand` and `RemoveVehicleCommand` per data-model.md `AddVehicleCommand.LineId`, `AddVehicleCommand.MaxSpeed`, `AddVehicleCommand.StartEdgeIndex` and `RemoveVehicleCommand.VehicleId`, using `VehicleFleet.AddVehicle`/`RemoveVehicle`, in `src/Talt.Core/Commands/AddVehicleCommand.cs` and `src/Talt.Core/Commands/RemoveVehicleCommand.cs`
- [X] T047 [US3] Add `Submit` and command processing at the start of `Tick()` (validate → apply or reject, raise `CommandProcessed`, notify recorder) in `src/Talt.Core/Simulation/GameSimulation.cs`
- [X] T048 [US3] Implement `SessionRecorder`, `RecordedSession`, `RecordedCommand` and a `System.Text.Json` source-generated context with a `type` discriminator per contracts/recorded-session.md, plus `SessionReplayer` (submit each entry so it is processed at its recorded tick, compare outcomes and checkpoints, report first failing tick) in `src/Talt.Core/Commands/SessionRecorder.cs`, `src/Talt.Core/Commands/RecordedSession.cs`, `src/Talt.Core/Commands/SessionReplayer.cs`
- [X] T049 [US3] Client shortcuts in `src/Talt.Client/GameWindow.cs`: `N` = submit `AddVehicleCommand` on a line chosen by a client-side `System.Random` (never the simulation's `DeterministicRandom`; the chosen `LineId` travels inside the command, so replay stays exact), `Delete` = submit `RemoveVehicleCommand` for the most recently added vehicle; show the last command result in the title for a few seconds using `Strings` (T020); record the session to `session.talt-session.json` when launched with `--record`

**Checkpoint**: Commands apply on the next tick, recording and replay reproduce identical state; determinism tests pass.

---

## Phase 6: User Story 4 - Headless simulation for tests and tools (Priority: P3)

**Goal**: Run and replay scenarios without a window, producing dates, fingerprints and timings; golden fingerprints to detect cross-platform divergence.

**Independent Test**: `Talt.Headless run --ticks 10000` completes without a GPU and prints date and fingerprint; two runs match; `replay` of a recorded session exits 0 with all checkpoints OK.

### Tests for User Story 4

- [X] T050 [P] [US4] Write golden fingerprint tests: a small fixed scenario (`MapSizeTiles` 256, 2 000 vehicles, seed 42) with a fixed command script, expected fingerprints at ticks 1 000 and 10 000 committed as constants (generate once, document in the commit), in `tests/Talt.Core.Tests/Determinism/GoldenFingerprintTests.cs` (research R12)
- [X] T051 [P] [US4] Write CLI argument parsing tests for `run` and `replay` (defaults, invalid values → exit code 2) against the parser class in `tools/Talt.Headless`, in `tests/Talt.Headless.Tests/HeadlessArgumentsTests.cs`

### Implementation for User Story 4

- [X] T052 [US4] Implement argument parsing and the `run` command (build scenario, optional `--record`, run N ticks parallel or `--sequential`, print stable `key: value` output, exit codes) per contracts/headless-cli.md in `tools/Talt.Headless/HeadlessArguments.cs` and `tools/Talt.Headless/Program.cs`
- [X] T053 [US4] Implement the `replay` command (load session, replay with `SessionReplayer`, print checkpoint lines, exit 1 with first failing tick on mismatch) per contracts/headless-cli.md in `tools/Talt.Headless/Program.cs`
- [X] T054 [US4] Verify headless and windowed runs produce the same fingerprint for the same recorded session: add a `--print-fingerprint-at <tick>` option to the client that logs the fingerprint to the console, in `src/Talt.Client/Program.cs` and `src/Talt.Client/GameWindow.cs`

**Checkpoint**: Headless run/replay work per the contract; golden fingerprints pass on Windows.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Performance validation, the SoA vs ECS decision, documentation and final verification

- [X] T055 [P] Add `SimulationTickBenchmarks` (full `GameSimulation.Tick()` at 10k/50k/100k/1M vehicles on a 4096 map, parallel and sequential, memory diagnoser) in `benchmarks/Talt.Benchmarks/SimulationTickBenchmarks.cs`; update existing benchmarks to the integer API in `benchmarks/Talt.Benchmarks/VehicleTickBenchmarks.cs`
- [X] T056 [P] Implement the same vehicle tick on Arch ECS (components mirroring the SoA fields, same rules, deterministic query order) and benchmark it at 100k and 1M vehicles in `benchmarks/Talt.Benchmarks/ArchComparisonBenchmarks.cs` (research R5)
- [X] T057 Run benchmarks (`--job short`), decide SoA vs Arch with the criteria in research R5, and record the numbers and the decision in `docs/ARCHITECTURE.md` (new "Entity storage decision" subsection) and in `specs/001-simulation-foundation/research.md` (R5 outcome); compare against the previous figures in `docs/ARCHITECTURE.md` (0.35 ms at 100k, 6.0 ms at 1M parallel) and justify any regression above 10 % in `docs/ARCHITECTURE.md` and `research.md` (constitution II)
- [X] T058 Verify SC-003 and SC-008 on the reference load: client with `-- 128 50000`, fast forward unlocked ≥ ×20 at ≥ 30 FPS; `Talt.Headless run --map 4096 --vehicles 50000 --ticks 10000` under 60 s; record results in `docs/ARCHITECTURE.md`
  - Status 2026-11-22: SC-008 verified (10 000 ticks of the reference load headless: 2.7 s simulate + 31 s setup). SC-003 pending: fast forward could not be driven with synthetic key presses; needs a manual check (estimate from measurements: ≥ ×100). With `-- 128` the window stays blank because the naive terrain mesh (~6.4 GB) does not fit in GPU memory; SC-003 is measured on smaller maps instead and the 4096 check moves to feature 003. Manual result with 50 000 vehicles: `-- 32` fast forward ×205 at 36 FPS (passes); `-- 64` ×48 at 16 FPS (terrain drawing limits it, 14 FPS even at ×8). See the table in `docs/ARCHITECTURE.md`.
- [X] T059 [P] Update `docs/ARCHITECTURE.md` with sections "Simulation loop" (ticks, runner, speeds, fast-forward unlock, interpolation), "Determinism" (fixed point, RNG, commands, fingerprints, golden tests, no-float rule) and "Headless runner"; update the controls table and the solution layout
- [X] T060 [P] Update `docs/VISION.md` (decision log: fixed-point simulation, 40 ticks/day, unlockable fast forward) and `README.md` (new controls, headless tool usage)
- [X] T061 Remove obsolete code paths (per-frame `dt` ticking, float simulation constants, unused helpers) and ensure the build has no warnings, across `src/Talt.Core/` and `src/Talt.Client/`
- [X] T062 Run the full [quickstart.md](quickstart.md) validation on Windows and, at least once, the tests and `replay` on Linux and macOS (FR-013, SC-002); note any platform not yet checked in `docs/ARCHITECTURE.md`
  - Status 2026-11-22: Windows validation done (tests, headless run/replay, client fingerprint = headless fingerprint, clean exit). Linux x64 verified under WSL 2 (Ubuntu 26.04): 151 tests pass, headless fingerprints equal Windows, a Windows-recorded session replays OK. macOS arm64 verified by CI (feature 002, 2026-11-22: 151 tests pass, fingerprints equal Windows, Windows-recorded session replays OK); Linux and Windows ARM64 verified by CI as well (same fingerprints). Keyboard controls checked by hand on Windows (32 chunks, 50 000 vehicles, fast forward unlocked): all keys work.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup; **blocks all user stories**.
  - T012–T014 before T015–T022; T015 before T016–T017; T017 before T018 and T021; T019 depends on T015, T021; T020 is independent; T022 depends on T013, T014, T017, T019; T023 depends on T022; T024 depends on T023.
- **US1 (Phase 3)**: depends on Phase 2.
- **US2 (Phase 4)**: depends on Phase 2; integrates with US1's title display (T031) but is testable alone.
- **US3 (Phase 5)**: depends on Phase 2 (uses T018 add/remove); independent of US1/US2.
- **US4 (Phase 6)**: depends on Phase 2; `replay` (T053) depends on US3's recorder/replayer (T048).
- **Polish (Phase 7)**: after the desired stories; T057 depends on T055–T056.

### User Story Dependencies

- **US1 (P1)**: none beyond the foundation.
- **US2 (P1)**: none beyond the foundation (title text shared with US1).
- **US3 (P2)**: none beyond the foundation.
- **US4 (P3)**: `run` needs only the foundation; `replay` needs US3.

### Within Each User Story

- Tests first (they must fail), then types, then simulation wiring, then client integration.
- Each story ends with its checkpoint verified in the client or the test runner.

### Parallel Opportunities

- Setup: T002, T003 and T004.
- Foundation tests T005–T011 all in parallel; T012–T014 in parallel.
- US1: T025, T026, T027 in parallel.
- US2: T032, T033, T034, T036, T038 in parallel.
- US3: T040–T045 in parallel.
- US4: T050, T051 in parallel.
- Polish: T055, T056, T059, T060 in parallel.
- After Phase 2, US1, US2 and US3 can proceed in parallel (different files except the shared
  `GameSimulation.cs`/`GameWindow.cs` edits, which should be done one story at a time).

---

## Parallel Example: Foundation

```text
Task: "Write tests for fixed-point helpers in tests/Talt.Core.Tests/Determinism/FixedMathTests.cs"
Task: "Write tests for DeterministicRandom in tests/Talt.Core.Tests/Determinism/DeterministicRandomTests.cs"
Task: "Write tests for StateHasher in tests/Talt.Core.Tests/Determinism/StateHasherTests.cs"
Task: "Write the no-floating-point guard test in tests/Talt.Core.Tests/Determinism/NoFloatingPointStateTests.cs"
```

## Parallel Example: User Story 3

```text
Task: "Write command tests in tests/Talt.Core.Tests/Commands/VehicleCommandsTests.cs"
Task: "Write determinism tests in tests/Talt.Core.Tests/Determinism/ReplayDeterminismTests.cs"
Task: "Write session serialization tests in tests/Talt.Core.Tests/Commands/RecordedSessionTests.cs"
Task: "Implement CommandQueue in src/Talt.Core/Commands/CommandQueue.cs"
```

---

## Implementation Strategy

### MVP First (Foundation + User Story 1)

1. Phase 1 Setup → Phase 2 Foundation (the biggest step: integer state, `GameSimulation`, basic runner, client integration).
2. Phase 3 US1 (calendar and date display).
3. **Stop and validate**: tests green, date advancing correctly, vehicles behave as before.

### Incremental Delivery

1. Foundation + US1 → date visible (MVP of this feature).
2. US2 → speeds, pause, unlockable fast forward.
3. US3 → commands, recording, replay, determinism guarantees.
4. US4 → headless tool, golden fingerprints.
5. Polish → benchmarks, ECS decision, documentation, cross-platform check.

---

## Notes

- [P] tasks touch different files and have no dependencies on incomplete tasks.
- Commit only when the developer asks (constitution workflow); natural commit points are each checkpoint.
- Golden fingerprints change only when simulation rules change deliberately; say so in the commit message.
- Keep `Talt.Core` free of `float`/`double` simulation state; the guard test (T011) enforces it.
