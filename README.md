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

Client options: `--start-year <year>`, `--language en|es`, `--unlock-fast-forward` /
`--lock-fast-forward` (saved in your settings), `--record [file]`, `--print-fingerprint-at <tick>`.

In-game keys: Space pause, `1`/`2`/`3` speed ×1/×3/×8, `F` fast forward (once unlocked), `N` add a
vehicle, `Delete` remove the last vehicle you added, mouse and WASD/arrows/Q/E for the camera.

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
