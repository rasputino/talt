# Quickstart: Audio (019)

**Feature**: 019 — Audio

## Prerequisites

- .NET 10 SDK.
- A working audio output device for the listening checks (all automated checks work without one).
- Windows, Linux or macOS, x64 or arm64.
- Optional, only to regenerate assets: network access for the `EggEncoder` package used by the
  file-based generator.

## Build and automated checks

```bash
dotnet build
dotnet test --filter "Category!=Gpu"
dotnet test tests/Talt.Client.Tests --filter "FullyQualifiedName~Audio"
```

Expected: build clean with warnings as errors; all tests pass; no test opens an audio device. The
allocation test reports zero allocated bytes per audio update after warm-up.

Benchmark:

```bash
dotnet run --project benchmarks/Talt.Benchmarks -c Release -- --filter "*Audio*" --job short
```

Expected: the audio update stays well under 0.5 ms for the reference vehicle count and contributes
no measurable frame-time regression. Record the result in `docs/ARCHITECTURE.md`.

## Manual listening checks (developer)

Launch the client from a terminal and leave it running for the developer:

```bash
dotnet run --project src/Talt.Client -c Release
```

The developer checks, in Spanish:

1. **Menu and music**: original music starts in the main menu and does not restart when starting a
   game or opening Settings; it moves to the next track when one ends.
2. **Interface**: clicking menu entries, toolbar buttons, speed controls and window buttons gives a
   short click; hovering gives nothing.
3. **Commands**: adding a vehicle sounds a positive notification; a rejected action (for example
   removing with no player-added vehicles) sounds a different warning.
4. **Construction**: PageUp/PageDown over terrain sounds construction only when the terrain changes.
5. **World**: pan/rotate/zoom over countryside, water and a town and listen to the ambience change
   smoothly; move near vehicles and check engines follow distance and left/right; zoom fully out and
   check the mix becomes quieter without clipping.
6. **Settings**: move master, music, effects and ambient sliders and hear each change only its bus;
   enable mute and check everything goes silent; disable mute and check the previous balance
   returns; restart the game and check the values persist.
7. **Language**: switch to Spanish and check all audio labels and tooltips are translated.

## Silent fallback checks (no device)

```bash
dotnet run --project src/Talt.Client -c Release -- --no-audio
```

Expected: the game starts and plays normally in silence, with one console line saying audio is
disabled. No repeated errors and no crash. On a machine or container with no audio device, the same
behaviour is expected without the switch, plus the backend's reason in the console.

## Asset regeneration (optional)

```bash
dotnet run tools/audio/generate-audio.cs
```

The generator writes the WAV/FLAC files under `src/Talt.Client/Assets/Audio/`. It is deterministic:
the same source produces the same music and sound effects. Review `README.md`'s third-party section
when adding a new asset.

## Data and settings checks

- `src/Talt.Client/Audio/Data/audio.json` is validated at startup. Break a value on purpose (for
  example `voiceBudget: 64`) and check that the game starts silently with one clear console error
  naming the field, instead of crashing.
- Delete a music file and check that the playlist skips it with one warning and continues.
- Edit the settings file (see `PlayerSettings.DefaultPath`) with an out-of-range volume and check
  that the game loads the default for it, reports it once and keeps the other values.

## Done when

- [ ] `dotnet build` and `dotnet test --filter "Category!=Gpu"` pass.
- [ ] The audio tests pass with a fake backend and no device.
- [ ] The benchmark is under budget and recorded in `docs/ARCHITECTURE.md`.
- [ ] The developer confirms the listening checks above.
- [ ] `--no-audio` and a machine without a device both keep the game playable and silent.
- [ ] README lists the audio library and asset provenance.
