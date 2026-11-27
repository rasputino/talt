# Implementation Plan: Destination demand model

**Branch**: `013-destination-demand` (worktree branch `claude/013-destination-demand-afb81f`) | **Date**: 2026-11-26 |
**Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/013-destination-demand/spec.md`

## Summary

Passengers, mail and goods get a destination town and travel there over the player's lines, with transfers at shared
stations, and are paid once on arrival. Four new pieces in a new `Talt.Core.Demand` namespace, wired into the existing
station (010), finance (011) and vehicle (012) code:

1. **Destination tables** (`DestinationTables`): per town and destination cargo, an own-town share (50 %) and the top
   N other towns weighted population^a / distance^k; per goods-producing industry, the goods-accepting towns by the same
   weight. Integer weights, recomputed a slice of towns per day (research R1, R2).
2. **Link graph** (`LinkGraphSnapshot`, built by `CargoRouter`): nodes are station groups (by their stable group key),
   edges are consecutive loading/unloading stops of lines, with monthly capacity and travel time. Built from any number
   of `ILinkSource`s (road vehicles implement it; rail in 015 adds itself) — never from roads (R3, R4).
3. **Route assignment** (`RouteAssignment`): per company, cargo and destination town a backward Dijkstra from the
   groups that serve the town, keeping the two best distinct serving sources per node (own-town cargo must leave its
   group), run twice with congestion costs (method of successive averages) giving up to two next hops with shares.
   Runs as a background job on an immutable snapshot started at tick t0 and applied at exactly t0 + delay; the main
   thread waits if it is late, and computes inline when ticking sequentially, so results never depend on thread
   timing (R5–R8).
4. **Destination stock** (`StationRegistry` partial `Destinations`): waiting destination cargo in buckets keyed by
   (group, cargo, next hop), packets with destination town, source town, origin, pickup day, waiting-since day and
   transfer credit. Supply splits amounts over destination tables with exact cumulative apportionment; units without a
   route stay unserved. Vehicles load the bucket whose next hop is their next stop, deliver at groups serving the
   destination, keep cargo whose next hop is their next stop and transfer the rest. Expiry after 60 days (R9–R13).

The client shows waiting cargo by destination in the station window, cargo by destination in the vehicle window,
demand and served share in the town window, and an unserved-demand overlay of dotted lines between towns (R14).

## Technical Context

**Language/Version**: C# latest on .NET 10 (nullable, warnings as errors).

**Primary Dependencies**: none new (xUnit, BenchmarkDotNet, Silk.NET + wgpu-native already in use).

**Storage**: embedded JSON data file `src/Talt.Core/Demand/Data/demand.json`; no save games yet (014).

**Testing**: xUnit in `tests/Talt.Core.Tests/Demand/` (tables, apportionment, graph, assignment on small graphs,
routing, transfers, payment and credits, expiry, conservation over a year, background = inline = sequential,
allocation through `AllocationAssert`); updated vehicle and station tests; client string tests.

**Target Platform**: Windows, Linux, macOS (x64, arm64), identical fingerprints.

**Project Type**: desktop game (Core library + client + headless tool).

**Performance Goals**: reference scenario (4096 map, its towns, ~5 000+ stations, 50 000 buses on lines): no tick above
8 ms, demand + routing < 1 ms average per tick on the main thread; route job (background) ≤ 50 % of its window at ×20;
station supply with destinations ≤ 1 ms per day boundary.

**Constraints**: no steady-state allocation on the tick path (job start reuses buffers and a preallocated work item);
integers only in state; map-less goldens and every game without a line keep their fingerprints.

**Scale/Scope**: up to ~1 000 towns, 10 000 station groups, 50 000 vehicles, 3 destination cargo types.

## Constitution Check

| Principle | How this plan complies |
|---|---|
| I. Core/Client separation | All demand and routing in `Talt.Core.Demand` and `Talt.Core.Stations`; client only reads and draws. |
| II. Performance | Budgets above; `DemandBenchmarks` (tables, snapshot build, assignment, supply, re-route) on the 012 reference scenario; numbers in `docs/ARCHITECTURE.md`; allocation tests. |
| III. Deterministic, data-oriented | Flat arrays; integer weights and cumulative apportionment (no randomness needed); background job is a pure function of an immutable snapshot applied at a fixed tick; parallel = sequential = inline tests; new types in `NoFloatingPointStateTests`; state hashed only once a route was applied. |
| IV. Tested increments | Each user story has Core tests with the code; small hand-made graphs for assignment. |
| V. Readable C# | Small partial files; XML docs; algorithm documented in ARCHITECTURE.md. Added complexity (background job) justified below. |
| VI. Original content | Own design from the vision's "aggregated flows"; mechanics only studied in other games. |
| VII. Data-driven | All shares, exponents, limits, intervals, expiry and penalties in `demand.json`, validated with field names. |
| VIII. Docs and localization | ARCHITECTURE.md section and numbers, VISION decision log, strings in en and es. |

**Gate**: passes. Re-checked after Phase 1: passes.

## Project Structure

### Documentation (this feature)

```text
specs/013-destination-demand/
├── spec.md, plan.md, research.md, data-model.md, quickstart.md
├── contracts/   demand-api.md, data-files.md
├── checklists/  requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
src/Talt.Core/
├── Demand/                         NEW
│   ├── DemandRules.cs              demand.json loading/validation
│   ├── Data/demand.json
│   ├── DestinationTables.cs        town and industry destination tables, daily slices, demand statistics
│   ├── Apportion.cs                exact cumulative apportionment helper
│   ├── ILinkSource.cs              mode-agnostic line contribution (LinkCollector)
│   ├── LinkGraphSnapshot.cs        immutable graph + serving towns + demand estimate per group
│   ├── RouteAssignment.cs          pure assignment (Dijkstra with two labels, MSA iterations)
│   ├── RouteTable.cs               per company/cargo sorted (group, town) → hops, shares, carries
│   └── CargoRouter.cs              schedules jobs, applies at fixed tick, hashes state
├── Stations/
│   ├── StationRegistry.Destinations.cs   NEW: destination buckets, supply split, load/unload by route, expiry, re-route
│   ├── StationRegistry.Cargo.cs    Load/Unload get the vehicle's next stop; deliveries to the destination town only
│   ├── StationRegistry.Supply.cs   destination cargo goes through the split
│   ├── CargoHold.cs                HeldCargo gets Credit
│   └── IDeliveryListener / ITransferValuer   credit estimate from finance
├── Vehicles/
│   ├── RoadVehicleSystem*.cs       implements ILinkSource; passes next stop; profit with credits
│   └── LineRegistry.cs             Version already exists (graph staleness)
├── Finance/FinanceSystem.cs        implements ITransferValuer
└── Simulation/GameSimulation.cs    creates DestinationTables + CargoRouter, events, hashing

src/Talt.Client/Screens/            StationWindow, VehicleWindows, TownWindow, DemandOverlay (NEW), InGameScreen toggle
src/Talt.Client/Localization/       Strings.en.json, Strings.es.json
benchmarks/Talt.Benchmarks/DemandBenchmarks.cs   NEW
tests/Talt.Core.Tests/Demand/       NEW
```

**Structure Decision**: a new `Demand` namespace holds everything that is independent of stations' storage (tables,
graph, assignment, router); the cargo storage and the vehicle operations stay in `StationRegistry` where packets
already live, so 015 trains call the same `Load`/`Unload` with their next stop.

## Complexity Tracking

| Addition | Why needed | Simpler alternative rejected because |
|---|---|---|
| Background route job applied at a fixed tick | Full assignment on the reference scenario costs tens of ms; doing it in one tick breaks the 8 ms budget | Time-slicing on the main thread adds the same cost to the main thread budget at ×20 and needs resumable Dijkstra state; the job is a pure function, so timing cannot leak into state |
| Two labels per node in Dijkstra | Own-town cargo must reach a serving group other than its own | One Dijkstra per serving group multiplies cost by the number of serving groups |
| Two next hops with shares | Capacity-aware spreading (US5 scenario 2) | A single hop per destination cannot split a saturated corridor |
