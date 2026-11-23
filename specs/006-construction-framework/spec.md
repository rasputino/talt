# Feature Specification: Construction and Terraforming Framework

**Feature Branch**: `006-construction-framework`

**Created**: 2026-11-23

**Status**: Draft

**Input**: User description: "Construction and terraforming framework. Everything the player builds
(roads, rails, stations, depots) shares the same interaction: choose a tool, see a preview with its
cost, confirm, and the money is spent. The player also needs to shape the terrain to make room for
infrastructure. P1 ghost preview on the map, green when valid and red when invalid, with the reason
and the total cost shown before confirming. P1 raise, lower and level land in an area, paying per
tile changed, within the rules that keep terrain buildable. P1 demolish objects I own and clear
scenery (trees), paying a removal cost. P2 drag to build along a path or over an area, and press a
key to cancel. P3 undo the last construction action within a short time window if nothing else
changed it (optional, confirm during clarify). Previews update every frame while dragging over 500
tiles without dropping below 60 FPS. Construction is impossible when money is insufficient, with a
clear message. Out of scope: specific objects (roads in 007, rail in 015, stations in 010)."

## Context

Today the player can look at a generated map (feature 004), read the company money, date and speed in
the top bar and click tiles, roads and vehicles to inspect them (feature 005). The only way to change
the world is a debug shortcut (PageUp/PageDown) that raises or lowers the tile under the camera with
no rules, no cost and no record in replays. The money in the top bar is a fixed figure that nothing
spends.

Every later building feature (roads, stations, rail, depots, airports) needs the same steps: pick a
tool, aim at the map, see what would happen and what it would cost, confirm, and pay. This feature
builds that shared interaction once, plus the first three tools that need nothing else to exist:
terraforming, demolition and clearing scenery. Specific buildable objects arrive with their own
features and plug into this framework.

## Clarifications

### Session 2026-11-23

- Q: Is "undo the last construction action" in version 1? → A: yes, within a short window: the
  company's last action can be undone for a short time if nothing else changed the tiles it touched,
  and its cost is refunded.
- Q: When raising or lowering makes a neighbour more than one step away, what happens? → A: the
  neighbours are adjusted in cascade just enough to keep the one-step rule; every changed tile is
  paid; if any tile that would have to change cannot (water, object not owned, height limit), the
  whole action is refused.
- Q: Can the player build and terraform while the game is paused? → A: yes, confirmed actions apply
  immediately while paused, without advancing the clock.
- Q: Trees exist in the map data but are not drawn; are they drawn in this feature? → A: yes, simple
  voxel trees (trunk and crown, the three kinds) drawn near the camera, which disappear when cleared.
- Q: Does releasing the drag build, or is an extra confirmation needed? → A: releasing builds; the
  cancel key or the right mouse button cancel before releasing (undo covers mistakes).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Preview with validity, reason and cost before confirming (Priority: P1)

The player chooses a construction tool from the toolbar. As the cursor moves over the map, a ghost
preview shows exactly which tiles the action would affect. Affected tiles are drawn green when the
action is allowed and red where it is not. Next to the cursor the game shows the total cost of the
action in the player's display currency and, when it is not allowed, the reason in plain words
(for example "Not enough money", "Too steep", "Owned by someone else", "Water"). Nothing changes
in the world or the company's money until the player confirms.

**Why this priority**: it is the base every other construction feature uses; without it no tool can
be offered.

**Independent Test**: choose any tool, hover several kinds of tiles (flat land, water, steep slope,
the map edge) and check colours, reason and cost; confirm one valid action and check the money drops
by exactly the previewed cost.

**Acceptance Scenarios**:

1. **Given** a tool is selected and the cursor is over a valid tile, **When** the preview is shown,
   **Then** the tile is green and the shown cost equals the amount spent when the action is confirmed.
2. **Given** a tool is selected and the cursor is over a tile where the action is not allowed,
   **When** the preview is shown, **Then** the tile is red and the reason is shown; confirming does
   nothing and costs nothing.
3. **Given** the company cannot afford the previewed action, **When** the preview is shown, **Then**
   it is red with the message "Not enough money" and the cost, and confirming is refused.
4. **Given** a tool is selected, **When** the player presses the cancel key or chooses another tool,
   **Then** the preview disappears and nothing is built.
5. **Given** an action is confirmed, **When** it is applied, **Then** the money in the top bar drops
   by the cost and the world shows the change within a few frames.

---

### User Story 2 - Raise, lower and level land (Priority: P1)

The player uses terraform tools to raise land, lower land, or level an area to one height. Each
changed tile costs a fixed amount from the game's data. The terrain stays playable: a change is only
allowed when the result keeps the rules that make terrain buildable (neighbouring land tiles never
differ by more than one step, land does not go below the water or above the height limit, and tiles
under objects are not changed — objects owned by others never, own objects only after demolishing them).

**Why this priority**: players must be able to make room for roads, rails and stations on hilly maps.

**Independent Test**: on a hilly map, raise one tile, lower one tile and level a rectangle; check the
terrain, the money spent (tiles changed × cost per tile) and that the slope rule still holds
everywhere around the edit.

**Acceptance Scenarios**:

1. **Given** a flat buildable tile, **When** the player raises it, **Then** it is one step higher and
   neighbours that would otherwise be more than one step lower are also adjusted (each adjusted tile is
   counted in the cost), or the action is refused if the adjustment is not allowed.
2. **Given** a rectangle with uneven land, **When** the player levels it, **Then** every tile in the
   rectangle ends at the height of the tile where the drag started, and the cost counts every tile
   whose height changed (inside or around the rectangle).
3. **Given** a tile under water, at the map edge rules, or at the height limit, **When** the player
   tries to change it in a way the rules forbid, **Then** the preview is red with the reason.
4. **Given** a terraform action is confirmed, **When** it is applied, **Then** the change is visible
   on screen within the same delay as other terrain edits (well under a tenth of a second).

---

### User Story 3 - Demolish own objects and clear scenery (Priority: P1)

The player uses a demolish tool to remove objects the company owns and to clear trees, paying a
removal cost per item from the game's data. Objects owned by others (other companies, towns) cannot
be demolished with this tool in this version.

**Why this priority**: trees cover much of the map and block construction; mistakes must be removable.

**Independent Test**: clear an area with trees and check the trees are gone and the cost is the
number of trees × the clearing cost; try to demolish something not owned by the company and check it
is refused with a reason.

**Acceptance Scenarios**:

1. **Given** an area with trees, **When** the player clears it, **Then** every tree in the area is
   removed and the company pays the clearing cost for each removed tree only.
2. **Given** a tile with an object owned by the company, **When** the player demolishes it, **Then**
   the object is removed and the removal cost is paid.
3. **Given** a tile with an object owned by a town or another company, **When** the player tries to
   demolish it, **Then** the preview is red with the reason "Owned by someone else" and nothing happens.
4. **Given** an empty tile, **When** the player uses the demolish tool on it, **Then** nothing
   happens and nothing is charged.

---

### User Story 4 - Drag along a path or over an area, cancel with a key (Priority: P2)

The player presses the mouse on the map and drags: area tools (terraform, clear) cover the rectangle
between press and cursor, path tools (used by later features such as roads) cover the tiles along a
straight line. The preview and the cost update continuously while dragging. Releasing the button
confirms; pressing the cancel key (or the right mouse button) while dragging cancels without building.

**Why this priority**: building tile by tile is tedious; dragging makes construction practical.

**Independent Test**: drag a 25 × 20 area with each area tool and watch the preview and cost follow
the cursor; press the cancel key before releasing and check nothing changed; release over a valid
area and check the result.

**Acceptance Scenarios**:

1. **Given** an area tool, **When** the player drags over a rectangle, **Then** the preview covers
   exactly the rectangle and the cost is the sum over it, updated every frame.
2. **Given** a drag in progress, **When** the player presses the cancel key, **Then** the drag ends,
   the preview returns to the single tile under the cursor and nothing is built or charged.
3. **Given** a drag where only some tiles are valid, **When** the player releases, **Then** the
   behaviour follows the all-or-nothing rule: if any part of the action is invalid, nothing is built
   (the invalid tiles are shown red with their reason).
4. **Given** a drag over 500 or more tiles, **When** the preview updates, **Then** the game keeps its
   normal frame rate.

---

### User Story 5 - Undo the last construction action (Priority: P3)

Right after confirming an action, the player can press the undo key (or a toolbar button) to undo
it: the terrain, trees and objects go back to how they were and the cost is refunded in full. Undo
only works for the company's last applied action, for a short time (10 seconds of game time at
normal speed), and only if nothing else has changed any tile the action touched since.

**Why this priority**: releasing the mouse builds straight away, so mistakes must be cheap to fix;
but the game is playable without it.

**Independent Test**: level an area, press undo and check terrain and money are exactly as before;
level again, wait past the window and check undo is refused; level, then clear a tree inside the
area and check undo of the levelling is refused with a reason.

**Acceptance Scenarios**:

1. **Given** an action was just applied, **When** the player undoes it within the window, **Then**
   the affected tiles return to their exact previous state and the full cost is refunded.
2. **Given** the window has passed, **When** the player presses undo, **Then** nothing changes and
   the message says the action can no longer be undone.
3. **Given** another action changed a tile the last action touched, **When** the player presses
   undo, **Then** it is refused with a reason.
4. **Given** an action was undone, **When** the player presses undo again, **Then** nothing happens
   (only one level of undo).

---

### Edge Cases

- A drag that leaves the map: the area is clipped to the map; tiles outside are ignored, not red.
- Money changes between preview and confirmation (for example another action was applied first): the
  action is checked again when applied and refused if it can no longer be afforded, with the message.
- Two actions on the same tiles confirmed in the same instant: they are applied one after the other in
  the order they were confirmed; the second is checked against the result of the first.
- A preview over tiles with vehicles or roads of the prototype network: terraforming is refused where
  roads or other objects stand (reason shown); clearing only removes trees.
- Raising a tile next to the sea, a lake or a river: land never rises through water tiles; water tiles
  themselves are not changed by terraform tools in this version.
- The game is paused: previews work and confirmed actions are applied immediately without advancing
  the clock; the undo window does not run out while paused.
- Terraforming a tile with a tree: the tree is removed as part of the action and its clearing cost is
  added to the total shown in the preview.
- Undo after a later action of another kind on different tiles: allowed (only touched tiles matter),
  but only the company's last action can be undone.
- Recorded sessions: every confirmed action is part of the recording and replays to the same terrain,
  money and state on every platform.
- The cost would exceed the largest amount the game can store: the action is refused as too large.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Every construction, terraform and demolition action MUST reach the simulation as a
  player command that is checked (validity and cost) and then applied or refused as one unit.
- **FR-002**: The same check used to apply an action MUST be usable to preview it without changing
  anything, so the preview's validity, reason and cost always match what applying would do.
- **FR-003**: The preview MUST mark each affected tile as valid or invalid, MUST show the total cost
  in the player's display currency and, when invalid, the reason for the first or most important
  problem, in the player's language.
- **FR-004**: The company MUST have a balance; confirming an action spends exactly its cost, and an
  action whose cost exceeds the balance MUST be refused with "Not enough money".
- **FR-005**: The starting balance and every construction cost (per tile raised, lowered, per tree
  cleared, per object demolished) MUST come from game data files validated when the game loads.
- **FR-006**: Players MUST be able to raise, lower and level land over an area; each tile whose
  height changes is charged once per step changed.
- **FR-007**: After any terraform action, neighbouring land tiles MUST differ by at most one height
  step (neighbours outside the chosen area are adjusted in cascade and paid), land MUST stay above
  the water surface next to it and inside the height limits, and tiles under objects MUST NOT change
  (objects not owned by the company never; own objects must be demolished first); actions that cannot satisfy this anywhere are refused as a whole. Existing
  cliffs not touched by the action stay as they are.
- **FR-008**: Terraforming MUST update the tile data the rest of the game reads (height, buildable
  state) and the world drawn on screen.
- **FR-009**: Players MUST be able to clear trees and demolish objects owned by their company over an
  area, paying a removal cost per removed item; objects owned by towns or other companies MUST NOT be
  demolished by this tool.
- **FR-010**: Every tile object MUST have an owner: the player's company, another company, a town or
  nobody; ownership is what the rules above check.
- **FR-011**: Area tools MUST cover the rectangle between the press and the cursor; path tools MUST
  cover a straight line of tiles; both MUST update the preview every frame while dragging.
- **FR-012**: Releasing the drag MUST confirm the action; a cancel key (rebindable) and the right
  mouse button MUST cancel a drag or deselect the tool without building.
- **FR-013**: Actions MUST be all-or-nothing: either every part is applied and paid, or nothing. The
  clear tool is the one exception decided during implementation: objects it may not remove (owned by
  towns or other companies) are left standing and shown as skipped, and the rest of the area is cleared;
  if only such objects are in the area it is refused.
- **FR-014**: Confirmed actions MUST be recorded in sessions and replay to identical state on every
  supported platform.
- **FR-015**: The debug terrain shortcut MUST be replaced by the terraform tools (or reduced to a
  development-only option that also goes through the rules and is recorded).
- **FR-016**: All new player-facing text (tool names, tooltips, reasons) MUST exist in English and
  Spanish.
- **FR-017**: The framework MUST let later features add their own tools and objects (roads,
  stations, rail) by describing what an action covers, how it is checked and what it costs, without
  changing the shared preview, confirmation and payment steps.
- **FR-018**: Confirmed actions MUST be applied even while the game is paused, without advancing the
  game clock.
- **FR-019**: The company's last applied action MUST be undoable for a window of game ticks set in
  the data file (default 200 ticks, 10 seconds at normal speed) if no tile it touched has changed since; undo restores the previous tile state
  exactly, refunds the full cost and is itself a recorded command. Only one level of undo exists.
- **FR-020**: Trees MUST be drawn on the map as simple voxel trees (the three kinds in the map data)
  near the camera, and MUST disappear when cleared.

### Key Entities

- **Construction action**: what the player asked for (tool, start and end tile, options such as the
  target height); checked as a whole and applied as a whole.
- **Action check result**: valid or not, the total cost, the affected tiles each with valid/invalid
  and a reason; used both by the preview and by the application.
- **Company**: the player's company with its balance (more companies arrive with later features).
- **Tile owner**: who owns what stands on a tile — the company, another company, a town or nobody.
- **Construction costs**: data listing the cost per terraform step, per tree cleared and per kind of
  demolished object, and the starting balance.
- **Construction tool**: an entry in the toolbar with its drag shape (single tile, line, area), its
  preview and the action it produces.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: While dragging a preview over 500 tiles, the game keeps 60 frames per second on the
  reference machine, and the preview check of such a drag takes under 1 ms.
- **SC-002**: In 100 % of tested cases the cost shown in the preview equals the money spent when the
  action is confirmed in the same state.
- **SC-003**: An action the company cannot afford is never applied, and the player sees the message
  "Not enough money" before confirming.
- **SC-004**: After any terraform action, no two neighbouring land tiles differ by more than one step
  except where the map already had cliffs before the action.
- **SC-005**: A terraform edit is visible on screen in under 100 ms after it is applied.
- **SC-006**: A session with construction actions recorded on one platform replays to identical
  state on every other supported platform.
- **SC-007**: Previewing and dragging allocate no memory per frame once the drag has started.

## Assumptions

- Only one company exists (the player's); "other company" ownership is modelled so later features and
  multiplayer can use it, but nothing creates such objects yet.
- In this version the objects that can stand on tiles are trees (scenery, owned by nobody) and the
  prototype road network (treated as owned by a town, so it blocks terraforming and cannot be
  demolished). Specific company objects arrive with features 007, 010 and 015; the demolish tool is
  tested with a test object.
- Money is counted in whole internal units (one unit = one US dollar, as in feature 005) and shown in
  the player's display currency.
- The full economy (income, loans, running costs, monthly accounts) is feature 011; this feature only
  adds the balance and spending needed for construction.
- Water tiles (sea, lakes, rivers) are not changed by terraform tools in this version; creating or
  draining water is a later feature.
- Towns' reactions to demolition (rating) arrive with feature 008.
- Trees are scenery data (feature 004) and are drawn by this feature; terrain voxels never contain
  trees.
- The undo window counts game ticks, so it is deterministic and replays identically; it is fixed in
  the data file with the costs.
