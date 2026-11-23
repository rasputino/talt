# Phase 0 Research: Audio

**Feature**: 019 — Audio | **Date**: 2026-11-23

## R1. Audio library

**Decision**: Use **miniaudio 0.11.26** through the **JAJ.Packages.MiniAudioEx** NuGet package
(version 4.0.1), a managed wrapper with native binaries for win-x64/win-arm64,
linux-x64/linux-arm64 and osx-x64/osx-arm64.

**Rationale**:

- No external runtime installation: the package ships `miniaudioex` native libraries under
  `runtimes/<rid>/native`, so a self-contained publish works on all required platforms, including
  arm64.
- miniaudio is a single-header C library with backends for WASAPI, CoreAudio, ALSA and PulseAudio;
  no device means initialization returns an error, which is exactly the silent fallback the spec
  requires instead of a crash.
- The package exposes both a small high-level API and the low-level engine (`MaEngine`, `MaSound`,
  `MaSoundGroup`, `MaDecoder`) needed for buses, streamed music and per-voice control.
- License: MIT No Attribution / Unlicense (dual), compatible with a closed commercial game.
- Maintenance: 181 commits, 4.0.1 released in 2025 with miniaudio 0.11.26, and an active issue
  tracker. Risk of a single-maintainer package is contained because only `MiniAudioBackend.cs`
  depends on it and a silent fallback exists.
- Size: 2.09 MB package; per-platform managed assembly is small and only the native library for the
  target RID is published.

**Alternatives considered**:

| Alternative | Why not |
|---|---|
| Silk.NET.OpenAL + OpenAL Soft | The NuGet package provides bindings only; distributing and locating OpenAL Soft on Windows, Linux and macOS (including arm64) adds a native packaging and maintenance burden. |
| SoundFlow | MIT and cross-platform, but its miniaudio codec factory in 1.4.2 supports WAV/MP3/FLAC only, not OGG, and it has no positional audio; we would need a separate Vorbis decoder plus our own panning. |
| Hexa.NET.MiniAudio | MIT and complete, with all required RIDs, but exposes a very large auto-generated 1:1 API and requires the extra `HexaGen.Runtime` dependency; more surface to audit for little benefit. |
| MiniAudioEx (`MiniAudioEx.NET`) | An earlier package name of the same project; the current package id is `JAJ.Packages.MiniAudioEx`. |
| SoLoudSharp | The bundled native builds cover win-x64, linux-x64 and osx x64/arm64, but not win-arm64/linux-arm64, which the spec requires. |
| NAudio / CSCore | Windows-only. |
| ManagedBass / BASS | Commercial license required for closed distribution. |
| Own WASAPI/CoreAudio/ALSA backends | Several platform APIs, COM/interop and device hot-plug code; more risk than a proven single-header library. |

## R2. Assets: all original, generated and versioned

**Decision**: All effects, ambience and music are synthesized by a committed file-based tool
(`tools/audio/generate-audio.cs`) and the resulting WAV/FLAC files are committed. The tool is a .NET
10 file-based app (`#:package`) using **EggEncoder 4.114.5** (MIT) only to encode the original PCM
into FLAC; it is not referenced by the game or shipped.

**Rationale**: this guarantees originality (no samples, loops or melodies copied from another
game), makes regeneration reproducible, and keeps the third-party surface to a build-time encoder.
Short one-shot effects stay WAV (small, no decode latency); ambience loops and music are FLAC and
streamed. The miniaudio 0.11.26 build shipped with MiniAudioEx decodes WAV and FLAC but rejects
Vorbis/OGG, so FLAC is the streamed format; it is lossless and the resulting assets are about 10 MB.
Music is composed from original chord progressions, bass lines, arpeggios and percussion written in
the generator, not transcribed from any existing work.

Asset budget: five effects (UI click, success notification, warning notification, construction,
vehicle engine loop), three ambience loops (countryside, water, town) and at least two music
tracks. Total is about 10 MB, mostly the two lossless FLAC music tracks.

## R3. Event bridge: simulation stays untouched

**Decision**: Audio subscribes on the client side to existing simulation signals:

- `GameSimulation.CommandProcessed` (already raised during `Tick`) → command success/rejection and
  construction-type commands by command type name.
- `InGameScreen` terrain-edit result → construction cue only when at least one voxel changed.
- `InGameScreen.ShowMessage` / `GameSession.Message` → interface notification (no double sound for
  command results, which use `CommandProcessed`).
- The camera and `GameSession.Instances`/`InstanceIds` snapshots → vehicle and ambience scene.

No `Talt.Core` file is changed for audio. Future features (006 construction, 008 towns, 009
industries, 010 stations, 015 rail) only need to expose their completion events to the same adapter
or use command type names; the mixer does not change.

**Alternative considered**: add a generic audio event stream to `GameSimulation`. Rejected: it
would put an audio concern in Core and require fingerprint/replay decisions for no gameplay value.

## R4. Mixer architecture

**Decision**: two layers.

1. **`AudioMixer` (pure managed, no device)**: owns bus gains, voice selection and per-voice
   gain/pan, ambience cross-fade, music playlist state and the one-shot drop policy. It is fully
   unit-testable and benchmarkable.
2. **`MiniAudioBackend` (native adapter)**: creates a `MaEngine` and four `MaSoundGroup` buses
   (master → endpoint; music/effects/ambient → master), a fixed pool of `MaSound` voices, and maps
   the mixer's per-voice gain/pan/pitch to native calls. Only this file references MiniAudioEx.

Buses include master, so muting is one native volume change and does not lose the individual
settings. Voices are non-spatial at the native level; the mixer computes distance attenuation and
stereo pan from the camera basis, which is cheap, deterministic and easy to test.

**Voice budget**: 8 vehicle loops, 4 one-shot effect voices and 2 ambience cross-fade voices, all
allocated at startup. Vehicle selection scans the already-built instance buffer once per frame and
keeps the best eight by `distance - priorityBonus`; the selected vehicle and the vehicle under the
cursor get a large priority bonus. A small hysteresis (a challenger must be meaningfully closer)
avoids rapid voice churn when two vehicles are at similar distances. One music voice is separate.

**Ambience**: a coarse per-game map (128×128 cells) is built once from `MapData`: water where the
cell is mostly water, town near a town site, countryside otherwise. Sampling the camera target is
O(1) per slow tick (about twice a second), and two ambience voices cross-fade over 1.5 s. This
avoids walking `Sites` or map tiles per frame.

## R5. Streamed music playlist

**Decision**: one dedicated `MaSound` initialized with `ma_sound_flags.stream`; tracks are FLAC
files from `audio.json`. `AudioSystem` polls `IsPlaying` every 250 ms; when the current track has
ended, the next track starts on the same voice. The playlist loops after the last track. Missing or
corrupt files are skipped with one warning, and if all tracks fail music is disabled for the
session. Music starts in the main menu and is never restarted by screen changes. The cost of opening
and starting the next track is measured; if it exceeds the frame budget it moves off the render
thread or reuses a prepared voice.

## R6. No device / device failure

**Decision**: `MiniAudioBackend.TryCreate` catches initialization failures and returns null;
`AudioSystem` then uses `NullAudioBackend`, which keeps time and state but produces no sound. The
real backend also catches exceptions from per-sound initialization; a failed asset disables that
cue, not the game. A `--no-audio` client switch forces the silent backend for CI and manual tests.
The console gets one line with the reason; repeated frames produce no repeated errors.

**Rationale**: CI runners commonly have no audio device, and some Linux containers have no ALSA or
PulseAudio. Audio is optional comfort, so silence is a valid success state.

## R7. No allocations and measurement

**Decision**:

- No LINQ, no string formatting/list enumeration and no per-frame object creation in the update
  path. Scene data is passed in a `readonly ref struct` with spans; arrays and voice objects are
  allocated at startup or when a game starts.
- Music track changes are measured separately. If opening/starting the next FLAC does not fit the
  frame budget, the next track is prepared off the render thread or on a second music voice while
  the previous one finishes.
- `AudioSystem` measures its own update with `Stopwatch.GetTimestamp`, keeps an exponential average
  and exposes active voice counts; `GameWindow` shows `audio <ms> ms / <voices> voices` in the F3
  title and in the console statistics line.
- A BenchmarkDotNet benchmark runs the pure mixer with the reference vehicle count and 8 active
  voices. A xUnit test warms up the mixer and asserts zero managed allocations over 1 000 updates.
- Results are recorded in `docs/ARCHITECTURE.md`.

## R8. Settings and UI

**Decision**: add `MasterVolume`, `MusicVolume`, `EffectsVolume`, `AmbientVolume` (0–1, defaults
0.8/0.5/0.8/0.65) and `Muted` (false) to `PlayerSettings` (version 3), with per-property validation
and defaults. The Settings window gets an "Audio" heading, four slider rows with a percentage and a
mute checkbox; every change applies immediately and is saved by the existing mechanism. Muting sets
the master bus to zero and leaves the four stored volumes untouched. All labels and tooltips go to
both `Strings.en.json` and `Strings.es.json`.

**Alternative considered**: a separate Audio settings window. Rejected: feature 005's settings
screen is the established place and the spec explicitly puts the controls there.

## R9. Concurrent features 006 and 009

**Decision**: put all new logic in new files; touch existing files only with small, isolated hooks:

- `GameWindow`: field, initialization, one per-frame call, one settings call, one dispose call.
- `GameSession`: one event raised from `OnCommandProcessed` and detached on `Detach`.
- `PlayerSettings`: four properties and their read cases.
- `Screens/Windows.cs`: audio rows in `SettingsWindow.Create`; the shared `Slider` gains keyboard
  focus and arrow-key adjustment (FR-018).
- `Screens/InGameScreen.cs`: one construction call.
- `UiContext`/`Button`: one activation callback.
- `IScreenHost`: one property; update `FakeHost` and every test double.

The plan expects a merge with feature 006 (same HUD/window/settings files) and feature 009 (already
modifying `GameWindow`, `GameSession`, `InGameScreen`, `Windows.cs`, strings and README). New files
avoid conflicts; edits are kept to a few adjacent lines so a three-way merge is mechanical.

## R10. Testing strategy

- **Pure tests**: gain math, bus multiplication, mute, pan/attenuation, voice budget and priority,
  hysteresis, ambience transition, playlist advance/loop/skip, construction mapping, settings
  round-trip and validation.
- **Fake backend**: records voices, volumes and played clips; simulates end-of-track and failures.
  No test creates a `MiniAudioBackend` or opens a device.
- **Allocation test**: warm up, then assert zero allocated bytes over repeated updates.
- **Localization tests**: existing coverage test walks the Settings window; new keys must resolve.
- **Benchmark**: pure mixer with 50 000 vehicles and 8 active voices.
- **Manual**: launch the client with sound and ask the developer what they hear; launch with
  `--no-audio` to confirm silence and normal play.

## Risks and mitigations

| Risk | Mitigation |
|---|---|
| Native library missing on an unusual Linux distribution/container | Silent fallback; CI does not depend on a device; native assets are copied by NuGet. |
| FLAC decoder unavailable in a miniaudio build | Verified: the shipped native library decodes the committed FLAC files; if a future build loses it, fall back to WAV and record the decision. |
| Single-maintainer audio package | Thin adapter behind `IAudioBackend`; only one file to replace; license and version pinned centrally. |
| Merge conflicts with 006/009 | New files, minimal hooks, re-read files before editing, run the full build/test after integration. |
| Music/effects too loud or clipping | Per-cue gains in data, master headroom, voice budget and a limiter test; all gains tunable without code. |
| Per-frame GC from accidental LINQ/strings | Allocation test plus benchmark; review update path carefully. |
