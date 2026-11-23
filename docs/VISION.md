# Transit & Logistics Tycoon — Vision

This document describes what TALT is, what it is not, and the principles that guide its development.
It is a living document: decisions marked as *tentative* must be validated before they become final.

## Elevator pitch

A transport management game where you build road, rail, sea and air networks to move passengers
and cargo across a living world. Voxel art style, and a simulation designed from day one to stay
fast on very large maps.

## Pillars

1. **Pure transport.** The player is a transport company, not an industrialist. There is no
   manufacturing, no production chains to manage, no factories to build. The world produces and
   consumes; the player connects it.
2. **Meaningful destinations.** Passengers, mail and goods want to go to a specific place.
   Building networks that serve real demand is the core challenge.
3. **Scales without slowing down.** Large maps must be possible without the performance loss that
   classic transport games suffer. Large maps are an option, not a requirement.
4. **Charming voxel look.** A clean, colorful, readable voxel art style.

## Gameplay

### Scope

- The player builds infrastructure (roads, rails, stations, ports, airports) and runs vehicles
  on routes.
- Industries and towns exist and evolve on their own. The player does not build or operate them.
- Everything (code, graphics, sounds, music, texts) is original.

### Cargo model

| Cargo kind | Examples | Where it can go |
|---|---|---|
| Raw materials | Coal, wood, ore, grain | Any place that accepts it |
| Most processed goods | Steel, lumber, fuel | Any place that accepts it |
| Destination cargo | Passengers, mail, consumer goods | A specific town or destination |

- Undirected cargo is cheap to simulate: it just needs an accepting destination.
- Destination cargo is the interesting (and expensive) part: it is modeled as **aggregated
  flows** (for example "120 passengers/month from A to B"), not as individual agents. This keeps
  the simulation cost independent of the number of passengers.

### Non-goals

- Manufacturing or production management.
- Individual simulation of each passenger.
- Recreating the look, assets or code of any existing game.

## Visual style

- Voxel art, similar in spirit to modern voxel transport games, with its own identity.
- Readability first: the player must understand the network at a glance at any zoom level.
- Level of detail (LOD): full detail near the camera, simplified representations far away.

## Performance goals

The root cause of slowdowns on large maps in classic transport games is the simulation
(single-threaded, iterating over every tile and vehicle), not the rendering. TALT addresses this
through architecture:

- **Chunked world.** Terrain and entities are grouped into chunks. Only what matters is simulated
  and rendered in full detail.
- **Data-oriented, multithreaded simulation.** An ECS keeps data contiguous in memory and lets
  vehicles and systems be processed in parallel.
- **Hierarchical, cached pathfinding.** Routes are computed first between regions, then locally,
  and reused when nothing has changed.
- **Aggregated demand.** Destination cargo is simulated as flows between places.
- **Allocation-free hot paths.** No garbage generated in the simulation loop.

Initial targets (*tentative*, to be validated with benchmarks):

- Maps of at least 4096 × 4096 tiles.
- Tens of thousands of active vehicles.
- Stable 60 FPS rendering, with the simulation tick independent of the frame rate.

## Technology

No game engine: the game is built directly on top of libraries, for full control over
performance. Target platforms: Windows, Linux and macOS.

| Area | Choice | Status |
|---|---|---|
| Language / runtime | C# on .NET 10 | Decided |
| Window, input, graphics bindings | Silk.NET | Working in prototype |
| Graphics API | WebGPU (wgpu-native: Vulkan, Metal or DirectX 12) | Working in prototype on Windows; Linux and macOS not tested yet |
| ECS | Arch | Tentative |
| Debug UI and tools | ImGui.NET | Tentative |
| Final game UI | — | Open |
| Tests | xUnit | Tentative |
| Benchmarks | BenchmarkDotNet | Tentative |

## Development principles

The game is developed mostly through AI-assisted coding. To keep the project manageable:

- **Document the architecture** in `docs/`, so every session shares the same context.
- **Small, verifiable steps**, each with its own tests.
- **Measure performance from day one**, with a large test map, to catch regressions early.
- **Readable, idiomatic C#**, so every change can be reviewed and debugged by a human.

## Decision log

| Date | Decision | Where |
|---|---|---|
| 2026-11-22 | Simulation state uses integer fixed-point arithmetic (no floats) so results are bit-identical on Windows, Linux and macOS, x64 and ARM64. | specs/001 research R1 |
| 2026-11-22 | 40 simulation ticks per in-game day, 20 per second at normal speed (one day ≈ 2 s). | specs/001 research R2 |
| 2026-11-22 | Speeds ×1, ×3, ×8 always available; unlimited fast forward is unlocked in the settings and has its own control. | specs/001 clarifications |
| 2026-11-22 | Player actions are commands applied at the next tick; sessions can be recorded and replayed. | specs/001 research R7 |
| 2026-11-22 | The interface is our own retained-mode UI drawn with WebGPU, with an SDF font atlas from the bundled Noto Sans (OFL) rasterized by StbTrueTypeSharp; no UI library. | specs/005 research R1, R2 |
| 2026-11-22 | Money is counted in one internal unit (one US dollar) and shown in a player-chosen currency with fixed exchange rates (21 currencies); the default follows the language (USD in English, EUR in Spanish). | specs/005 clarifications |
| 2026-11-22 | Settings apply and are saved as soon as they change; each clicked map object gets its own information window. | specs/005 clarifications |
| 2026-11-22 | Map picking is done on the CPU (ray against tile columns and object boxes) so the answer is ready in the same frame. | specs/005 research R5 |
| 2026-11-23 | Freight economy: 13 cargo types and 11 industry kinds, all in validated data files; raw materials and processed goods are destination-free, passengers/mail/town goods are destination-based. | specs/009 contracts/content-schema.md |
| 2026-11-23 | About 75 % of the reserved industry sites start occupied; industries grow with deliveries, decline after ~24 months of neglect and may close after ~5 years, while new ones open on free sites. Waiting cargo is capped per cargo and production beyond the cap is discarded. | specs/009 clarifications |
| 2026-11-23 | Audio lives only in the client (miniaudio through MiniAudioEx, MIT/Unlicense) and never touches the simulation; music starts in the main menu and continues through the game, with separate master/music/effects/ambient volumes, a mute control and a silent fallback when there is no device. All sounds and music are original. | specs/019 plan, research |

## Next steps

1. ~~Technical prototype: open a window and render voxel terrain.~~ Done on Windows (see [ARCHITECTURE.md](ARCHITECTURE.md)).
2. ~~Simulation benchmark: thousands of vehicles moving on a large map.~~ Done: one million
   independent vehicles tick in ~6 ms (see [ARCHITECTURE.md](ARCHITECTURE.md#benchmarks)).
3. Vehicle interactions: following distance, junctions, signals and blocks, keeping the tick parallel.
4. Rendering for large maps: greedy meshing, frustum culling and level of detail.
5. Visual quality: shadows, smaller voxels per tile, scenery (trees, buildings).
6. Test the prototype on Linux and macOS.

## Open questions

- Transport modes for the first version (road and rail only, or more?).
- Map generation approach.
- Multiplayer: in scope or not?
- Modding support.
- Final game UI technology.
