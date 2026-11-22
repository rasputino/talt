# Implementation Plan: Simulation Foundation

**Branch**: `001-simulation-foundation` | **Date**: 2026-11-22 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/001-simulation-foundation/spec.md`

## Summary

Turn the prototype's per-frame vehicle update into a deterministic, fixed-step game simulation.
`Talt.Core` gains a `GameSimulation` that owns all state and advances it with `Tick()`: 40 ticks per
in-game day (20 per second at normal speed), a Gregorian calendar from a configurable start year,
daily/monthly/yearly events, a command queue for player actions (add/remove vehicle), a seeded
xoshiro256\*\* random generator and a 64-bit state fingerprint. To guarantee bit-identical results on
Windows, Linux and macOS (x64 and ARM64), simulation state moves to **integer fixed-point** (research
R1). A pure `SimulationRunner` turns real elapsed time into ticks with standard speeds (×1, ×3, ×8)
and an unlockable fast forward (enabled from player settings, with its own toggle), a catch-up cap and a fast-forward time budget; the client interpolates vehicle poses
between ticks for smooth motion. A new headless console tool runs and replays scenarios. Storage
stays structure-of-arrays unless a benchmark against Arch ECS proves otherwise (research R5).

## Technical Context

**Language/Version**: C# (latest) on .NET 10

**Primary Dependencies**: Silk.NET 2.23 (client only), Silk.NET.WebGPU + wgpu-native (client only),
`System.IO.Hashing` 10.0.15 (new, Core: state fingerprint), `System.Text.Json` source generation
(in-box), `Arch` 2.1.0 (benchmarks project only, for the SoA vs ECS comparison)

**Storage**: N/A (recorded sessions as JSON files; saves come in feature 014)

**Testing**: xUnit (unit, determinism, golden fingerprints), BenchmarkDotNet (tick benchmarks)

**Target Platform**: Windows, Linux, macOS; x64 and ARM64

**Project Type**: Desktop game (core library + client executable + headless console tool)

**Performance Goals**: full tick for 50 000 vehicles ≤ 2.5 ms so fast forward reaches ≥ ×20 at
≥ 30 FPS on a 4096 map (SC-003); headless 10 000 ticks of the reference load < 60 s (SC-008);
normal-speed tick within the global 4 ms budget

**Constraints**: no floating point in simulation state; no wall clock or unseeded randomness in
`Talt.Core`; no allocations in the tick hot path (commands and events may allocate); parallel ==
sequential; catch-up ≤ 0.25 s per frame

**Scale/Scope**: reference load 4096 × 4096 map, 50 000 vehicles, ~6 000 lines; 1 000+ in-game years
without overflow

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle | Status | How this plan complies |
|---|---|---|
| I. Core/Client separation | ✅ Pass | `GameSimulation`, runner, commands, RNG, hashing in `Talt.Core`; client only feeds elapsed time, input and rendering. Headless tool references Core only. |
| II. Performance is a feature | ✅ Pass | Tick benchmarks for 10k/50k/100k/1M vehicles; budgets above; allocation-free tick verified by test; results in ARCHITECTURE.md. |
| III. Deterministic, data-oriented | ✅ Pass | Fixed ticks, commands, xoshiro RNG, integer state, SoA storage, fingerprint + golden tests, sequential = parallel tests. |
| IV. Tested increments | ✅ Pass | Each user story has its own test set (calendar, runner, commands, headless/replay); golden fingerprints. |
| V. Readable C# | ✅ Pass | Plain integers with small `FixedMath` helpers instead of a clever fixed-point type; XML docs on public API. |
| VI. Original content / legal | ✅ Pass | xoshiro256\*\* and SplitMix64 implemented from public-domain descriptions; new deps MIT (System.IO.Hashing) and Apache-2.0 (Arch, benchmarks only, not shipped). |
| VII. Data-driven content | ✅ Pass (N/A) | No game content added; scenario settings are a record that later features can load from data. |
| VIII. Living docs in English | ✅ Pass | ARCHITECTURE.md gains "Simulation loop", "Determinism" and "Headless runner" sections; decision log updated. Date, speed and command texts go through a minimal en/es string table until feature 005 provides full localization. |

**New dependency justification:** `System.IO.Hashing` (fingerprint; MIT; Microsoft-maintained;
~100 KB). `Arch` limited to `benchmarks/` for the comparison; adopting it in Core would require a new
justification after the benchmark.

**Post-design re-check:** ✅ all gates still pass; no complexity violations.

## Project Structure

### Documentation (this feature)

```text
specs/001-simulation-foundation/
├── spec.md
├── plan.md              # This file
├── research.md          # Decisions R1–R12
├── data-model.md        # Entities and invariants
├── quickstart.md        # Validation guide
├── contracts/
│   ├── simulation-api.md
│   ├── recorded-session.md
│   └── headless-cli.md
├── checklists/
│   └── requirements.md
└── tasks.md             # Created by /speckit-tasks
```

### Source Code (repository root)

```text
src/
├── Talt.Core/
│   ├── Simulation/
│   │   ├── GameSimulation.cs        # NEW: owns state, Tick(), Submit(), fingerprint
│   │   ├── GameClock.cs             # NEW: ticks ↔ date, boundaries
│   │   ├── GameDate.cs              # NEW
│   │   ├── SimulationEvents.cs      # NEW: day/month/year handlers
│   │   ├── PoseInterpolator.cs      # NEW: rendering-only pose interpolation (pure, testable)
│   │   ├── SimulationRunner.cs      # NEW: speeds, fast-forward unlock, accumulator, cap, budget,
│   │   │                            #      interpolation
│   │   ├── GameSpeed.cs             # NEW
│   │   ├── ITimeSource.cs           # NEW (+ StopwatchTimeSource in client/tools)
│   │   ├── VehicleFleet.cs          # CHANGED: fixed point, stable ids, add/remove, hashing
│   │   └── SyntheticTraffic.cs      # CHANGED: DeterministicRandom, integer units
│   ├── Commands/
│   │   ├── ISimulationCommand.cs    # NEW
│   │   ├── CommandQueue.cs          # NEW
│   │   ├── CommandResult.cs         # NEW
│   │   ├── AddVehicleCommand.cs     # NEW
│   │   ├── RemoveVehicleCommand.cs  # NEW
│   │   ├── SessionRecorder.cs       # NEW
│   │   └── RecordedSession.cs       # NEW (+ JSON source-generated context)
│   ├── Determinism/
│   │   ├── DeterministicRandom.cs   # NEW: xoshiro256** + SplitMix64
│   │   ├── FixedMath.cs             # NEW: units, conversions, integer sqrt
│   │   └── StateHasher.cs           # NEW: XxHash3 fingerprint
│   ├── Scenarios/
│   │   └── ScenarioSettings.cs      # NEW: settings + builder shared by client and headless
│   └── Network/                     # CHANGED: integer node coords and edge lengths
├── Talt.Client/
│   ├── GameWindow.cs                # CHANGED: runner, pose snapshot + interpolation, date/speed
│   │                                #          display, speed/pause/add/remove shortcuts,
│   │                                #          separate fast-forward key (only when unlocked)
│   ├── PlayerSettings.cs            # NEW: persisted player settings (fast-forward unlock)
│   ├── Localization/                # NEW: minimal en/es string table (Strings.cs + JSON)
│   └── Program.cs                   # CHANGED: builds GameSimulation from ScenarioSettings
tools/
└── Talt.Headless/                   # NEW console project (run, replay)
tests/
├── Talt.Client.Tests/               # NEW: string table tests (localization)
├── Talt.Headless.Tests/             # NEW: headless CLI argument tests
└── Talt.Core.Tests/
    ├── Simulation/                  # calendar, runner, events, fleet (updated), determinism
    ├── Commands/                    # add/remove, rejection, ordering, recorder/replay
    ├── Determinism/                 # RNG, fixed math, hashing, golden fingerprints, no-float guard
    └── TestCommands/                # test-only commands
benchmarks/
└── Talt.Benchmarks/
    ├── SimulationTickBenchmarks.cs  # NEW: full GameSimulation.Tick at 10k/50k/100k/1M
    └── ArchComparisonBenchmarks.cs  # NEW: same vehicle tick on Arch ECS (decision input)
```

**Structure Decision**: keep the existing solution layout (Core / Client / tests / benchmarks) and
add one console project under `tools/`. Simulation infrastructure gets its own folders
(`Commands`, `Determinism`, `Scenarios`) so later features reuse it without touching vehicle code.

## Implementation Notes

**Order of work (each step leaves the build green):**

1. Determinism primitives: `FixedMath`, `DeterministicRandom`, `StateHasher` + tests.
2. Integer network: node coordinates and edge lengths in units; float views for rendering.
3. `VehicleFleet` to fixed point, stable ids, add/remove; preserve behaviour (tests compare arrival
   ticks with the expected values derived from the old float model within one tick).
4. `GameClock`, `GameDate`, `SimulationEvents`, `GameSimulation` (US1, US3 core).
5. Commands, queue, recorder, JSON session format (US3).
6. `SimulationRunner` with standard speeds, fast-forward lock/unlock and toggle, cap, fast-forward
   budget (US2) + tests with a fake time source; `PlayerSettings` persistence in the client.
7. Client integration: runner, interpolation, date/speed display, shortcuts (US1, US2 visible).
8. Headless tool `run`/`replay` (US4) and golden fingerprint tests.
9. Benchmarks (tick, Arch comparison) → decision and numbers in ARCHITECTURE.md.

**Key risks and mitigations:**

- *Behaviour drift from float to integer*: convert constants with explicit rounding; tests for
  stopping exactly at stops and dwell times; visual check in the client.
- *Hidden float use in Core*: reflection guard test on simulation types; code review rule.
- *Golden fingerprints churn*: regenerate only on deliberate rule changes, with a note in the commit.
- *Fast forward starving rendering*: per-frame time budget, measured with an injected time source.

## Complexity Tracking

No constitution violations; nothing to justify.
