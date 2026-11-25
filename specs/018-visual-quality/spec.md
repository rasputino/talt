# Feature Specification: Visual Quality

**Feature Branch**: `018-visual-quality`

**Created**: 2026-11-24

**Status**: Draft

**Input**: User description: "Visual quality. The game should look charming and polished. Testing
showed the prototype looks good for a first version but less appealing than modern voxel transport
games, mostly due to lighting and detail. P1: soft directional shadows from terrain, buildings, trees
and vehicles, adjusting with zoom. P1: finer voxel detail on terrain, buildings, vehicles and trees,
recognisable up close and clean from afar. P1: trees, bushes and rocks on the landscape, and water
that looks like water (subtle animation, shoreline). P2: final original voxel models replace
placeholders for buildings, industries, stations, depots and vehicles. P2: graphics quality presets
(low, medium, high). P3: optional day/night cycle with lit windows at night. Success: 'high' keeps
60 FPS on the reference hardware on a 2048 × 2048 map; 'medium' keeps 60 FPS on 4096 × 4096;
side-by-side before/after screenshots approved by the developer."

## Context

Features 003 to 009 built a world that is fast and complete but plain: one directional light without
shadows, flat colours, objects drawn as coloured boxes, water as a still blue surface. Early testers
said the game "looks good for a prototype but less pretty", mainly because of lighting and detail.
This feature is about how the world looks, not how it behaves: nothing in the simulation changes,
and a game replayed before and after this feature reaches exactly the same state.

Feature 010 (stations and depots) is being built at the same time and draws its objects with
placeholders through the existing renderer. This feature must let stations and depots receive models
later by adding model files and data, without further renderer work.

## Clarifications

### Session 2026-11-24

- Q: What detail scale do object models use? → A: 8 model voxels per terrain tile (a 1 × 1 house is
  8 × 8 voxels in plan); terrain stays at one voxel per tile.
- Q: Who makes the first batch of models? → A: The project generates them: a tool in the repository
  writes original MagicaVoxel `.vox` files from code (houses, industries, vehicles, trees, bushes,
  rocks); they can later be edited or replaced in MagicaVoxel without code changes.
- Q: How long is a full day/night cycle? → A: 12 real minutes on a presentation clock that advances
  only while the game is not paused; night is about a quarter of the cycle.
- Q: How is a vehicle's model chosen while vehicles have no type? → A: 3–4 variants (car, bus,
  truck, van) picked stably from the vehicle id, painted with the vehicle's current colour;
  presentation only.

## Scope and deferrals

- **In scope now**: shadows, improved lighting and edge smoothing, animated water with shorelines,
  decorative bushes and rocks, finer detail through voxel models for objects, a model pipeline
  (model files authored in a free voxel editor, loaded when the game starts), first original models
  for vehicles, town buildings, industries and trees, quality presets, a day/night cycle with lit
  windows, and the measured frame cost of each effect.
- **Deferred to feature 010**: the station and depot models themselves (the pipeline must accept
  them without renderer changes).
- **Out of scope**: changes to the simulation, map generation or saved data; seasons and weather;
  per-vehicle-type models beyond what the vehicle data offers today.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Soft shadows (Priority: P1)

As a player, the world casts soft directional shadows: hills shade valleys, buildings and trees
shade the ground and each other, and vehicles have a shadow under them. Shadows stay sharp near the
camera and remain visible but cheaper when zoomed out.

**Why this priority**: shadows are the single biggest step from "flat" to "charming"; they give
depth and make objects sit on the ground.

**Independent Test**: start a game, look at a town and a hill at close, medium and far zoom with
shadows on and off; compare screenshots.

**Acceptance Scenarios**:

1. **Given** a town at close zoom, **When** shadows are on, **Then** each building, tree and vehicle
   casts a shadow on the ground in the direction away from the light.
2. **Given** a hill next to a valley, **When** shadows are on, **Then** the side facing away from
   the light and the ground behind it are shaded.
3. **Given** the camera zooms from close to far, **When** shadows are on, **Then** shadows remain
   present over the visible area without visible flicker, gaps or hard stair-step edges at close zoom.
4. **Given** shadows are switched off in the settings, **When** the frame is drawn, **Then** the
   world looks as before this feature (apart from the other effects) and the shadow cost disappears.

---

### User Story 2 - Finer detail (Priority: P1)

As a player, objects (buildings, industries, vehicles, trees) are built from voxels several times
smaller than a terrain tile, so they are recognisable up close, while from afar they stay clean and
uncluttered.

**Why this priority**: detail is the second half of the testers' feedback.

**Independent Test**: zoom to the closest distance on a town, an industry, a road with vehicles and
a forest; each object type is identifiable by its shape alone; zoom out and check no shimmering or
noise appears.

**Acceptance Scenarios**:

1. **Given** the camera at close zoom, **When** looking at a house, **Then** details such as door,
   windows, roof edge and chimney are visible as small voxels.
2. **Given** the camera at far zoom, **When** looking at the same area, **Then** objects show simpler
   shapes, and edges do not shimmer while the camera moves.
3. **Given** terrain, **When** seen up close, **Then** its surface shows subtle variation and softened
   corners (ambient occlusion) so that slopes and cliffs read clearly.

---

### User Story 3 - Living landscape and water (Priority: P1)

As a player, the countryside has trees, bushes and rocks, and water looks like water: a gentle,
continuous animation and a lighter band where water meets land.

**Why this priority**: the landscape fills most of the screen most of the time.

**Independent Test**: on a new map, look at a river, a lake shore and open countryside; check
animation, shoreline, bushes and rocks.

**Acceptance Scenarios**:

1. **Given** water on screen, **When** time passes (also while paused), **Then** its surface shows a
   subtle moving pattern and highlights from the light.
2. **Given** a shore, **When** looking at it, **Then** a lighter foam band follows the edge between
   water and land.
3. **Given** grassland and rocky ground, **When** looking at it, **Then** bushes and rocks appear
   scattered on free land, with more of them at closer zoom and the density set by the quality
   preset.
4. **Given** the same map is loaded twice, **When** looking at the same place, **Then** bushes and
   rocks are in the same positions.
5. **Given** a player builds a road or a building on a tile, **When** it is built, **Then** no bush or
   rock is drawn on that tile any more.

---

### User Story 4 - Original voxel models (Priority: P2)

As a player, buildings, industries and vehicles are drawn with final original voxel models instead
of coloured boxes; stations and depots can receive models the same way.

**Why this priority**: models make the world recognisable; they depend on the detail scale of
story 2.

**Independent Test**: start a game; every town building type, industry kind and vehicle has a model;
replace a model file with another and restart; the new model appears without code changes.

**Acceptance Scenarios**:

1. **Given** the game starts, **When** model files are loaded, **Then** every building type, industry
   kind and vehicle refers to a valid model, or the game stops with a clear message naming the file
   and the problem.
2. **Given** an object type that has no model assigned (for example a future station), **When** it
   is drawn, **Then** a placeholder is drawn and the game keeps working.
3. **Given** a model author adds a new model file and refers to it from the data, **When** the game
   restarts, **Then** the object is drawn with the new model, with no code change.
4. **Given** a model's palette colours, **When** drawn, **Then** the colours match the model as seen
   in the editor, adjusted only by lighting.

---

### User Story 5 - Quality presets (Priority: P2)

As a player, I choose Low, Medium or High graphics quality in the settings, or adjust individual
effects, to suit my computer. The choice is saved.

**Why this priority**: keeps the game smooth on weaker computers while letting others enjoy the
full look.

**Independent Test**: switch between presets in the settings while in a game; the look and the frame
time change immediately; restart; the choice is kept.

**Acceptance Scenarios**:

1. **Given** the settings window, **When** the player picks a preset, **Then** shadows, edge
   smoothing, scenery density, detail distances and other effects change immediately without
   restarting.
2. **Given** a preset is chosen, **When** the player changes one effect on its own, **Then** the
   preset shows as "Custom" and the individual choice is kept.
3. **Given** the game is restarted, **When** it loads, **Then** the last chosen quality is used.
4. **Given** a first start with no saved choice, **When** the game starts, **Then** Medium is used.

---

### User Story 6 - Day and night (Priority: P3)

As a player, I can turn on a day/night cycle: the light changes from morning to evening to night,
and building windows light up at night.

**Why this priority**: pleasant atmosphere, not essential.

**Independent Test**: enable the cycle, watch a town through a full cycle; disable it and the world
returns to the fixed daylight.

**Acceptance Scenarios**:

1. **Given** the cycle is enabled, **When** time passes, **Then** the light colour, direction and
   brightness change smoothly through day, dusk, night and dawn.
2. **Given** night, **When** looking at a town, **Then** many building windows are lit and the scene
   stays readable (never fully dark).
3. **Given** the cycle is disabled (the default), **When** playing, **Then** a fixed daylight is used.
4. **Given** the game is paused, **When** the cycle is enabled, **Then** the time of day does not
   advance.

---

### Edge Cases

- A model file is missing, unreadable, from a newer editor version with unknown sections, or larger
  than the allowed size: unknown sections are skipped; missing or invalid required parts stop the
  game at start with the file name and the reason.
- A model uses more colours than the palette allows or has no voxels: rejected with a clear message.
- The GPU does not support a requested effect setting (for example a sample count): the nearest
  supported setting is used and the settings screen shows what is in use.
- The window is resized or minimized while effects are on: effects keep working at the new size.
- Very large maps at far zoom: shadows cover only the visible distance; far regions are unshadowed
  rather than slowing the frame.
- Changing quality while a game is running must not drop frames for longer than a brief moment
  (well under a second).
- Bushes and rocks never appear on water, roads, buildings, industries or under trees.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The world MUST be lit by a directional light that casts shadows from terrain, roads,
  bridges, buildings, industries, trees, scenery and vehicles onto everything else.
- **FR-002**: Shadow quality MUST adapt to the camera distance so that shadows near the camera are
  detailed and shadows across the whole visible area remain present, without flicker while the
  camera moves.
- **FR-003**: Shadow edges MUST be soft (filtered), and shaded areas MUST keep readable colours
  through ambient sky light.
- **FR-004**: Lighting MUST be computed in linear colour with a tone-mapping step, so bright and dark
  areas keep detail and colours stay consistent between objects.
- **FR-005**: Edges of objects MUST be smoothed (anti-aliasing) at medium and high quality.
- **FR-006**: Water MUST show a continuous subtle animation and light highlights, driven by real time
  (so it also moves while the game is paused), and a lighter band along shores.
- **FR-007**: Decorative bushes and rocks MUST be placed on free land from a deterministic function
  of the map position, so they are identical every time the same map is shown; they are decoration
  only and MUST NOT affect the simulation, construction or saved data.
- **FR-008**: Decorative scenery MUST disappear from tiles that become occupied (roads, buildings,
  industries, water, trees) and its density MUST depend on zoom and quality.
- **FR-009**: Objects (buildings, industries, vehicles, trees, and later stations and depots) MUST be
  drawable from voxel models whose voxels are smaller than a terrain tile.
- **FR-010**: Models MUST be read at startup from MagicaVoxel `.vox` files, with a reader written for
  this game from the published format description. Model voxels are 1/8 of a terrain tile (8 per
  tile side).
- **FR-011**: Each model MUST be turned into an efficient mesh at load time, with softened corners
  (ambient occlusion), and reused for every object that shows it.
- **FR-012**: Which model each object kind uses MUST be defined in data files validated at startup;
  a reference to a missing or invalid model MUST stop the start with a clear message, and object
  kinds without a model MUST fall back to a placeholder.
- **FR-013**: Models MUST have simpler versions for far zoom, chosen by distance, so far views stay
  clean and cheap.
- **FR-014**: Adding a model for a new object kind (for example a station or depot) MUST require only
  a model file and a data entry, not renderer changes.
- **FR-015**: The game MUST offer quality presets Low, Medium and High, plus Custom when single
  effects are changed, controlling at least: shadows on/off, shadow detail and reach, edge smoothing,
  water animation, decorative scenery density, model detail distance and ambient occlusion strength.
- **FR-016**: Every effect MUST be switchable on its own, and every change MUST apply immediately
  without restarting the game.
- **FR-017**: The graphics choices MUST be saved in the player's settings and restored at start;
  invalid saved values fall back to defaults (Medium).
- **FR-018**: An optional day/night cycle (off by default, 12 real minutes per cycle, advancing only
  while the game is not paused, night about a quarter of it) MUST change light direction, colour and
  brightness smoothly, MUST light building windows at night, and MUST keep night scenes readable.
- **FR-019**: The render statistics (F3, `--stats`) MUST show the current graphics settings and the
  work of each effect (shadow cascades and draws, model draws and instances, decoration count), and
  an automatic measurement mode MUST report the frame cost of each effect so it can be recorded.
- **FR-020**: All new player-facing text (settings labels, preset names, error messages for players)
  MUST be localized in English and Spanish.
- **FR-021**: This feature MUST NOT change the simulation: replaying any recorded game MUST give the
  same fingerprints as before.
- **FR-022**: All art (models, colour palettes) MUST be original. The first models are generated by
  a tool in the repository that writes `.vox` files, so they can be regenerated or replaced.
- **FR-023**: Until vehicles have types, each vehicle MUST show one of 3–4 vehicle models chosen
  stably from its id and painted with its current colour.

### Key Entities *(include if feature involves data)*

- **Voxel model**: a small 3-D grid of coloured voxels authored in an editor; size, palette, voxels;
  may hold several frames or parts.
- **Model mesh**: the drawable form of a model at one or more detail levels, built once at load.
- **Model assignment**: data that links an object kind (building type, industry kind, vehicle,
  tree kind, station, depot) to a model, with scale, offset and optional colour variations.
- **Graphics settings**: the chosen preset and each effect's value, saved with the player settings.
- **Decorative scenery**: bushes and rocks computed from map position; not stored.
- **Time of day**: a presentation-only clock for the day/night cycle; not simulation state.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: With the High preset, a 2048 × 2048 map keeps 60 FPS on the reference hardware at
  normal speed at all zoom levels.
- **SC-002**: With the Medium preset, a 4096 × 4096 map keeps 60 FPS on the reference hardware at
  normal speed at all zoom levels.
- **SC-003**: The developer approves side-by-side before/after screenshots of the same places (town,
  industry, countryside with water, close and far zoom).
- **SC-004**: The frame cost of each effect (shadows, anti-aliasing, water, scenery, models, AO,
  day/night) is measured on the reference hardware and recorded in the architecture document.
- **SC-005**: All models load and are meshed at start in under 1 second in total.
- **SC-006**: Recorded sessions and golden fingerprints are unchanged.
- **SC-007**: Switching presets in a running game takes effect within one second.

## Assumptions

- Reference hardware is the development machine (integrated AMD Radeon GPU, Vulkan) from the
  performance budgets.
- The day/night cycle is presentation only: its clock runs from real time while the game runs
  unpaused, not from the simulation, so it cannot affect determinism.
- Decorative bushes and rocks are client-side only; trees remain the simulation-side scenery from
  feature 004.
- First models are generated by the project (original art) as `.vox` files; the art quality can be
  improved later by editing or replacing files in MagicaVoxel.
- Terrain keeps its one-voxel-per-tile resolution in this feature; finer terrain detail comes from
  shading (variation, ambient occlusion, shadows), not from smaller terrain voxels.
