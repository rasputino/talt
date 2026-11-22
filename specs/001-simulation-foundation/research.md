# Research: Simulation Foundation

**Feature**: 001-simulation-foundation | **Date**: 2026-11-22 | **Spec**: [spec.md](spec.md)

Each section records a decision, its rationale and the alternatives considered. Items marked
**validate during implementation** have a task that confirms the decision with measurements.

---

## R1. Numeric representation of simulation state (cross-platform determinism)

**Decision:** Simulation state uses **integer fixed-point arithmetic**. Floating point is allowed only
outside the simulation (rendering, interpolation, UI, statistics shown to the player) and in
one-off setup code whose results are converted to integers once and stored (never recomputed during
ticks).

- Distances: `long` in **sub-tile units**, 1 tile = 65 536 units (Q16 fraction). A 4096-tile map spans
  2^28 units, so per-edge values fit in `int`; route and odometer sums use `long`.
- Time: one tick is the unit. Speeds are stored as **units per tick**, accelerations as **units per
  tick²** (both `int`). No `dt` multiplication happens inside the tick.
- Edge lengths: computed from integer node coordinates with an **integer square root** (exact,
  deterministic) when the network is built, stored as `int` units.
- Braking distance: `v² / (2a)` computed in `long` integer arithmetic.
- Aggregates (total distance travelled) are `long` sums; integer addition is associative, so parallel
  batches can be summed in any order with identical results.

**Rationale:**
- The spec requires bit-identical state on Windows, Linux and macOS, x64 and ARM64 (FR-013, SC-002).
- IEEE-754 basic operations are deterministic in principle, but in .NET several common paths are
  not guaranteed identical across hardware: `MathF.Pow`, `Sin`, `Exp` and friends call
  platform runtime libraries; `System.Numerics` vector helpers and `*Estimate` APIs may use fused
  multiply-add or reciprocal estimates depending on the CPU; SIMD width varies (Vector128/256/512),
  changing reduction order; and parallel floating-point sums depend on scheduling (the prototype's
  `TotalDistance` accumulation already has this problem).
- Guarding every float operation forever is fragile, especially with AI-assisted code changes.
  Integers make determinism the default and easy to review: "no `float`/`double` in simulation
  state" is a rule a reviewer or analyzer can check.
- Integer math is as fast or faster for this workload (adds, compares, a few multiplies).

**Alternatives considered:**
- *Constrained IEEE floats* (only `+ - * / sqrt`, no vector helpers, explicit ordering): workable but
  relies on discipline and on JIT behaviour staying stable across .NET versions; rejected as the
  default because one careless `Vector2.Lerp` or `MathF` call silently breaks replays.
- *A custom `Fixed` struct with operator overloading*: nicer syntax, but hides overflow and rounding
  rules and adds abstraction cost in hot loops. Rejected for now; small static helpers
  (`FixedMath`) with explicit `long`/`int` are clearer. Can be revisited if formulas grow complex.
- *`decimal`*: deterministic but ~10–100× slower; rejected.

**Guard:** a unit test scans `Talt.Core` simulation types by reflection and fails if a `float` or
`double` field is found in simulation state types (an allow-list covers setup-only types).

---

## R2. Tick rate and calendar

**Decision:** **40 ticks per in-game day**, **20 ticks per second at normal speed** (one day = 2 s,
FR-004). The date is derived from the tick counter: `day index = tick / 40`, converted to a
Gregorian date from the starting year with `DateOnly.FromDayNumber` (deterministic, handles leap
years, valid to year 9999, far beyond the 1 000-year requirement). The tick counter is a `long`.

**Rationale:** 20 Hz keeps per-tick motion small (a 10 tiles/s vehicle moves 0.5 tile per tick),
leaves a large budget per tick, and interpolation hides the step rate visually. At fast forward
(≥ ×20) the game runs ≥ 400 ticks/s; current benchmarks (0.35 ms per tick for 100 000 vehicles)
show this fits comfortably for the 50 000-vehicle reference load.

**Alternatives considered:** 30 or 60 Hz (smoother without interpolation but 1.5–3× the CPU cost
at every speed, hurting fast forward); 10 Hz (too coarse for later vehicle interactions such as
following distance and junction reservations).

---

## R3. Where the simulation runs (threading)

**Decision:** Run the simulation on the **main thread**, inside the window's update callback, using
a fixed-step accumulator. Each tick itself still uses worker threads internally (parallel vehicle
batches). A dedicated simulation thread is deferred until profiling shows the main thread cannot
meet the budgets.

Runner rules (implemented as pure logic in `Talt.Core`, fed with elapsed real time by the client, so
it is unit-testable and never reads the clock itself):

- `accumulator += elapsedRealSeconds × speedMultiplier`; run ticks while `accumulator ≥ tickLength`.
- **Catch-up cap** (FR-011, SC-007): at most `0.25 s × speedMultiplier × 20` ticks per frame for fixed
  speeds; excess accumulated time is discarded (the game falls behind real time instead of
  freezing).
- **Fast forward** (only when unlocked in player settings, via its own toggle): no multiplier; ticks run until a per-frame **time budget** (default 25 ms) is
  spent, guaranteeing ≥ 30 FPS (SC-003) whenever a single tick is shorter than the budget. The
  runner measures tick duration through an injected time source.
- **Pause**: no ticks; accumulator is reset so resuming causes no jump.
- **Interpolation factor** for rendering: `accumulator / tickLength` (0..1); 1 during fast forward.

**Rationale:** simplest design that meets the budgets; no cross-thread synchronization, no snapshot
double-buffering, easier to debug. Rendering and simulation already take well under a frame.

**Alternatives considered:** *dedicated simulation thread with double-buffered render snapshots*:
better isolation of long ticks from frame rate, but adds synchronization, snapshot copies and
harder debugging. Revisit with feature 013 (background flow assignment) or if ticks exceed ~8 ms.

---

## R4. Smooth rendering (interpolation)

**Decision:** The client keeps two pose buffers. When a frame runs one or more ticks, it copies the
current poses into the *previous* buffer **just before the last tick of that frame**, then runs the
last tick. Rendering interpolates `previous → current` with the runner's interpolation factor. If a
frame runs no ticks, both buffers are reused. Interpolation is rendering-only floating point.

Special cases: a vehicle that changed edge between the two poses is interpolated along the straight
segment between the two positions (visually indistinguishable at 0.5 tile per tick); a vehicle
added or removed between snapshots is drawn at its current pose without interpolation.

**Location:** the interpolation math is a pure function (`PoseInterpolator`) in `Talt.Core` so
it can be unit-tested without a window; it is rendering-only, never part of simulation state, and
is allow-listed in the no-floating-point guard test. The client owns the two pose buffers.

**Rationale:** no simulation cost (the simulation stores no "previous" state), constant memory, and
only one extra copy per frame regardless of how many ticks ran.

**Alternatives considered:** storing previous route index and distance inside the simulation every
tick (extra writes in the hot path, and every later system would need the same); extrapolation
from velocity (overshoots at stops).

---

## R5. Entity storage: structure of arrays vs Arch ECS

**Decision (provisional, validate during implementation):** **Keep plain structure-of-arrays (SoA)**
storage owned by each system (`VehicleFleet` today). A benchmark task ports the vehicle tick to Arch
(`Arch` 2.1.0) in the benchmarks project only, and compares 100 000 and 1 000 000 vehicles. Arch is
adopted only if it is at least as fast **and** keeps the tick deterministic and allocation-free;
otherwise SoA is confirmed. The result and decision are recorded in `docs/ARCHITECTURE.md`.

**Rationale (why SoA is expected to win for TALT):**
- Measured already: 0.35 ms (100 k) and 6 ms (1 M) per parallel tick, zero allocations.
- Deterministic iteration order and stable indices are trivial with arrays; ECS archetype moves
  (adding/removing components) reorder entities, which complicates determinism and save files.
- TALT's systems are few, large and homogeneous (all vehicles, all stations); ECS flexibility
  (mixing arbitrary components) matters less than raw iteration speed and explicit data layout.
- No external dependency in `Talt.Core`; easier for a human reviewer to follow.

**Alternatives considered:** Arch (fast, popular .NET ECS; candidate in the benchmark); other .NET
ECS libraries (DefaultEcs, Friflo.Engine.ECS) — not evaluated now; would only be considered if Arch
wins clearly and a comparison is warranted.

**Outcome (2026-11-22):** benchmarked (see `docs/ARCHITECTURE.md`, "Entity storage decision").
Separate per-field arrays were ~25 % slower than Arch; packing hot fields into a `VehicleState` struct
closed most of the gap (equal at 1 M vehicles, Arch ~16 % faster at 100 k single-threaded). Own
storage is kept because the production path is the parallel tick, Arch's parallel path was not
measured, and the gap does not justify the dependency. Revisit in feature 012.

**Entity identity:** vehicles get a stable `VehicleId` (monotonic `int`, never reused within a game)
separate from their array slot. Removal uses swap-with-last on the arrays plus an id→slot map, so
iteration stays dense; because swap order depends only on command order, it is deterministic.

---

## R6. Deterministic random numbers

**Decision:** Implement **xoshiro256\*\*** (public-domain algorithm, written from its published
description) as `DeterministicRandom` in `Talt.Core`, seeded with **SplitMix64**. The simulation owns
a root generator; each system gets an independent stream derived from `(game seed, system id)`, so
adding a system does not change the random sequence of others. Integer outputs only (`NextUInt64`,
`NextInt(maxExclusive)` with unbiased rejection, `NextFixed(range)`); no float output in simulation.
Generator state (4 × `ulong`) is part of the hashed simulation state.

**Rationale:** `System.Random(seed)` is deterministic today but its algorithm is an implementation
detail that may change between .NET versions, and its API encourages float use. xoshiro256\*\* is
fast, small and well studied.

**Alternatives considered:** PCG32 (also fine; xoshiro chosen for 64-bit output and speed);
`System.Random` (rejected above); cryptographic RNG (slow, not seedable).

Setup code (map and synthetic traffic generation) also moves to `DeterministicRandom` so scenarios
are reproducible across .NET versions.

---

## R7. Player actions (commands)

**Decision:** Every player action is a **command** object (an immutable record implementing
`ISimulationCommand`) submitted to the simulation's queue with an issuer id. At the start of each
tick the simulation dequeues all pending commands **in submission order**, validates each against the
current state and applies it or rejects it with a reason (`CommandResult`). Applied and rejected
commands are appended to the session recorder with the tick number. Commands never run in parallel.

First real commands (clarified): `AddVehicleCommand(lineId, maxSpeed)` and
`RemoveVehicleCommand(vehicleId)`. Test-only commands live in the test project.

**Rationale:** single entry point for state changes (FR-015–FR-017), natural recording/replay, and
the basis for saves (014) and possible multiplayer (lockstep).

**Alternatives considered:** direct method calls from the client (not recordable, races with
ticks); event sourcing of every internal change (overkill; only player inputs need recording).

---

## R8. Recorded sessions format

**Decision:** A recorded session is a **JSON** document: format version, scenario settings
(seed, map and traffic parameters, start year), and the ordered list of `{ tick, command }` entries
using a type discriminator. Serialized with `System.Text.Json` source generation (no reflection at
runtime). Defined in [contracts/recorded-session.md](contracts/recorded-session.md).

**Implementation note:** instead of `System.Text.Json` source generation, commands write and read
their own properties through `Utf8JsonWriter`/`JsonDocument` with a small registry of readers per
`type` (`CommandSerializer`). It is equally reflection-free, keeps each command's format next to the
command, and lets tests and later features register their own command types.

**Rationale:** human-readable for debugging and code review, small for typical sessions, no new
dependency. Performance is irrelevant here (sessions are small); saves (014) will use a binary
format.

**Alternatives considered:** binary format (premature), raw text lines (fragile).

---

## R9. State fingerprint

**Decision:** `StateHasher` computes a 64-bit **XxHash3** over every piece of simulation state in a
fixed order: tick counter, RNG states, then each system's arrays (only the live range, as raw bytes
via `MemoryMarshal.AsBytes`). Uses `System.IO.Hashing` 10.0.15 (Microsoft, MIT license, part of
the .NET ecosystem). Systems implement `IHashable.AppendTo(ref hasher)` so new systems join the
fingerprint explicitly.

**Rationale:** fast (GB/s), stable across platforms (defined algorithm, endianness-independent
because all supported targets are little-endian; the hasher asserts `BitConverter.IsLittleEndian`).

**Alternatives considered:** FNV-1a written by hand (no dependency but ~10× slower; acceptable, kept
as fallback if the dependency is rejected in review); SHA-256 (slow, unnecessary).

**New dependency justification (constitution):** `System.IO.Hashing` — purpose: state fingerprint;
license: MIT; maintained by Microsoft with each .NET release; size: ~100 KB.

---

## R10. Scheduled events

**Decision:** `SimulationEvents` lets systems register callbacks for `DayEnded`, `MonthEnded` and
`YearEnded`. After the tick that completes a day, handlers run in order **daily → monthly → yearly**,
and within a period in **registration order** (registration happens at simulation construction, so
it is deterministic). Handlers receive the simulation context (date, RNG stream) and run on the
simulation thread (they may parallelize internally under the same determinism rules).

**Rationale:** FR-019/FR-020; simple, explicit ordering; no hidden global event bus.

---

## R11. Headless runner

**Decision:** New console project `tools/Talt.Headless` referencing only `Talt.Core`. Commands:
`run` (scenario settings + ticks → final date, fingerprint, timings) and `replay` (recorded session →
fingerprints at checkpoints, optional comparison against expected values). Contract in
[contracts/headless-cli.md](contracts/headless-cli.md). The same scenario-building code is shared
with the client through `Talt.Core.Scenarios`.

**Rationale:** FR-022/FR-023, SC-008; also the tool CI (002) will use to compare fingerprints across
platforms.

**Alternatives considered:** test-only helper (not usable from CI scripts or by hand); a
`--headless` flag on the client (would pull window and GPU dependencies into a headless run).

---

## R12. Reference fingerprints for cross-platform checks

**Decision:** Determinism tests include **golden fingerprints**: a small fixed scenario run for
10 000 ticks with a recorded session, with the expected fingerprint values committed in the test
project. Any platform producing a different value fails the test. Golden values are regenerated
only deliberately (when simulation rules change), documented in the commit.

**Rationale:** until CI on three platforms exists (feature 002), this is the cheapest way to detect
cross-platform divergence: run the tests once on Linux and macOS.
