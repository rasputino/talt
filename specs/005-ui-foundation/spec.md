# Feature Specification: User Interface Foundation

**Feature Branch**: `005-ui-foundation`

**Created**: 2026-11-22

**Status**: Draft

**Input**: User description: "User interface foundation. The game needs a clear, attractive interface
that matches the voxel art style and scales to many windows (vehicles, stations, finances) without
becoming slow or cluttered. P1 main menu (new game, load game, settings, quit) and, in game, a top bar
with company money, current date and speed controls, and a toolbar to open tools and windows. P1 hover
and click on the map: the game knows exactly which tile, vehicle, station or building is under the
cursor and highlights it. P1 open, move, stack, pin and close windows; tooltips explain buttons.
P2 play in English or Spanish and switch language in settings. P2 scale the interface for small and
high-resolution screens. P3 change key bindings for camera and common actions. With 20 windows open
the frame time increases by less than 1 ms; picking resolves the correct object within one frame at
any zoom; 100% of player-facing text comes from localization files. Out of scope: the content of
specific windows (they arrive with their features)."

## Context

Today the game opens straight into a generated map. The only interface is the window title (date,
speed, frames per second, vehicle count) and keyboard shortcuts documented in code: Space pauses,
1/2/3 choose a speed, F toggles fast forward when unlocked, N adds a vehicle, Delete removes the last
vehicle the player added, F3 shows render statistics, PageUp/PageDown edit the terrain, Esc quits.
There is no way to point at something on the map, no menu, no settings screen, and nothing on screen
but the world. Every later feature (construction, vehicles, stations, finances, saving) needs a
common, fast and localizable interface to show its windows and tools, so this feature builds that
foundation and the first screens that use it. The specific windows of later features are not part of
it.

Feature 004 (map generation and new game) is being built at the same time and provides the map
generation settings and their generator; it does not provide an in-window new-game screen. This
feature provides the screen, designed so it can be connected to 004's settings once both are
available.

## Clarifications

### Session 2026-11-22

- Q: Which font and text rendering approach? → A: an open-licensed font (SIL Open Font License)
  bundled with the game, rasterized at startup into a distance-field atlas so text stays sharp at
  every scale (technology recorded in the plan).
- Q: What money does the top bar show before the economy exists, and in which currency? → A: a fixed
  starting capital read from a data file; the default currency follows the language (US dollar in
  English, euro in Spanish) and the player can choose any major world currency in the settings.
- Q: Does choosing another currency convert the amount? → A: yes, with a fixed exchange rate per
  currency defined in a data file; the simulation counts in one internal unit and conversion is
  display only.
- Q: How do settings changes apply? → A: immediately, and they are saved as soon as they change (no
  Apply/Cancel buttons).
- Q: How do information windows from clicking map objects behave? → A: one window per object, stacked
  in cascade; clicking an object whose window is already open brings that window to the front.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Main menu and in-game HUD (Priority: P1)

The player starts the game and sees a main menu with "New game", "Load game", "Settings" and "Quit".
"New game" opens a new-game screen with the map options and a button to start; the game then shows
the map with a top bar (company money, current date, speed controls: pause, ×1, ×3, ×8 and fast
forward when unlocked) and a toolbar along the screen edge with buttons that open tools and windows.
Esc in game opens a pause menu (resume, settings, back to main menu, quit) instead of closing the
program.

**Why this priority**: it is the frame every other feature plugs into; without it the game is a tech
demo that opens straight into a map.

**Independent Test**: launch the game, use only the mouse to start a new game, change the speed with
the top bar buttons and check the date advances and the speed shown matches; open the pause menu with
Esc and return to the main menu; quit from the menu.

**Acceptance Scenarios**:

1. **Given** the game has just started, **When** the main menu appears, **Then** it shows New game,
   Load game, Settings and Quit, and the world simulation is not running.
2. **Given** the main menu, **When** the player chooses New game and then Start, **Then** the map
   opens with the top bar and the toolbar visible.
3. **Given** a game in progress, **When** the player clicks the ×3 button in the top bar, **Then** the
   simulation runs at ×3 and the button shows as selected; pressing 1 on the keyboard selects ×1 and
   the top bar follows.
4. **Given** fast forward is locked in the settings, **When** the player looks at the top bar,
   **Then** the fast forward control is not offered (or shown disabled with a tooltip saying why).
5. **Given** a game in progress, **When** the player presses Esc, **Then** the pause menu opens and
   the simulation pauses until the player resumes; choosing Quit closes the program.
6. **Given** Load game is chosen, **When** saving and loading do not exist yet, **Then** the entry is
   shown disabled with a tooltip saying it arrives later.

---

### User Story 2 - Hovering and clicking on the map (Priority: P1)

When the player moves the mouse over the map, the object under the cursor is highlighted: a vehicle
if there is one, otherwise a piece of road, otherwise the terrain tile. Clicking selects it and shows
a small information window saying what it is (for example "Tile 812, 455 — grass, height 14",
"Road", "Vehicle 3 021"). Stations and buildings do not exist yet; the mechanism must accept them
when they arrive without changes to the way the player interacts.

**Why this priority**: every tool (building, demolishing, selecting vehicles) depends on knowing
exactly what is under the cursor.

**Independent Test**: on a generated map, hover over terrain, roads and moving vehicles at the
nearest and farthest zoom levels and check the highlight and the information window name the right
object; hover over a window and check nothing on the map is highlighted.

**Acceptance Scenarios**:

1. **Given** the cursor over a terrain tile, **When** the frame is drawn, **Then** that exact tile is
   highlighted, including on slopes and at the farthest zoom.
2. **Given** the cursor over a moving vehicle, **When** the frame is drawn, **Then** the vehicle is
   highlighted rather than the road or tile under it, and the highlight follows the vehicle.
3. **Given** the cursor over a road, **When** the player clicks, **Then** an information window opens
   that names the road and its tile.
4. **Given** the cursor over an interface element, **When** the player clicks, **Then** the
   interface handles the click and nothing on the map is selected.
5. **Given** the cursor outside the map (sky), **When** the frame is drawn, **Then** nothing is
   highlighted.

---

### User Story 3 - Windows and tooltips (Priority: P1)

The interface manages many windows at once: the player opens windows from the toolbar or by clicking
objects, drags them by the title bar, brings one to the front by clicking it, closes it with its
close button or a key, and pins a window so it stays open when closing all others. Windows of the same
kind can be stacked (cascaded) so they do not open exactly on top of one another. Hovering a button
for a short moment shows a tooltip that explains it, including its keyboard shortcut.

**Why this priority**: vehicles, stations and finances will all be windows; the foundation must
handle many of them without becoming slow or messy.

**Independent Test**: open 20 windows (information windows from clicking objects plus toolbar
windows), move, stack, pin and close them, use "close all" and check only pinned windows remain;
hover each top bar and toolbar button and read its tooltip.

**Acceptance Scenarios**:

1. **Given** two overlapping windows, **When** the player clicks the one behind, **Then** it comes to
   the front and receives further input.
2. **Given** a window, **When** the player drags its title bar, **Then** it follows the cursor and
   cannot be dragged completely off screen.
3. **Given** several windows, one of them pinned, **When** the player uses "close all windows",
   **Then** every window except the pinned one closes.
4. **Given** the player opens the same kind of window several times, **When** they appear, **Then**
   each one is offset from the previous so all title bars stay visible.
5. **Given** the cursor rests on a button for about half a second, **When** the tooltip appears,
   **Then** it explains the button and its shortcut and stays inside the screen.
6. **Given** 20 windows are open, **When** the player moves the camera, **Then** the frame rate is not
   visibly affected.

---

### User Story 4 - English and Spanish (Priority: P2)

The player chooses English or Spanish in the settings and every text on screen changes at once,
without restarting: menus, top bar, tooltips, windows, messages. Dates, numbers and money follow the
language's conventions. The first time the game starts it uses the system language when it is
supported, English otherwise; afterwards it remembers the choice.

**Why this priority**: required by the constitution (player text localizable in English and Spanish);
important for the author's audience but the game is playable in one language.

**Independent Test**: switch the language in settings while windows are open and check every visible
text, number and date changes; restart and check the language is kept.

**Acceptance Scenarios**:

1. **Given** the game in English with windows open, **When** the player selects Spanish, **Then**
   every visible text changes to Spanish in the next frame and the layout still fits.
2. **Given** Spanish is selected, **When** the top bar shows money and the date, **Then** they use
   Spanish formatting (for example "1.234.567 €" and "3 ene 1950").
4. **Given** the player selects another currency (for example yen), **When** the top bar shows
   money, **Then** the amount is converted with that currency's fixed rate and shown with its symbol;
   the simulation is not affected.
3. **Given** a text is missing in Spanish, **When** it is shown, **Then** the English text appears
   instead of an empty or broken label, and the test suite reports the missing key.

---

### User Story 5 - Interface scale and display settings (Priority: P2)

The player sets the interface size (for example 75 % to 200 %) so it is comfortable on a small
laptop or a high-resolution monitor, and changes display settings: window size or fullscreen and
vertical sync. The game suggests a scale from the screen's pixel density the first time. All settings
are remembered between sessions.

**Why this priority**: high-resolution and small screens are both common; text that is too small is
unreadable.

**Independent Test**: change the scale from 75 % to 200 % with windows open and check text stays
sharp, nothing overlaps and windows stay on screen; restart and check it is kept.

**Acceptance Scenarios**:

1. **Given** scale 100 %, **When** the player selects 200 %, **Then** every interface element doubles
   in size, text stays sharp, and windows that would leave the screen are moved back inside.
2. **Given** the player switches to fullscreen, **When** they restart the game, **Then** it opens in
   fullscreen.
3. **Given** the window is resized very small, **When** the interface is drawn, **Then** the top bar
   and toolbar remain usable (they may hide less important parts) and no window is unreachable.

---

### User Story 6 - Key bindings (Priority: P3)

The player opens a key bindings list in the settings, sees every action (camera pan, rotate, zoom,
pause, speeds, fast forward, add vehicle, remove vehicle, statistics, close windows, pause menu) with
its key, and changes any of them by clicking the action and pressing a new key. Conflicts are shown,
and a "restore defaults" button exists. The defaults are today's controls.

**Why this priority**: valued by experienced players and for non-QWERTY keyboards, but the defaults
work for most people.

**Independent Test**: rebind "pause" from Space to P, check P pauses and Space no longer does,
restart and check it is kept, restore defaults.

**Acceptance Scenarios**:

1. **Given** the bindings list, **When** the player rebinds an action to a key already used, **Then**
   the conflict is shown and the player can confirm (the other action loses the key) or cancel.
2. **Given** a rebound key, **When** the player restarts, **Then** the new key still works.
3. **Given** custom bindings, **When** the player chooses restore defaults, **Then** every action
   returns to its default key.

---

### Edge Cases

- Text input fields (for example a company name or a seed) receive key presses; shortcuts must not
  fire while typing.
- The window is minimized or resized to zero: the interface must not crash or lay out negative sizes.
- A settings file is missing, unreadable or from an older version: defaults are used for what cannot
  be read, the rest is kept, and the player is told once.
- A binding file refers to an action or key that no longer exists: it is ignored with a log message.
- The cursor is over a window and over a vehicle at the same time: the window wins (User Story 2,
  scenario 4).
- A very long Spanish text in a narrow button: the layout must clip or wrap, never overflow into
  other elements; tests check the longest texts fit the default layout.
- The mouse button is pressed on the map (camera rotate/pan drag) and released over a window: the
  drag ends normally and the window does not receive a click.
- Characters with accents and "ñ", "¿", "¡", "€" render correctly.
- Hundreds of windows opened by mistake: the game stays responsive; windows beyond a reasonable limit
  replace the oldest unpinned ones.

## Requirements *(mandatory)*

### Functional Requirements

**Screens and HUD**

- **FR-001**: The game MUST start on a main menu with New game, Load game, Settings and Quit; the
  simulation MUST NOT advance on the main menu.
- **FR-002**: New game MUST open a new-game screen with the map options available at that time (at
  minimum map size and seed, plus any options feature 004 provides) and a Start button; it MUST be
  built against a small settings description so new options can be added without changing the
  screen's code.
- **FR-003**: Load game MUST be present and disabled with an explanatory tooltip until saving exists.
- **FR-004**: In game, a top bar MUST show the company's money, the current date and the speed
  controls (pause, ×1, ×3, ×8, and fast forward only when unlocked), always reflecting the current
  state whether it was changed by mouse or keyboard.
- **FR-005**: In game, a toolbar MUST offer buttons that open tools and windows; this feature MUST
  provide at least: settings, close all windows, render statistics, and the existing add/remove
  vehicle actions; later features add their own buttons.
- **FR-006**: Esc in game MUST open a pause menu (resume, settings, main menu, quit) that pauses the
  simulation while open; when windows are open, Esc MUST first close the front window.
- **FR-007**: Messages that today go to the window title (command applied/rejected) MUST appear on
  screen as short notifications that fade after a few seconds.

**Map picking**

- **FR-008**: The game MUST determine, every frame, which object is under the cursor with this
  priority: interface element, vehicle, road (or any infrastructure), terrain tile, nothing.
- **FR-009**: The hovered object MUST be highlighted in the same frame; the selection made by a click
  MUST stay highlighted until another selection or Esc.
- **FR-010**: Clicking an object MUST open an information window naming the object and its basic
  facts (tile coordinates, terrain type, height; road; vehicle number). Each object has its own
  window (stacked in cascade); clicking an object whose window is open brings it to the front.
- **FR-011**: The picking mechanism MUST let later features register new kinds of objects (stations,
  buildings, industries) with their own shapes and priority without changing the interaction.
- **FR-012**: Picking MUST be correct at every zoom level, on slopes and on terrain edited during play.
- **FR-012a**: Mouse drags that start on the map (camera rotate and pan) MUST keep working as today
  and MUST NOT select objects; a click is a press and release without significant movement.

**Windows and widgets**

- **FR-013**: Windows MUST be movable by the title bar, kept at least partly on screen, brought to
  the front on click, closable with a close button, and pinnable.
- **FR-014**: A "close all windows" action MUST close every unpinned window.
- **FR-015**: Windows of the same kind MUST open offset from one another (stacked) so their title bars
  remain visible; a window kind MAY declare itself single-instance (reopening brings it to front).
- **FR-016**: The interface MUST provide the reusable elements later features need: panel, window,
  button, label, icon, list, scroll area, tabs, slider, checkbox, drop-down choice, text input and
  tooltip.
- **FR-017**: Every button MUST be able to show a tooltip after the cursor rests on it about half a
  second; tooltips MUST stay inside the screen and include the keyboard shortcut when one exists.
- **FR-018**: The interface MUST consume the mouse and keyboard input it handles, so it never also
  reaches the camera or the map; while a text input has focus, key shortcuts MUST NOT fire.
- **FR-019**: The interface MUST follow one visual theme (colors, corner radius, spacing, fonts)
  defined in a data file, with flat panels with rounded corners matching the voxel style.

**Localization**

- **FR-020**: Every player-facing text MUST come from localization files; English and Spanish MUST be
  complete; a missing translation MUST fall back to English and MUST be detected by tests.
- **FR-021**: Numbers, money and dates MUST be formatted with the selected language's conventions;
  texts MUST support plural forms (for example "1 vehicle" / "2 vehicles").
- **FR-021a**: Money MUST be shown in the player's chosen currency, converted from the simulation's
  internal unit with a fixed exchange rate per currency; currencies (code, symbol, rate, decimals)
  MUST be defined in a data file covering the major world currencies (at least USD, EUR, GBP, JPY,
  CNY, INR, CAD, AUD, CHF, MXN, BRL, KRW, SEK, NOK, PLN, RUB, TRY, ZAR, ARS, CLP, COP). The default
  currency MUST follow the language (USD for English, EUR for Spanish) until the player picks one.
- **FR-022**: Changing the language MUST update every visible text without restarting.
- **FR-023**: The text rendering MUST display all characters needed by English and Spanish
  (including á é í ó ú ü ñ ¿ ¡ € and typographic quotes) and the symbols of the supported
  currencies, using a bundled font whose license allows commercial distribution.

**Scale, display and persistence**

- **FR-024**: The interface scale MUST be adjustable from 75 % to 200 % in steps of 25 %, with text
  staying sharp at every step; the first start MUST suggest a scale from the screen's pixel density.
- **FR-025**: The settings screen MUST offer language, currency, interface scale, fullscreen or
  windowed, vertical sync, the fast-forward unlock and key bindings. Every change MUST apply
  immediately and be saved at once (no Apply/Cancel step).
- **FR-026**: Settings MUST persist in the user's profile folder between sessions, MUST keep the
  existing fast-forward setting, and a missing or partly invalid file MUST fall back to defaults for
  the invalid parts only.

**Key bindings**

- **FR-027**: Actions and their default keys MUST be defined in a data file, not hard-coded; the
  defaults MUST be today's controls.
- **FR-028**: The player MUST be able to rebind any action, see conflicts, and restore defaults;
  custom bindings MUST persist.

**Performance**

- **FR-029**: The interface MUST NOT allocate memory per frame in steady state (no windows opening or
  closing, no text changing) and MUST keep the large-map rendering budgets of feature 003.

### Key Entities

- **Screen**: a full-screen state of the game (main menu, new game, settings, in game, pause menu)
  that decides what is drawn and whether the simulation runs.
- **Window**: a movable panel with a title, a kind, a position, pinned and front/back state, and
  content made of widgets.
- **Widget**: a reusable element (button, label, list, ...) with a size, a layout rule, an optional
  tooltip key and optional action.
- **Theme**: colors, spacing, corner radius and font sizes used by every widget.
- **Localized text**: a key with its text per language, optional plural forms and placeholders.
- **Pick result**: the kind of object under the cursor (none, tile, road, vehicle, later station,
  building) with its identity and position.
- **Player settings**: language, currency, interface scale, display mode, vertical sync,
  fast-forward unlock, key bindings.
- **Currency**: code, symbol, fixed exchange rate from the internal unit, number of decimals.
- **Input action**: a named action (pause, speed ×3, pan left...) with its default and current key.
- **New-game options**: the list of map options the new-game screen shows, each with a label key, a
  kind (number, choice, seed) and a range, filled from what map generation supports.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: With 20 windows open (each with a title, at least five widgets and text), the average
  frame time on the reference hardware increases by less than 1 ms compared with no windows, on the
  4096 × 4096 reference map.
- **SC-002**: The object highlighted is the one under the cursor in 100 % of checks across terrain,
  roads and vehicles at the nearest, a middle and the farthest zoom level, and the highlight appears
  in the same frame the cursor moves.
- **SC-003**: 100 % of player-facing texts come from localization files; an automated check finds no
  literal player text in the interface code, and English and Spanish have the same keys.
- **SC-004**: A first-time player can start a new game, change speed, open and close a window and
  change the language using only the mouse, without instructions, in under 2 minutes.
- **SC-005**: The large-map budgets still hold with the interface visible: 4096 × 4096 at 60 FPS at
  every zoom, no new per-frame allocations.
- **SC-006**: Every interface element responds within one frame of the input (hover highlight,
  pressed state, tooltip timer start).
- **SC-007**: Text is readable and sharp at every interface scale from 75 % to 200 % (checked by hand
  at each step, on the reference machine).

## Assumptions

- The company and its money do not exist in the simulation yet (finances arrive with a later
  feature); until then the top bar shows a fixed starting capital from a data file, in internal
  units converted to the chosen currency. The slot, its formatting and its localization are this
  feature's responsibility; the finances feature replaces the source of the amount.
- Exchange rates are fixed game data, not live rates; they only change how amounts are displayed.
- Stations, buildings and industries do not exist yet; picking supports terrain tiles, roads and
  vehicles now and is designed so later features register more object kinds (FR-011).
- The specific windows of later features (vehicle list, station, finances) are out of scope; this
  feature provides the window system plus the windows it needs itself: settings, key bindings,
  information about the picked object, pause menu, new game.
- The new-game screen offers the map options this branch knows about (map size, seed); it is bound to
  feature 004's settings when both features are merged. Generation progress display belongs to 004
  but uses this feature's widgets once merged.
- Debug terrain edits (PageUp/PageDown) and the statistics overlay (F3) stay as development aids and
  become bindable actions.
- Sound for the interface is out of scope (no audio exists yet).
- Gamepad and touch input are out of scope; mouse and keyboard only.
- The interface is drawn by the game itself on top of the 3D world in the same window; no operating
  system dialogs are used.
- Command-line options keep working for development and the smoke test (for example skipping the main
  menu and starting a game straight away).
