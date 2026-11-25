# Spec Kit Implementation Guide

This guide drives the full implementation of **Transit & Logistics Tycoon (TALT)** with
[GitHub Spec Kit](https://github.com/github/spec-kit). It contains every prompt needed, in order:
the constitution, then one specification and one plan prompt per feature, plus what to answer during
clarification and how to verify each feature before moving on.

Read [VISION.md](VISION.md) and [ARCHITECTURE.md](ARCHITECTURE.md) first: this guide builds on them.

---

## 1. How to use this guide

### 1.1 Workflow per feature

Spec Kit works one feature at a time, each on its own branch (`001-…`, `002-…`; numbering is
sequential in this repository). For every feature in [section 6](#6-feature-roadmap):

| Step | Command | What to do |
|---|---|---|
| 1 | `/speckit-specify` | Paste the feature's **Specify prompt**. It describes *what* and *why*, never *how*. |
| 2 | `/speckit-clarify` | Answer the questions. Use the feature's **Clarify guidance** for recommended answers. |
| 3 | `/speckit-plan` | Paste the feature's **Plan prompt**. This is where technology and architecture go. |
| 4 | `/speckit-checklist` | Optional. Use the checklist topics suggested for the feature. |
| 5 | `/speckit-tasks` | No prompt needed. Review that tasks are small and include tests and docs. |
| 6 | `/speckit-analyze` | Fix any inconsistency it reports between spec, plan and tasks before implementing. |
| 7 | `/speckit-implement` | Implement. Stop at each checkpoint, run the app and the tests. |
| 8 | `/speckit-converge` | If anything was left unbuilt, it adds the missing tasks; implement them. |
| 9 | Verify | Run the feature's **Done when** checks. Update `docs/ARCHITECTURE.md`. Merge. |

### 1.2 Rules of thumb

- **Do not specify the whole game in one feature.** One spec per feature, in roadmap order. Each one
  must leave the game runnable.
- **Paste the shared context block** ([section 4](#4-shared-context-block)) at the top of every
  Specify prompt. Spec Kit starts each feature cold; the block gives it the essentials.
- **Keep Specify prompts free of technology.** Technology belongs in the Plan prompt and the
  constitution.
- **Answer clarifications with the defaults in this guide** unless you have changed your mind.
  When you do, update [VISION.md](VISION.md) too, so the next features see the decision.
- **Never skip the verification step.** Performance budgets ([section 5](#5-performance-budgets))
  are part of "done".

---

## 2. Decisions already made

These are settled. They go in the constitution and the shared context block.

| Topic | Decision |
|---|---|
| Genre | Transport management game. The player is a transport company. |
| Scope of play | **Transport only.** No manufacturing, no production chains managed by the player. The world produces and consumes; the player connects it. |
| Cargo model | Raw materials and most processed goods go to **any** place that accepts them. Passengers, mail and consumer goods go to a **specific destination**, modelled as aggregated flows, not individual agents. |
| Visual style | Voxel art, readable at every zoom level, with its own identity. Inspired in spirit by modern voxel transport games; no copied assets or look. |
| Map size | Large maps (at least 4096 × 4096 tiles) must be possible without slowdown. Small maps remain available. |
| Platforms | Windows, Linux, macOS. |
| Technology | C# on .NET 10, no game engine. Silk.NET (window, input, bindings), WebGPU through wgpu-native (Vulkan, Metal, DirectX 12), xUnit, BenchmarkDotNet, central package management, warnings as errors. |
| Architecture | `Talt.Core` (pure logic, no graphics) and `Talt.Client` (window, input, rendering). Data-oriented, parallel simulation. |
| Content | 100% original: code, graphics, sounds, music, text. OpenTTD may be read only to understand mechanics, never copied or translated. |
| License | Source-available, PolyForm Strict 1.0.0. Copyright © 2026 Pablo León Manzano. |
| Documentation | English. |
| Development | Mostly AI-assisted. Code must stay readable and reviewable by a C# developer. |

## 3. Open decisions and proposed defaults

These were open in [VISION.md](VISION.md). This guide proposes defaults so implementation can
proceed. Confirm or change them during `/speckit-clarify` of the feature that first needs them.

| Topic | Proposed default | First needed in |
|---|---|---|
| Transport modes in version 1 | Road (buses, trucks) and rail. Ships and aircraft in version 2. | 007, 015 |
| Competitors (AI companies) | Not in version 1. | — |
| Multiplayer | Not in version 1, but the simulation stays deterministic so it remains possible. | 001 |
| Modding | Version 1: game content (vehicles, cargo, industries, buildings) defined in data files the game loads at startup. No scripting. | 009, 016 |
| Map generation | Procedural from a seed, with size and terrain settings. Optional heightmap import later. | 004 |
| In-game UI technology | Decided by research in feature 005's plan (see its Plan prompt). | 005 |
| Languages | English and Spanish from the start. | 005 |
| Time model | Calendar starting in 1950 by default; one in-game day ≈ 2 seconds at normal speed (tunable). | 001 |
| Tile scale | 1 tile = 1 terrain voxel column today; feature 018 may introduce smaller voxels per tile for detail. | 018 |
| Game over | Bankruptcy after a grace period with negative money and maximum loan. Sandbox mode without game over. | 011 |

## 4. Shared context block

Paste this at the top of every `/speckit-specify` prompt.

```text
PROJECT CONTEXT (shared by every feature)
Transit & Logistics Tycoon (TALT) is an original transport management game for Windows, Linux and
macOS. The player runs a transport company that builds road and rail networks and operates vehicles
to move passengers and cargo across a living, voxel-art world. There is no manufacturing: towns and
industries produce and consume on their own; the player connects them. Raw materials and most
processed goods can be delivered to any place that accepts them. Passengers, mail and consumer
goods want to reach a specific destination (modelled as aggregated flows between places, not
individual agents). Very large maps (at least 4096 x 4096 tiles, tens of thousands of vehicles)
must stay smooth: performance is a core feature. All content is original.

The repository already contains a technical prototype: chunked voxel terrain generation and
meshing, a WebGPU renderer with an orbit camera, a road-graph with A* pathfinding, and a parallel
vehicle simulation (vehicles on two-stop lines, no interactions yet) with benchmarks. Read
docs/VISION.md and docs/ARCHITECTURE.md before writing the spec, and build on the existing code
rather than replacing it, unless the spec explains why.
```

## 5. Performance budgets

Proposed global budgets. Every feature's plan must state how it stays within them, and every feature
that touches simulation or rendering must add or update a benchmark.

**Reference hardware:** the development machine (AMD Ryzen 7 160, 8 cores / 16 threads, integrated
Radeon GPU). **Reference scenario:** 4096 × 4096 map, 500 towns, 1 000 industries, 50 000 vehicles,
5 000 stations.

| Metric | Budget |
|---|---|
| Frame time | ≤ 16.6 ms (60 FPS) at normal speed in the reference scenario |
| Simulation tick (normal speed) | ≤ 4 ms average, ≤ 8 ms worst case |
| Fast forward (maximum speed) | ≥ 30 FPS while simulating |
| New game generation, 4096 × 4096 | ≤ 20 s, with a progress screen |
| Save / load, reference scenario | ≤ 3 s / ≤ 5 s |
| Memory, reference scenario | ≤ 4 GB |
| Garbage collection | No allocations in per-tick and per-frame hot paths; no visible GC pauses |
| Input latency | Camera and UI respond within one frame |

---

## 6. Feature roadmap

| # | Feature | Depends on | Milestone |
|---|---|---|---|
| 001 | Simulation foundation | — | MVP |
| 002 | Continuous integration and quality gates | 001 | MVP |
| 003 | Large-map rendering | 001 | MVP |
| 004 | Map generation and new game | 003 | MVP |
| 005 | User interface foundation | 001, 003 | MVP |
| 006 | Construction and terraforming framework | 004, 005 | MVP |
| 007 | Roads | 006 | MVP |
| 008 | Towns | 004, 007 | MVP |
| 009 | Cargo types and industries | 004 | MVP |
| 010 | Stations and cargo handling | 007, 008, 009 | MVP |
| 011 | Economy and finance | 005, 010 | MVP |
| 012 | Road vehicles, depots and lines | 007, 010, 011 | MVP |
| 013 | Destination demand model | 008, 010, 012 | MVP |
| 014 | Save and load | 001–013 | MVP |
| 015 | Railways | 006, 010, 012 | Version 1 |
| 016 | Vehicle catalog and eras | 012, 015 | Version 1 |
| 017 | Line management and statistics | 012, 013 | Version 1 |
| 018 | Visual quality | 003, 008, 009 | Version 1 |
| 019 | Audio | 005 | Version 1 |
| 020 | Game modes, goals and settings | 011, 014 | Version 1 |
| 021 | Ships and aircraft | 015, 016 | Version 2 |
| 022 | Packaging and release | 002, all v1 | Version 1 release |

**MVP** = a playable vertical slice: generate a map, build roads and stops, run buses and trucks
between towns and industries, earn money, save and load. **Version 1** adds rail, eras, statistics,
visuals, audio and game modes.

---

## 7. Constitution

Run once, before feature 001.

### `/speckit-constitution` prompt

```text
Create the constitution for Transit & Logistics Tycoon (TALT), an original transport management
game in C# on .NET 10 for Windows, Linux and macOS, built without a game engine on Silk.NET and
WebGPU (wgpu-native). Development is mostly AI-assisted and reviewed by a human C# developer.

Core principles:

I. Core and Client separation (NON-NEGOTIABLE)
Talt.Core holds all game logic and must never reference graphics, windowing, audio or platform
libraries. Everything in Talt.Core is testable without a GPU or a window. Talt.Client (window,
input, rendering, audio, UI) depends on Talt.Core, never the other way around. Platform-specific
code lives only in the client, behind small interfaces.

II. Performance is a feature
The game must stay smooth on very large maps. Global budgets (docs/SPECKIT_GUIDE.md, section 5) are
acceptance criteria, not wishes. Simulation and rendering hot paths must not allocate per tick or
per frame. Every feature that touches simulation or rendering adds or updates a BenchmarkDotNet
benchmark and records its results in docs/ARCHITECTURE.md. A measured regression beyond 10% on a
reference benchmark blocks the merge unless justified in the plan.

III. Data-oriented, deterministic simulation
Simulation state is stored in contiguous, cache-friendly structures (structure of arrays or an ECS).
The simulation advances in fixed ticks and never reads the wall clock or uses unseeded randomness.
Same seed and same player commands produce the same state, tick by tick. Parallel execution must
produce results identical to sequential execution, verified by tests. Player actions reach the
simulation as commands, which keeps saves, replays and future multiplayer possible.

IV. Tested increments
All Talt.Core logic has xUnit tests written with or before the code. Every bug fix includes a
regression test. Each user story is independently testable. The full test suite passes on every
commit; tests never depend on a GPU, a window or timing.

V. Readable, idiomatic C#
Code is written to be reviewed and debugged by a human: clear names, small focused types, XML
documentation on public APIs, comments that explain why. Unsafe code and native interop are
confined to the rendering and platform layers. Prefer simple designs; any added complexity must be
justified in the plan.

VI. Original content and legal compliance (NON-NEGOTIABLE)
No code, graphics, sounds, music, text or level data from OpenTTD, Transport Tycoon, Voxel Tycoon or
any other game. Existing games may be studied only to understand mechanics; implementations are
designed independently. The trademark "Transport Tycoon" is never used. The project is
source-available under PolyForm Strict 1.0.0, copyright Pablo León Manzano. Third-party libraries
must have licenses compatible with distributing a closed commercial game.

VII. Data-driven content
Game content (cargo types, industries, vehicles, buildings, eras, balancing values) is defined in
versioned data files loaded at startup, validated with clear error messages, not hard-coded. This
keeps balancing easy and leaves the door open for modding.

VIII. Living documentation in English
All documentation, code comments and identifiers are in English. docs/ARCHITECTURE.md is updated by
every feature that changes structure, data flow or performance numbers. docs/VISION.md records
product decisions. Player-facing text is localizable (English and Spanish at minimum).

Technology constraints:
- .NET 10, C# latest, nullable enabled, warnings as errors, recommended analyzers,
  central package management (Directory.Packages.props).
- Window, input and GPU bindings: Silk.NET. GPU API: WebGPU via wgpu-native. No game engine.
- Tests: xUnit. Benchmarks: BenchmarkDotNet. CI: GitHub Actions on Windows, Linux and macOS.
- New dependencies require a justification in the plan (purpose, license, maintenance, size).

Development workflow:
- One Spec Kit feature per branch; each feature leaves the game buildable and runnable.
- Implementation proceeds in small tasks; after each user story the app is run and verified.
- AI agents never commit, push or merge unless the developer explicitly asks.
- A feature is done when: tests pass, benchmarks meet budgets, the app runs and the feature is
  demonstrated, documentation is updated.

Governance:
The constitution supersedes other practices. Plans include a Constitution Check; violations must be
justified in the plan's complexity tracking or the design changes. Amendments are versioned with
semantic versioning and dated.
```

---

## 8. Features

Each feature has: goal, **Specify prompt** (paste after the shared context block), **Clarify
guidance**, **Plan prompt**, suggested checklists and **Done when**.

---

### 001 — Simulation foundation

**Goal:** turn the prototype's per-frame update into a real, deterministic game simulation with a
fixed tick, a calendar and speed control, independent of rendering.

#### Specify prompt

```text
FEATURE: Simulation foundation

The game needs a simulation that advances in-game time independently of how fast the screen
refreshes, so behaviour is identical on fast and slow computers and the game can be paused, sped up
and later saved and replayed.

User stories:
- P1: As a player, I see the current in-game date (day, month, year) and it advances steadily while
  the game runs, starting from a configurable year (default 1950).
- P1: As a player, I can pause, resume and change the game speed (normal, fast, very fast, and a
  maximum "fast forward"), and vehicles move consistently at every speed.
- P2: As a player, my actions (for example, placing something or buying a vehicle) take effect in a
  predictable way: the same actions on the same map always lead to the same result.
- P3: As a developer, I can run the simulation without any window (headless) for tests, benchmarks
  and tools.

Acceptance highlights:
- The visual movement of vehicles is smooth even though the simulation updates at a fixed rate.
- Pausing stops all simulated change; the camera and interface keep working while paused.
- Running the same seed and the same sequence of player commands twice produces identical state.
- Periodic events can be scheduled daily, monthly and yearly (used later by towns, economy, etc.).

Success criteria:
- Simulation determinism verified over 10 000 ticks with recorded commands.
- At maximum speed the game simulates at least 20x normal speed in the reference scenario while
  keeping at least 30 frames per second.

Out of scope: saving and loading (feature 014), any user interface beyond showing date and speed.
```

#### Clarify guidance

- Tick rate: fixed simulation step (propose 20 ticks per second at normal speed); speed multiplies
  ticks per second, never the step size.
- Speeds: normal ×1, fast ×3 and very fast ×8 are always available. Unlimited fast forward is
  locked by default, unlocked from the game settings, and then has its own separate control
  (decided in feature 001 clarification).
- One in-game day ≈ 2 seconds at normal speed; month and year boundaries trigger events.
- Commands are queued and applied at the start of the next tick.
- Rendering interpolates between the last two simulation states for smooth motion.

#### Plan prompt

```text
Implement in Talt.Core a Simulation (or GameSession) type that owns all simulation state and
advances it with Tick(): fixed time step, tick counter, GameDate/calendar, scheduled daily, monthly
and yearly events, a command queue applied at tick start, and a seeded deterministic random number
generator (no System.Random shared state, no wall clock). Refactor the existing VehicleFleet to be
driven by this simulation.

In Talt.Client, run the simulation with a fixed-step accumulator decoupled from rendering; the
renderer interpolates vehicle positions between the previous and current tick (store previous
poses). Decide in research.md whether the simulation runs on the main thread or a dedicated thread
with double-buffered render snapshots; prefer the simplest option that meets the budgets and
document the trade-off.

Decide in research.md whether to keep plain structure-of-arrays storage or adopt the Arch ECS for
simulation entities. Benchmark both on the vehicle tick (100k and 1M vehicles) and pick one; record
the result in docs/ARCHITECTURE.md. Whatever is chosen becomes the pattern for later features.

Add a headless runner (console project or test helper) that loads a scenario, applies recorded
commands and runs N ticks. Add determinism tests (state hash after N ticks, sequential vs parallel)
and benchmarks for a full tick. Keep Talt.Core free of graphics references.
```

**Checklists:** determinism, performance.
**Done when:** date shows and advances; pause and speeds work with smooth interpolated motion;
determinism tests pass; tick benchmarks recorded; ECS decision documented.

---

### 002 — Continuous integration and quality gates

**Goal:** every push builds and tests on all three platforms and catches performance regressions.

#### Specify prompt

```text
FEATURE: Continuous integration and quality gates

The project targets Windows, Linux and macOS but is developed on Windows. Every change must be
checked automatically on all three platforms so problems are found immediately, and performance
regressions must be visible before they reach the main branch.

User stories:
- P1: As the developer, every push and pull request builds the solution and runs all tests on
  Windows, Linux and macOS, and I see a clear pass/fail result.
- P2: As the developer, I can trigger a benchmark run and compare the results against the previous
  baseline, with regressions above 10% highlighted.
- P3: As the developer, each successful build of the main branch produces downloadable game builds
  for the three platforms.

Success criteria:
- A failing test or build on any platform blocks merging.
- A full CI run (build and tests, three platforms) completes in under 15 minutes.

Out of scope: store releases, installers, code signing (feature 022).
```

#### Clarify guidance

- The repository is private on GitHub; use GitHub Actions.
- Benchmarks run on demand (manual trigger) and nightly, not on every push, because shared CI
  runners are noisy. Baselines are stored as artifacts or in the repo under `benchmarks/baselines/`.
- GPU tests are not run in CI; the client is only built.

#### Plan prompt

```text
Add GitHub Actions workflows: build-and-test on windows-latest, ubuntu-latest and macos-latest
(.NET 10 SDK, restore with cache, build Release with warnings as errors, run dotnet test with
results uploaded). Add a benchmark workflow (workflow_dispatch and nightly schedule) that runs
BenchmarkDotNet with JSON export and compares against a stored baseline with a small script,
failing or warning on regressions above 10%. Add a publish job on main that produces
self-contained builds for win-x64, linux-x64 and osx-arm64 as artifacts, verifying that the
wgpu-native and GLFW native libraries are included for each runtime. Document the workflows in
docs/ARCHITECTURE.md (new "Continuous integration" section).
```

**Done when:** a pull request shows three green platform checks; a manual benchmark run produces a
comparison report; artifacts download and start on each platform (verify macOS and Linux manually
at least once).

---

### 003 — Large-map rendering

**Goal:** draw 4096 × 4096 maps at 60 FPS with smooth camera movement (the prototype already
struggles at 2048 × 2048).

#### Specify prompt

```text
FEATURE: Large-map rendering

Players must be able to play on very large maps without the slowdowns typical of classic transport
games. Today a 2048 x 2048 map makes camera movement jerky. The world must look the same, but
render efficiently at any size and zoom level, and update quickly when the terrain changes.

User stories:
- P1: As a player on a 4096 x 4096 map, I can pan, rotate and zoom the camera smoothly at every
  zoom level, from close-up to the whole map.
- P1: As a player, when terrain or objects change (later: building, terraforming), the view updates
  within a fraction of a second without stutter.
- P2: As a player, distant parts of the map remain readable (terrain shape, water, roads) even when
  simplified.
- P3: As a player, I can see the whole map from far away without the game becoming slow.

Success criteria:
- 60 FPS on the reference hardware on a 4096 x 4096 map at all zoom levels, with no frame above
  33 ms during continuous camera movement.
- Modifying the terrain of one tile updates the view in under 100 ms.
- Memory used by rendering for a 4096 x 4096 map stays under 2 GB.

Out of scope: new visual effects such as shadows (feature 018).
```

#### Clarify guidance

- Keep the current look (colors, ambient occlusion, fog); visual upgrades come in 018.
- Map edges may show the terrain's side walls; nothing renders below the world.

#### Plan prompt

```text
Optimize the existing chunk renderer (Talt.Core.Meshing and Talt.Client.Rendering):
- Greedy meshing: merge coplanar faces with the same color and ambient occlusion; keep a test that
  greedy and naive meshes cover exactly the same surface.
- Compressed, chunk-relative vertices (for example packed 16-bit positions, normal index, color
  index or palette) with a per-chunk offset, reducing vertex memory at least 3x.
- Frustum culling of chunks on the CPU using chunk bounding boxes; skip empty and fully hidden
  chunks; never emit the bottom faces of the world.
- Level of detail: for distant chunks, build simplified meshes (for example 2x and 4x downsampled
  heightfield-style meshes) with seamless transitions or skirts; choose LOD by screen-space error.
- Asynchronous meshing on worker threads with a dirty-chunk queue; upload to the GPU on the render
  thread; remesh only affected chunks (and neighbours when edits touch borders).
- Reduce draw calls (batch chunks into larger buffers or use indirect draws) if profiling shows
  draw-call overhead.
Add benchmarks for meshing (naive vs greedy, per chunk) and a render statistics overlay (frame time,
draw calls, triangles, visible chunks). Record results for 512, 2048 and 4096 maps in
docs/ARCHITECTURE.md.
```

**Checklists:** performance, rendering correctness.
**Done when:** 4096 map at 60 FPS with smooth panning; single-tile edit updates in < 100 ms; numbers
recorded.

---

### 004 — Map generation and new game

**Goal:** generate believable, varied maps of any supported size from a seed, ready for towns and
industries.

#### Specify prompt

```text
FEATURE: Map generation and new game

Players start a game by generating a new map. Maps must look natural, offer interesting
transport challenges (hills, rivers, lakes, coasts) and be reproducible from a seed so players can
share them.

User stories:
- P1: As a player, I choose map size (from 256 x 256 up to at least 4096 x 4096, non-square
  allowed), seed, terrain roughness, maximum height, amount of water and sea level, and generate a
  map with a progress indicator.
- P1: As a player, the same settings and seed always produce the same map.
- P2: As a player, maps contain seas or lakes, rivers that flow downhill to water, beaches, hills,
  mountains with snow, and scattered trees/forests.
- P2: As a player, the generator reserves suitable locations for towns and industries (used by
  features 008 and 009), spaced sensibly and placed on buildable land.
- P3: As a player, I can preview the map in a small overview before starting.

Success criteria:
- A 4096 x 4096 map generates in under 20 seconds on the reference hardware.
- Rivers always connect to a lake or the sea; no floating water.
- At least 95% of reserved town sites are on flat, connected land.

Out of scope: heightmap import, multiple climates (temperate only in version 1).
```

#### Clarify guidance

- Height range stays within one vertical chunk for now unless more height is needed for good
  mountains; if so, allow multiple vertical chunks.
- Terrain must remain buildable: limit slopes so most land can host roads (classic transport
  games use "one step per tile" slopes; decide whether TALT enforces maximum steepness).
- Trees are scenery objects (removable when building), not terrain voxels.

#### Plan prompt

```text
Replace the prototype TerrainGenerator with a staged, deterministic map generator in Talt.Core:
settings record, seeded noise (keep ValueNoise or add a simplex/OpenSimplex implementation written
from scratch), continent/height shaping, sea level, erosion-like smoothing to keep land buildable,
river generation (downhill flow from springs to water with carving), lakes, beaches, snow line,
forest density map and tree placement as scenery data, and site selection for towns and
industries (Poisson-disc sampling on flat buildable land, with minimum distances).
Run stages in parallel where possible (per chunk or per row) and report progress through an
IProgress<T>. Store tile-level data (height, terrain type, scenery) in compact arrays separate from
the render voxels, so gameplay queries do not walk voxels. Add the new-game screen hook in the
client (a simple screen is enough until feature 005 provides the UI framework). Tests: determinism
per seed, rivers reach water, sites on valid land. Benchmarks: generation time per map size.
```

**Done when:** maps of all sizes generate deterministically within budget; rivers and sites
validated by tests; screenshots of three seeds attached to the PR.

---

### 005 — User interface foundation

**Goal:** a complete in-game interface framework (menus, HUD, windows, tooltips), mouse picking and
localization.

#### Specify prompt

```text
FEATURE: User interface foundation

The game needs a clear, attractive interface that matches the voxel art style and scales to many
windows (vehicles, stations, finances) without becoming slow or cluttered.

User stories:
- P1: As a player, I start the game on a main menu (new game, load game, settings, quit) and,
  in game, see a top bar with company money, current date and speed controls, and a toolbar to open
  tools and windows.
- P1: As a player, I can hover and click on the map and the game knows exactly which tile, vehicle,
  station or building is under the cursor, highlighting it.
- P1: As a player, I can open, move, stack, pin and close windows; tooltips explain buttons.
- P2: As a player, I can play in English or Spanish and switch language in settings.
- P2: As a player, I can scale the interface for small and high-resolution screens.
- P3: As a player, I can change key bindings for camera and common actions.

Success criteria:
- With 20 windows open the frame time increases by less than 1 ms.
- Picking resolves the correct object within one frame, at any zoom.
- 100% of player-facing text comes from localization files.

Out of scope: the content of specific windows (they arrive with their features).
```

#### Clarify guidance

- Visual style: clean, flat panels with rounded corners and voxel-styled icons; readable fonts
  with good Spanish character support.
- Settings persist between sessions (resolution, fullscreen, VSync, UI scale, language, bindings).

#### Plan prompt

```text
Research in research.md the UI technology and pick one, considering: rendering through our WebGPU
renderer, text quality (font atlas or SDF fonts), layout system, performance with many windows,
maintenance and license. Candidates: (a) a custom retained-mode UI library inside Talt.Client
rendered with our WebGPU pipeline and an SDF font atlas; (b) Dear ImGui via ImGui.NET with heavy
styling (fast to build, harder to make look polished); (c) an existing .NET UI library that can
render through a custom backend (only if actively maintained and license-compatible). Prefer (a)
unless research shows a clearly better option; keep ImGui.NET only for debug overlays.

Implement: UI layer (widgets: panel, window, button, label, icon, list, scroll, tabs, slider,
checkbox, text input, tooltip), layout, theming, input routing between UI and world, window
manager. Picking: GPU ID buffer or CPU ray against tile heights plus object bounds; choose and
justify. Localization: string tables in data files (English and Spanish), keys in code, plural and
number/date formatting per culture. Settings persistence in the user profile folder. Key bindings
data-driven. Tests for layout, localization lookup and input routing; benchmark UI frame cost.
```

**Done when:** main menu, HUD, windows and tooltips work in both languages; picking highlights the
right object; UI cost measured.

---

### 006 — Construction and terraforming framework

**Goal:** a shared way to build, preview, cost and demolish anything, plus terrain editing.

#### Specify prompt

```text
FEATURE: Construction and terraforming framework

Everything the player builds (roads, rails, stations, depots) shares the same interaction: choose a
tool, see a preview with its cost, confirm, and the money is spent. The player also needs to shape
the terrain to make room for infrastructure.

User stories:
- P1: As a player, when I use a build tool I see a ghost preview on the map, coloured green when
  valid and red when invalid, with the reason and the total cost shown before confirming.
- P1: As a player, I can raise, lower and level land in an area, paying per tile changed, within
  the rules that keep terrain buildable.
- P1: As a player, I can demolish objects I own and clear scenery (trees), paying a removal cost.
- P2: As a player, I can drag to build along a path or over an area, and press a key to cancel.
- P3: As a player, I can undo my last construction action within a short time window if nothing
  else changed it (optional, confirm during clarify).

Success criteria:
- Previews update every frame while dragging over 500 tiles without dropping below 60 FPS.
- Construction is impossible when money is insufficient, with a clear message.

Out of scope: specific objects (roads in 007, rail in 015, stations in 010).
```

#### Clarify guidance

- Terraforming cannot affect tiles under objects owned by others or under towns' buildings without
  demolishing them first (and towns dislike it, see 008).
- Undo: propose *not* in version 1 to keep commands simple; confirm.

#### Plan prompt

```text
Design construction as simulation commands in Talt.Core (validate, compute cost, apply) so they are
deterministic, testable and saveable. A tool controller in the client produces previews by calling
validation without applying. Terraform operations edit the tile height data and mark affected
render chunks dirty (from feature 003). Define an ownership model for tile objects (company,
town, none). Tests for validation rules, cost computation and terrain constraints; benchmark
validation of a 500-tile drag.
```

**Done when:** terraform, demolish and generic previews work with costs; commands covered by tests.

---

### 007 — Roads

**Goal:** players build road networks that vehicles use; the road graph updates incrementally.

#### Specify prompt

```text
FEATURE: Roads

Roads are the first infrastructure players build: they connect towns and industries and are used by
buses and trucks.

User stories:
- P1: As a player, I build roads by dragging between two points; the game proposes a path, shows
  cost, and builds straight segments, curves and junctions automatically.
- P1: As a player, roads follow gentle slopes; steep terrain requires terraforming or is rejected
  with a reason.
- P1: As a player, I build bridges over water, valleys and other roads, and tunnels through hills.
- P2: As a player, I remove road pieces I own; town roads can only be removed under conditions.
- P2: As a player, I can choose between road types that become available over time (affecting
  speed limit and cost).
- P3: As a player, I can make a road one-way.

Success criteria:
- Building or removing a road updates the network used by vehicles within the same tick.
- A route search across a fully connected 4096 x 4096 road network finds a path in under 2 ms on
  average.

Out of scope: road vehicles (012), stations (010).
```

#### Clarify guidance

- Road geometry: grid-aligned segments plus diagonal (45°) pieces, or grid only? Propose
  grid-aligned with smooth visual corners for version 1.
- Bridges: maximum length grows with era; tunnels straight only.

#### Plan prompt

```text
Store roads at tile level (per-tile road pieces with connection masks, type, owner, one-way flags)
and derive the routing graph (Talt.Core.Network.TransportNetwork) from it. Replace the immutable
network with one that supports incremental updates (add/remove nodes and edges, with dirty regions)
while keeping flat-array storage for traversal speed; consider rebuilding CSR per region. Implement
hierarchical pathfinding for long routes (for example a coarse graph between regions or junction
contraction) with cached routes invalidated by network changes; keep A* for local searches.
Render roads as meshes on top of terrain (not by recolouring voxels as the prototype does), with
slope, bridge and tunnel pieces. Tests: connectivity, incremental updates equal a full rebuild,
pathfinding correctness. Benchmarks: long-route queries, incremental update cost.
```

**Done when:** roads, bridges and tunnels build and render; graph updates incremental; pathfinding
within budget.

---

### 008 — Towns

**Goal:** living towns that grow, generate passengers and mail, and react to the player.

#### Specify prompt

```text
FEATURE: Towns

Towns are the source and destination of passengers and mail and consume goods. They should feel
alive: they grow, build roads and buildings, and react to the service the player provides.

User stories:
- P1: As a player, I see towns with names, populations and voxel buildings of different sizes and
  types (houses, apartments, shops, offices) placed along their roads.
- P1: As a player, towns grow over time by adding buildings and roads; growth speeds up with good
  transport service and stalls without it.
- P2: As a player, I can open a town window with population, buildings, passengers and mail
  generated and transported last month, and the town's rating of my company.
- P2: As a player, town ratings fall when I demolish town property or trees and rise with good
  service; low ratings block some actions (for example building stations in town).
- P3: As a player, larger towns get bigger buildings and a denser centre.

Success criteria:
- 500 towns update their monthly growth in under 50 ms total.
- Town names are generated from original name lists (English and Spanish style).

Out of scope: destination flows (013), station catchment details (010).
```

#### Clarify guidance

- Town growth is monthly with randomness from the seeded generator.
- Buildings are data-driven definitions (size, capacity, era, voxel model).

#### Plan prompt

```text
Town simulation in Talt.Core: town data (name, centre, population, buildings list, road network
ownership, ratings per company, monthly statistics). Building definitions loaded from data files
(footprint, population, passenger/mail production rates, goods acceptance, era range, model id).
Growth algorithm: choose growth points along town roads, extend roads following simple patterns
(grid or organic, a per-town setting), place buildings respecting slopes and existing objects.
All updates through the scheduled monthly/daily events from feature 001, parallel across towns
where data is independent. Rendering: instanced voxel building models (start with simple
procedural voxel boxes with roofs and windows; final art in 018). Town name generator with original
syllable lists. Tests: growth determinism, ratings, building placement validity. Benchmark: monthly
update for 500 towns.
```

**Done when:** towns grow visibly over years; town window shows statistics; budget met.

---

### 009 — Cargo types and industries

**Goal:** industries that produce raw materials and processed goods and accept cargo, all
data-driven.

#### Specify prompt

```text
FEATURE: Cargo types and industries

Industries give the map its freight economy. The player does not manage production; industries
produce and accept cargo on their own, and the player profits by transporting it.

User stories:
- P1: As a player, I see industries on the map (for example coal mine, forest, farm, oil well,
  power station, sawmill, steel mill, refinery, factory) with distinctive voxel models.
- P1: As a player, I can open an industry window showing what it produces per month, what it
  accepts, and how much was transported last month.
- P1: As a player, raw materials and most processed goods can be delivered to any industry or town
  that accepts them; I do not choose a specific destination for them.
- P2: As a player, processing industries turn accepted cargo into other cargo automatically, so
  delivering raw materials makes nearby processed goods available to transport.
- P2: As a player, industry production changes over time; well-served industries tend to grow,
  neglected ones may shrink or close, and new industries occasionally open.
- P3: As a player, cargo types have properties (category, how quickly their value drops with travel
  time, which vehicles can carry them).

Success criteria:
- 1 000 industries update daily in under 1 ms total.
- All cargo and industry definitions come from data files validated at startup.

Out of scope: passenger/mail/goods destinations (013).
```

#### Clarify guidance

- Version 1 cargo list (proposal): passengers, mail, coal, wood, iron ore, grain, livestock, oil,
  steel, lumber, fuel, food, goods. Passengers, mail and goods/food for towns use destinations.
- Processing is automatic and immediate on delivery (the player never manages it).

#### Plan prompt

```text
Data-driven definitions (JSON or similar, validated with clear errors) for cargo types (id, name
key, category, unit, value curve over travel time, destination-based or not, colour/icon) and
industry types (footprint, model, production table, acceptance, processing recipes, placement
rules, era range, probability). Industry instances in structure-of-arrays storage; daily production
accumulation and monthly production changes using the seeded RNG. Placement uses sites from
feature 004. Rendering via instanced voxel models (simple procedural placeholders until 018).
Tests: data validation, production and processing math, determinism. Benchmark: daily update for
1 000 industries.
```

**Done when:** industries appear, produce and process; windows show statistics; data files validated.

---

### 010 — Stations and cargo handling

**Goal:** stations collect cargo from their surroundings and hand it to vehicles.

#### Specify prompt

```text
FEATURE: Stations and cargo handling

Stations connect the player's network to towns and industries. Cargo appears at stations from
nearby producers, waits, and is loaded onto vehicles; delivered cargo is accepted by nearby
consumers.

User stories:
- P1: As a player, I build bus stops and truck loading bays next to roads; each station shows its
  catchment area when placing it, and which cargo it will receive and accept.
- P1: As a player, cargo produced within the catchment area appears at the station only after a
  vehicle has started serving that cargo there, and waits until loaded.
- P1: As a player, vehicles unload cargo that the station accepts and get paid (feature 011); cargo
  not accepted stays on board or is transferred if ordered.
- P2: As a player, each station has a rating per cargo that depends on waiting time, vehicle speed,
  frequency and amount waiting; a higher rating means more cargo is delivered to the station.
- P2: As a player, I can expand stations by building adjacent pieces that join the same station.
- P3: As a player, I can open a station window with waiting cargo (by destination when applicable),
  ratings and recent activity.

Success criteria:
- 5 000 stations update ratings and cargo daily in under 2 ms total.
- Catchment previews update in real time while moving the placement cursor.

Out of scope: rail stations (015), destination routing logic (013).
```

#### Clarify guidance

- Catchment radius per station type (proposal: bus stop 3 tiles, truck bay 3, rail station 4).
- Waiting cargo is capped; excess is lost and lowers the rating.

#### Plan prompt

```text
Station data in Talt.Core: station pieces on tiles, joined into stations; catchment computed from
piece footprints; per-station per-cargo waiting amounts (aggregated packets with origin and, for
destination cargo, destination), ratings and statistics. Producers (towns, industries) distribute
production to stations in catchment proportionally to rating. Loading/unloading operations used by
vehicles (feature 012). Keep storage flat and cache-friendly; avoid per-unit cargo objects. Tests:
catchment, distribution, ratings formula, loading/unloading conservation of cargo. Benchmark:
daily station update for 5 000 stations.
```

**Done when:** stations collect, store and accept cargo correctly; windows show ratings; budget met.

**Built (2026-11-24):** `specs/010-stations-cargo/`. The developer added warehouses in three sizes that group
nearby stations into one virtual station, both stop forms (bay and drive-through), place-suffix names for
warehouses and renaming (see the clarifications in the spec).

---

### 011 — Economy and finance

**Goal:** money, income, costs and loans that make decisions matter.

#### Specify prompt

```text
FEATURE: Economy and finance

The player's company earns money by delivering cargo and spends it on construction, vehicles and
running costs. Finances must be clear and the economy balanced so good networks are profitable and
bad ones are not.

User stories:
- P1: As a player, I start with an initial amount of money and a loan facility; I can borrow and
  repay in steps up to a maximum loan, paying interest monthly.
- P1: As a player, each delivery pays according to cargo type, amount, distance between pickup and
  drop-off, and travel time; I see the income appear on the map at the vehicle.
- P1: As a player, vehicles and infrastructure have running and maintenance costs charged over time.
- P2: As a player, I open a finance window with income and expenses by category for the current and
  previous years, cash, loan and company value.
- P2: As a player, if I stay bankrupt (negative money with maximum loan) for a grace period, the
  game ends (except in sandbox mode).
- P3: As a player, optional inflation increases prices and payments over the years.

Success criteria:
- All money calculations are exact and deterministic (no floating-point drift across platforms).
- Balancing values live in data files.

Out of scope: share trading, competitors.
```

#### Clarify guidance

- Currency is fictional and localizable in display (symbol and formatting).
- Grace period: proposal 3 consecutive months bankrupt → warning, 6 → game over.

#### Plan prompt

```text
Represent money as 64-bit integers in the smallest unit (no floating point in money). Payment
formula from data (base rate per cargo, distance factor, time decay curve per cargo). Ledger with
categories (construction, vehicle purchase, running costs, maintenance, income per cargo, interest,
other) and periods (month, year). Loans and interest via monthly events. Company value computed
monthly. Income/expense floating texts in the client. Finance window built with the UI framework.
Tests: payment formula, ledger sums, loan rules, bankruptcy rules, cross-platform determinism.
```

**Done when:** income and costs flow correctly; finance window accurate; bankruptcy rules tested.

---

### 012 — Road vehicles, depots and lines

**Goal:** buy buses and trucks, assign them to lines with stops, and have them interact realistically
on roads.

#### Specify prompt

```text
FEATURE: Road vehicles, depots and lines

Vehicles are how the player earns money. Buses and trucks are bought at depots, given a line (an
ordered list of stops) and drive along the road network, interacting with other traffic.

User stories:
- P1: As a player, I build a road depot, buy a bus or truck model available in the current year,
  and see it leave the depot.
- P1: As a player, I create a line by choosing stops in order, assign vehicles to it, and they
  travel stop to stop loading and unloading cargo.
- P1: As a player, vehicles keep a safe distance from the vehicle ahead, queue at busy stops, wait
  at junctions when needed and never drive through each other.
- P2: As a player, I set loading rules per stop (load if available, full load, unload only, no
  loading) and the vehicle follows them.
- P2: As a player, vehicles age, become less reliable, occasionally break down, and visit a depot
  for servicing at set intervals.
- P2: As a player, I can sell vehicles at a depot, clone them, and send them to the depot.
- P3: As a player, vehicles find a new route automatically when a road on their route is removed.

Success criteria:
- 50 000 road vehicles with interactions simulate within the tick budget (4 ms average).
- No vehicle ever overlaps another in the same lane (verified by tests and a debug check).
- Gridlock situations resolve or are reported; no permanent deadlock at simple junctions.

Out of scope: rail (015), vehicle catalog and eras beyond a starter set (016).
```

#### Clarify guidance

- Two lanes, right-hand traffic; no overtaking in version 1.
- Junction rule: first come, first served with a junction reservation; roundabouts not required.
- Starter set: 2 buses and 3 trucks (by cargo category), more in 016.

#### Plan prompt

```text
Evolve VehicleFleet into a full road vehicle system: lines with ordered stops and per-stop orders,
vehicle states (driving, entering stop, loading, unloading, leaving, waiting, broken down, going to
depot), cargo holds per vehicle, aging and reliability.

Interactions while staying parallel: partition vehicles by lane segment; per tick, (1) build a
per-segment ordering of vehicles (sorted by distance), (2) compute each vehicle's allowed speed from
the vehicle ahead and from junction/stop reservations (read-only phase, parallel), (3) apply
movement and reservations (write phase, parallel per segment, deterministic conflict resolution by
vehicle id for shared junctions). Document the algorithm in docs/ARCHITECTURE.md and prove
sequential/parallel equivalence with tests.

Use the cached hierarchical pathfinding from feature 007 for routes between stops; recompute only
on network changes, spread across ticks. Render vehicles with instancing using per-model voxel
meshes (placeholders until 018), oriented along their travel direction, interpolated between ticks.
Tests: following distance, queues, junction reservations, orders, breakdowns, determinism.
Benchmarks: 10k / 50k / 100k vehicles with interactions.
```

**Checklists:** performance, determinism, gameplay rules.
**Done when:** vehicles bought, routed, interacting without overlaps; tick budget met at 50k.

---

### 013 — Destination demand model

**Goal:** passengers, mail and goods with real destinations, routed over the player's network, at
scale.

#### Specify prompt

```text
FEATURE: Destination demand model

A core pillar of the game: passengers, mail and consumer goods want to go to specific places. The
player's challenge is to build networks that serve real demand. This must scale to very large maps
without slowing down.

User stories:
- P1: As a player, towns generate passengers and mail with destinations in other towns; demand
  between two towns depends on their sizes and distance.
- P1: As a player, waiting passengers at a station are grouped by destination, and vehicles only
  pick up cargo that their line can bring closer to its destination, including transfers between
  my lines at shared stations.
- P1: As a player, goods and food produced by industries are wanted by specific towns, not any town.
- P2: As a player, I can see, for a station or a town, where its passengers and goods want to go
  and how much of that demand my network currently serves.
- P2: As a player, when I add or change lines, demand gradually redistributes to use the new
  connections.
- P3: As a player, unserved demand is visible as a map overlay to suggest new connections.

Success criteria:
- On the reference scenario, demand and routing updates fit within the tick budget, amortised over
  time (no single tick above 8 ms).
- Cargo is conserved: everything generated is either delivered, waiting, on board or explicitly
  lost (expired), verified by tests.

Out of scope: individual passenger simulation (explicitly excluded by the vision).
```

#### Clarify guidance

- Demand model: gravity-style (size × size / distance^k), symmetric for passengers, asymmetric for
  mail and goods; parameters in data files.
- Routing granularity: station-to-station flows over a link graph built from lines (not from the
  road graph).
- Update cadence: link graph recomputed incrementally, in time-sliced jobs (for example a fraction
  of the graph per tick).

#### Plan prompt

```text
Implement in Talt.Core: (1) a demand generator producing origin-destination demand between towns
(and industry-to-town for goods/food) with a gravity model; (2) a link graph whose nodes are stations
and whose edges are line connections with capacity and travel time, updated when lines change;
(3) a flow assignment that splits demand over paths in the link graph (start with a capacity-aware
shortest-path assignment, iterated over time), computed in background jobs on worker threads using
immutable snapshots and applied deterministically at a fixed tick; (4) cargo packets at stations
keyed by next hop and final destination, consumed by vehicle loading rules. Ensure determinism by
scheduling job results to apply at a known tick regardless of thread timing. Tests: conservation,
assignment correctness on small graphs, determinism with background jobs. Benchmarks: link graph
update and assignment for 5 000 stations and 50 000 vehicles. Record numbers in ARCHITECTURE.md.
```

**Checklists:** performance, determinism, data conservation.
**Done when:** destination cargo routes over transfers; overlays show demand; budgets met.

---

### 014 — Save and load

**Goal:** reliable, fast, versioned saves; completes the MVP.

#### Specify prompt

```text
FEATURE: Save and load

Players must be able to save and continue games safely, including very large ones, and saves must
keep working after the game is updated.

User stories:
- P1: As a player, I save the game with a name and later load it to continue exactly where I was.
- P1: As a player, the game autosaves at a configurable interval, keeping the last N autosaves.
- P2: As a player, the load screen shows each save's date, company name, money, map size and a
  thumbnail.
- P2: As a player, saves from older game versions still load (with upgrades applied).
- P3: As a player, a corrupted save is detected and reported clearly; it never crashes the game.

Success criteria:
- Save under 3 s and load under 5 s for the reference scenario; autosave does not freeze the game
  for more than 100 ms.
- A loaded game continues identically to the original (same state hash after N further ticks).

Out of scope: cloud saves.
```

#### Clarify guidance

- Saves stored in the user profile folder; file extension `.talt`.
- Autosave every in-game year by default (or every N real minutes; choose one).

#### Plan prompt

```text
Binary save format with a header (magic, format version, game version, metadata, thumbnail),
chunked sections per subsystem, each versioned, compressed (built-in .NET compression such as
Brotli or ZLib; justify), with checksums. Serialization written explicitly per subsystem (no
reflection-based serializers on hot data) using spans and pooled buffers. Snapshot the simulation
state at a tick boundary and write on a background thread to keep autosave non-blocking. Upgrade
path: per-section migration functions. Tests: round trip equality (state hash), version migration
with fixture saves, corruption detection. Benchmarks: save/load for the reference scenario.
```

**Done when:** save/load/autosave work within budget; round-trip and migration tests pass.
**MVP complete** after this feature: tag a release candidate and play-test.

---

### 015 — Railways

**Goal:** trains, tracks and signals, the deepest transport mode.

#### Specify prompt

```text
FEATURE: Railways

Rail is the backbone of large networks: high capacity, high speed and the most interesting
building challenges (junctions, signals, stations).

User stories:
- P1: As a player, I build railway tracks by dragging, including curves, slopes, bridges and
  tunnels, and junctions with switches.
- P1: As a player, I build rail stations of configurable length and platforms, and rail depots.
- P1: As a player, I buy locomotives and wagons, compose trains, assign them to lines and they run
  between stations, loading and unloading.
- P1: As a player, I place signals that divide track into blocks; trains never enter an occupied
  block, so they do not collide.
- P2: As a player, path signals let several trains use complex junctions and stations safely,
  reserving a path through them.
- P2: As a player, trains choose free platforms automatically at stations.
- P3: As a player, I can build electrified track and electric locomotives that require it.

Success criteria:
- 5 000 trains with signalling simulate within the tick budget.
- No collisions and no deadlock in standard layouts (tested with scenario fixtures: single line
  with passing loops, double track, terminus station, junctions).

Out of scope: track types beyond normal and electrified.
```

#### Clarify guidance

- Track geometry: grid-aligned and 45° diagonals (diagonals matter a lot for rail layouts); confirm.
- Signal types for version 1: block signal, path signal, one-way path signal.

#### Plan prompt

```text
Rail as a second network type reusing the construction framework (006), the tile-level storage
pattern and the incremental graph from roads (007). Trains as consists (head + wagons) with length,
mass, power and tractive effort for acceleration on slopes. Block occupancy and path reservations
stored per track segment; reservation phase deterministic (ordered by train id) to stay
parallel-safe, following the same read/write phase pattern as road vehicles (012). Train
pathfinding over the rail graph with signal-aware cost and platform selection. Rendering of tracks,
switches, signals and train models (placeholders until 018). Tests: block safety, reservations,
deadlock scenarios, platform choice. Benchmarks: 1k / 5k trains.
```

**Done when:** trains run safely on signalled networks; fixtures pass; budget met.

---

### 016 — Vehicle catalog and eras

#### Specify prompt

```text
FEATURE: Vehicle catalog and eras

Transport technology changes over decades. New, faster, more reliable vehicles appear; old ones are
retired. This gives long games a sense of progress.

User stories:
- P1: As a player, available vehicle models change with the in-game year; new models are announced
  and old models stop being sold.
- P1: As a player, each model shows speed, capacity, cargo types, cost, running cost, reliability
  and lifespan before buying.
- P2: As a player, I can enable automatic renewal of old vehicles and automatic replacement of a
  model by a chosen newer one.
- P2: As a player, infrastructure options evolve too (road types, bridge lengths, electrification).
- P3: As a player, I can preview a vehicle's voxel model in the purchase window.

Success criteria:
- The catalog covers 1950 to 2050 with at least 8 road and 12 rail models in version 1, all original
  designs and names.
- All models defined in data files.
```

#### Plan prompt

```text
Data-driven vehicle definitions (stats, era range, model id, sounds id) and infrastructure
availability by year; preview-model introduction events; autoreplace/autorenew executed at depots
through simulation commands. Model viewer widget in the UI. Tests: availability by date, autoreplace
rules. No performance-sensitive code expected; keep definitions in flat lookup tables.
```

---

### 017 — Line management and statistics

#### Specify prompt

```text
FEATURE: Line management and statistics

With hundreds of vehicles, players need tools to understand and tune their network.

User stories:
- P1: As a player, I see a list of all lines with vehicles, profit this year and last year,
  passengers/cargo carried and load factor, sortable and filterable.
- P1: As a player, I see a list of all vehicles with status (running, loading, broken down,
  unprofitable, too old) and can jump to any of them.
- P2: As a player, I can set a target frequency (interval between departures) for a line and
  vehicles space themselves out.
- P2: As a player, I see company graphs over time: income, expenses, cargo delivered by type,
  company value.
- P3: As a player, map overlays show line routes, station catchments and traffic density.

Success criteria:
- Lists with 50 000 vehicles open and scroll smoothly (virtualized).
```

#### Plan prompt

```text
Statistics collected in the simulation at monthly/yearly events into compact ring buffers. UI
lists virtualized; graphs drawn with the UI layer. Interval keeping: vehicles adjust dwell time at
the first stop to approach the target headway. Overlays rendered as separate passes. Tests:
statistics aggregation, headway control. Benchmark: statistics collection cost.
```

---

### 018 — Visual quality

**Goal:** close the gap with modern voxel games ("prettier"): shadows, finer detail, scenery, models.

#### Specify prompt

```text
FEATURE: Visual quality

The game should look charming and polished. Testing showed the prototype looks good for a first
version but less appealing than modern voxel transport games, mostly due to lighting and detail.

User stories:
- P1: As a player, the world has soft directional shadows from terrain, buildings, trees and
  vehicles, adjusting with zoom.
- P1: As a player, terrain, buildings, vehicles and trees have finer voxel detail, so objects are
  recognisable up close while staying clean from afar.
- P1: As a player, I see trees, bushes and rocks on the landscape, and water that looks like water
  (subtle animation, shoreline).
- P2: As a player, final original voxel models replace placeholders for buildings, industries,
  stations, depots and vehicles.
- P2: As a player, I can choose graphics quality presets (low, medium, high) to suit my computer.
- P3: As a player, I can enable an optional day/night cycle with lit windows at night.

Success criteria:
- The "high" preset keeps 60 FPS on the reference hardware on a 2048 x 2048 map; "medium" keeps
  60 FPS on 4096 x 4096.
- Side-by-side screenshots before/after approved by the developer.
```

#### Clarify guidance

- Detail scale: propose sub-tile voxels for objects (for example 8 × 8 voxels per tile for models)
  while terrain stays coarser; confirm.
- Art pipeline: models authored in a free voxel editor (for example MagicaVoxel `.vox` format,
  original art only) and loaded at runtime.

#### Plan prompt

```text
Rendering upgrades in Talt.Client: cascaded shadow maps for the directional light; tone mapping
and gamma-correct pipeline; optional SSAO or improved baked AO; MSAA or FXAA; animated water shader
with shoreline foam; instanced scenery with LOD and density by zoom. A voxel model pipeline: load
.vox files (write the parser from the format specification), mesh them (greedy, with AO) into
model meshes at load time, cache them. Quality presets controlling shadow resolution, cascades,
scenery density, LOD distances and AA. Keep all features toggleable and measured; record frame
cost of each effect in ARCHITECTURE.md.
```

---

### 019 — Audio

#### Specify prompt

```text
FEATURE: Audio

Sound brings the world to life: ambient sounds, vehicles, construction and interface feedback,
plus original music.

User stories:
- P1: As a player, I hear interface sounds (clicks, notifications) and construction sounds.
- P1: As a player, I hear vehicles and ambient sounds (towns, countryside, water) depending on what
  the camera shows, quieter when zoomed out.
- P2: As a player, original background music plays as a playlist with volume control.
- P2: As a player, I control master, music, effects and ambient volumes separately.

Success criteria:
- Audio never causes stutter; CPU cost under 0.5 ms per frame.
- All sounds and music are original or properly licensed for commercial use.
```

#### Plan prompt

```text
Research a cross-platform audio library compatible with .NET 10 on Windows, Linux and macOS and
with commercial distribution (for example OpenAL Soft through Silk.NET.OpenAL, or miniaudio
bindings); justify license and maintenance. Mixer with buses, positional sounds limited by a voice
budget chosen by distance and importance, streamed music (OGG). Audio lives in Talt.Client; the
simulation emits events (for example vehicle arrived) consumed by the audio layer.
```

---

### 020 — Game modes, goals and settings

#### Specify prompt

```text
FEATURE: Game modes, goals and settings

Players need reasons to play and options to tune difficulty.

User stories:
- P1: As a player, I start a game in Career mode (finances matter, bankruptcy possible) or Sandbox
  mode (unlimited money, no bankruptcy).
- P1: As a player, I choose difficulty settings: starting money, maximum loan, interest, running
  costs, construction costs, breakdowns on/off, inflation on/off, town growth speed, industry
  changes.
- P2: As a player, I can play scenarios with goals (for example "deliver 10 000 tonnes of coal by
  1970" or "connect all towns above 2 000 inhabitants") and see progress.
- P2: As a player, I receive news messages for important events (new vehicle, industry opening or
  closing, first vehicle arriving, records) and can configure which ones I see.
- P3: As a player, I earn a company rating at the end of a period based on performance.
```

#### Plan prompt

```text
Game settings record applied at new game and stored in saves. Goal system with data-defined goal
types evaluated at monthly events. News system as simulation events with priorities, consumed by the
UI. Tests: settings effects, goal evaluation, news generation.
```

---

### 021 — Ships and aircraft (version 2)

#### Specify prompt

```text
FEATURE: Ships and aircraft

Water and air transport extend the game: ships for bulk cargo across seas and rivers, aircraft for
fast long-distance passengers and mail.

User stories:
- P1: As a player, I build docks on coasts and lakes and buy ships that sail between docks,
  finding routes over water (including canals I build).
- P1: As a player, I build airports of different sizes and buy aircraft that fly between them,
  limited by runway size, with queueing for take-off and landing.
- P2: As a player, ships and aircraft follow the same lines, orders, cargo and economy rules as
  other vehicles.

Success criteria:
- Water pathfinding over a 4096 x 4096 map completes within the same budgets as roads.
- Airport operations never let two aircraft use the runway at the same time.
```

#### Plan prompt

```text
Reuse lines, orders, cargo holds and economy from 012/013. Water routing on a coarse navigation
grid with hierarchical search and caching; canals and locks as construction objects. Airports as
station types with an internal state machine (approach, landing, taxi, gate, take-off) and runway
reservations. Aircraft fly above terrain in straight lines with altitude profiles. Benchmarks for
water pathfinding and airport throughput.
```

---

### 022 — Packaging and release

#### Specify prompt

```text
FEATURE: Packaging and release

The game must be easy to install and run on Windows, Linux and macOS, and problems in the field must
be diagnosable.

User stories:
- P1: As a player, I download a single package for my platform and run the game without installing
  .NET or other dependencies.
- P1: As a player on macOS, the app is signed and notarized so the system allows it to run.
- P2: As a player, if the game crashes, a crash report with logs is saved and I am told where to
  find it.
- P2: As the developer, I produce release builds with version numbers from a tag in one step.
- P3: As a player, the game checks its data files' integrity at startup.

Success criteria:
- Cold start to main menu under 5 seconds on the reference hardware.
- Release package size reasonable (target under 500 MB with all assets).
```

#### Plan prompt

```text
Self-contained publish per runtime (consider ReadyToRun or NativeAOT for startup time; justify
given reflection and native interop constraints). Windows installer or zip, Linux AppImage or
tarball, macOS .app bundle with signing and notarization in CI using secrets. Structured logging to
rolling files; unhandled exception handler writing crash reports. Version stamping from git tags.
Store requirements (for example Steam or itch.io) to be evaluated in research.md.
```

---

## 9. Cross-cutting checklists

Suggested topics for `/speckit-checklist`:

- **Performance:** budgets stated and measured; hot paths allocation-free; benchmarks added; numbers
  in ARCHITECTURE.md.
- **Determinism:** no wall clock or unseeded randomness in Talt.Core; commands for player actions;
  parallel equals sequential; state hash tests.
- **Core/Client separation:** no graphics, window or audio references in Talt.Core.
- **Data-driven content:** definitions in data files with validation and clear errors.
- **Localization:** no hard-coded player-facing text; English and Spanish strings present.
- **Originality:** no copied assets, names or code; new art and audio are original or properly
  licensed.
- **Documentation:** ARCHITECTURE.md and, when a product decision changed, VISION.md updated.

## 10. Glossary

| Term | Meaning |
|---|---|
| Tile | One square of the map grid; the unit of construction and distance. |
| Voxel | A small cube; the world and models are made of them. |
| Chunk | A 32 × 32 × 32 block of voxels, the unit of storage, meshing and rendering. |
| Node / edge | Junction and one-way segment of a transport network graph. |
| Line | An ordered list of stops that vehicles repeat. |
| Station / stop | Where cargo is collected, waits and is loaded or unloaded. |
| Catchment area | Tiles around a station whose producers and consumers it serves. |
| Acceptance | Whether a place (industry, town building) takes a cargo type. |
| Destination cargo | Passengers, mail and consumer goods: they want a specific destination. |
| Flow | Aggregated amount of cargo per period between two places or stations. |
| Link graph | Graph of stations connected by the player's lines, used to route destination cargo. |
| Tick | One fixed simulation step. |
| Reference scenario | The large test game used for performance budgets (section 5). |
