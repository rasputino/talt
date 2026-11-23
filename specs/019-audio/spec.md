# Feature Specification: Audio

**Feature Branch**: `019-audio`

**Created**: 2026-11-23

**Status**: Draft

**Input**: User description: "Audio. Sound brings the world to life: ambient sounds, vehicles, construction and interface feedback, plus original music. P1: interface sounds (clicks, notifications) and construction sounds. P1: vehicles and ambient sounds (towns, countryside, water) depending on what the camera shows, quieter when zoomed out. P2: original background music plays as a playlist with volume control. P2: master, music, effects and ambient volumes separately. Audio never causes stutter; CPU cost under 0.5 ms per frame. All sounds and music are original or properly licensed for commercial use."

## Context

The game currently has no sound at all. Feature 005 built the interface (main menu, in-game top bar and toolbar, windows, settings) and feature 012's synthetic traffic moves vehicles on the map, but nothing is heard when the player clicks a button, opens a window, builds, or watches vehicles move.

This feature adds sound to the client only. It listens to what the game already reports (player actions, commands processed by the simulation, map and camera state) and turns it into interface feedback, world ambience, vehicle sounds and original background music. The simulation must not know that audio exists and must produce exactly the same results with or without it.

Construction tools are being built at the same time in feature 006 and stations, towns and industries will arrive in features 008–010. The audio layer is designed so their sounds plug into the same cues without reworking the mixer. Until those features exist, construction feedback is triggered by the actions the game already exposes (terrain edits and construction commands) and the sound set is limited to what those actions can report.

The game must run on Windows, Linux and macOS (x64 and arm64), including computers and continuous-integration machines with no audio output device. There, the game must keep running, finish normally and simply be silent.

## Clarifications

### Session 2026-11-23

- Q: Should music play everywhere or only inside a running game? → A: It plays from the main menu and continues through a running game; changing screens never restarts it.
- Q: How many vehicle sounds may play at once, and how are they chosen? → A: At most eight, chosen by proximity; the selected vehicle or the one under the cursor takes priority when it is within earshot.
- Q: What should construction feedback cover before feature 006's construction tools are merged? → A: Terrain edits and any successful construction-type command that exists now or arrives with feature 006 (roads, demolition, terraforming, rail, stations), mapped by command type so new construction actions sound without changing the mixer.
- Q: What volumes should the game start with the first time? → A: Audio enabled and balanced at master 80%, music 50%, effects 80% and ambient 65%, with mute off.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Interface and construction feedback (Priority: P1)

The player clicks buttons and menu entries and hears a short, quiet click. Notifications (a command was applied or rejected, an action is not possible) are accompanied by a short distinctive sound. When the player edits or demolishes terrain, and when construction tools arrive, the action has its own sound. If the computer has no audio output, the game stays silent and everything else works normally.

**Why this priority**: immediate feedback makes the interface feel responsive and tells the player that an action was accepted or rejected, especially when the result is off-screen or subtle.

**Independent Test**: with audio enabled, click menu and toolbar buttons, apply and reject a command, raise and lower terrain, and check that each produces the intended feedback; set master volume to zero or mute and check that no sound is heard while the interface still works; start the game on a machine without an output device and check that it runs and stays silent.

**Acceptance Scenarios**:

1. **Given** the game is running with audio available, **When** the player clicks a button, **Then** a short interface click is heard once per click and not while merely hovering.
2. **Given** a command is applied, **When** the notification appears, **Then** a positive notification sound is heard; when a command is rejected, a different, neutral warning sound is heard.
3. **Given** the terrain editing action changes tiles, **When** the action completes, **Then** a construction sound is heard; if nothing changed, no construction sound is played.
4. **Given** the master volume is zero or mute is on, **When** the player uses the interface, **Then** no sound is heard and no sound-related error is shown.
5. **Given** the computer has no audio output device, **When** the game starts and is played, **Then** the game runs normally in silence, with a single non-blocking notice in the console and no repeated errors.

---

### User Story 2 - World sounds that follow the camera (Priority: P1)

The player hears the world around the camera: a quiet ambient bed that suits what is in view (countryside, water or a town), and engines of nearby vehicles. Sounds that are farther away are quieter, and sounds to the left or right of the view are heard on that side. Zooming out lowers vehicle and ambient detail so the world becomes a quiet background rather than a wall of noise.

**Why this priority**: it is what makes the map feel alive and gives the player a sense of where the action is without looking at every vehicle.

**Independent Test**: in a running game, move the camera over countryside, water and a town and check that the ambient bed changes; move near and far from moving vehicles and check that vehicle sounds get louder or quieter and move from one side to the other; zoom fully out and check that the mix becomes quieter and never clips or stutters.

**Acceptance Scenarios**:

1. **Given** the camera is over the countryside, **When** the view settles, **Then** a countryside ambience is heard; over water, a water ambience replaces it; near a town, a town ambience is heard, with smooth transitions rather than abrupt cuts.
2. **Given** vehicles are moving near the camera, **When** the camera moves or the vehicles pass, **Then** their engine sounds follow their position and distance, and vehicles behind the camera are not incorrectly heard in front.
3. **Given** the camera is zoomed far out, **When** the view is drawn, **Then** vehicle and ambient volumes decrease with distance and the total loudness stays controlled, with no more than eight simultaneous vehicle sounds.
4. **Given** one vehicle is selected or under the cursor, **When** it is within earshot, **Then** its engine sound is favoured over other vehicles at a similar distance.

---

### User Story 3 - Original background music (Priority: P2)

The player hears original background music that plays as a playlist, moves from one track to the next without gaps or clicks, loops when the last track ends, and never overpowers effects and ambience. The music has its own volume control and can be turned off without affecting other sounds.

**Why this priority**: music sets the mood and makes long sessions pleasant, but the game is fully playable without it.

**Independent Test**: start the game, let the first track finish, check that the next one starts, then that the playlist returns to the first track; change the music volume while it plays and check that only the music becomes louder or quieter; set it to zero and check that effects and ambience still work.

**Acceptance Scenarios**:

1. **Given** music is enabled, **When** the game starts in the main menu, **Then** an original track plays, the next track follows when it ends, the playlist loops after the last one, and starting a game or changing screens does not restart the music.
2. **Given** music is playing, **When** the player changes the music volume, **Then** the change is heard immediately and does not alter effects or ambience.
3. **Given** the music volume is zero, **When** the player plays, **Then** no music is heard and effects and ambience are unaffected.
4. **Given** the game is paused or a menu is open, **When** music is playing, **Then** music continues unless the player chose otherwise, and resuming never restarts it from the beginning.

---

### User Story 4 - Separate volume and mute controls (Priority: P2)

The player opens Settings and finds master, music, effects and ambient volumes plus a mute control. Every change applies at once and is remembered the next time the game starts. The controls are usable with the mouse and keyboard, and their labels are in the player's language.

**Why this priority**: players have different environments and preferences; without these controls many would mute the whole game at the operating-system level.

**Independent Test**: change each slider, close and reopen the game, and check that the values are kept; set mute, check that all sound stops, unmute and check that the previous volumes return; switch language and check that all audio labels are translated.

**Acceptance Scenarios**:

1. **Given** the Settings screen, **When** the player moves a volume slider, **Then** the corresponding bus changes during the same session and the value is saved.
2. **Given** saved volumes, **When** the game is restarted, **Then** the same volumes and mute state are restored.
3. **Given** the audio is muted, **When** the player unmutes, **Then** the volumes from before muting are restored, not reset to defaults.
4. **Given** the game is in Spanish, **When** the player opens Settings, **Then** every audio label and tooltip is shown in Spanish, with no untranslated text.

### Edge Cases

- No output device, a device that disappears while the game runs, or a device that refuses the requested format: the game keeps running, goes silent, reports the situation once in the console and recovers automatically if a device becomes available again.
- The player changes volume while a sound is starting or a track is changing: no click, no clipping and no lost setting.
- Sounds requested faster than they can play (for example holding a button or a flood of notifications): the oldest/lowest-priority sounds are dropped, and the interface is never blocked.
- Music track missing, unreadable or corrupt: the playlist skips it and continues, logging one warning; the rest of the audio still works.
- A vehicle is removed or the game returns to the main menu while its sound plays: the sound stops and nothing references the removed vehicle.
- The window is minimized, unfocused or the game is fast-forwarding: audio keeps a stable cost, does not accumulate delayed sounds and does not stutter the interface.
- Extreme settings (all volumes at maximum at once): the mix stays within the output range and does not distort.
- Audio must never affect simulation results, saves, replays or fingerprints.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The client MUST play a short interface click once for each button activation (mouse or keyboard), and MUST NOT play it for hovering, dragging or a disabled button.
- **FR-002**: The client MUST play distinct notification sounds for successful and rejected player actions, at most once per notification.
- **FR-003**: The client MUST play a construction sound when a construction-related action (terrain edit or a successful construction-type command) changes the world, and MUST NOT play it when the action changes nothing. For commands, a successful result is the proxy for "changed" until construction commands report a changed flag.
- **FR-004**: The client MUST play ambient beds that correspond to the camera's surroundings (countryside, water, town) and cross-fade between them when the surroundings change.
- **FR-005**: The client MUST play engine sounds for nearby vehicles, positioned by their place in the view (left/right and distance), and MUST cap the number of simultaneous vehicle sounds at eight.
- **FR-006**: When choosing which vehicle sounds to play, the client MUST prefer closer vehicles and MUST give priority to a selected vehicle or to the vehicle under the cursor within earshot.
- **FR-007**: The client MUST reduce vehicle and ambient loudness as the camera zooms out, so that the overall mix stays quiet and intelligible.
- **FR-008**: The client MUST play original background music as a playlist that advances automatically, loops after the last track and changes tracks without audible gaps or clicks.
- **FR-009**: Music MUST be streamed rather than fully loaded, so that adding longer tracks does not increase memory use proportionally.
- **FR-010**: The client MUST provide master, music, effects and ambient volume settings plus a mute control, each applying immediately and independently; the first-start defaults are master 80%, music 50%, effects 80% and ambient 65%, with mute off.
- **FR-011**: The client MUST persist all audio settings and restore them on the next start; mute MUST preserve the volumes in use before muting.
- **FR-012**: The client MUST apply all player-facing audio labels through the existing English and Spanish localization files, with no hard-coded text.
- **FR-013**: The audio layer MUST live entirely in the client and MUST NOT be referenced by the simulation; enabling, disabling or failing audio MUST NOT change simulation results, saves, replays or fingerprints.
- **FR-014**: The client MUST degrade to silence without failing when there is no audio device, when initialization fails, or when the device disappears; a failure MUST be reported once in the console and the player MUST still be able to play.
- **FR-015**: Audio processing per frame MUST stay under 0.5 ms of CPU on the reference hardware and MUST NOT allocate memory in the steady state.
- **FR-016**: Audio MUST NOT block or delay the frame loop: sound loading, decoding, device changes and playlist changes happen away from the frame's critical path or within the per-frame budget; a music track change MUST be measured and must not cause a visible or audible gap.
- **FR-017**: All shipped sounds and music MUST be original works created for this game or carry a license compatible with a closed commercial game; their origin and license MUST be listed in the project's third-party documentation.
- **FR-018**: The audio settings MUST be usable with the mouse and the keyboard (focus a volume control and change it with the arrow keys), respect the interface scale, and show their current value.
- **FR-019**: The client MUST stop sounds that belong to a game when leaving that game, and MUST NOT leak sounds, devices or threads across games.
- **FR-020**: The audio layer MUST expose a small, documented set of cues that later features (construction, stations, towns, industries, rail) can trigger without changing the mixer.

### Key Entities *(include if feature involves data)*

- **Sound cue**: a named event the game can report (interface click, notification, warning, construction, vehicle engine, ambience, music) with its category and default importance.
- **Audio bus**: one of master, music, effects or ambient; carries a volume between silent and full and a mute state.
- **Voice**: one playing sound instance, with its bus, position in the world (optional), loudness, stereo placement and priority; the mixer chooses which voices are active within the voice budget.
- **Audio scene**: the per-frame information the audio layer reacts to: camera position and orientation, zoom, the visible/nearby vehicles with their positions and importance, the current surroundings and whether a game is running.
- **Audio settings**: master, music, effects and ambient volumes and the mute state, persisted between sessions.
- **Music playlist**: an ordered list of original tracks, with the current track, its playback state and the rule that advances to the next track.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: With audio available, 100% of button activations, applied/rejected commands and world-changing construction actions produce the intended sound, and no unintended sound is heard on hover or on actions that change nothing.
- **SC-002**: On the reference scenario, audio adds less than 0.5 ms of CPU time per frame and causes zero managed allocations per frame in steady state, measured over at least 60 seconds of play.
- **SC-003**: The game runs and remains playable on Windows, Linux and macOS (x64 and arm64) with no audio device, producing silence and no crash, hang or repeated error output.
- **SC-004**: The player can set master, music, effects and ambient volumes and mute from the Settings screen; all values survive a restart, and muting/unmuting restores the previous balance.
- **SC-005**: The original music playlist plays for at least 30 minutes without manual intervention and moves between tracks without an audible gap, click or error.
- **SC-006**: All shipped sounds and music are original or carry a documented license compatible with commercial distribution, listed in the third-party documentation.
- **SC-007**: Every audio label and tooltip appears in English and Spanish, with no untranslated or hard-coded text.

## Assumptions

- Audio is optional comfort, not gameplay information: no rule, objective or achievement depends on hearing a sound.
- The game ships with a small, curated sound set (interface, construction, three ambiences, one vehicle engine family and at least two music tracks); richer content arrives with later features.
- The player's operating system and sound device work normally; unusual configurations (exclusive-mode devices, network audio, virtual devices) are handled by the silent fallback rather than by dedicated support.
- Music uses the same output device as effects and ambience; choosing an output device is out of scope.
- Sounds are mixed in stereo; full 3D/HRTF audio and surround output are out of scope for version 1.
- Volume changes are applied by the game's own mixer, not by changing the operating-system volume.
- Audio assets ship with the game; downloading music or sounds at runtime is out of scope.
- Feature 006's construction tools, feature 008's towns and features 009–010's industries and stations will use the cue interface when they arrive; until then, the cues are driven by existing actions and no simulation feature is modified for audio.
