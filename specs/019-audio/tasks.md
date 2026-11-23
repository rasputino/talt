# Tasks: Audio

**Input**: Design documents from `/specs/019-audio/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md),
[data-model.md](data-model.md), [contracts/audio-api.md](contracts/audio-api.md)

**Tests**: Required by the project constitution; every user story includes tests written before or
with its implementation. Tests use a fake audio backend and never open a real device.

**Organization**: Tasks are grouped by user story so each story can be implemented and validated
independently.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1, US2, US3 or US4 (maps to `spec.md`)
- Every task includes the exact file path(s) it changes.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Dependency, data file, original assets and the data model that every story uses.

- [X] T001 Add `JAJ.Packages.MiniAudioEx` 4.0.1 to `Directory.Packages.props`, reference it from
  `src/Talt.Client/Talt.Client.csproj`, and copy `Assets/Audio/**` to output/publish under `audio/`
  (preserve relative paths).
- [X] T002 [P] Create `src/Talt.Client/Audio/Data/audio.json` with cues, three ambiences, two music
  tracks and the vehicle mix, following `contracts/audio-api.md` and
  `data-model.md` (`AudioDefinition.Version`, `AudioDefinition.VoiceBudget`,
  `AudioDefinition.OneShotVoices`, `AudioDefinition.AmbientVoices`,
  `AudioDefinition.MaxVehicleDistanceTiles`, `AudioDefinition.HysteresisTiles`,
  `SoundDefinition.File`, `SoundDefinition.Bus`, `SoundDefinition.Gain`,
  `SoundDefinition.Priority`, `SoundDefinition.Loop`, `SoundDefinition.Stream`,
  `MusicDefinition.Tracks`, `MusicDefinition.Gain`, `MusicDefinition.Stream`,
  `VehicleMixDefinition.FullVolumeDistanceTiles`, `VehicleMixDefinition.Rolloff`,
  `VehicleMixDefinition.ZoomReference`, `VehicleMixDefinition.MinZoomGain`,
  `VehicleMixDefinition.BehindGain`, `VehicleMixDefinition.PanWidth`).
- [X] T003 [P] Create `tools/audio/generate-audio.cs` (file-based app, `#:package EggEncoder@4.114.5`)
  that synthesizes all original WAV/FLAC assets deterministically; run it to produce
  `src/Talt.Client/Assets/Audio/effects/*.wav`, `ambient/*.flac` and `music/*.flac`. Then verify with
  a throwaway `MaEngine`/`MaSound` program that the shipped native backend decodes each generated
  FLAC (duration > 0); if a codec is missing, switch those assets to WAV and record why in
  `research.md`.
- [X] T004 [P] Add `AudioBus`, `AudioCue` and `AudioEnvironment` enums plus `SoundDefinition`,
  `MusicDefinition`, `VehicleMixDefinition` and `AudioDefinition` in
  `src/Talt.Client/Audio/AudioCue.cs` and `src/Talt.Client/Audio/AudioDefinition.cs`, matching
  `data-model.md` names and field types.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The native boundary, validated data loading, pure mixer, settings and client hooks that
all four stories need. **No user story can be validated until this phase is complete.**

- [X] T005 [P] Define `IAudioBackend` and `IAudioVoice` in
  `src/Talt.Client/Audio/IAudioBackend.cs` and implement `NullAudioBackend` in
  `src/Talt.Client/Audio/NullAudioBackend.cs` exactly as in `contracts/audio-api.md`.
- [X] T006 [P] Implement `AudioDefinition.Load` validation in
  `src/Talt.Client/Audio/AudioDefinition.cs` using `DataFiles.Load<AudioDefinition>("audio.json")`:
  `AudioDefinition.Version`, `AudioDefinition.VoiceBudget`, `AudioDefinition.OneShotVoices`,
  `AudioDefinition.AmbientVoices`, `AudioDefinition.MaxVehicleDistanceTiles`,
  `AudioDefinition.HysteresisTiles`, enum completeness of `AudioDefinition.Cues`,
  `AudioDefinition.Ambient`, `AudioDefinition.Music` and `AudioDefinition.Vehicle`, relative paths
  without `..`, and file existence under the audio root. Add tests in
  `tests/Talt.Client.Tests/Audio/AudioDefinitionTests.cs` covering every validation bullet in
  `data-model.md` ("Validation and error handling").
- [X] T007 [P] Implement `AudioAssets` in `src/Talt.Client/Audio/AudioAssets.cs`: locate
  `AppContext.BaseDirectory/audio`, resolve and cache cue/track paths, and report missing files
  once.
- [X] T008 Implement `MiniAudioBackend` in `src/Talt.Client/Audio/MiniAudioBackend.cs`: `MaEngine`,
  four `MaSoundGroup` buses, fixed voice pool, `Play/Stop/SetVolume/SetPan/SetPitch`, and a
  non-throwing `TryCreate` that returns null with a reason when the engine cannot initialize.
  This is the only file that may reference `JAJ.Packages.MiniAudioEx`.
- [X] T009 Implement `FakeAudioBackend` in `tests/Talt.Client.Tests/Audio/FakeAudioBackend.cs`:
  records voices, gains and played clips, advances playback time, can simulate failed files and
  failed initialization.
- [X] T010 [P] Implement `AudioMixer` in `src/Talt.Client/Audio/AudioMixer.cs`: effective bus gain
  (`Muted ? 0 : MasterVolume` times the other bus), definition gain, distance attenuation, zoom
  gain, behind-camera gain, pan, one-shot priority/drop policy and `VoiceSlot` selection with
  hysteresis (`data-model.md: VoiceSlot.VehicleId`, `VoiceSlot.Distance`, `VoiceSlot.Gain`,
  `VoiceSlot.Pan`, `VoiceSlot.Active`; `VehicleMixDefinition.FullVolumeDistanceTiles`,
  `VehicleMixDefinition.Rolloff`, `VehicleMixDefinition.ZoomReference`,
  `VehicleMixDefinition.MinZoomGain`, `VehicleMixDefinition.BehindGain`,
  `VehicleMixDefinition.PanWidth`). No LINQ, strings or allocations in `Update`.
- [X] T011 [P] Implement `AmbientMap` in `src/Talt.Client/Audio/AmbientMap.cs`: build a 128×128
  environment grid from `MapData` (water > 35%, town within free radius, else country) with
  `EnvironmentAt` O(1); add `AudioEnvironment` sampling tests in
  `tests/Talt.Client.Tests/Audio/AmbientMapTests.cs` (`data-model.md: AmbientMap.Width`,
  `AmbientMap.Depth`, `AmbientMap.Cells`, `AmbientMap.TilesPerCell`).
- [X] T012 [P] Implement `MusicPlaylist` in `src/Talt.Client/Audio/MusicPlaylist.cs`: ordered
  tracks, current index, start/advance/loop, skip missing/corrupt tracks and `Disabled` when all
  fail (`data-model.md: MusicPlaylist.Tracks`, `MusicPlaylist.CurrentIndex`,
  `MusicPlaylist.Playing`, `MusicPlaylist.HasStarted`). Tests in
  `tests/Talt.Client.Tests/Audio/MusicPlaylistTests.cs` written before the implementation.
- [X] T013 Implement `AudioSystem` in `src/Talt.Client/Audio/AudioSystem.cs`: `TryCreate` backend
  or silent fallback plus a `Silent` instance for tests; apply settings; `SetGame`/`ClearGame`;
  `PlayInterfaceClick`, `PlayCommandFeedback`, `PlayConstruction`; `Update(in AudioScene)`;
  `AudioStatistics`. Uses the mixer, ambience map and playlist and never allocates in `Update`.
- [X] T014 Add `AudioScene` (`src/Talt.Client/Audio/AudioScene.cs`) and build it in
  `GameWindow.OnUpdate` from the camera and `GameSession.Instances`/`InstanceIds`; add the
  `_audio` field, creation in `OnLoad`, `ApplySettings` call, dispose and one console line when
  audio is unavailable (`data-model.md: AudioScene.InGame`, `AudioScene.Paused`,
  `AudioScene.CameraPosition`, `AudioScene.CameraTarget`, `AudioScene.CameraForward`,
  `AudioScene.CameraRight`, `AudioScene.CameraDistance`, `AudioScene.Vehicles`,
  `AudioScene.VehicleIds`, `AudioScene.SelectedVehicleId`, `AudioScene.HoveredVehicleId`,
  `AudioScene.Ambients`, `AudioScene.DeltaSeconds`).
- [X] T015 Add audio settings to `PlayerSettings` (version 3): `MasterVolume`, `MusicVolume`,
  `EffectsVolume`, `AmbientVolume`, `Muted` with defaults 0.8/0.5/0.8/0.65/false and per-property
  validation (`data-model.md: AudioSettings.MasterVolume`, `AudioSettings.MusicVolume`,
  `AudioSettings.EffectsVolume`, `AudioSettings.AmbientVolume`, `AudioSettings.Muted`). Extend
  `tests/Talt.Client.Tests/PlayerSettingsTests.cs`
  for defaults, valid values, out-of-range values, NaN and old-file loading.
- [X] T016 Add `--no-audio` to `src/Talt.Client/Program.cs` and `ClientOptions` in
  `src/Talt.Client/GameWindow.cs`; it forces `NullAudioBackend` and never skips the menu by itself.
- [X] T017 Add the `UiContext.Activated` callback and invoke it once from
  `src/Talt.Client/Ui/Widgets/Button.cs` when a mouse activation happens; wire
  `AudioSystem.PlayInterfaceClick` in `GameWindow.OnLoad`.
- [X] T018 Add `event Action<bool, bool>? CommandFeedback` to `src/Talt.Client/GameSession.cs`,
  raise it from `OnCommandProcessed`, detach it in `Detach`, and add
  `AudioCommandClassifier.IsConstruction(string typeName)` with the documented construction
  substrings plus tests in `tests/Talt.Client.Tests/Audio/AudioCommandClassifierTests.cs`.
  Subscribe/unsubscribe in `GameWindow.EnterGame`/`LeaveGame` without changing simulation code.
- [X] T019 Add `AudioSystem Audio { get; }` to `IScreenHost` in `src/Talt.Client/Screens/Screens.cs`
  and implement it on `GameWindow`; update `FakeHost` in
  `tests/Talt.Client.Tests/Ui/UiTestKit.cs` and every other `IScreenHost` implementer/test double
  to return `AudioSystem.Silent`; call `Audio.PlayConstruction()` from
  `src/Talt.Client/Screens/InGameScreen.cs` only when a terrain edit returns at least one voxel.
- [X] T020 Add all audio localization keys to `src/Talt.Client/Localization/Strings.en.json` and
  `Strings.es.json` (audio heading, four volume labels, volume percentage, mute label/tooltip,
  audio tooltips and the console "no audio" message if localized; console-only text may stay in
  English).

**Checkpoint**: The project builds with the package and assets, the data file validates, the silent
backend works, settings round-trip and the hooks exist. All four stories can now proceed.

---

## Phase 3: User Story 1 — Interface and construction feedback (Priority: P1) 🎯 MVP

**Goal**: Clicks, command notifications and construction actions produce the right sound, with
silent fallback.

**Independent Test**: With the fake backend, simulate button activation, successful/rejected
commands, terrain edits that change nothing and terrain edits that change voxels; assert exactly
one correct cue per action and none when nothing changed. Launch with `--no-audio` and confirm the
game is silent and playable.

### Tests for User Story 1

- [X] T021 [P] [US1] Add `tests/Talt.Client.Tests/Audio/AudioSystemTests.cs` cases: one click per
  activation, hover/drag/disabled produce none, applied command → `CommandApplied`, rejected →
  `CommandRejected`, construction command → `Construction`, terrain edit with zero voxels → none,
  terrain edit with voxels → one `Construction`, and all of it with `NullAudioBackend`.
- [X] T022 [P] [US1] Add `tests/Talt.Client.Tests/Audio/AudioQuietFallbackTests.cs`:
  unavailable backend starts, accepts every cue and update without throwing or logging repeatedly.

### Implementation for User Story 1

- [X] T023 [US1] Connect `UiContext.Activated`, `GameSession.CommandFeedback` and the terrain-edit
  path to `AudioSystem`; make one-shot voice selection honour `SoundDefinition.Priority` and drop
  the oldest/lowest-priority sound when all one-shot voices are busy
  (`data-model.md: SoundDefinition.Priority`, `AudioDefinition.OneShotVoices`). A successful
  construction command is the proxy for "changed" until construction commands report a changed flag.
- [X] T024 [US1] Add the silent-fallback console message and the `--no-audio` path, and make every
  `AudioSystem` entry point safe when the backend is null (FR-014).
- [X] T025 [US1] Run the audio tests, `dotnet test tests/Talt.Client.Tests --filter
  "FullyQualifiedName~Audio"` and a `--no-audio` launch; record the result in `tasks.md`.

**Checkpoint**: US1 is playable and testable on its own; it is the MVP.

---

## Phase 4: User Story 2 — World sounds that follow the camera (Priority: P1)

**Goal**: Eight positional vehicle engines and a camera-driven ambience bed, quieter when zoomed
out.

**Independent Test**: Feed the mixer a synthetic scene with vehicles at different distances and
sides, a selection and extreme zoom; assert the eight chosen ids, gains/pan monotonicity, priority
wins, ambience environment and cross-fade. In the client, listen near/far/left/right and over
country/water/town.

### Tests for User Story 2

- [X] T026 [P] [US2] Add `tests/Talt.Client.Tests/Audio/AudioMixerTests.cs`: eight-voice cap,
  nearest-first selection, selected/hovered priority, hysteresis, distance attenuation, zoom gain,
  behind-camera gain, pan sign and clamping, and bus gain multiplication/mute.
- [X] T027 [P] [US2] Add `tests/Talt.Client.Tests/Audio/AmbientTransitionTests.cs`: country/water/
  town selection from the map, cross-fade over 1.5 s, no abrupt cut, and no allocation while
  sampling.
- [X] T028 [P] [US2] Add `tests/Talt.Client.Tests/Audio/AudioSceneTests.cs`: building the scene
  from a small `GameSession` keeps ids aligned with positions and clamps delta time.

### Implementation for User Story 2

- [X] T029 [US2] Implement vehicle voice management in `AudioMixer`/`AudioSystem`: select up to
  `VoiceBudget`, assign/reuse slots, start/stop native voices, update gain/pan/pitch every frame,
  and stop all vehicle voices in `ClearGame` (FR-005, FR-006, FR-019).
- [X] T030 [US2] Implement ambience in `AudioSystem`: two ambient voices, environment sampled twice
  per second from `AmbientMap`, 1.5 s cross-fade, zoom gain and bus volume (FR-004, FR-007).
- [X] T031 [US2] Build the `AudioScene` as a `readonly ref struct` in
  `src/Talt.Client/Audio/AudioScene.cs` and fill it in `GameWindow.OnUpdate` without allocating;
  pass camera position/forward/right/distance, instance spans, selected/hovered ids and delta time.
- [X] T032 [US2] Call `SetGame(game.Map)` in `GameWindow.EnterGame` and `ClearGame()` in
  `LeaveGame`; stop vehicle/ambience voices but keep music when returning to the menu (FR-019).
- [X] T033 [US2] Run the mixer/scene tests and measure the per-frame cost with `--stats` on the
  reference scenario; record the numbers for T040.

**Checkpoint**: US2 adds the living world; US1 remains independently testable.

---

## Phase 5: User Story 3 — Original background music (Priority: P2)

**Goal**: Original music from the main menu through the game, streamed, playlist with loop and
independent volume.

**Independent Test**: With the fake backend, start in the menu, end a track, assert the next and
then the first; change music volume and assert only the music bus changes; set it to zero and
assert effects/ambience still play.

### Tests for User Story 3

- [X] T034 [P] [US3] Add `tests/Talt.Client.Tests/Audio/MusicPlaylistTests.cs` (extend T012):
  starts in the menu, advances on end, loops after the last track, skips a missing/corrupt file,
  disables after all fail, preserves position across a menu/game screen change, applies the
  music bus volume independently, and a fake-time soak runs at least 30 simulated minutes (SC-005)
  with no manual intervention.
- [X] T035 [P] [US3] Add an asset existence/size test that every track in `audio.json` exists under
  `src/Talt.Client/Assets/Audio/` and is a non-empty FLAC file.

### Implementation for User Story 3

- [X] T036 [US3] Implement streamed music playback in `AudioSystem`/`MusicPlaylist`: one music
  voice, `stream: true`, start in `OnLoad`, poll at most every 250 ms, advance/loop, and skip
  failures with one warning (FR-008, FR-009). Measure the cost of opening/starting the next track;
  if it does not fit the frame budget, prepare the next track off the render thread or on a second
  music voice (FR-016).
- [X] T037 [US3] Ensure starting a game, opening Settings, pausing and returning to the menu never
  restart the track; only mute/volume 0 silence it (US3 scenarios 1 and 4).
- [X] T038 [US3] Run the playlist tests and the manual music check from `quickstart.md`; record the
  result in `tasks.md`.

**Checkpoint**: US3 adds music without affecting US1/US2.

---

## Phase 6: User Story 4 — Separate volume and mute controls (Priority: P2)

**Goal**: Four sliders and mute in Settings, immediate and persisted, localized, keyboard/mouse
usable.

**Independent Test**: Build `SettingsWindow.Create` with a fake host, move each slider and toggle
mute; assert `PlayerSettings` values and `AudioSystem` bus gains; reload settings from JSON and
assert persistence; switch language and assert labels resolve.

### Tests for User Story 4

- [X] T039 [P] [US4] Add `tests/Talt.Client.Tests/Audio/AudioSettingsTests.cs`: defaults, slider
  callbacks update the right property, mute does not change stored volumes, unmute restores them,
  save/load round-trip, and out-of-range/NaN handling.
- [X] T040 [P] [US4] Extend `tests/Talt.Client.Tests/Localization/LocalizationCoverageTests.cs`
  so the new Settings rows and tooltips are covered by the existing walk, and assert that every
  new key resolves in both English and Spanish with no `[key]` fallback.
- [X] T041 [P] [US4] Add a `SettingsWindow` construction test (in `tests/Talt.Client.Tests/Audio/`
  or `ScreensTests.cs`) that finds four sliders and a mute checkbox and asserts their labels exist
  in English and Spanish.

### Implementation for User Story 4

- [X] T042 [US4] Add the "Audio" section to `SettingsWindow.Create` in
  `src/Talt.Client/Screens/Windows.cs`: four slider rows with a percentage label, a mute checkbox,
  tooltips, and callbacks that set the settings property and call `host.ApplySettings()`
  (FR-010, FR-011, FR-018). Make `Slider` focusable and adjustable with the left/right (and
  up/down) arrow keys, set focus on click, and add a UI test for keyboard adjustment in
  `tests/Talt.Client.Tests/Ui/UiFrameworkTests.cs`.
- [X] T043 [US4] Apply the settings in `GameWindow.ApplySettings` by calling
  `_audio.ApplySettings(Settings)` once; verify mute sets the effective master to zero and unmute
  restores the stored volumes.
- [X] T044 [US4] Run the settings tests, restart the client manually, change every volume, close
  and reopen, and confirm persistence and translations.

**Checkpoint**: All four stories work; US4 is the last player-facing control.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [X] T045 [P] Add `benchmarks/Talt.Benchmarks/AudioBenchmarks.cs` (pure mixer, 50 000 vehicles,
  8 active voices, 4 one-shots, 2 ambience) and run it with `--job short`.
- [X] T046 [P] Add the allocation test (`GC.GetAllocatedBytesForCurrentThread` after warm-up,
  1 000 updates, zero bytes) in `tests/Talt.Client.Tests/Audio/AudioAllocationTests.cs`.
- [X] T047 [P] Update `docs/ARCHITECTURE.md` with the audio architecture, data flow, silent
  fallback, package justification, benchmark method and measured frame cost; update
  `docs/VISION.md` only if a product decision changed (music scope is the only candidate).
- [X] T048 [P] Update `README.md` third-party section: `JAJ.Packages.MiniAudioEx` (MIT/Unlicense,
  miniaudio 0.11.26), `EggEncoder` (MIT, build-time tool), the original asset provenance and
  the `--no-audio` option. Add the option to the README command-line list.
- [X] T049 Run `dotnet build`, `dotnet test --filter "Category!=Gpu"` and
  `pwsh ci/check-fingerprints.ps1`; fix any regression and confirm the fingerprint goldens are
  unchanged (audio must not touch the simulation).
- [X] T050 Run the quickstart listening checks: launch the client in a terminal for the developer
  and ask in Spanish what they hear (menu music, clicks, notifications, construction, ambience,
  engines, volume/mute, translations). Record the answers and any tuning in `tasks.md`.
- [X] T051 Mark completed tasks `[X]`, update the README/ARCHITECTURE numbers, and ask the developer
  whether to commit (never commit without explicit permission).

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on T001–T004; blocks all user stories.
- **US1 (Phase 3)**: depends on T005–T020; can be the MVP.
- **US2 (Phase 4)**: depends on T005–T020; can run after or in parallel with US1 (different files,
  but both touch `AudioSystem` and `GameWindow`, so sequence the integration).
- **US3 (Phase 5)**: depends on T005–T020 and T003 (music assets).
- **US4 (Phase 6)**: depends on T005–T020 and T015 (settings model).
- **Polish (Phase 7)**: depends on all selected stories.

### User Story Dependencies

- US1 has no dependency on US2/US3/US4.
- US2 has no dependency on US1 content, but both modify `AudioSystem`; implement US1 first or merge
  carefully.
- US3 depends on the music assets from T003 but not on US1/US2.
- US4 depends on the settings model T015 but not on the other stories.

### Within Each Story

Tests first (T021/T022, T026–T028, T034–T035, T039–T041), then implementation, then the story's
validation task. The fake backend T009 is required by every test.

### Parallel Opportunities

- T002, T003, T004 are independent of each other.
- T005, T006, T007, T010, T011, T012 can be written in parallel by different agents.
- US2 tests, US3 tests and US4 tests can be written in parallel once the fake backend exists.
- T045, T046, T047, T048 are independent polish tasks.

---

## Parallel Example: User Story 2

```bash
# Tests in parallel:
Task: "Add AudioMixerTests.cs for voice budget and gain/pan"
Task: "Add AmbientTransitionTests.cs for environment and cross-fade"
Task: "Add AudioSceneTests.cs for scene building"

# Implementation:
Task: "Implement vehicle voice management in AudioMixer/AudioSystem"
Task: "Implement ambience in AudioSystem"
```

---

## Implementation Strategy

### MVP First (User Story 1)

1. Setup + Foundational.
2. US1 tests and implementation.
3. Validate with `--no-audio` and the fake backend; launch for the developer.
4. This delivers useful feedback even if the world/music work is not finished.

### Incremental Delivery

1. Setup + Foundational → build and silent run.
2. US1 → clicks, notifications, construction.
3. US2 → ambience and vehicle engines.
4. US3 → music.
5. US4 → volume controls in Settings.
6. Polish → benchmark, docs, full CI, manual listening.

### Merge strategy for features 006 and 009

All new logic is in `src/Talt.Client/Audio` and new test files. Existing-file edits are limited to
the small hooks listed in T014–T019 and T042–T043. Re-read each existing file immediately before
editing (parallel agents change `GameWindow.cs`, `GameSession.cs`, `InGameScreen.cs`,
`Screens/Windows.cs`, `PlayerSettings.cs`, strings and README), and run the full build after each
merge.

---

## Notes

- `[P]` tasks touch different files and can run in parallel.
- No task changes `Talt.Core`, `Talt.Headless` or the fingerprint goldens.
- Every test runs without a GPU, window, timing dependency or real audio device.
- A task is only checked when its code is built and its tests pass.

---

## Implementation results (2026-11-23)

- Build, `dotnet test -c Release --filter "Category!=Gpu"` (174 client, 370 core, 23 headless
  tests) and `pwsh ci/check-fingerprints.ps1` pass; the goldens are unchanged.
- `AudioBenchmarks` (50 000 vehicles, 8 voices, short job): 0.285 ms per update, 0 B allocated.
- Client `--stats` on a 128 × 128 map with 100 vehicles: audio 0.012–0.056 ms per frame,
  10 active voices, 0 B allocated per frame. `--no-audio` starts silently and plays normally.
- The shipped miniaudio build decodes WAV and FLAC but not Vorbis; all streamed assets are FLAC
  encoded by EggEncoder. The native decode of every generated FLAC was verified with a throwaway
  `MaEngine`/`MaSound` program.
- `dotnet publish -r win-x64` includes the ten audio assets under `audio/` and `miniaudioex.dll`.
- Hand test (developer, Windows, 2026-11-23): music and clicks in the menu, music continues in the
  game, ambience and engines follow the camera, construction, and all four volume controls plus
  mute work. Small engine crackles were reported; fixed by keeping each vehicle on the same voice
  slot when its rank changes, ramping volume/pan, correcting sample-loop cross-fades and adding a
  louder construction cue. The developer confirmed it is much better and acceptable; a faint
  background residue was left for a later mixing pass.
- Commit: asked the developer, who chose a `019-audio` branch and a feature commit.