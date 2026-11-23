# Feature Specification: Map Generation and New Game

**Feature Branch**: `004-map-generation`

**Created**: 2026-11-22

**Status**: Draft

**Input**: User description: "Map generation and new game: players start a game by generating a new
map. Maps must look natural, offer interesting transport challenges (hills, rivers, lakes, coasts)
and be reproducible from a seed so players can share them. P1 choose map size (256 x 256 up to at
least 4096 x 4096, non-square allowed), seed, terrain roughness, maximum height, amount of water and
sea level, and generate with a progress indicator; P1 same settings and seed always give the same
map; P2 seas or lakes, rivers flowing downhill to water, beaches, hills, mountains with snow,
scattered trees/forests; P2 reserved locations for towns and industries (features 008 and 009),
spaced sensibly on buildable land; P3 small overview preview before starting. 4096 x 4096 generates
in under 20 s; rivers always connect to a lake or the sea, no floating water; at least 95% of town
sites on flat, connected land. Out of scope: heightmap import, multiple climates (temperate only in
version 1)."

## Context

Today the game builds its world with a prototype terrain generator (simple value noise plus a
procedural road grid) that cannot be tuned, has no rivers, lakes, beaches, snow or trees, and
reserves nothing for towns and industries. Varied, believable, shareable maps are the stage on which
every later feature (towns, industries, construction, finances) plays out, so map generation is the
foundation of replayability.

## Clarifications

### Session 2026-11-22

- Q: How much maximum terrain height must the generator allow? → A: Up to 127 voxels (four stacked
  vertical chunks of 32); the default maximum height stays low (28) and the setting may go up to 127.
  The world must therefore support several vertical chunks.
- Q: Does TALT enforce a maximum steepness between neighbouring tiles? → A: Yes. Buildable terrain
  changes by at most 1 voxel per tile step ("one step per tile"); steeper ground exists only as
  deliberate cliffs/rock faces in mountains, which are marked not buildable.
- Q: How are a map's settings shared so another player reproduces it? → A: As one short text that
  encodes seed, size and all settings (copy/paste, accepted by the command line); the generator
  version is part of it so a map is never silently regenerated differently.

## Scope and deferrals

Feature 005 (user interface foundation) is built in parallel and will own the in-window UI layer.
To avoid conflicts, this feature delivers the **generator and its data**, and exposes new-game
settings through **command-line options** of the game client plus **console progress output**.

- **In scope now**: the generator, its settings, the compact per-tile map data, rivers, lakes, coasts,
  snow, trees as scenery data, site reservation for towns and industries, progress reporting,
  command-line new-game options, wiring the generator into the game in place of the prototype one,
  tests and benchmarks.
- **Deferred until after feature 005**: the in-window new-game screen (settings form, progress bar)
  and the overview preview (User Story 5, P3). They are specified here so they are not lost, but they
  are built on top of the UI framework once it exists. The settings and progress interfaces built now
  are what that screen will consume.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Generate a map from chosen settings (Priority: P1)

A player starts a new game by choosing the map size (width and height independently, from 256 up to at
least 4096 tiles per side), a seed, terrain roughness, maximum height, amount of water and sea level,
and then generates the map while seeing progress. In this version the choices are given as
command-line options when launching the game (and the same settings are what the future new-game
screen will present); the progress is shown as a percentage with the name of the current stage on the
console.

**Why this priority**: Without a way to create a map from settings there is no new game.

**Independent Test**: Launch the game (or the headless tool) with different sizes and settings; each
produces a map of exactly that size whose look responds to the settings, with progress reported from
0 to 100%.

**Acceptance Scenarios**:

1. **Given** no settings are given, **When** the player starts a new game, **Then** a map of the
   default size is generated with default settings and a random seed that is displayed so it can be
   shared.
2. **Given** a non-square size such as 1024 x 512, **When** the map is generated, **Then** its extent
   is exactly 1024 x 512 tiles.
3. **Given** a higher roughness (or maximum height) than another run with the same seed, **When** the
   map is generated, **Then** the terrain is measurably more rugged (higher relief variation).
4. **Given** a higher water amount or sea level, **When** the map is generated, **Then** a larger
   share of the map is under water.
5. **Given** a setting outside its supported range or an unsupported size, **When** the player
   starts, **Then** it is rejected with a clear message naming the setting and the allowed range
   (nothing is silently clamped).
6. **Given** generation is running, **When** it progresses, **Then** progress is reported
   monotonically from 0 to 100% together with the current stage; **And** generation can be cancelled
   without leaving a half-built map in use.

---

### User Story 2 - Same settings and seed give the same map (Priority: P1)

A player shares a seed and settings with a friend, and the friend gets exactly the same map on any
supported platform.

**Why this priority**: Shareable, reproducible maps are an explicit goal, and the simulation is
deterministic (constitution principle III); the world it runs on must be too.

**Independent Test**: Generate the same settings twice, and on every CI platform, and compare a
fingerprint of all tile data; they are identical. Changing only the seed changes the map.

**Acceptance Scenarios**:

1. **Given** identical settings and seed, **When** generated twice (also on different operating
   systems and processor architectures, and with a different number of worker threads), **Then** every
   tile (height, terrain type, water, scenery) and every reserved site is identical.
2. **Given** two different seeds, **When** generated, **Then** the maps differ substantially.
3. **Given** the same seed with a different map size, **When** generated, **Then** the result is
   valid and deterministic for that size (it is not required to be a crop of the other size).

---

### User Story 3 - Natural terrain with water, rivers, coasts and scenery (Priority: P2)

A player sees a believable landscape: seas and lakes, rivers that run downhill to the sea or a lake,
beaches along coasts, rolling hills, mountains capped with snow, and scattered trees and forests,
creating real transport challenges (crossing rivers, climbing hills, going around lakes).

**Why this priority**: This is what makes a map look natural and interesting; the settings of User
Story 1 already work without it, so it follows.

**Independent Test**: Generate maps for many seeds and sizes and run structural checks: every river
reaches a lake or the sea, no water body floats above its surroundings, snow only on high terrain,
beaches only beside water, trees only on suitable land.

**Acceptance Scenarios**:

1. **Given** any generated map, **When** each river is followed from its source, **Then** the height
   never rises along the way and it ends in the sea or a lake; there are no dead-end rivers.
2. **Given** any generated map, **When** water tiles are inspected, **Then** every water surface is
   either at sea level or a lake whose surface is level and enclosed by higher ground; no water is
   floating above lower terrain.
3. **Given** a coast, **When** viewed, **Then** a strip of beach lies between land and sea where the
   slope is gentle.
4. **Given** terrain above the snow line, **When** viewed, **Then** it is snow-covered; the snow line
   follows the maximum height setting.
5. **Given** land that is neither water, beach, steep rock nor snow, **When** viewed, **Then** it is
   covered by a mix of open grass and clustered forests, with trees stored as removable scenery
   (not as terrain), denser in forests than in open land.
6. **Given** the default settings, **When** generated, **Then** most land is gently sloped enough to
   build roads and railways on.

---

### User Story 4 - Reserved sites for towns and industries (Priority: P2)

The generator selects and reserves locations suited to towns and to industries, spread sensibly over
the map and placed on buildable land, so features 008 (towns) and 009 (industries) can found them
there without searching the map again.

**Why this priority**: Later features depend on it, but it is not needed to look at or play on a map.

**Independent Test**: Generate maps for many seeds and sizes; check that site counts scale with map
area, minimum distances hold and at least 95% of town sites are on flat land connected to other
sites.

**Acceptance Scenarios**:

1. **Given** a generated map, **When** town sites are listed, **Then** each lies on flat, buildable
   land, no two are closer than the minimum town spacing, and at least 95% belong to the main
   connected landmass (reachable over land from other sites without crossing water).
2. **Given** a generated map, **When** industry sites are listed, **Then** each lies on buildable
   land, respects its minimum distance from towns and from other industry sites, and the kinds of site
   (e.g., near coast, in forest, on open land, in hills) suit the future industry types.
3. **Given** a larger map, **When** sites are listed, **Then** the number of sites grows roughly in
   proportion to land area; **And** a tiny or mostly-water map yields fewer sites rather than invalid
   ones.

---

### User Story 5 - Preview before starting (Priority: P3) — DEFERRED

A player sees a small overview picture of the generated map before committing to start the game, and
can re-roll the seed or adjust settings.

**Why this priority**: Convenient but not essential.

**Status**: Deferred until after feature 005 (UI foundation). Not built in this feature, except that
the map data must expose enough information (height, terrain type, water) for a small overview image
to be derived cheaply later.

**Independent Test (when built)**: The overview of a generated map shows water, land, mountains and
rivers in recognisable colours, and appears in under one second for any supported size.

**Acceptance Scenarios**:

1. **Given** settings are changed, **When** the preview is regenerated, **Then** it reflects them
   within one second.

---

### Edge Cases

- Extreme settings: minimum or maximum water, flat terrain (roughness 0), maximum roughness, sea
  level above or below all land. The map must stay valid (no crash, no invalid rivers or sites);
  with no land there are simply no sites, and with no high ground there are no rivers or snow.
- Narrow maps (e.g., 256 x 4096) and the smallest size (256 x 256) must work like any other.
- Seed value `0`, negative values and the largest value are accepted as is; invalid input (such as
  non-numeric text) is rejected with a clear message.
- Map edges: rivers may leave the map only into the sea or a lake that touches the edge; terrain
  at the edge is valid land or water, never a hole.
- Cancelling generation midway: no partial map is handed to the game.
- Memory: generating 4096 x 4096 must fit the same memory envelope as the rest of the game; the
  generator must not hold per-tile working data beyond what the budget allows.
- Maximum height above one vertical chunk (32 voxels): the world must still be represented
  correctly across up to four stacked chunks (see FR-006).
- Heights and tile data must stay compatible with existing consumers: road building, the vehicle
  simulation, rendering and the recorded sessions.

## Requirements *(mandatory)*

### Functional Requirements

**Settings and generation**

- **FR-001**: The generator MUST accept a settings set containing: map width and height (each from 256
  to at least 4096 tiles, independent, so non-square maps are allowed), seed, terrain roughness,
  maximum height, water amount and sea level, and MUST validate them with clear errors naming the
  setting and its allowed range.
- **FR-002**: The game MUST generate a new map in place of the prototype generator whenever a new game
  starts, using default settings and a random seed when none are given, and MUST show the seed used.
- **FR-003**: In this version, settings MUST be selectable through command-line options of the game
  client, and generation progress (percentage and stage name) MUST be printed to the console. The
  settings and progress contract MUST be reusable by a future in-window new-game screen.
- **FR-004**: Generation MUST report progress (monotonic, 0 to 100%, with the current stage) and
  support cancellation.
- **FR-005**: Generation MUST be a pure function of settings and seed: the same inputs MUST produce
  identical results on every supported platform, regardless of thread count or scheduling.
- **FR-006**: The maximum height setting MUST allow values from a low minimum up to 127 voxels (default
  28). Heights above one vertical chunk (32 voxels) require the world, rendering and tile data to
  support up to four stacked vertical chunks, and a map of up to 4096 x 4096 x 127 MUST stay within
  the memory and rendering budgets of feature 003.
- **FR-007**: Terrain MUST remain buildable: between neighbouring tiles the ground height differs by
  at most 1 voxel, except on deliberate cliff/rock faces in mountains; those tiles are marked not
  buildable. Rivers and lakes never create steeper steps than this rule on their banks outside cliffs.
- **FR-020**: Map settings MUST be convertible to and from one short text (seed, size, all settings and
  the generator version) that the client accepts on the command line and prints after generation.

**Terrain features**

- **FR-008**: Maps MUST contain seas and/or lakes according to the water amount and sea level; every
  water surface MUST be at sea level or be a level lake enclosed by higher ground (no floating water).
- **FR-009**: Maps MUST contain rivers that start at springs on higher ground, flow strictly
  downhill (never rising) and always end in the sea or a lake; rivers MUST carve their channel into
  the terrain.
- **FR-010**: Maps MUST have beaches along gently sloping coasts, hills and mountains by height, and
  snow-covered terrain above a snow line that scales with the maximum height setting.
- **FR-011**: Maps MUST have trees and forests placed as removable scenery objects, separate from the
  terrain, with forest regions denser than open land and no trees on water, beach, snow or steep
  terrain.
- **FR-012**: Terrain classification (water, beach, grass, rock, snow) and scenery MUST be stored per
  tile in a compact form that gameplay code can query directly, without examining rendered voxels.

**Sites**

- **FR-013**: The generator MUST reserve candidate town sites and industry sites on flat buildable land,
  honoring configurable minimum distances (between towns, between industries, between towns and
  industries), spread roughly evenly, with counts scaling with land area.
- **FR-014**: At least 95% of reserved town sites MUST be on flat land connected over land to the
  other town sites. Each reserved site MUST record its position, kind and an area that is guaranteed
  free of water and steep slopes.
- **FR-015**: The reserved sites MUST be available as map data for features 008 and 009 and MUST be
  included in the deterministic result (FR-005).

**Integration and quality**

- **FR-016**: Generated maps MUST remain compatible with existing features: the vehicle simulation,
  road network painting, rendering of maps of every size and recorded sessions. If the golden
  fingerprints of the simulation scenario change because the scenario's terrain changes, they MUST be
  regenerated deliberately and recorded as such.
- **FR-017**: Generation MUST use multiple processor cores where the work allows, without affecting
  determinism.
- **FR-018**: Any player-facing text added by this feature (console messages, setting names, errors)
  MUST be localizable in English and Spanish.
- **FR-019**: The headless tooling MUST be able to generate a map from the same settings and report
  timing and a content fingerprint, so determinism can be checked in continuous integration.

### Key Entities *(include if feature involves data)*

- **Map settings**: the player's choices — width, height, seed, roughness, maximum height, water
  amount, sea level; validated; shareable as a short text (seed plus settings) so a map can be
  reproduced.
- **Tile data**: per tile — ground height, terrain type (water, beach, grass, rock, snow), water
  surface level, river flag, scenery (tree kind and density); compact, directly queryable.
- **River**: an ordered path of tiles from a spring to the sea or a lake, never rising.
- **Water body**: the sea or a lake, with a surface level.
- **Site**: a reserved location with position, kind (town or industry category), free area and
  suitability information.
- **Generation progress**: stage name and fraction complete.
- **Generated map**: tile data plus sites plus the settings that produced it, with a content
  fingerprint for determinism checks.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A 4096 x 4096 map generates in under 20 seconds on the reference hardware; 256 x 256
  generates in under 1 second.
- **SC-002**: The same settings and seed produce a bit-identical map across all continuous-integration
  platforms (Windows, Linux x64/arm64, macOS arm64) and any worker-thread count.
- **SC-003**: In every tested seed and size, 100% of rivers end in the sea or a lake and never rise
  along their course, and 0 water tiles float above lower terrain.
- **SC-004**: At least 95% of reserved town sites are on flat land connected to the others, in every
  tested seed and size.
- **SC-005**: With default settings, at least 90% of land tiles are gentle enough to build roads on.
- **SC-006**: Changing roughness, maximum height, water amount or sea level in a controlled
  experiment changes relief variation or water share in the expected direction for 100% of tested
  seeds.
- **SC-007**: Maps generated from three different seeds look recognisably different and natural to
  the project owner (reviewed from screenshots on the pull request).
- **SC-008**: Generated maps of every size still render at 60 frames per second (feature 003 budget)
  and generation does not raise peak memory of the game beyond its budget.

## Assumptions

- Temperate climate only in version 1; heightmap import and multiple climates are out of scope.
- Settings are chosen through command-line options in this feature; the in-window new-game screen and
  the overview preview wait for feature 005.
- Trees are scenery data, not terrain voxels; removing them when building belongs to the construction
  features. This feature only generates and stores them (and may show them in the client in the
  simplest way the renderer allows, without new rendering systems).
- Town and industry reservation here only picks locations; founding towns/industries, naming and
  sizing them belong to features 008 and 009.
- The generator itself accepts sides from 64 (smaller maps for tests); the player-facing minimum is 256 and is enforced by the client. Map sizes are whole tiles (multiples of 32) with each side from 256 to 4096 (larger is allowed if memory permits);
  default size is 1024 x 1024.
- The reference hardware and performance methodology are those used for features 002 and 003.
- Slope policy and vertical range are those recorded in Clarifications.
