# Feature Specification: Save and load

**Feature Branch**: `014-save-and-load`

**Created**: 2026-11-28

**Status**: Draft

**Input**: User description: "Players must be able to save and continue games safely, including very
large ones, and saves must keep working after the game is updated. P1: I save the game with a name and
later load it to continue exactly where I was. P1: the game autosaves at a configurable interval,
keeping the last N autosaves. P2: the load screen shows each save's date, company name, money, map size
and a thumbnail. P2: saves from older game versions still load (with upgrades applied). P3: a corrupted
save is detected and reported clearly; it never crashes the game. Success: save under 3 s and load under
5 s for the reference scenario; autosave does not freeze the game for more than 100 ms; a loaded game
continues identically to the original (same state hash after N further ticks). Out of scope: cloud
saves. Coverage must include every integrated system: map/terrain/construction, roads, towns,
industries, stations and cargo, finance, road vehicles/depots/lines, railways (015) and destination
demand/cargo routing (013). Leave room for future save sections from 016 (vehicle catalog/eras) and 017
(line statistics)."

## Context

Features 001 to 013 and 015 built a complete, deterministic game: a generated voxel map with terrain
the player can reshape (004, 006), roads and town streets (007), towns that grow (008), industries that
produce and accept cargo (009), stations where cargo waits and is loaded (010), company accounts, loans
and game over (011), road vehicles, depots and lines (012), destination demand with routes and
transfers over the player's lines (013), and railways with signals, trains and rail depots (015). Every
piece of that state is covered by the simulation's state fingerprint, which is how the project proves
that two games are identical.

Today a game exists only while the client is open. Closing it, or a crash, loses everything; the only
persistent artefacts are recorded sessions (settings plus the player's commands), which rebuild a game
by replaying it from the start and become slower to load the longer the game was played. This feature
lets the player keep a game: save it under a name, have it saved automatically, see their saves with
enough information to recognise them, and load one to continue exactly where they stopped, even after
the game has been updated.

Two features are being specified at the same time and will add state of their own: 016 (vehicle
catalog and eras: which models exist, renewal and replacement settings) and 017 (line management and
statistics: monthly and yearly histories, departure intervals). Their specs ask this feature for a save
contract; this spec defines it (FR-020 to FR-023) so they can add their state without changing the
format of existing saves.

## Clarifications

### Session 2026-11-28

- Q: How often does the game autosave by default? → A: Every 10 minutes of real, unpaused play time
  (choices: off, 5, 10, 15, 30 minutes), keeping the last 3 autosaves; the pace is the same at every
  game speed, including fast forward.
- Q: What happens when the installed game data differs from the data a save was played with? → A: The
  save loads when every item it references (cargo, industry, vehicle model, rail or signal type) still
  exists by its identifier; if the data differ, the player is warned that the game may play
  differently. If a referenced identifier is missing, the save is refused with a clear message naming
  what is missing.
- Q: What picture is each save's thumbnail? → A: A top-down overview of the whole map (terrain, water,
  roads, tracks, towns) drawn from the game state without the graphics card, so saves written by the
  headless tool get one too.
- Q: Are there quick save and quick load keys? → A: Yes: F5 saves to a single "Quick save" slot without
  asking; F9 loads it (asking for confirmation when there is unsaved progress). Both keys can be
  rebound.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Save a game and continue it later (Priority: P1)

During a game the player opens the game menu, chooses "Save", types a name (or keeps the suggested
one) and confirms. The game reports that it was saved. Later, possibly after quitting the game, the
player chooses "Load" in the main menu (or in the game menu), picks the save and continues: the map,
the terrain they reshaped, roads, tracks, signals, stations, towns, industries, every vehicle and train
where it was and with the cargo it carried, the waiting cargo and its destinations, the money, loans
and accounts, the date and the game speed settings are all exactly as they were at the moment of
saving.

**Why this priority**: without it no game can last beyond one sitting; it completes the minimum viable
product.

**Independent Test**: build a game that uses every system (construction, roads, rail with signals,
stations, road vehicles and trains on lines with destination cargo, a loan), save it, load it into a
new game and check that the state fingerprint equals the original's at the moment of loading and that,
after both games run the same further ticks with the same commands, their fingerprints are still equal.

**Acceptance Scenarios**:

1. **Given** a running game, **When** the player saves it under the name "Coast line", **Then** a save
   called "Coast line" appears in the save list and the game continues without interruption.
2. **Given** a save, **When** the player loads it, **Then** the game resumes paused at the saved date
   with a state identical to the saved one (same fingerprint).
3. **Given** a game loaded from a save and the original game that was saved, **When** both run 10 000
   further ticks with the same commands, **Then** their fingerprints are equal at every checkpoint.
4. **Given** a save name that already exists, **When** the player saves under it, **Then** the game
   asks whether to overwrite it and only replaces it after confirmation.
5. **Given** a game being played, **When** the player loads another save or returns to the main menu
   with unsaved progress, **Then** the game asks for confirmation before discarding it.
6. **Given** a map-less benchmark scenario (vehicles only), **When** it is saved and loaded, **Then** it
   continues identically too.

---

### User Story 2 - Autosave (Priority: P1)

While the player plays, the game saves automatically at a configurable interval and keeps only the
most recent autosaves, so a crash or a mistake costs little progress. Autosaving never freezes the
game noticeably.

**Why this priority**: protects players against crashes and forgotten saves; large games are hours
of work.

**Independent Test**: run a game with autosave at a short interval and a limit of 3; after 5 intervals
exactly 3 autosaves exist, they are the newest ones, each loads and continues identically, and no
frame during autosave took longer than the stall budget.

**Acceptance Scenarios**:

1. **Given** autosave enabled with the default interval, **When** an interval elapses during play,
   **Then** an autosave is written and the oldest autosave beyond the kept count is deleted.
2. **Given** autosave disabled in the settings, **When** play continues, **Then** no autosave is written.
3. **Given** an autosave being written, **When** the player keeps playing, **Then** the game keeps
   running smoothly (no frame is delayed by more than 100 ms because of the autosave).
4. **Given** the game is paused, **When** the interval would elapse, **Then** no autosave is written
   (nothing changed).
5. **Given** an autosave is still being written when the next one is due, **Then** the new one waits
   or is skipped; two writes never corrupt each other.

---

### User Story 3 - Recognise saves on the load screen (Priority: P2)

The load screen lists every save, newest first, with its name, the real date and time it was saved,
the in-game date, the company name, the company's money, the map size and a small top-down picture
of the whole map at the moment of saving. Autosaves are marked as such. The player can delete a save from the list
(after confirmation).

**Why this priority**: with many saves and autosaves players need to find the right one quickly; it
does not block saving and loading itself.

**Independent Test**: create three saves at different moments of different games; the list shows them
newest first with the right details and a picture for each, without loading any game.

**Acceptance Scenarios**:

1. **Given** several saves, **When** the player opens the load screen, **Then** each entry shows name,
   save time, in-game date, company name, money, map size and a thumbnail, newest first.
2. **Given** a save written by the headless tool, **When** the load screen lists it, **Then** it shows
   the same kind of map overview thumbnail as a save written by the client.
3. **Given** a selected save, **When** the player deletes it and confirms, **Then** it disappears from
   the list and from disk.
4. **Given** 200 saves, **When** the player opens the load screen, **Then** it opens without a visible
   pause (details are read without loading whole games).

---

### User Story 4 - Older saves still load after updates (Priority: P2)

When a later version of the game changes what a part of the game stores, saves written by earlier
versions still load: the missing or changed information is upgraded to sensible values and the game
continues. Saves written by a newer version than the running one are refused with a clear message.

**Why this priority**: players' long games must survive updates; it is a promise that gets harder the
later it is made, so the mechanism has to exist from the first save format.

**Independent Test**: keep fixture saves written by each earlier save format version in the test
data; each loads in the current version, is upgraded, and continues to run.

**Acceptance Scenarios**:

1. **Given** a fixture save from an earlier format version, **When** it is loaded, **Then** it is
   upgraded, the game continues, and saving it again writes the current format.
2. **Given** a save that lacks a part added by a later feature (for example statistics history from
   feature 017), **When** it is loaded, **Then** that part starts empty or at its defaults and the rest
   of the game is unchanged.
3. **Given** a save from a newer game version, **When** the player tries to load it, **Then** the game
   explains that the save needs a newer version and stays where it was.

---

### User Story 5 - Corrupted saves are reported, never crash (Priority: P3)

If a save file is damaged (truncated, altered, unreadable, or not a save at all), the game detects it
before using any of its contents, tells the player which save is damaged in plain words, and stays
usable. Damaged saves are still listed (marked as damaged) so the player can delete them.

**Why this priority**: rare, but a crash on load would lose the player's trust and could block the
load screen entirely.

**Independent Test**: corrupt a valid save in each possible way (truncate it, flip bytes in each part,
replace it with an unrelated file, empty it); every load attempt reports a clear error and the game
continues running; the original game state is untouched.

**Acceptance Scenarios**:

1. **Given** a save with one changed byte anywhere, **When** the player loads it, **Then** the game
   reports that the save is damaged and does not start a broken game.
2. **Given** a damaged save in the folder, **When** the load screen opens, **Then** the other saves are
   listed normally and the damaged one is marked damaged.
3. **Given** a power cut or crash while a save is being written, **Then** the previous save of the same
   name is still intact (a save is never left half-written in place of a good one).

---

### Edge Cases

- Saving while paused, while a construction tool is active, or right after a command was issued: the
  save holds the state at a tick boundary; commands queued but not yet processed are kept and applied
  after loading exactly as they would have been.
- Saving during game over (feature 011): allowed; loading it shows the game-over state again.
- Disk full, permission denied or folder missing: the save fails with a clear message, the game
  continues, and no previous save is damaged.
- Save names with characters invalid for file names, very long names, or names differing only in
  letter case: names are made safe for every platform and clashes are detected.
- A save written on Windows loads on Linux and macOS (and the reverse) and continues identically.
- A save whose game data (cargo, industries, vehicles, rail rules) no longer matches the installed
  data files: it loads with a warning when every referenced identifier still exists, and is refused
  naming the missing items otherwise (FR-018).
- Quick save while an autosave is being written: the quick save waits for it; F9 with no quick save
  yet tells the player there is none.
- Loading a save while another game is running replaces it; the replaced game's background work
  (chunk meshing, a pending autosave) is finished or cancelled cleanly first.
- Very large games (reference scenario): save and load stay within budget and show progress when
  loading takes more than a moment.
- The undo history of construction (feature 006) and money shown in another currency: undo history is
  part of the game and is kept; display currency and language are player settings, not part of a save.
- A session recording (`--record`) of a game started from a save: see Assumptions.

## Requirements *(mandatory)*

### Functional Requirements

**Saving and loading**

- **FR-001**: The player MUST be able to save the running game under a name of their choice from the
  game menu, with a suggested default name (company name and in-game date).
- **FR-002**: A save MUST hold the complete game state at a tick boundary: map and terrain changes,
  scenery and objects, construction state and undo history, roads and town streets, towns, industries,
  stations with their waiting cargo, ratings and catchments, company accounts, loans and history,
  road vehicles, depots and lines, rail track, signals, reservations, trains and rail depots,
  destination tables, routes and destination cargo, the calendar and tick counter, every random
  stream, and the commands queued but not yet processed.
- **FR-003**: Loading a save MUST reproduce a game whose state fingerprint equals the saved game's
  fingerprint at the moment of saving, and which continues identically (same fingerprint after any
  number of further ticks with the same commands) on every supported platform.
- **FR-004**: State that the game can rebuild from saved data (meshes, routing graphs, caches, coverage
  lookups) MAY be rebuilt on load instead of saved, provided FR-003 holds.
- **FR-005**: A loaded game MUST start paused, with the camera at the position it had when saved.
- **FR-006**: The game MUST ask for confirmation before overwriting a save of the same name and before
  discarding unsaved progress (loading, quitting to the menu, starting a new game).
- **FR-007**: Saves MUST be written so that an interrupted write never replaces or damages an existing
  good save.
- **FR-008**: The player MUST be able to load a save from the main menu and from the in-game game menu,
  and to delete a save from the load screen after confirmation.
- **FR-008a**: A quick save key (default F5) MUST save to a single "Quick save" slot without asking,
  replacing the previous quick save; a quick load key (default F9) MUST load it, asking for
  confirmation when there is unsaved progress. Both keys MUST be rebindable like every other key.
- **FR-009**: Saves MUST be stored in a per-user folder of the player's profile, one file per save with
  the extension `.talt`, and the same files MUST load on every supported platform.
- **FR-010**: The headless tool MUST be able to save a game after running it and to load a save and
  continue it, printing fingerprints, so saves can be verified without a window.

**Autosave**

- **FR-011**: The game MUST autosave after every 10 minutes of real, unpaused play time by default;
  the player MUST be able to choose off, 5, 10, 15 or 30 minutes in the settings. Paused time does not
  count, and the interval does not depend on the game speed.
- **FR-012**: The game MUST keep only the most recent autosaves, by default 3 (the count is
  configurable), deleting the oldest when a new one is written; manual saves are never deleted
  automatically.
- **FR-013**: Autosaving MUST NOT stall play by more than 100 ms in any frame, including in the
  reference scenario; the game keeps running while the autosave is written.
- **FR-014**: If saving fails (disk full, permissions), the game MUST tell the player and keep running.

**Save list and details**

- **FR-015**: The load screen MUST list every save, newest first, with name, real save time, in-game
  date, company name, money, map size and a thumbnail; autosaves MUST be marked as such and damaged
  saves MUST be marked as damaged. The thumbnail MUST be a top-down overview of the whole map
  (terrain, water, roads, tracks, towns) produced from the game state without the graphics card, so
  every save has one, including saves written by the headless tool.
- **FR-016**: Save details and thumbnails MUST be readable without loading the game state, so a list
  of 200 saves opens without a visible pause.

**Compatibility and integrity**

- **FR-017**: Every save MUST record its format version and the game version that wrote it. Saves from
  any earlier format version MUST load, upgraded to the current version; saves from a newer version
  MUST be refused with a clear message.
- **FR-018**: A save MUST record which game data (cargo, industries, vehicles, rules) it was played
  with. When the installed data differs, the game MUST load the save if every item it references
  still exists by identifier, warning the player that the game may play differently; if any referenced
  identifier is missing, the save MUST be refused with a message naming the missing items. With
  identical data a loaded game continues identically (FR-003); with different data only loading is
  promised, not identical continuation.
- **FR-019**: Every load MUST check the save's integrity before using any of its contents; a damaged,
  truncated or unrelated file MUST be reported with a clear, localised message and MUST NOT crash the
  game or change the running game.

**Save contract for later features**

- **FR-020**: A save MUST be divided into independent parts, one per game system, each with its own
  version, so a feature can add or change its part without changing the others.
- **FR-021**: A save without a part that the current version knows MUST load with that part at its
  defaults (for example: no statistics history, renewal off), and a part the current version does not
  know MUST cause the save to be refused as "newer" (FR-017), never silently dropped.
- **FR-022**: Content items (cargo types, industry types, vehicle models, rail and signal types) MUST be
  referenced in saves by stable identifiers, not by their position in the data files, so reordering or
  adding entries (feature 016's catalog) does not change saved games.
- **FR-023**: The rules for adding a part (identifier, version, defaults when missing, upgrade from
  earlier versions, tests with fixture saves) MUST be documented for features 016 and 017, which add
  catalog/era/renewal state and statistics/interval state respectively.

**Interface and text**

- **FR-024**: All player-facing text of this feature (menus, dialogs, messages, list columns) MUST be
  localised in English and Spanish.
- **FR-025**: Loading MUST show progress when it takes longer than about half a second.

### Key Entities

- **Save**: one stored game. Has a name, a kind (manual or autosave), a details block and the game
  state divided into parts.
- **Save details**: what the load screen shows without loading the game: name, kind, real save time,
  in-game date, company name, money, map size, game and format versions, game data identity, and the
  thumbnail.
- **Save part**: the saved state of one game system (map and construction, roads, rail, towns,
  industries, stations, finance, road vehicles, trains, destination demand, simulation core, and later
  catalog and statistics), with its own version and upgrade rules.
- **Autosave settings**: on or off, interval, number of autosaves to keep; a player setting, not part of
  any save.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In the reference scenario (4096 × 4096 map, 500 towns, 1 000 industries, 50 000
  vehicles, 5 000 stations) on the reference machine, saving takes at most 3 seconds and loading at
  most 5 seconds.
- **SC-002**: Autosave never delays a frame by more than 100 ms, in the reference scenario included.
- **SC-003**: For every test game (covering every system, including rail and destination cargo), the
  state fingerprint after loading equals the fingerprint at saving, and equals the original game's
  fingerprint after 10 000 further ticks; this holds for saves written on any supported platform and
  loaded on any other.
- **SC-004**: 100% of corrupted-save test cases (truncation, changed bytes in every part, empty file,
  unrelated file) are reported as damaged, with no crash and no change to the running game.
- **SC-005**: Fixture saves of every earlier format version load in the current version (from the
  second format version onwards; the first version has no predecessor).
- **SC-006**: The load screen with 200 saves opens in under half a second.
- **SC-007**: Saving and loading do not change any existing golden fingerprint: a game that is never
  saved plays exactly as before this feature.

## Assumptions

- Saves are local files only; cloud saves, save sharing and save encryption are out of scope.
- One company (the player) per game, as in every feature so far; the format does not need to store
  several players yet, but the parts are versioned so it can later.
- Display settings (language, currency, graphics, key bindings, volume) are player settings, not part
  of a save; the game speed setting is not saved, a loaded game starts paused (FR-005).
- Saves are not meant to be edited by hand or protected against deliberate tampering; integrity checks
  detect damage, not cheating.
- Recording a session (`--record`) from a game that was loaded from a save is out of scope for this
  feature; recordings keep starting from a new game. Recorded sessions remain the tool for replays and
  determinism checks.
- Saving never changes the simulation: it reads state at a tick boundary, so existing golden
  fingerprints stay as they are (SC-007).
- The first format version written by this feature is version 1; "older version" upgrades are proven
  with fixture saves as soon as a part changes version (features 016 and 017 are expected to be the
  first to do so).
