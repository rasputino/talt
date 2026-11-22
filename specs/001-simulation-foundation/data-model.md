# Data Model: Simulation Foundation

**Feature**: 001-simulation-foundation | **Date**: 2026-11-22

All types below live in `Talt.Core` unless noted. Simulation state contains **no floating-point
fields** (research R1). Units: distances in sub-tile units (1 tile = 65 536), time in ticks.

---

## GameSimulation

Owns the complete simulated state and advances it one tick at a time.

| Field | Type | Notes |
|---|---|---|
| `CurrentTick` | `long` | Number of completed ticks since the game started. Starts at 0. |
| `Clock` | `GameClock` | Derives the date from `CurrentTick` and the start year. |
| `Random` | `DeterministicRandom` | Root generator; per-system streams are derived from it. |
| `Commands` | `CommandInbox` | Pending player commands, applied at the start of the next tick. |
| `Events` | `SimulationEvents` | Daily, monthly and yearly handlers. |
| `Recorder` | `SessionRecorder?` | Optional; records applied/rejected commands with their tick. |
| `Network` | `TransportNetwork` | Existing road graph (immutable in this feature). |
| `Vehicles` | `VehicleFleet` | Existing fleet, refactored to fixed point and stable ids. |
| `Settings` | `ScenarioSettings` | Seed and parameters the game was created from. |

**Operations**

- `Submit(command, issuer)` → enqueues; returns a submission id.
- `Tick(parallel = true)` → (1) apply queued commands in order, (2) advance systems (vehicles),
  (3) increment `CurrentTick`, (4) fire day/month/year events if a boundary was crossed.
- `ComputeFingerprint()` → `ulong` (see StateHasher).

**Invariants**

- `CurrentTick` increases by exactly 1 per `Tick()` call; nothing else changes it.
- No state changes outside `Tick()` and construction.
- Same `Settings` + same command sequence (with ticks) ⇒ same fingerprint after every tick, on every
  platform, for any `parallel` value.

---

## GameClock and GameDate

| Field | Type | Notes |
|---|---|---|
| `TicksPerDay` | `const int` | 40 (research R2). |
| `TicksPerSecondAtNormalSpeed` | `const int` | 20. |
| `StartYear` | `int` | From settings; default 1950; valid range 1 – 9000. |
| `StartDayNumber` | `int` | Day number of 1 January `StartYear`. |

`GameDate` (readonly struct): `Year`, `Month` (1–12), `Day` (1–31), plus `DayNumber`. Derived as
`DateOnly.FromDayNumber(StartDayNumber + CurrentTick / TicksPerDay)`.

Derived boundaries: a day ends when `CurrentTick % TicksPerDay == 0` (after increment); month and year end
when the new date's day is 1 (and month is 1 for years).

**Validation:** start year outside the valid range is rejected when creating the scenario.

---

## GameSpeed (client-side state, not simulation state)

Enum: `Paused`, `Normal` (×1), `Fast` (×3), `VeryFast` (×8), `FastForward` (unlimited, ≥ ×20).
Changing speed never touches simulation state (FR-008); it lives in `SimulationRunner`.

Speed selection rules (FR-007 to FR-007d):
- `Normal`, `Fast`, `VeryFast` and `Paused` are always selectable; the standard speed controls only
  cycle through these.
- `FastForward` is selectable only when `FastForwardUnlocked` is true, through a separate toggle.
- The runner remembers `LastStandardSpeed`; leaving fast forward (toggle off, or locking it) restores
  it. Pausing while in fast forward remembers fast forward for resume.

---

## SimulationRunner

Pure logic that decides how many ticks to run per frame. Lives in `Talt.Core` (no clock access:
real time is passed in) so it is unit-testable.

| Field | Type | Notes |
|---|---|---|
| `Speed` | `GameSpeed` | Current speed. |
| `AccumulatedSeconds` | `double` | Real time owed to the simulation (runner state, not simulation state). |
| `MaxCatchUpSeconds` | `double` | 0.25 s of normal-speed time per frame (SC-007). |
| `FastForwardBudget` | `TimeSpan` | Default 25 ms of tick work per frame. |
| `FastForwardUnlocked` | `bool` | From player settings; false by default. |
| `LastStandardSpeed` | `GameSpeed` | Speed restored when fast forward is turned off or locked. |
| `InterpolationFactor` | `float` | 0..1, for rendering. |

**Speed operations:** `SetStandardSpeed(speed)` (Normal/Fast/VeryFast), `TogglePause()`,
`ToggleFastForward()` (no effect when locked), `SetFastForwardUnlocked(bool)`.

**Operations:** `Advance(elapsedRealSeconds, simulation, timeSource, beforeLastTick)` → runs N ticks,
returns statistics (ticks run, time spent). `beforeLastTick` lets the client snapshot poses.

**Rules:** see research R3 (cap, budget, pause resets accumulator).

---

## PlayerSettings (client-side, persisted)

| Field | Type | Default | Notes |
|---|---|---|---|
| `FastForwardUnlocked` | `bool` | false | Unlocks the separate fast-forward control. |

Stored as a small JSON file in the user's profile folder; feature 005 extends it into the full
settings system. Not part of simulation state or recorded sessions.

---

## DeterministicRandom

xoshiro256\*\* generator (research R6).

| Field | Type | Notes |
|---|---|---|
| `s0..s3` | `ulong` | Generator state; hashed as part of the fingerprint. |

Operations: `NextUInt64()`, `NextInt(maxExclusive)` (unbiased), `NextInt(min, maxExclusive)`,
`Fork(streamId)` → new independent generator seeded via SplitMix64 from `(state, streamId)`.

**Validation:** `maxExclusive > 0`; seeding never produces the all-zero state.

---

## Commands

`ISimulationCommand` — immutable record with:
- `TypeName` (stable discriminator for recorded sessions) and `WriteProperties(Utf8JsonWriter)`,
- `Validate(GameSimulation) → CommandResult` (no side effects),
- `Apply(GameSimulation)` (only called after successful validation, same tick).

`CommandResult`: `Success` or `Rejected(reasonCode, message)`.

`CommandInbox` (named Inbox because analyzers reserve the `Queue` suffix): FIFO of `(submissionId, issuerId, command)`; drained completely at the start of each
tick in submission order.

### AddVehicleCommand

| Field | Type | Validation |
|---|---|---|
| `LineId` | `int` | Line must exist. |
| `MaxSpeed` | `int` (units/tick) | `> 0` and `≤` the global maximum speed. |
| `StartEdgeIndex` | `int` | `0 ≤ value < line edge count`; default 0 (first stop). |

Effect: creates a vehicle with a new `VehicleId`, standing still, waiting the standard dwell time.

### RemoveVehicleCommand

| Field | Type | Validation |
|---|---|---|
| `VehicleId` | `int` | Vehicle must exist (not already removed). |

Effect: removes the vehicle (swap-with-last in storage, id→slot map updated).

---

## SessionRecorder and RecordedSession

`RecordedSession`: `FormatVersion` (int), `Settings` (`ScenarioSettings`), `Entries` (ordered list of
`RecordedCommand { Tick: long, Command: ISimulationCommand, Outcome: Applied | Rejected }`).

The recorder appends entries as commands are processed. Replay re-submits each command so that it is
applied at its recorded tick, then verifies the outcome matches. Format: see
[contracts/recorded-session.md](contracts/recorded-session.md).

---

## ScenarioSettings

| Field | Type | Default | Notes |
|---|---|---|---|
| `Seed` | `int` | 2026 | Drives network and traffic generation. |
| `StartYear` | `int` | 1950 | |
| `MapSizeTiles` | `int` | 512 | Square map; multiple of 32. |
| `RoadSpacing` | `int` | 16 | Tiles between junctions. |
| `RoadRemovalChancePerMille` | `int` | 150 | Integer to keep settings exact. |
| `LineCount` | `int` | vehicles / 10 | |
| `VehicleCount` | `int` | 20 000 | |

Reference load for SC-003/SC-008: `MapSizeTiles = 4096`, `VehicleCount = 50 000`.

---

## VehicleFleet (refactored)

Dense slots `0..Count-1` (research R5). The hot fields below are packed in one `VehicleState`
struct array (24 bytes per vehicle) for cache locality; `Id` is a separate array.

| Array | Type | Notes |
|---|---|---|
| `Id` | `int[]` | Stable `VehicleId` per slot. |
| `Line` | `int[]` | Line index. |
| `RouteIndex` | `int[]` | Edge index within the line loop. |
| `Distance` | `int[]` | Units travelled along the current edge. |
| `Speed` | `int[]` | Units per tick. |
| `MaxSpeed` | `int[]` | Units per tick. |
| `DwellTicks` | `int[]` | Ticks left waiting at a stop; 0 when moving. |

Plus: `Dictionary<int, int>` id→slot (only touched by commands, never in the tick), `NextVehicleId`,
and `TotalDistance` as `long` (units).

Line data (unchanged structure, integer units): `LineEdges`, `DistanceToStop` (`long`), `LineStart`,
`LineLength`, `LineReturn`. Edge lengths come from `TransportNetwork` as integer units.

Constants (units and ticks): `Acceleration`, `Deceleration`, `CreepSpeed`, `DwellTicks = 3 s × 20`.
Converted once from the current tiles/s values; behaviour preserved (FR-021).

**State transitions (per vehicle):** `Dwelling (DwellTicks > 0)` → `Accelerating` → `Cruising` →
`Braking (stopping distance ≥ remaining)` → `Arrived` (snap to stop, `Speed = 0`, start dwell) →
`Dwelling`.

---

## TransportNetwork (adjusted)

Node positions become integer tile coordinates scaled to units (`int` X, Z); edge lengths are
`int` units computed with integer square root at build time. A float view (`Vector2`) remains
available for rendering only.

---

## StateHasher

Appends, in fixed order: `CurrentTick`, root RNG state, each system's `AppendTo(ref XxHash3)` (vehicles:
live slices of every array and `TotalDistance`; network: hashed once at creation and included as a
constant). Result: `ulong` fingerprint.

---

## Client and rendering-only types (not simulation state)

These use floating point or player-facing text and are excluded from the fingerprint and from the
no-floating-point guard test.

- **PoseInterpolator** (`Talt.Core`, pure function): `Interpolate(previous, current, factor)` returns
  the pose to draw between two ticks. Never moves a vehicle backwards along its route; does not
  interpolate vehicles added or removed between the two snapshots. Lives in Core only so it can be
  unit-tested without a window.
- **Strings** (`Talt.Client.Localization`): key → text lookup loaded from `Strings.en.json` and
  `Strings.es.json`; every key must exist in both languages; missing keys fall back to English and
  are reported in tests. Selected with `--language en|es`.
