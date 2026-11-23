# Data Model: Audio

**Feature**: 019 — Audio | **Date**: 2026-11-23

All types below live in `Talt.Client` except `PlayerSettings`, which already exists there. Nothing
in this feature enters `Talt.Core` or the simulation state.

## Enumerations

### `AudioBus`

| Value | Meaning |
|---|---|
| `Master` | Final gain applied to every sound. |
| `Music` | Streamed playlist only. |
| `Effects` | Interface, command, construction and vehicle sounds. |
| `Ambient` | Countryside, water and town beds. |

### `AudioCue`

| Value | Data file key | Played when |
|---|---|---|
| `InterfaceClick` | `interfaceClick` | A button is activated by the mouse. |
| `CommandApplied` | `commandApplied` | A command succeeds. |
| `CommandRejected` | `commandRejected` | A command is rejected. |
| `Construction` | `construction` | A terrain edit changes voxels, or a construction-type command succeeds. |
| `VehicleEngine` | `vehicleEngine` | A vehicle is selected as one of the eight audible engines. |

### `AudioEnvironment`

`Country`, `Water`, `Town`. Selected from the coarse ambient map for the camera target.

## Entities

### `SoundDefinition` (from `Audio/Data/audio.json`)

| Field | Type | Rules |
|---|---|---|
| `File` | string | Relative to the audio root (`audio/`), non-empty, no `..`, file must exist. |
| `Bus` | `AudioBus` | Must not be `Master`. |
| `Gain` | float | 0–2; multiplied by the voice's distance/zoom gain and the bus volume. |
| `Priority` | int | 0–100; higher wins when one-shot voices are scarce. |
| `Loop` | bool | True for engine and ambience; false for one-shots. |
| `Stream` | bool | True for ambience, engine and music; false for short one-shots. |

### `AudioDefinition` (validated root object)

| Field | Type | Rules |
|---|---|---|
| `Version` | int | Must be 1. |
| `VoiceBudget` | int | 1–32; default 8. |
| `OneShotVoices` | int | 1–16; default 4. |
| `AmbientVoices` | int | At least 2. |
| `MaxVehicleDistanceTiles` | float | > 0; default 220. |
| `HysteresisTiles` | float | 0–20; default 4. |
| `Cues` | map `AudioCue` → `SoundDefinition` | Every cue present exactly once. |
| `Ambient` | map `AudioEnvironment` → `SoundDefinition` | All three present. |
| `Music` | `MusicDefinition` | At least one track. |
| `Vehicle` | `VehicleMixDefinition` | Distance and zoom curves. |

### `MusicDefinition`

| Field | Type | Rules |
|---|---|---|
| `Tracks` | string[] | At least one existing FLAC file. |
| `Gain` | float | 0–2. |
| `Stream` | bool | Must be true (music is streamed). |

### `VehicleMixDefinition`

| Field | Type | Rules |
|---|---|---|
| `FullVolumeDistanceTiles` | float | ≥ 0; default 18. |
| `Rolloff` | float | > 0; default 1.4. |
| `ZoomReference` | float | > 0; camera distance at which vehicle gain is 1. |
| `MinZoomGain` | float | 0–1; default 0.2. |
| `BehindGain` | float | 0–1; default 0.65. |
| `PanWidth` | float | 0–1; default 0.8. |

### `AudioScene` (per-frame input, `readonly ref struct`)

| Field | Type | Meaning |
|---|---|---|
| `InGame` | bool | A game is loaded. |
| `Paused` | bool | Simulation is paused (music continues). |
| `CameraPosition` | `Vector3` | Camera eye position. |
| `CameraTarget` | `Vector3` | Camera target on the ground, used for ambience. |
| `CameraForward` | `Vector3` | Normalized view direction. |
| `CameraRight` | `Vector3` | Normalized right vector. |
| `CameraDistance` | float | Orbit distance, used for zoom gain. |
| `Vehicles` | `ReadOnlySpan<VehicleInstance>` | Positions of drawn vehicles. |
| `VehicleIds` | `ReadOnlySpan<int>` | Stable ids matching `Vehicles`. |
| `SelectedVehicleId` | int | -1 when none. |
| `HoveredVehicleId` | int | -1 when none. |
| `Ambients` | `AmbientMap?` | Coarse environment map for the current game. |
| `DeltaSeconds` | float | Frame time, clamped by the audio layer. |

### `AmbientMap` (per game, built once)

| Field | Type | Meaning |
|---|---|---|
| `Width`, `Depth` | int | 128 × 128 cells (or map size if smaller). |
| `Cells` | byte[] | `AudioEnvironment` per cell. |
| `TilesPerCell` | float | Map tiles per cell. |

`EnvironmentAt(float x, float z)` returns the cell's environment with O(1) work and no allocation.
Built from `GeneratedGame.Map` (`MapData`): a cell is `Water` when more than 35% of its sampled tiles
have water, `Town` when it is within the reserved town site's free radius, otherwise `Country`.

### `VoiceSlot` (inside the mixer)

| Field | Type | Meaning |
|---|---|---|
| `VehicleId` | int | -1 for an idle slot. |
| `Distance` | float | Last frame's distance, for hysteresis. |
| `Gain` | float | Current target gain after all curves. |
| `Pan` | float | -1 (left) to +1 (right). |
| `Active` | bool | Slot currently has a native voice. |

There are exactly `VoiceBudget` vehicle slots. One-shot slots store the cue, remaining priority and
age; ambience slots store the current and next environment.

The native mixer has a master sound group with music, effects and ambient child groups. Child bus
gains therefore hold only their own volume (`BusGain`), and the total a voice receives is
`MasterGain × BusGain` (`EffectiveGain` in tests). Mute sets `MasterGain` to zero without changing
the four stored settings.

### `MusicPlaylist`

| Field | Type | Meaning |
|---|---|---|
| `Tracks` | string[] | Ordered track paths. |
| `CurrentIndex` | int | Track being played. |
| `Playing` | bool | A track is loaded and playing. |
| `HasStarted` | bool | Guards against treating the first frames as "ended". |

State transitions: `Stopped → Playing(first track) → Playing(next) → … → Playing(first)`, with
`Skipped` for missing/corrupt files and `Disabled` when every track fails.

### `AudioSettings` (fields added to `PlayerSettings`, version 3)

| Field | Type | Default | Validation |
|---|---|---|---|
| `MasterVolume` | float | 0.80 | 0–1, else default + load problem. |
| `MusicVolume` | float | 0.50 | 0–1, else default + load problem. |
| `EffectsVolume` | float | 0.80 | 0–1, else default + load problem. |
| `AmbientVolume` | float | 0.65 | 0–1, else default + load problem. |
| `Muted` | bool | false | JSON boolean, else default + load problem. |

Muting never overwrites the four volumes; the effective master gain is
`Muted ? 0 : MasterVolume`. The settings file keeps `Version = 3`; older files load with defaults
for the new fields.

## Relationships

```text
PlayerSettings ──► AudioSystem (applies bus gains)
AudioDefinition ──► AudioSystem (cues, ambience, playlist, budgets)
GeneratedGame.MapData ──► AmbientMap ──► AudioScene
GameSimulation.CommandProcessed ──► GameSession.CommandFeedback ──► AudioSystem
InGameScreen terrain edit ──► AudioSystem.PlayConstruction
Button activation ──► UiContext.Activated ──► AudioSystem.PlayInterfaceClick
Camera + GameSession.Instances ──► AudioScene ──► AudioMixer ──► IAudioBackend
MusicPlaylist ──► IAudioVoice (stream)
```

## Validation and error handling

- `AudioDefinition.Load` uses `DataFiles.Load<AudioDefinition>("audio.json")`, then validates every
  rule above and throws `InvalidDataException` naming the field and value. `AudioSystem.TryCreate`
  catches that, logs one line and returns a silent system, so bad audio data cannot stop the game.
- Each cue's file is checked for existence at startup. A missing or unreadable cue is logged once
  and disabled; missing music tracks are skipped.
- A native initialization failure (`ma_engine_init` returns a non-success result) yields
  `NullAudioBackend`; no exception escapes.
- Volumes are clamped to 0–1 on load and when set; NaN and infinity are rejected.
