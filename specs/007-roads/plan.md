# Implementation Plan: Roads

**Branch**: `007-roads` (worktree branch `claude/007-roads-1cd198`) | **Date**: 2026-11-23 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/007-roads/spec.md`

## Summary

Roads become tile-level data (`RoadLayer`: 8-direction link and exit masks, type, flags, long-link span)
owned by feature 006's `ConstructionState`, built and removed by construction commands (road with
proposed path, bridge, tunnel, removal, one-way, motorway, motorway connector) that plan, preview, pay and
undo exactly like 006's tools. A junction-contracted routing graph (`RoadNetwork`) with stable node ids and
per-region (64 × 64) CSR blocks is updated incrementally after every commit/undo; `RoadRouter` runs A* with
tile-index tie-breaking and a version-invalidated route cache. Games with a map lose the prototype grid and
its synthetic vehicles and start with seeded town streets; map-less scenarios keep the prototype unchanged,
so CI goldens do not move. The client draws roads as their own meshes (`RoadMesher` → `roads.wgsl`) with
detail near the camera and merged runs far away. Design decisions: [research.md](research.md) R1–R12.

## Technical Context

**Language/Version**: C# latest on .NET 10 (nullable, warnings as errors)

**Primary Dependencies**: existing only (Silk.NET, wgpu-native, xUnit, BenchmarkDotNet); no new packages

**Storage**: embedded data file `roads.json`; state in memory, recorded sessions as JSON

**Testing**: xUnit (Core, Client GPU-free, Headless), BenchmarkDotNet

**Target Platform**: Windows, Linux, macOS (x64, arm64)

**Project Type**: desktop game (engine-less) + headless tool

**Performance Goals**: route query < 2 ms average on a fully connected 4096² network (SC-002); network
update < 1 ms per 100-tile action (SC-003); 200-tile preview < 2 ms, 0 B (SC-004); 60 FPS on 4096 with a
dense network (SC-005)

**Constraints**: deterministic integer state; no per-tick / per-frame allocations; Core GPU-free

**Scale/Scope**: up to 16 384² maps; reference 4096² with 65 536 junctions

## Constitution Check

| Principle | Status |
|---|---|
| I Core/Client separation | Pass: roads, rules, planner, network, router in Core; meshing/rendering/tools in Client (mesher GPU-free, testable). |
| II Performance | Pass: budgets SC-002..005 with `RoadNetworkBenchmarks` and client `--stats`; planning and routing reuse buffers; road meshes rebuilt only for changed blocks. Results go to ARCHITECTURE.md. |
| III Deterministic, data-oriented | Pass: dense byte arrays per tile, CSR per region; integer costs/lengths; changes only via commands; tie-breaks by tile index; parallel tick unaffected (roads change only in command processing). |
| IV Tested increments | Pass: tests per story (rules, planner, network incremental = full rebuild, router vs brute-force Dijkstra, commands, replay golden, mesher). |
| V Readable C# | Pass: small focused types; complexity tracked below. |
| VI Original content | Pass: designs from mechanics only; names, art and data original. |
| VII Data-driven | Pass: all costs, types, eras, limits in `roads.json`, validated with field names. |
| VIII Docs | ARCHITECTURE.md (roads, network, rendering, numbers), VISION.md (decisions from clarify), en/es strings. |

Post-design re-check: still passes; the one added complexity is listed below.

## Project Structure

### Documentation (this feature)

```text
specs/007-roads/
├── plan.md, research.md, data-model.md, quickstart.md
├── contracts/ (commands.md, road-api.md, data-files.md)
├── checklists/requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
src/Talt.Core/
├── Roads/            Dir8, RoadPiece, RoadFeatures, RoadLayer, RoadRules (+ Data/roads.json), RoadType,
│                     RoadGeometry (validity checks), RoadPlanner (path, bridge, tunnel, removal, one-way,
│                     motorway, connector), RoadSeeder (town streets, debug grid), road commands
├── Network/          RoadNetwork (incremental, regions), RoadRouter (A* + cache), MinHeap (shared),
│                     existing TransportNetwork/PathFinder/GridNetworkGenerator (map-less prototype)
├── Construction/     TileObjectKind.Road, TileChange/TileSnapshot road fields, Commit/Undo → roads +
│                     network, terraform/clear guards, reasons, ConstructionPlanner.Roads
├── Scenarios/        ScenarioBuilder: no grid/vehicles with a map
├── Simulation/       GameSimulation: Year → construction, seeding, industry footprint guard
└── Industries/       IndustryFleet: skip openings on tiles with objects
src/Talt.Client/
├── Construction/     road tools (ConstructionTool), RoadToolOptions, ToolController shapes
├── Rendering/        RoadMesher, RoadRenderer, Shaders/roads.wgsl; WebGpuRenderer hook
├── Picking/          road pick layer from RoadLayer
├── Screens/          toolbar buttons, type selector, road info window
├── Input/Data/actions.json, Localization/Strings.*.json
├── GameSetup.cs, GameSession.cs, Program.cs (--road-grid, --start-year if missing)
tools/Talt.Headless/  summary line road-tiles
tests/Talt.Core.Tests/Roads/*, Network/RoadNetworkTests, RoadRouterTests, Determinism updates
tests/Talt.Client.Tests/Rendering/RoadMesherTests, Construction tool tests
benchmarks/Talt.Benchmarks/RoadNetworkBenchmarks.cs
```

**Structure Decision**: one new Core namespace (`Talt.Core.Roads`) beside `Construction`; routing graph
next to the prototype in `Talt.Core.Network`, as the plan prompt asks.

## Map-less scenarios and goldens (research R1)

Map-less scenarios build exactly what they build today (grid `TransportNetwork`, synthetic lines and
vehicles); no construction state, so no road state is hashed: `GoldenFingerprintTests`,
`ci/golden-fingerprints.json` and `ci/sessions/windows-reference.json` stay as they are. Games with a map
change on purpose: empty prototype network and fleet, seeded town roads, road layer hashed. The map golden
of `ConstructionReplayTests` is regenerated and a road replay golden is added; the commit message says so.

## Phases

1. **Core data and rules** (US1–US2 base): Dir8, RoadPiece, RoadLayer, roads.json + RoadRules, RoadGeometry,
   TileChange/TileSnapshot/Commit/Undo integration, terraform/clear guards, seeder, ScenarioBuilder change.
2. **Network and router**: RoadNetwork (build, incremental update, canonical dump), RoadRouter, tests
   incremental = full rebuild, router vs brute force, benchmarks.
3. **Commands**: road (planner), bridge, tunnel, removal (+ town rule), one-way, motorway, connector;
   serializer; replay golden.
4. **Client**: tools and shapes, type selector, preview, road mesher/renderer, picking/info window,
   strings, key bindings, remove voxel painting on map games, `--road-grid`.
5. **Docs and verification**: ARCHITECTURE.md, VISION.md, benchmarks recorded, hand test with the user.

## Complexity Tracking

| Addition | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| Second routing graph (`RoadNetwork`) beside `TransportNetwork` | Keeps CI goldens and prototype vehicles intact while roads become mutable | Mutating `TransportNetwork` in place changes every golden and still strands vehicles on removed edges |
| Per-region CSR with dirty-chain walks | FR-003 (update cost ∝ change) with flat-array traversal | Global CSR rebuild is O(map) per action |
