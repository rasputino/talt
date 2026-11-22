# Feature Specification: Simulation Foundation

**Feature Branch**: `001-simulation-foundation`

**Created**: 2026-11-22

**Status**: Draft

**Input**: User description: "Simulation foundation: a simulation that advances in-game time
independently of how fast the screen refreshes, so behaviour is identical on fast and slow computers
and the game can be paused, sped up and later saved and replayed. In-game date, pause and speed
control, predictable player actions, headless runs, scheduled daily/monthly/yearly events."

## Context

Today the prototype moves vehicles by however much real time passed since the previous screen
refresh. That makes behaviour depend on the computer and on the frame rate, offers no in-game
calendar and cannot be reproduced. Every later feature (towns, economy, saving, replays, possible
multiplayer) needs a simulation whose results depend only on the starting state and the player's
actions. This feature provides that foundation and moves the existing vehicles onto it, without
changing what the player sees beyond the date and speed display.

## Clarifications

### Session 2026-11-22

- Q: Must the simulation produce bit-identical results across Windows, Linux and macOS (including
  ARM Macs)? → A: Yes, bit-identical across platforms (Windows, Linux, macOS; x64 and ARM64).
- Q: Which real player action proves the action mechanism in this feature? → A: Adding a vehicle to
  a line and removing a vehicle.
- Q: Which game speed levels are offered? → A: Normal ×1, fast ×3, very fast ×8 and unlimited fast
  forward (as fast as the computer allows, at least ×20).
- Q: How are the speeds offered to the player? → A: Normal, fast and very fast are always available
  and the player switches freely between them (and pause). Fast forward is locked by default: it is
  unlocked from the game settings and, once unlocked, appears as a separate control, apart from the
  other speeds.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - In-game date advances steadily (Priority: P1)

As a player, I see the current in-game date (day, month, year) on screen, and it advances at a
steady pace while the game runs, starting from a configurable year (1950 by default).

**Why this priority**: The calendar is the backbone of the game's sense of progress and the clock
that later features (town growth, monthly finances, vehicle eras) depend on.

**Independent Test**: Start the prototype and watch the date: it begins on 1 January of the
configured year and advances one day roughly every two seconds at normal speed, rolling over months
and years correctly (including leap years).

**Acceptance Scenarios**:

1. **Given** a new game with the default settings, **When** it starts, **Then** the date shown is
   1 January 1950.
2. **Given** a new game configured to start in 1975, **When** it starts, **Then** the date shown is
   1 January 1975.
3. **Given** the game running at normal speed, **When** 60 seconds of real time pass, **Then** the
   date has advanced by about 30 days (one day ≈ 2 seconds), within one day of tolerance.
4. **Given** the date is 28 February of a leap year, **When** one day passes, **Then** the date is
   29 February; in a non-leap year it is 1 March.
5. **Given** the date is 31 December, **When** one day passes, **Then** the date is 1 January of the
   next year.

---

### User Story 2 - Pause and change game speed (Priority: P1)

As a player, I can pause and resume the game and switch freely between the standard speeds
(normal, fast, very fast). If I unlock fast forward in the game settings, it becomes available
through its own separate control. Vehicles behave the same at every speed, only faster.

**Why this priority**: Pausing to think and speeding through quiet periods are essential to the
genre; consistency across speeds proves the simulation is independent of the screen.

**Independent Test**: Run the prototype, switch speeds and pause; verify the date and vehicles stop
when paused, move faster at higher speeds, and that a vehicle completes the same route in the same
number of in-game days at every speed.

**Acceptance Scenarios**:

1. **Given** the game is running, **When** I pause, **Then** the date, vehicles and every simulated
   value stop changing, while I can still move the camera and the screen keeps refreshing.
2. **Given** the game is paused, **When** I resume, **Then** everything continues exactly from where
   it stopped, with no jump.
3. **Given** a vehicle on a line, **When** I run the game at normal speed and, separately, at very
   fast speed from the same starting state, **Then** the vehicle reaches the same stops on the same
   in-game dates in both runs.
4. **Given** fast forward is locked (the default), **When** I look at the speed controls or press
   the fast-forward control, **Then** fast forward is not offered and the speed does not change.
5. **Given** I unlock fast forward in the game settings, **When** I return to the game, **Then** a
   separate fast-forward control is available, and turning it off returns to the standard speed I
   was using before.
6. **Given** fast forward is unlocked and active on the reference scenario, **When** it runs, **Then**
   in-game time advances at least 20 times faster than at normal speed and the screen keeps at least
   30 refreshes per second.
7. **Given** the screen refresh rate is limited or uneven (for example the window is dragged or the
   computer is busy), **When** the game runs, **Then** in-game time still advances at the selected
   speed on average, and vehicle movement on screen stays smooth without visible stepping.

---

### User Story 3 - Predictable player actions (Priority: P2)

As a player, my actions take effect in a predictable way: the same actions, applied at the same
in-game moments on the same map, always lead to exactly the same result.

**Why this priority**: Predictability underpins saving and loading, replays, bug reproduction and
any future multiplayer. It must be designed in from the start.

**Independent Test**: Record a short session of player actions, replay it twice from the same seed
and compare the resulting game state; it must be identical. Repeat on a computer with a different
number of processor cores.

**Acceptance Scenarios**:

1. **Given** a map generated from a seed and a recorded list of player actions with the in-game
   moments when they happened, **When** the session is replayed twice, **Then** the game state after
   10 000 simulation steps is identical in both replays.
2. **Given** an action issued by the player, **When** the simulation processes it, **Then** it takes
   effect at a well-defined simulation step (the next one), never in the middle of a step.
3. **Given** the same recorded session, **When** it is replayed on computers with different numbers
   of processor cores or at different game speeds, **Then** the final state is identical.
4. **Given** an action that is not valid when processed (for example it refers to something that no
   longer exists), **When** the simulation processes it, **Then** it is rejected without side effects
   and the rejection is reported, identically in every replay.

---

### User Story 4 - Headless simulation for tests and tools (Priority: P3)

As a developer, I can run the simulation without opening any window, for automated tests,
benchmarks and tools such as replaying recorded sessions.

**Why this priority**: Fast, reliable automated verification of the simulation is a project rule;
it is valuable for every later feature but not visible to players.

**Independent Test**: Run a scenario for a given number of simulation steps from a command line or
test, without a graphics device, and obtain the final date and a fingerprint of the game state.

**Acceptance Scenarios**:

1. **Given** a computer without a usable graphics device, **When** a scenario is run headless for
   10 000 steps, **Then** it completes and reports the final in-game date and a state fingerprint.
2. **Given** a recorded session, **When** it is replayed headless and in the windowed game, **Then**
   both produce the same state fingerprint after the same number of steps.

---

### Edge Cases

- **Very slow frames:** if a single screen refresh takes a long time (for example one second while
  the window is dragged), the simulation must not try to catch up so much that the game freezes; it
  limits catch-up and may fall behind real time rather than lock up.
- **Simulation too slow for the selected speed:** if the computer cannot keep up with fast forward,
  the game runs as fast as it can while keeping the screen responsive, and in-game time simply
  advances more slowly than requested.
- **Speed change or pause in the middle of motion:** changing speed never makes vehicles jump,
  stutter or change their in-game timing.
- **Very long games:** the calendar must work for at least 1 000 in-game years and the step counter
  must not overflow in that time.
- **Events on the same step:** when a day, a month and a year all end on the same step, their
  scheduled events run in a fixed, documented order (daily, then monthly, then yearly).
- **Actions while paused:** player actions issued while paused are queued and applied, in order, on
  the first step after resuming; the game does not advance while paused just to apply them.
- **Many actions on one step:** several actions arriving for the same step are applied in the order
  they were issued.

## Requirements *(mandatory)*

### Functional Requirements

**Time and calendar**

- **FR-001**: The game MUST advance simulated time in fixed, equal steps, independent of the screen
  refresh rate and of the computer's speed.
- **FR-002**: The game MUST keep an in-game calendar date (day, month, year) using the Gregorian
  calendar, including leap years.
- **FR-003**: A new game MUST start on 1 January of a configurable starting year, defaulting to 1950.
- **FR-004**: At normal speed, one in-game day MUST last approximately 2 seconds of real time; the
  number of simulation steps per day MUST be fixed so a day always contains the same amount of
  simulated time regardless of speed.
- **FR-005**: The current date and the current speed (or paused state) MUST be visible on screen at
  all times during play.

**Speed control**

- **FR-006**: The player MUST be able to pause and resume the game.
- **FR-007**: The player MUST be able to switch freely between three standard speeds: normal (×1),
  fast (×3) and very fast (×8), plus pause.
- **FR-007a**: Fast forward (unlimited: as fast as the computer allows, at least ×20) MUST be locked
  by default and MUST NOT be reachable through the standard speed controls.
- **FR-007b**: A game setting MUST allow the player to unlock (and lock again) fast forward; the
  setting persists between sessions.
- **FR-007c**: When unlocked, fast forward MUST be offered through its own separate control (toggle);
  turning it off MUST return to the standard speed that was active before; pausing while in fast
  forward pauses the game, and resuming returns to fast forward.
- **FR-007d**: Locking fast forward while it is active MUST return the game to the previous standard
  speed.
- **FR-008**: Changing speed MUST change only how many simulation steps run per second of real time,
  never the size of a step, so results are identical at every speed.
- **FR-009**: While paused, no simulated value may change; camera movement and screen refresh MUST
  keep working.
- **FR-010**: On-screen movement of vehicles MUST look smooth at every speed and refresh rate,
  without visible stepping caused by the fixed simulation rate.
- **FR-011**: When a frame takes unusually long, the game MUST limit how many simulation steps it
  runs to catch up, so the game never freezes.

**Determinism and player actions**

- **FR-012**: Given the same starting state (including the map seed) and the same sequence of player
  actions applied at the same simulation steps, the simulation MUST produce exactly the same state
  at every step.
- **FR-013**: The result of the simulation MUST NOT depend on real time, on the screen refresh rate,
  on the selected speed, on the number of processor cores, on the operating system (Windows, Linux,
  macOS) or on the processor architecture (x64, ARM64): the same inputs MUST produce bit-identical
  state on every supported platform.
- **FR-014**: All randomness in the simulation MUST come from seeded sources stored as part of the
  game state, so it is reproducible.
- **FR-015**: Every player action that changes the game MUST be delivered to the simulation as an
  action request that is applied at the start of the next simulation step, in the order issued.
- **FR-016**: Action requests and the step at which they were applied MUST be recordable, and a
  recorded session MUST be replayable to reproduce the same state.
- **FR-017**: Invalid action requests MUST be rejected without side effects, and the rejection MUST
  be reported to the issuer.
- **FR-017a**: The player MUST be able to add a vehicle to an existing line and to remove an existing
  vehicle, both as action requests; these are the real actions that demonstrate recording and replay
  in this feature (keyboard shortcuts are enough until the interface feature).
- **FR-018**: The game MUST be able to compute a fingerprint of the full simulation state, so two
  states can be compared quickly for equality.

**Scheduled events**

- **FR-019**: Game systems MUST be able to register work that runs at the end of every in-game day,
  month and year.
- **FR-020**: When several period boundaries coincide, events MUST run in the order daily, monthly,
  yearly, and within the same period in a fixed, documented order.

**Existing simulation and headless use**

- **FR-021**: The existing vehicle simulation MUST run on the new fixed-step simulation, preserving
  its current behaviour (acceleration, braking, stops, dwell time) expressed in in-game time.
- **FR-022**: The simulation MUST be runnable without any window or graphics device, for a given
  number of steps, reporting the final date and state fingerprint.
- **FR-023**: Running a scenario headless and in the windowed game with the same inputs MUST produce
  the same state fingerprint.

### Key Entities

- **Simulation**: the complete simulated game state and the rules that advance it one step at a time;
  owns the clock, the calendar, the random sources, the pending action requests and all simulated
  systems (vehicles today, more later).
- **Simulation step (tick)**: one fixed advance of simulated time; the smallest unit at which
  anything in the game changes.
- **Game date**: the in-game calendar date derived from the step count and the starting year.
- **Game speed**: paused, normal, fast, very fast or (if unlocked) fast forward; determines how many steps run per
  second of real time.
- **Action request (command)**: a player intention (for example "build", "buy vehicle") with the step
  at which it is applied; validated and applied by the simulation, or rejected.
- **Recorded session**: the starting conditions plus the ordered list of action requests with their
  steps; enough to reproduce a game exactly.
- **Scheduled event**: work registered by a game system to run at the end of every day, month or
  year.
- **State fingerprint**: a compact value computed from the whole simulation state, equal for equal
  states.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Replaying a recorded session twice produces identical state fingerprints at every one
  of 10 000 consecutive steps.
- **SC-002**: The same recorded session produces the same final fingerprint when run headless, in the
  windowed game, at every game speed, with different numbers of processor cores, and on Windows,
  Linux and macOS (x64 and ARM64).
- **SC-003**: On the reference hardware with the reference load for this feature (a 4096 × 4096 map
  with 50 000 vehicles), fast forward (once unlocked) advances in-game time at least 20 times faster than normal
  speed while the screen keeps at least 30 refreshes per second.
- **SC-004**: At normal speed, the date advances 30 days per minute of real time (±1 day), and over a
  10-minute run the drift from the expected pace is under 2%.
- **SC-005**: Pausing stops every simulated value within the same screen refresh in which the player
  pauses; resuming introduces no visible jump in any vehicle's position.
- **SC-006**: At every speed, viewers see vehicles move smoothly: no frame shows a vehicle moving
  backwards or jumping more than its expected per-frame distance.
- **SC-007**: A single 1-second frame hitch never causes more than 0.25 seconds of catch-up
  simulation at normal speed, so the game never appears frozen after a hitch.
- **SC-008**: A headless run of 10 000 steps of the reference load completes in under 60 seconds on
  the reference hardware.

## Assumptions

- **Reference hardware** is the development machine (AMD Ryzen 7 160, 8 cores / 16 threads,
  integrated Radeon GPU), as defined in `docs/SPECKIT_GUIDE.md`. The full reference scenario (towns,
  industries, stations) does not exist yet; this feature uses a 4096 × 4096 map with 50 000 vehicles
  as its reference load.
- **Step rate**: a fixed number of simulation steps per in-game day is chosen during planning
  (around 40 steps per day, i.e. about 20 steps per second at normal speed, is expected); the exact
  value is a technical decision as long as FR-004 holds.
- **Speeds**: normal ×1, fast ×3, very fast ×8 and unlimited fast forward (≥ ×20), confirmed in
  clarification; multipliers can be tuned later without changing simulation results. Fast forward
  is an unlockable option with its own control (clarification).
- **Settings storage**: until the interface feature (005) provides a settings screen, the
  fast-forward unlock is a persisted setting the player toggles with a launch option or a settings
  file, and the separate control is a dedicated keyboard shortcut; feature 005 turns them into a
  settings toggle and a separate button.
- **Display**: a minimal on-screen display of date and speed is enough (for example in the window
  title or a simple overlay); the real interface arrives with the user interface feature (005).
- **Controls**: keyboard shortcuts are enough to pause and change speed until the interface feature
  provides buttons. The existing prototype controls (Space, +, −) are a reasonable starting point.
- **Determinism scope**: bit-identical results are required for the same game build on every
  supported operating system and processor architecture. Until continuous integration exists
  (feature 002), cross-platform equality is verified with reference fingerprints committed with the
  tests and checked on each platform at least once; any difference is a bug.
- **Actions available today**: the prototype has few player actions; this feature defines the
  action-request mechanism with two real actions (add a vehicle to a line, remove a vehicle) plus
  test-only actions. Building actions arrive with later features.
- **Starting year configuration**: provided as a launch setting for now; the new-game screen comes
  with feature 004.
- **Out of scope**: saving and loading (feature 014), replay user interface, any interface beyond
  date and speed display, multiplayer.
