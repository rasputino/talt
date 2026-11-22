# Transit & Logistics Tycoon (TALT)

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
dotnet run --project src/Talt.Client -c Release -- [chunks-per-side] [vehicles] [seed]
dotnet run --project benchmarks/Talt.Benchmarks -c Release -- --filter * --job short
```

Example: `dotnet run --project src/Talt.Client -c Release -- 16 20000` shows a 512 × 512 map with
20 000 vehicles.

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
