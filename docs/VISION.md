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
| Window, input, graphics bindings | Silk.NET | Tentative |
| Graphics API | Vulkan (MoltenVK on macOS) or WebGPU | To be decided in prototype |
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

## Next steps

1. Technical prototype: open a window on all three platforms and render a voxel chunk.
2. Simulation benchmark: thousands of vehicles moving on a large map.
3. Choose the graphics API based on the prototype results.

## Open questions

- Transport modes for the first version (road and rail only, or more?).
- Map generation approach.
- Multiplayer: in scope or not?
- Modding support.
- Final game UI technology.
