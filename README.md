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
dotnet run --project benchmarks/Talt.Benchmarks -c Release -- --filter * --job short
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
`--print-fingerprint-at <tick>`, `--stats` (render statistics from the start), `--zoom <distance|max>`
(initial camera distance), `--ui-test-windows <n>` (opens sample windows to measure the interface).

In game: the top bar shows money, date and speed buttons, the toolbar on the left opens settings and
the other tools, and clicking the map selects the tile, road or vehicle under the cursor. Construction
tools (toolbar or keys): `PageUp` raise land, `PageDown` lower land, `L` level, `X` clear trees and
demolish your own objects, `Backspace` undo the last construction for a few seconds. Hover to see a
green/red preview with the cost, drag over an area, release to build, `Esc` or the right button to
cancel; building works while paused. Default keys:
Space pause, `1`/`2`/`3` speed ×1/×3/×8, `F` fast forward (once unlocked), `N` add a vehicle, `Delete`
remove the last vehicle you added, `Shift+Delete` close all windows, `F10` settings, `F3` render
statistics, `Esc` close window / pause menu, mouse and WASD/arrows/Q/E for the camera. All keys can be changed in
Settings → Key bindings; settings (language, currency, interface size, full screen, vertical sync) are
saved in your user profile.

`Talt.Headless` runs and replays simulations without a window (`run`, `replay`); see
[the headless runner contract](specs/001-simulation-foundation/contracts/headless-cli.md).

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
