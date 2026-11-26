# Implementation Plan: Railways

**Branch**: `015-railways` (worktree branch `claude/015-railways-657d8e`) | **Date**: 2026-11-26 |
**Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/015-railways/spec.md`

## Summary

Rail is a second network type next to roads. Track is tile-level data in a new `RailLayer` (eight-direction link
masks like `RoadLayer`, plus type, bridge/tunnel long links and a signal byte per tile) changed only through the
construction framework of feature 006, so previews, costs, ownership, undo and the "vehicle in the way" check work
unchanged (research R1–R3). Which way a train may leave a tile is implied by the link mask: a train heading `h` may
leave by link `h−1`, `h` or `h+1` only (45° at most per tile, clarification Q1), so a mask describes switches and
diamond crossings without extra data (R2).

Trains are consists of locomotives and wagons from a new data file `rail.json` (R6). A new `TrainSystem` in
`Talt.Core.Rails` owns them with the same storage pattern as road vehicles (dense slots, stable ids, swap-with-last).
Safety rests on one invariant: **every track tile has at most one owner train**, the owner being the train whose body
covers the tile or that has reserved it (R4). Trains move only over tiles they own, so collisions are impossible by
construction and checked by a debug invariant. Signals only decide *when* a train may reserve more track: a block
signal needs its whole block (a flood-filled component of track cut at signal edges) to have no other owner; a path
signal or one-way path signal only needs the tiles of the path to the next signal (R5).

The tick follows feature 012's pattern (R7): **(1) move — parallel**: each train updates its speed from power,
tractive effort, mass, slope, curves and the distance left in its authority, advances its head along owned track and
releases the tiles its tail has left (it writes only its own slot and tiles it owns); **(2) reserve and events —
sequential, ascending train id**: trains near the end of their authority try to reserve the next segment, then stops,
loading, depots and routing. Parallel and sequential ticks are therefore identical.

Routes come from an A* over (tile, heading) on the rail layer that skips through unbranching runs in one expansion
(R8); costs prefer short, fast track and penalize tiles owned by other trains, so a train choosing at a path signal
takes a free platform or a free loop track (R9). Stations are feature 010 stations with a new `rail-platform` piece
type (R10); trains load per wagon through `StationRegistry.Load/Unload`, so feature 013's destinations work for rail
too. Long trains at short platforms pull forward (clarification Q5, R11). Level crossings close for road vehicles
while a train owns the tile (R12). Lines are feature 012's shared `LineRegistry` (R13).

The client draws track with a `RailMesher` through the road renderer's block/area machinery (made generic over the
mesher), signals and trains as model instances through feature 018's pipeline with generated placeholder models, adds
rail tools, a rail depot window with the consist editor and a train window, and plays engine sounds through feature
019's vehicle voices (R14).

## Technical Context

**Language/Version**: C# latest on .NET 10 (nullable, warnings as errors).

**Primary Dependencies**: none new. Silk.NET + wgpu-native (client), xUnit, BenchmarkDotNet.

**Storage**: embedded JSON data file `Rails/Data/rail.json` (track types, signals, bridges, tunnels, rolling stock,
physics); `stations.json` gains the `rail-platform` piece; `finance.json` its maintenance. Recorded sessions for the new
commands. No save games yet (feature 014).

**Testing**: xUnit in `tests/Talt.Core.Tests/Rails/` (layer invariants, planner, blocks, reservations, movement,
physics, scenario fixtures for collisions and deadlocks, platform choice, pull-forward, depots, commands, determinism
parallel = sequential, allocation through `AllocationAssert`), client string tests, a golden replay of a map game with
trains.

**Target Platform**: Windows, Linux, macOS (x64 and arm64), same fingerprints on all.

**Project Type**: desktop game (Core library + client + headless tool).

**Performance Goals**: 5 000 trains with signals on a 4096 map within the tick budget (≤ 4 ms average for all
vehicles; trains alone ≤ 2 ms), fast forward ≥ ×20, 60 FPS; a rail route between stations 1 000 tiles apart ≤ 2 ms.

**Constraints**: no allocation per tick or per frame in steady state; fixed-point integers only in simulation state;
existing goldens unchanged (map-less, windows-reference, and every map golden): rail state is hashed only once rail
exists (R15).

**Scale/Scope**: 5 000 trains (benchmark 1 k / 5 k), networks of a few hundred thousand track tiles.

## Constitution Check

| Principle | How this plan complies |
|---|---|
| I. Core/Client separation | All rail simulation in `Talt.Core.Rails`; the client reads poses and submits commands. |
| II. Performance | Budgets above; `TrainBenchmarks` (1 k / 5 k trains with signals) and `RailRouteBenchmarks`; numbers in `docs/ARCHITECTURE.md`; allocation tests. |
| III. Deterministic, data-oriented | Packed per-tile bytes; dense train arrays; integer kinematics; parallel move phase writes only own state and own tiles; reservations in ascending id order; breakdowns from forked seeds; commands registered in `CommandSerializer`; parallel = sequential tests; new types in `NoFloatingPointStateTests`; fingerprint only once rail exists. |
| IV. Tested increments | Each user story has Core tests written with the code; scenario fixtures for safety and deadlocks. |
| V. Readable C# | Partial files per phase; XML docs; the algorithm documented in ARCHITECTURE.md. |
| VI. Original content | Original rules, names and generated models; mechanics only studied; no "Transport Tycoon". |
| VII. Data-driven | Track types, signals, rolling stock, physics, timing and costs in `rail.json`, validated with file and field names. |
| VIII. Docs and localization | ARCHITECTURE.md section; every text in `Strings.en.json` and `Strings.es.json`. |

**Gate**: passes. Re-checked after Phase 1: passes. Two small touches outside rail are justified in Complexity
Tracking.

## Project Structure

### Documentation (this feature)

```text
specs/015-railways/
├── spec.md  plan.md  research.md  data-model.md  quickstart.md  tasks.md
├── contracts/ (commands.md, data-files.md, rail-api.md, cli.md)
└── checklists/requirements.md
```

### Source Code (repository root)

```text
src/Talt.Core/Rails/
├── RailLayer.cs  RailPiece.cs  RailSignals.cs      # tile data, signals
├── RailRules.cs  Data/rail.json                    # data file
├── RailPlanner*.cs  RailCommands.cs                # construction (track, bridge, tunnel, removal, signals, electrify, station, depot)
├── RailDepotRegistry.cs  RailBlocks.cs             # depots, block labelling
├── RailRouter.cs                                   # A* over (tile, heading)
├── TrainSystem*.cs  TrainCommands.cs  TrainSeeder.cs
src/Talt.Core/Construction/  (TileChange/TileSnapshot gain the rail piece; TileObjectKind.Rail, RailDepot)
src/Talt.Core/Stations/      (rail platform pieces)
src/Talt.Core/Vehicles/      (level crossing guard for road vehicles)
src/Talt.Client/Rendering/   (RailMesher, generic tile-layer renderer, train/signal instances)
src/Talt.Client/Construction (rail tools)  src/Talt.Client/Screens (rail depot, train windows)
tests/Talt.Core.Tests/Rails/  benchmarks/Talt.Benchmarks/TrainBenchmarks.cs
```

**Structure Decision**: a new `Talt.Core.Rails` namespace parallel to `Roads` + `Vehicles`; shared framework types
(construction plan, station registry, line registry, finance) are extended, not copied.

## Complexity Tracking

| Change | Why needed | Simpler alternative rejected because |
|---|---|---|
| `TileChange`, `TileSnapshot`, `UndoRecord` and `ConstructionState.Commit` carry a rail piece | rail must use the same undo/commit path | a parallel commit path would duplicate money, undo and occupancy checks |
| Road vehicles consult a crossing guard before entering a tile | level crossings (clarification Q4) | refusing level crossings was rejected by the user |

## Phase outputs

- Phase 0: [research.md](research.md) (R1–R15, no open questions).
- Phase 1: [data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md).
