# Transit & Logistics Tycoon (TALT)

[![CI](https://github.com/rasputino/talt/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/rasputino/talt/actions/workflows/ci.yml)

An original transport management game with a voxel art style and support for very large maps.

## Status

Early stage project: a technical prototype renders a voxel terrain with a road grid and
thousands of simulated vehicles, using WebGPU.

- [Vision](docs/VISION.md): goals, scope and technology.
- [Architecture](docs/ARCHITECTURE.md): how the code is organized.
- [Spec Kit guide](docs/SPECKIT_GUIDE.md): roadmap and prompts to implement the full game with Spec Kit.

## Building and running

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download) and a GPU with Vulkan,
Metal or DirectX 12 support.

```
dotnet build
dotnet test
dotnet run --project src/Talt.Client -c Release -- [chunks-per-side] [vehicles] [seed] [options]
dotnet run --project tools/Talt.Headless -c Release -- run --ticks 10000
dotnet run --project tools/Talt.Headless -c Release -- run --ticks 4000 --map-text T2-512x512-s7-r50-h28-w25-l7
dotnet run --project benchmarks/Talt.Benchmarks -c Release -- --filter * --job short
dotnet run tools/audio/generate-audio.cs   # regenerate the original audio assets (optional)
```

Example: `dotnet run --project src/Talt.Client -c Release -- 16 20000` shows a 512 × 512 map with
20 000 vehicles.

Without game arguments the client opens on the main menu (new game, settings, quit). Any game
argument (map size, vehicles, seed, `--record`, `--print-fingerprint-at`, `--play`) starts a game
straight away.

New map from the command line (seed, size, roughness, height, water, sea level; the settings text is printed
so a map can be shared): `dotnet run --project src/Talt.Client -c Release -- --map 1024x512 --seed 7 --water 35`.

Client options: `--start-year <year>`, `--language en|es` and `--ui-scale <0.75–2>` (for this run),
`--unlock-fast-forward` / `--lock-fast-forward` (saved in your settings), `--record [file]`,
`--print-fingerprint-at <tick>`, `--stats` (render statistics from the start), `--graphics low|medium|high` (one run), `--measure-effects [seconds]` (graphics effect costs, then exit), `--day-night`, `--time-of-day <0-1>`, `--camera <x,z>`, `--zoom <distance|max>`
(initial camera distance), `--ui-test-windows <n>` (opens sample windows to measure the interface),
`--no-audio` (starts silently, for CI and machines without a sound device), `--road-grid <spacing>`
(debug: lays a full road grid every `<spacing>` tiles, to check the frame rate with a dense network).
Towns (feature 008): `--town-names mixed|english|spanish` and `--town-size small|normal|large` (also on the
new-game screen), and `--town-service <0–100>` (debug: counts that percentage of every town's passengers and
mail as transported each month, so towns grow before stations exist).
Stations (feature 010): `--station-service <0–100>` (debug: every station's cargo counts as served and that
percentage of the waiting cargo is collected every five days, so cargo flows before vehicles exist) and
`--seed-stations <n>` (debug: builds up to n bays beside industries and town roads at the start, as recorded
commands). The headless runner takes the same two options and prints a station summary.
Finance (feature 011): `--sandbox` (the company never goes bankrupt) and `--inflation` (prices and payments rise
over the years) and `--infinite-money` (money never limits you; it can also be turned on during play from the
finance window and never turned off), all also on the new-game screen, and `--station-service-pays` (debug: the cargo collected by
`--station-service` is paid as a delivery over 40 tiles, so income shows before vehicles exist). The headless
runner takes the same options and prints the cash, loan, company value, income and expenses.
Road vehicles (feature 012): `--seed-vehicles <n>` (debug: bus bays on a 40-tile lattice, a depot beside each, lines
between neighbouring connected bays and up to n buses, at most eight per line, as recorded commands; turns infinite
money on) and `--check-overlaps` (debug: checks after every tick that no two vehicles overlap in a lane; the client
pauses with a message, the headless runner stops with exit code 1). The headless runner also takes `--road-grid
<spacing>` and prints a vehicle summary and the average tick of the run's second half.
In game: build a depot (J) beside a road, open it to buy a bus or truck, press "New line" in the vehicle window and
click your stations in order (Esc ends); the vehicle starts. F4 lists the lines.

Railways (feature 015): `--seed-trains <n>` (debug: racetrack loops with two stations, one-way path signals, a depot
and up to eight trains each, levelling sloping land under a loop first; turns infinite money on; cannot be combined with
`--seed-vehicles`, since both predict station and line ids from the same unbuilt map). `--check-overlaps` also checks that no two trains share a tile.

In game: the top bar shows money, date and speed buttons, the toolbar on the left opens settings and
the other tools, and clicking the map selects the tile, road, vehicle, industry or town under the cursor
(industries have their own information window with production, acceptance, recipes and last month's
figures). Construction tools (toolbar or keys): `PageUp` raise land, `PageDown` lower land, `L` level,
`X` clear trees and demolish your own objects, `Backspace` undo the last construction for a few
seconds. Station tools (third column): `G` bus stop and `H` truck stop (a drive-through stop on a straight
road, a bay beside one; the preview shows the catchment and the cargo received and accepted), `K` warehouse
(`Shift+K` changes its size: small, medium, very large), `Shift+H` removes a station piece; clicking a station
opens its window with waiting cargo, ratings, last month's figures, the catchment toggle and renaming. Hover to see a green/red preview with the cost, drag over an area, release to build, `Esc` or
the right button to cancel; building works while paused. Default keys:
Space pause, `1`/`2`/`3` speed ×1/×3/×8, `F` fast forward (once unlocked), `N` add a vehicle, `Delete`
remove the last vehicle you added, `Shift+Delete` close all windows, `F10` settings, `F3` render
statistics, `Esc` close window / pause menu, mouse and WASD/arrows/Q/E for the camera. All keys can be
changed in Settings → Key bindings; settings (language, currency, interface size, display, audio
volumes and mute) are saved in your user profile. Audio starts in the main menu, follows the camera
with countryside, water and town ambience plus nearby vehicle engines, and plays original music from a
playlist; settings has separate master, music, effects and ambient volumes.

`Talt.Headless` runs and replays simulations without a window (`run`, `replay`). `run --map-text
<T2-...>` generates the map and its industries; a run without it stays map-less and keeps the
pre-009 fingerprints. See the [headless contract](specs/001-simulation-foundation/contracts/headless-cli.md).

## Inspiration

The gameplay is inspired by classic transport tycoon games. This project contains no code,
graphics, sounds, music or text from any of those games. Everything is written and created from scratch.

## License

This project is **source-available, not open source**. It is licensed under the
[PolyForm Strict License 1.0.0](LICENSE.md):

- You may read the code and use it for personal, noncommercial purposes.
- You may not distribute it, modify it, create derivative works or use it commercially.

All rights not expressly granted are reserved by the author.

Copyright © 2026 Pablo León Manzano. All rights reserved.

### Third-party components

- [Noto Sans](https://github.com/notofonts/noto-fonts) (interface font), Copyright 2018 The Noto
  Project Authors, licensed under the [SIL Open Font License 1.1](src/Talt.Client/Ui/Font/OFL.txt).
  The font is bundled with the game and is not covered by the PolyForm licence above.
- [StbTrueTypeSharp](https://github.com/StbSharp/StbTrueTypeSharp) (font rasterizer), MIT or
  Unlicense.
- [JAJ.Packages.MiniAudioEx](https://github.com/japajoe/MiniAudioExNET) / [miniaudio](https://miniaud.io)
  (audio engine and cross-platform native output), MIT No Attribution / Unlicense. Native binaries
  are bundled for Windows, Linux and macOS, x64 and arm64; no external installation is required.
  The game falls back to silence when no output device is available.
- [EggEncoder](https://github.com/eggspot/EggEncoder) (FLAC encoding for the asset generator only,
  not shipped with the game), MIT.
- **Original audio assets**: every WAV and FLAC under `src/Talt.Client/Assets/Audio` was composed
  and synthesized for TALT by `tools/audio/generate-audio.cs`; no samples, loops or melodies come
  from another game or library. They are covered by the project licence and can be regenerated
  deterministically with `dotnet run tools/audio/generate-audio.cs`.
