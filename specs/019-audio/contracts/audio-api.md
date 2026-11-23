# Audio API Contract

**Feature**: 019 — Audio | **Scope**: internal to `Talt.Client`

This is an internal contract: the game has no public API or network surface for audio. It defines the
small interfaces that keep the native audio library replaceable, the data file the game validates,
and the hooks existing client code uses.

## Native boundary

Only `MiniAudioBackend.cs` references `JAJ.Packages.MiniAudioEx` or native code. Everything else
works against these interfaces, which are implemented by `MiniAudioBackend` and `NullAudioBackend`,
and by `FakeAudioBackend` in tests.

```csharp
internal enum AudioBus { Master, Music, Effects, Ambient }

internal interface IAudioVoice : IDisposable
{
    bool IsPlaying { get; }
    bool Play(string filePath, bool loop, bool stream);
    void Stop();
    void SetVolume(float volume); // 0..1 effective gain
    void SetPan(float pan);       // -1 left .. +1 right
    void SetPitch(float pitch);   // 1 = normal
}

internal interface IAudioBackend : IDisposable
{
    bool IsAvailable { get; }
    string? UnavailableReason { get; }
    IAudioVoice? CreateVoice(AudioBus bus);
    void SetBusVolume(AudioBus bus, float volume); // 0..1, already includes mute
    void Update(); // device house-keeping; may be a no-op
}
```

Rules:

- `CreateVoice` returns `null` when the backend cannot create a voice; the caller treats that voice
  as silent.
- `Play` returns `false` when the file cannot be decoded or the voice is unavailable; the caller
  disables that cue/track and logs once.
- No method allocates in steady state. Paths are cached by the caller and only changed when a
  cue/track changes.
- `SetVolume` receives the fully multiplied gain (bus × definition gain × distance/zoom), so the
  backend does no mixing policy.
- Disposing the backend stops the engine and frees native voices/groups. It must be safe after a
  failed initialization.

## Orchestration

```csharp
internal sealed class AudioSystem : IDisposable
{
    static AudioSystem TryCreate(PlayerSettings settings, string assetRoot, Action<string> log);
    static AudioSystem Silent { get; } // test/fallback instance, never opens a device
    bool IsAvailable { get; }
    AudioStatistics Statistics { get; }

    void ApplySettings(PlayerSettings settings);
    void SetGame(MapData map);   // builds AmbientMap; stops previous game sounds
    void ClearGame();            // stops vehicle/ambience voices, keeps music

    void PlayInterfaceClick();
    void PlayCommandFeedback(bool succeeded, bool isConstruction);
    void PlayConstruction();

    void Update(in AudioScene scene); // once per frame, no allocations
}
```

`AudioStatistics` exposes `LastUpdateMilliseconds`, `AverageUpdateMilliseconds`, `ActiveVoices`,
`VoiceBudget`, `MusicTrack` and `UnavailableReason` for the F3 title and tests.

## Client hooks

| Existing code | Hook | Effect |
|---|---|---|
| `UiContext` / `Button` | `Action? Activated` invoked once per mouse activation | `AudioSystem.PlayInterfaceClick` |
| `GameSession` | `event Action<bool, bool>? CommandFeedback` (succeeded, construction) raised from `OnCommandProcessed` | success/warning/construction cue |
| `InGameScreen` | after a terrain edit that changed at least one voxel | `AudioSystem.PlayConstruction` |
| `GameWindow` | `OnLoad`, `OnUpdate`, `EnterGame`, `LeaveGame`, `ApplySettings`, `Dispose` | create, update, set/clear game, volumes, dispose |
| `IScreenHost` | `AudioSystem Audio { get; }` | lets `InGameScreen` play construction |
| `PlayerSettings` | five new properties | persisted volumes and mute |
| `Screens/SettingsWindow` | four sliders + mute checkbox; sliders focusable and adjustable with arrow keys | immediate apply through `IScreenHost.ApplySettings` |

`GameSession` decides whether a command is construction with a documented case-insensitive type-name
classifier (for example `Build`, `Construct`, `Terraform`, `Road`, `Track`, `Rail`, `Station`,
`Demolish`, `Bulldoze`, `Clear`, `Land`). AddVehicle/RemoveVehicle do not match. No simulation code
changes; when feature 006 adds construction commands their names are classified automatically. A
successful construction command is treated as "the world changed" until construction commands expose
a changed flag; this is the documented proxy for FR-003.

## Client command line

`--no-audio` forces the silent backend for CI, no-device machines and manual silence tests. It is
accepted anywhere in the argument list and does not skip the menu by itself.

## Data file `Audio/Data/audio.json`

The file is embedded through the existing `**/Data/*.json` rule and loaded with
`DataFiles.Load<AudioDefinition>("audio.json")`, then validated. Example:

```json
{
  "version": 1,
  "voiceBudget": 8,
  "oneShotVoices": 4,
  "ambientVoices": 2,
  "maxVehicleDistanceTiles": 220,
  "hysteresisTiles": 4,
  "cues": {
    "interfaceClick": { "file": "effects/interface-click.wav", "bus": "effects", "gain": 0.35, "priority": 2, "loop": false, "stream": false },
    "commandApplied": { "file": "effects/command-applied.wav", "bus": "effects", "gain": 0.45, "priority": 3, "loop": false, "stream": false },
    "commandRejected": { "file": "effects/command-rejected.wav", "bus": "effects", "gain": 0.45, "priority": 3, "loop": false, "stream": false },
    "construction": { "file": "effects/construction.wav", "bus": "effects", "gain": 0.55, "priority": 4, "loop": false, "stream": false },
    "vehicleEngine": { "file": "effects/vehicle-engine.flac", "bus": "effects", "gain": 0.55, "priority": 1, "loop": true, "stream": true }
  },
  "ambient": {
    "country": { "file": "ambient/country.flac", "bus": "ambient", "gain": 0.55, "priority": 0, "loop": true, "stream": true },
    "water": { "file": "ambient/water.flac", "bus": "ambient", "gain": 0.55, "priority": 0, "loop": true, "stream": true },
    "town": { "file": "ambient/town.flac", "bus": "ambient", "gain": 0.50, "priority": 0, "loop": true, "stream": true }
  },
  "music": {
    "tracks": ["music/open-road.flac", "music/daybreak.flac"],
    "gain": 0.8,
    "stream": true
  },
  "vehicle": {
    "fullVolumeDistanceTiles": 18,
    "rolloff": 1.4,
    "zoomReference": 80,
    "minZoomGain": 0.2,
    "behindGain": 0.65,
    "panWidth": 0.8
  }
}
```

Validation errors name the field and value, for example:

- `Data file 'audio.json': cues.vehicleEngine.file 'effects/engine.flac' does not exist under 'audio/'.`
- `Data file 'audio.json': voiceBudget must be between 1 and 32, got 64.`
- `Data file 'audio.json': ambient must define country, water and town (missing town).`

## Asset layout

Assets are copied to the output and publish directories, preserving the relative paths used by
`audio.json`:

```text
audio/
├── effects/interface-click.wav
├── effects/command-applied.wav
├── effects/command-rejected.wav
├── effects/construction.wav
├── effects/vehicle-engine.flac
├── ambient/country.flac
├── ambient/water.flac
├── ambient/town.flac
├── music/open-road.flac
└── music/daybreak.flac
```

The asset generator is `tools/audio/generate-audio.cs`; every file is original and its provenance is
listed in `README.md`.
