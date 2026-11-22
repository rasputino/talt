# Feature Specification: Large-Map Rendering

**Feature Branch**: `003-large-map-rendering`

**Created**: 2026-11-22

**Status**: Draft

**Input**: User description: "Large-map rendering: players must be able to play on very large maps
without the slowdowns typical of classic transport games. Today a 2048 x 2048 map makes camera
movement jerky. The world must look the same, but render efficiently at any size and zoom level, and
update quickly when the terrain changes. P1 smooth camera on 4096 x 4096 at every zoom level; P1
terrain changes visible within a fraction of a second without stutter; P2 distant parts readable when
simplified; P3 whole map visible from far away without slowdown. 60 FPS on reference hardware on
4096 x 4096 at all zoom levels, no frame above 33 ms during camera movement; one-tile terrain change
visible in under 100 ms; rendering memory under 2 GB on 4096 x 4096. Out of scope: new visual effects
such as shadows (feature 018)."

## Context

The prototype draws every visible face of every voxel of the map, every frame, whether it is on
screen or not. Measured on the reference hardware with 50 000 vehicles: a 1024 × 1024 map draws
4.8 million triangles at 36 FPS; a 2048 × 2048 map draws 19.1 million triangles at 14 FPS; a
4096 × 4096 map needs about 6.4 GB of graphics memory and does not render at all (the window stays
blank). Very large maps are one of the game's main differentiators, so the world must render
smoothly at the largest supported size, and terrain changes (needed by construction and
terraforming in later features) must show up almost instantly. This feature changes how the world
is drawn, not what it looks like.

## Clarifications

### Session 2026-11-22

- Q: How far must the camera be able to zoom out? → A: Far enough to see the whole 4096 × 4096 map
  on screen, with fog adjusted so the whole map is visible at the farthest zoom.
- Q: Where are the rendering statistics shown, given the game cannot draw text yet? → A: A key (F3)
  switches the window title between the game status and the rendering statistics, and the
  statistics are also printed to the console once per second; an on-screen overlay comes with the
  user interface feature (005).
- Q: Must roads stay visible in simplified distant areas? → A: Yes, always: simplification gives
  priority to roads so the network looks continuous at every distance (it may look slightly wider
  from far away).
- Q: What does the player see while a 4096 map's geometry is being prepared at start? → A: The
  window opens as soon as the terrain is generated and areas appear within a few seconds, nearest
  to the camera first; no loading screen.
- Guidance (from the roadmap): keep the current look (colours, ambient occlusion, fog behaviour);
  visual upgrades come in 018. Map edges may show the terrain's side walls; nothing renders below
  the world.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Smooth camera on a 4096 × 4096 map (Priority: P1)

As a player on a 4096 × 4096 map, I can pan, rotate and zoom the camera smoothly at every zoom
level, from a close-up of a few tiles to the whole map.

**Why this priority**: Without it, the largest maps are unplayable (today they do not even appear),
and large maps are a core promise of the game.

**Independent Test**: Start the prototype on a 4096 × 4096 map with 50 000 vehicles at normal speed,
pan, rotate and zoom continuously from the closest to the farthest zoom for a minute, and read the
frame rate and the longest frame from the on-screen statistics.

**Acceptance Scenarios**:

1. **Given** a 4096 × 4096 map, **When** the game starts, **Then** the terrain, water, roads and
   vehicles are visible and look like they do on smaller maps today.
2. **Given** the camera at its closest zoom, **When** the player pans and rotates continuously,
   **Then** the game runs at 60 FPS with no frame longer than 33 ms.
3. **Given** the camera at any zoom level between closest and farthest, **When** the player zooms in
   and out continuously, **Then** the game runs at 60 FPS with no frame longer than 33 ms.
4. **Given** a 512, 1024 or 2048 map, **When** the player moves the camera, **Then** the game runs at
   least as fast as on the 4096 map and looks the same as before this feature.

---

### User Story 2 - Terrain changes appear almost instantly (Priority: P1)

As a player, when terrain or objects change (later: building, terraforming), the view updates
within a fraction of a second, without stutter.

**Why this priority**: Construction and terraforming (feature 006 and later) depend on it; a view
that lags behind edits feels broken.

**Independent Test**: In the prototype, trigger a single-tile terrain change (a debug key that
raises or lowers the tile under the camera target) and measure the time from the change to the
first frame that shows it; repeat for a tile on a chunk border and for a burst of changes.

**Acceptance Scenarios**:

1. **Given** a running game on any map size, **When** one tile of terrain is changed, **Then** the
   change is visible on screen in under 100 ms.
2. **Given** a change on a tile at the border between two areas of the map, **When** it is applied,
   **Then** both sides show the change correctly, with no holes or leftover faces.
3. **Given** many tiles changed at once, **When** they are applied, **Then** frames keep coming
   without any frame above 33 ms, and all changes are visible within a few frames.

---

### User Story 3 - Distant terrain stays readable (Priority: P2)

As a player, distant parts of the map remain readable — terrain shape, water, coasts and roads —
even when they are drawn in less detail.

**Why this priority**: Simplifying the distant view is what makes large maps affordable, but it must
not make the map confusing.

**Independent Test**: Look at the map from medium and far zoom levels and compare with the
full-detail view: hills, valleys, water areas and the road grid are recognisable, there are no holes
or visible cracks between areas drawn at different detail, and the switch between detail levels
does not distract.

**Acceptance Scenarios**:

1. **Given** the camera far from part of the map, **When** that part is drawn simplified, **Then**
   its hills, water and roads are still recognisable and in the same places.
2. **Given** two neighbouring areas drawn at different detail, **When** the player looks at the
   boundary, **Then** no gaps through which the sky shows are visible.
3. **Given** the camera moving towards an area, **When** its detail increases, **Then** the change
   happens far enough away that it is not distracting (no obvious popping near the centre of the
   view).

---

### User Story 4 - Whole map from far away (Priority: P3)

As a player, I can zoom out until the whole map is on screen, and the game stays smooth.

**Why this priority**: An overview of the whole network is useful, but players spend most of their
time closer in.

**Independent Test**: On a 4096 × 4096 map, zoom out to the farthest zoom so that the whole map is
visible and rotate the camera; read the frame rate and longest frame.

**Acceptance Scenarios**:

1. **Given** the camera at the farthest zoom over a 4096 × 4096 map, **When** the whole map is on
   screen, **Then** the game runs at 60 FPS with no frame longer than 33 ms.

---

### Edge Cases

- Map edges: the outer side walls of the terrain may be visible; nothing is drawn below the world
  and no faces of the bottom of the world are drawn.
- Areas with no visible geometry (all air or completely enclosed) cost nothing to draw.
- A terrain change in an area that is currently off screen or drawn simplified must be correct once
  it comes into view or into full detail.
- A burst of changes larger than can be processed in one frame is spread over several frames
  instead of freezing the game.
- Rapid zooming or panning across the whole map must not cause long frames from preparing detail
  for many areas at once; areas may briefly show at lower detail instead.
- Window resize and minimise keep working; when the window is minimised nothing is drawn.
- Changes made while the game is paused are still shown.
- Fast forward: drawing must leave the simulation enough time per frame to keep feature 001's
  fast-forward criterion (at least ×20 at 30 FPS or more on the 4096 map with 50 000 vehicles).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The game MUST render maps of 512 × 512 up to 4096 × 4096 tiles, with the same terrain
  colours, ambient occlusion, lighting and fog as before this feature when viewed at full detail.
- **FR-002**: At full detail, the drawn surface MUST be exactly the same set of visible voxel faces
  as before (no missing, extra or shifted faces), verified by automated tests.
- **FR-003**: Parts of the map outside the camera's view, empty, or hidden behind fog MUST NOT be
  drawn.
- **FR-004**: Distant parts of the map MUST be drawn at reduced detail, chosen so the simplification
  stays below a small on-screen error (about one pixel of shape change per tile at the detail
  switch), and MUST keep terrain shape, water and coasts recognisable. Roads MUST stay visible and
  continuous at every level of detail (they may be drawn slightly wider when simplified).
- **FR-005**: Boundaries between areas drawn at different detail MUST NOT show gaps through which
  the sky or background is visible.
- **FR-006**: Faces at the bottom of the world MUST NOT be drawn; the outer side walls at the map
  edges MAY be visible.
- **FR-007**: When the terrain changes, only the affected areas (and their neighbours when the
  change touches a border) MUST be rebuilt, the work MUST happen off the frame's critical path, and
  the result MUST be visible in under 100 ms for a single tile.
- **FR-008**: Preparing or rebuilding map geometry MUST NOT cause frames longer than 33 ms; pending
  work MUST be spread across frames.
- **FR-009**: The game MUST provide a way to change one tile of terrain at runtime for testing (for
  example a debug key that raises or lowers the tile under the camera target), so FR-007 can be
  verified until real construction exists.
- **FR-010**: The game MUST show rendering statistics on demand: frame time (average and worst over
  the last second), number of draw operations, triangles drawn, visible areas, and graphics memory
  used by the world. A key (F3) switches the window title between the game status and these
  statistics; the statistics are also printed to the console once per second while shown.
- **FR-011**: Graphics memory used to draw the world of a 4096 × 4096 map MUST stay under 2 GB.
- **FR-012**: The window MUST open as soon as the terrain is generated, without waiting for the
  map's geometry; areas then appear over the first seconds, nearest to the camera first, with no
  loading screen and without frames above 33 ms caused by the filling-in.
- **FR-013**: Rendering changes MUST NOT change the simulation's behaviour or its state
  fingerprints.
- **FR-014**: Player-facing text added by this feature (statistics labels) MUST be localizable in
  English and Spanish.
- **FR-015**: The camera MUST be able to zoom out far enough to show the whole map on screen
  (4096 × 4096 included); at that zoom, fog MUST NOT hide any part of the map.

### Key Entities

- **Map area (chunk)**: a fixed-size block of the voxel world; the unit that is rebuilt when the
  terrain changes and that is tested for visibility.
- **Drawing region**: a group of neighbouring map areas drawn together, tested for visibility and
  given one level of detail at a time.
- **Area mesh**: the drawable surface of an area at a given level of detail; has bounds, a size in
  memory and a triangle count.
- **Level of detail**: full detail or a simplified version for distant areas; chosen per drawing
  region every frame from its distance and on-screen size.
- **Dirty area**: an area whose voxels changed and whose meshes must be rebuilt.
- **Render statistics**: per-frame counters shown to the player and used to verify the budgets.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On the reference hardware, a 4096 × 4096 map with 50 000 vehicles runs at 60 FPS at
  normal speed at every zoom level (up to the whole map on screen), with no frame above 33 ms during one minute of continuous camera
  movement.
- **SC-002**: A single-tile terrain change is visible in under 100 ms on every supported map size.
- **SC-003**: Graphics memory used by the world of a 4096 × 4096 map is under 2 GB.
- **SC-004**: 512, 1024 and 2048 maps run at least as fast as before this feature and at least as
  fast as the 4096 map; results for 512, 2048 and 4096 are recorded in the architecture document.
- **SC-005**: At full detail the drawn surface matches the previous renderer face for face (automated
  check), and players see no difference in colours, shading or fog.
- **SC-006**: Feature 001's fast-forward criterion holds on the 4096 map: at least ×20 effective
  speed at 30 FPS or more with 50 000 vehicles.
- **SC-007**: On a 4096 × 4096 map the window opens within 10 seconds of the simulation being
  created on the reference hardware (terrain generation included). Creating the prototype's
  synthetic traffic (about 30 s of route finding for 5 000 lines, feature 001) is not part of this
  feature.

## Assumptions

- Reference hardware is the development machine from `docs/SPECKIT_GUIDE.md` section 5 (AMD Ryzen 7
  160, integrated Radeon GPU); "reference scenario" for this feature is the prototype's synthetic
  map and traffic (50 000 vehicles), since towns and industries do not exist yet.
- The map remains a single layer of terrain at most one area tall (heights below 32 voxels), as
  generated today; the design must not prevent taller worlds later.
- Vehicle drawing stays as it is (one instanced draw); only the world's terrain is optimised here.
- The 60 FPS target is with vertical sync on a 60 Hz display; "no frame above 33 ms" is measured
  over continuous camera movement, not during the first seconds after start while the map fills in.
- Real construction and terraforming come later (feature 006); a debug edit is enough to prove the
  update path now.
- Visual upgrades (shadows, better water, new materials) are out of scope (feature 018).
