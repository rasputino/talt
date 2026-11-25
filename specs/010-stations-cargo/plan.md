# Implementation Plan: Stations and cargo handling

**Branch**: `010-stations-cargo` (worktree branch `claude/010-stations-ab7b84`) | **Date**: 2026-11-24 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/010-stations-cargo/spec.md`

## Summary

Add stations to `Talt.Core` as a new deterministic system, `StationRegistry` (namespace
`Talt.Core.Stations`), owned by `GameSimulation` for games with a map and attached to
`ConstructionState`. Road stations come in two types (bus stop: passengers; truck stop: mail and
freight) and two forms (bay beside a road, drive-through on a straight road), built, joined and removed
by construction commands with previews and costs; warehouses in three sizes group the stations inside
their influence radius into one virtual station that shares stock, acceptance and ratings. Each day,
industries hand part of their production to serving stations through a new production sink in
`IndustryFleet.AdvanceDay`, town buildings send passengers and mail, ratings move toward a data-driven
target and neglected cargo is lost; monthly statistics rotate. Vehicles (012) and payments (011) get a
tested API: `MarkServed`, `Load`, `Unload` (normal/transfer) with `CargoHold`, and delivery reports
through `IDeliveryListener`. A debug option (`--station-service`) and a deterministic seeder
(`--seed-stations`) show cargo flow before vehicles exist. Balancing is in `stations.json`.

The client gets a third toolbar column (bus stop, truck stop, warehouse with size option, remove
station), a catchment preview with received/accepted cargo in the cursor label, placeholder boxes
appended to the industry placeholder instances, a station pick layer and the station window with a
catchment toggle and renaming. Benchmarks cover the daily and monthly update of 5 000 stations.

## Technical Context

**Language/Version**: C# (latest) on .NET 10

**Primary Dependencies**: none new (System.Text.Json `JsonDocument`, existing renderer pipelines).

**Storage**: embedded `Talt.Core.Stations.Data.stations.json`; recorded sessions gain the optional
`stations` settings object; four new command types.

**Testing**: xUnit `tests/Talt.Core.Tests/Stations` (rules validation, placement, joining, groups,
catchment/acceptance, supply distribution, ratings, loss, load/unload/transfer, conservation over 1 000
days, parallel = sequential, fingerprints, commands round trip); client tests for tools, preview text,
pick layer, window and localization; BenchmarkDotNet `StationBenchmarks`.

**Target Platform**: Windows, Linux x64/arm64, macOS x64/arm64; identical results on all.

**Project Type**: Desktop game (Core library + client + headless tool + benchmarks).

**Performance Goals**: daily update of 5 000 served stations < 2 ms, monthly < 2 ms, 0 B in steady state
(SC-001); catchment preview recomputed only when the cursor tile changes, < 0.1 ms (SC-002); 60 FPS
on the 4 096 map; tick cost unchanged (stations run only on day/month boundaries).

**Constraints**: integer-only state; no randomness needed; parallel group phase equal to sequential;
`Talt.Core` graphics-free; map-less and station-less fingerprints unchanged (FR-030).

**Scale/Scope**: up to ~10 000 stations and ~100 000 covered producers on large maps; 13 cargo types
(≤ 64 supported by masks).

## Constitution Check

*Initial gate and post-design re-check: PASS. Justified choices in Complexity Tracking.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I Core/Client separation | PASS | all station rules in `Talt.Core.Stations`; the client draws, picks and formats |
| II Performance | PASS | precomputed coverage (R5), preallocated pools; `StationBenchmarks` recorded in ARCHITECTURE.md |
| III Deterministic, data-oriented | PASS | SoA arrays, packet pool, fixed iteration orders, no randomness; group phase parallel with per-group writes only, tested equal to sequential; station types registered in `NoFloatingPointStateTests` |
| IV Tested increments | PASS | tests per user story, conservation property test, command round trips |
| V Readable C# | PASS | small partial files (`StationRegistry.Construction/Coverage/Supply/Cargo/Groups.cs`), XML docs |
| VI Original content | PASS | own formulas, names, values and placeholder models; mechanics only studied |
| VII Data-driven | PASS | piece types, warehouses, caps, rating and loss tables in validated `stations.json` |
| VIII Living docs | PASS | ARCHITECTURE.md (Stations section, data flow, numbers), README options, VISION decisions |

## Project Structure

### Documentation (this feature)

```text
specs/010-stations-cargo/
├── spec.md, plan.md, research.md, data-model.md, quickstart.md
├── contracts/ (stations-api.md, commands.md, data-files.md, cli.md)
└── tasks.md
```

### Source Code (repository root)

```text
src/Talt.Core/
├── Stations/
│   ├── Data/stations.json
│   ├── StationRules.cs           # parsed + validated stations.json
│   ├── StationSettings.cs        # DebugServicePercent (scenario settings)
│   ├── StationRegistry.cs        # SoA stations, pieces, queries, hashing, day/month
│   ├── StationRegistry.Construction.cs  # add/remove pieces and warehouses, names, rename
│   ├── StationRegistry.Groups.cs # union–find, merge/split of group state
│   ├── StationRegistry.Coverage.cs      # catchment, acceptance, producer table, preview
│   ├── StationRegistry.Supply.cs # daily supply, carry, ratings, loss, debug service
│   ├── StationRegistry.Cargo.cs  # packet pool, MarkServed/Load/Unload
│   ├── CargoHold.cs, CargoDelivery.cs (+ IDeliveryListener, UnloadResult)
│   ├── StationPlanner.cs         # plans for build/remove (used by commands and tool previews)
│   ├── StationCommands.cs        # buildStation, buildWarehouse, removeStation, renameStation
│   ├── StationSeeder.cs          # debug placements as commands
│   └── StationInfo.cs            # window snapshot
├── Industries/IndustryFleet.cs   # IProductionSink in AdvanceDay
├── Towns/TownBuildings.cs, TownRegistry.Streets.cs   # sim-side changed tiles; skip station tiles
├── Roads/RoadPlanner*.cs         # station-road protection
├── Construction/ (TileObjectKind.Station, ConstructionCommand.Committed hook, ConstructionState.Stations,
│                  clear tool refusal, reasons, construction.json)
├── Commands/CommandSerializer.cs, RecordedSession.cs ("stations")
├── Scenarios/ScenarioSettings.cs (Stations)
└── Simulation/GameSimulation.cs (create, day/month events, hashing)

src/Talt.Client/
├── Construction/ConstructionTool.cs (station tools), StationToolOptions.cs (warehouse size), CatchmentBoxes
├── Screens/InGameScreen.cs (stations column, cursor label), StationWindow.cs, Windows.cs (ObjectInfoWindow)
├── Picking/Picking.cs (StationPickLayer, PickKind.Station)
├── GameSession.cs (station placeholder instances, catchment highlight), Program.cs (options)
├── Input/Data/actions.json, Ui/Data/icons.json, Localization/Strings.*.json
tools/Talt.Headless/ (options, summary)
tests/Talt.Core.Tests/Stations/*, tests/Talt.Client.Tests/*Station*
benchmarks/Talt.Benchmarks/StationBenchmarks.cs
docs/ARCHITECTURE.md, README.md, docs/VISION.md, docs/SPECKIT_GUIDE.md (warehouses noted)
```

**Structure Decision**: one new Core folder `Stations/` beside `Towns/` and `Industries/`, same SoA +
data-file pattern; small hooks in industries, towns, roads and construction; client additions mirror
the road tools and the industry window/pick layer. Rendering changes stay minimal (placeholder boxes in
the existing industry instance buffer) because feature 018 replaces them.

## Complexity Tracking

| Choice | Why Needed | Simpler Alternative Rejected Because |
|--------|------------|-------------------------------------|
| Precomputed coverage + producer table with dirty tracking (R5) | 2 ms daily budget for 5 000 stations | scanning each catchment daily costs several ms |
| Group recomputation with merge/split rules (R4) | warehouses must pool stock and keep cargo conserved | first-warehouse-wins was rejected by the developer |
| Station commands clear the undo record | registry state (cargo, names, groups) is not tile data | extending the generic undo record couples 006 to stations |
| Production sink parameter on `IndustryFleet.AdvanceDay` | stations must take a rating share of each day's output, rest to stock (FR-014) | pulling from industry stock would let stations drain stock built before service began |
