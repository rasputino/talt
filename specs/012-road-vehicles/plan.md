# Implementation Plan: Road vehicles, depots and lines

**Branch**: `012-road-vehicles` (worktree branch `claude/012-road-vehicles-99b925`) | **Date**: 2026-11-25 |
**Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/012-road-vehicles/spec.md`

## Summary

The first playable loop: depots on the road network, a starter catalogue of buses and trucks (data), lines shared by
vehicles with per-stop loading rules, and road vehicles that drive lane by lane on the player's and towns' roads, keep
safe distances, queue at stops, take turns at junctions, load and unload through feature 010 and earn money through
feature 011. The simulation is a new `RoadVehicleSystem` in `Talt.Core.Vehicles` for games with a map; the synthetic
prototype fleet stays only in map-less scenarios, so the CI goldens do not move (research R1). Each tick is
**read → decide (parallel) → write lanes (sequential) → events (sequential)** with every vehicle reading only the
previous tick (R7), which makes parallel and sequential runs identical by construction and gives a hard no-overlap
guarantee (R3). Junctions are reserved first come, first served with "room to leave" (R4); routes come from feature
007's cached router and are validated lazily at junctions (R6). The client draws the vehicles with feature 018's model
pipeline, adds a depot tool, depot / vehicle / line windows and a line-building mode.

## Technical Context

**Language/Version**: C# latest on .NET 10 (nullable, warnings as errors).

**Primary Dependencies**: none new. Silk.NET + wgpu-native (client), xUnit, BenchmarkDotNet.

**Storage**: embedded JSON data file `Vehicles/Data/vehicles.json`; recorded sessions (`.talt-session.json`) for the
new commands. No save games yet (feature 014).

**Testing**: xUnit in `tests/Talt.Core.Tests/Vehicles/` (kinematics, lanes, junctions, stops, orders, money, ageing,
breakdowns, rerouting, commands, determinism, allocation through `AllocationAssert`), `tests/Talt.Client.Tests`
(strings, windows, line mode), golden replay test for a map game with vehicles.

**Target Platform**: Windows, Linux, macOS (x64 and arm64), same fingerprints on all.

**Project Type**: desktop game (Core library + client + headless tool).

**Performance Goals**: 50 000 interacting vehicles on a 4096 map ≤ 4 ms average tick (≤ 8 ms worst), fast forward
≥ ×20 at ≥ 30 FPS; route between stops 1 000 tiles apart ≤ 2 ms uncached; client keeps 60 FPS with 50 000 vehicles.

**Constraints**: no allocation per tick or per frame in steady state; fixed-point integers only in simulation state;
existing goldens unchanged (map-less, windows-reference, and every map golden) unless a rule changes on purpose.

**Scale/Scope**: up to 100 000 vehicles (benchmark), thousands of lines, a few hundred depots.

## Constitution Check

| Principle | How this plan complies |
|---|---|
| I. Core/Client separation | All simulation in `Talt.Core.Vehicles`; the client only reads poses and submits commands. |
| II. Performance | Budgets above; `RoadVehicleBenchmarks` and `RouteBenchmarks` (10 k / 50 k / 100 k); numbers recorded in `docs/ARCHITECTURE.md`; allocation tests. |
| III. Deterministic, data-oriented | Packed `RoadVehicleState` structs in dense arrays with stable ids; integer kinematics; parallel phase reads only the previous tick; breakdowns from a stateless hash of a forked seed; all actions are commands registered in `CommandSerializer`; parallel = sequential tests; new types in `NoFloatingPointStateTests`; state in the fingerprint only once something exists. |
| IV. Tested increments | Each user story has Core tests written with the code; bug fixes get regression tests. |
| V. Readable C# | Small partial files per phase; XML docs on public APIs; the algorithm documented in ARCHITECTURE.md. |
| VI. Original content | Original mechanics design, names and generated voxel models; no "Transport Tycoon". |
| VII. Data-driven | Models, physics, timing, reliability, prices in `vehicles.json`, validated with file and field names. |
| VIII. Docs and localization | ARCHITECTURE.md section and numbers; every text in `Strings.en.json` and `Strings.es.json`. |

**Gate**: passes; no violations. Re-checked after Phase 1 design: passes (the occupancy check in construction is a small
interface, R12; the money event gets an optional field, R11).

## Project Structure

### Documentation (this feature)

```text
specs/012-road-vehicles/
├── spec.md, plan.md, research.md, data-model.md, quickstart.md
├── contracts/   commands.md, data-files.md, vehicles-api.md, cli.md
├── checklists/  requirements.md
└── tasks.md     (/speckit-tasks)
```

### Source Code (repository root)

```text
src/Talt.Core/
├── Vehicles/                      NEW
│   ├── VehicleRules.cs            vehicles.json loading/validation (models, physics, timing, reliability, depot)
│   ├── Data/vehicles.json
│   ├── RoadVehicleState.cs        packed hot state (lane, S, speed, caps, flags, held junction, timers)
│   ├── RoadVehicleSystem.cs       storage, ids, public queries, hashing
│   ├── RoadVehicleSystem.Drive.cs phase 1 (parallel read/decide): speeds, obstacles, lane crossing
│   ├── RoadVehicleSystem.Lanes.cs phase 2 lane lists, lane registry
│   ├── RoadVehicleSystem.Events.cs phase 3: junction grants, stops, depots, routes, stuck, turnarounds
│   ├── RoadVehicleSystem.Daily.cs ageing, reliability, breakdowns, servicing, profit, monthly value
│   ├── RoutePlanner.cs            node-ahead / drive-through goals over RoadRouter
│   ├── LineRegistry.cs            lines, stops, loading rules
│   ├── DepotRegistry.cs           depots
│   ├── DepotPlanner.cs            depot placement/removal plans (bay-like)
│   ├── VehicleCommands.cs         buy, sell, clone, start/stop, send to depot, cargo, service interval
│   ├── LineCommands.cs            createLine, deleteLine, renameLine, editLineStops, assignLine
│   ├── DepotCommands.cs           buildDepot, removeDepot (ConstructionCommand)
│   ├── VehicleSeeder.cs           --seed-vehicles debug commands
│   └── VehicleNotices.cs          stuck / no route / skipped stop feed for the client (not state)
├── Construction/                  TileObjectKind.Depot, IVehicleOccupancy check, ConstructionReasons
├── Finance/                       RunningCosts.SetValue, MoneyEvent.VehicleId, FinanceSystem hooks
├── Stations/                      (unchanged API) removal protection through the occupancy check
├── Simulation/GameSimulation.cs   creates and ticks RoadVehicles, events, fingerprint
├── Scenarios/ScenarioSettings.cs  (no new settings; seeding is commands)
└── Commands/CommandSerializer.cs  registers the new commands

src/Talt.Client/
├── GameSession.cs                 poses from RoadVehicleSystem, vehicle models per model id, breakdown effects
├── Construction/                  depot tool
├── Screens/                       DepotWindow, VehicleWindow, LineWindow, LinesWindow, line-building mode in InGameScreen
├── Picking/                       vehicle picks open VehicleWindow; depot pick layer
├── Rendering/                     depot placeholders/models; smoke/sparks boxes
├── Localization/Strings.*.json    all new texts
└── Program.cs                     --seed-vehicles, --check-overlaps

tools/Talt.Headless                --seed-vehicles, --check-overlaps, vehicle summary
tools/Talt.ModelGen                bus and truck models + depot model
tests/Talt.Core.Tests/Vehicles/    unit, property and determinism tests; golden replay
benchmarks/Talt.Benchmarks/        RoadVehicleBenchmarks, RouteBenchmarks
docs/ARCHITECTURE.md, README.md    algorithm, numbers, options
```

**Structure Decision**: one new Core namespace with partial classes per tick phase, small touch points in
construction, finance and the simulation; client screens follow the existing window pattern.

## Design notes

- **Tick order** in `GameSimulation.Tick`: commands → prototype fleet (map-less) → road vehicles → tick++ → day events
  (stations, industries, finance, then vehicles' daily step after finance so a day's running costs are booked first).
- **Fingerprint**: `RoadVehicleSystem.AppendTo` writes nothing until a depot, line or vehicle has ever existed; after
  that it hashes rules fingerprint, ids, hot and cold arrays, holds, route steps and indices, lines, depots, notices
  are excluded (not state).
- **Commands while paused** work as before (`ProcessPendingCommands`), so the player can set up lines while paused.
- **Game over**: already refused globally by `GameSimulation`.
- **Notices** (stuck, no route, skipped stop, breakdown) are a fixed feed like `MoneyEventFeed`, not state.
- **Debug overlap check** (`CheckInvariants`): every lane list sorted, gaps ≥ minGap − 0, every vehicle on a road link
  of its lane; tests call it every tick; `--check-overlaps` runs it every tick in client and headless and stops with a
  message on failure.

## Complexity Tracking

No constitution violations.
