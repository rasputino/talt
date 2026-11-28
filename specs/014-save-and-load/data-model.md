# Data model: Save and load (feature 014)

Types are in `Talt.Core.Saves` unless noted. Byte layout: [contracts/save-format.md](contracts/save-format.md).
Section ids and the rules for later features: [contracts/save-sections.md](contracts/save-sections.md).

## SaveDetails (record, details block)

What the load screen shows without loading the game (FR-015, FR-016).

| Field | Type | Notes |
|---|---|---|
| `Name` | string | The player's name for the save, 1–80 characters. |
| `Kind` | `SaveKind` | `Manual`, `Autosave`, `QuickSave`. |
| `SavedAtUtc` | DateTimeOffset | Real time of the save (client clock; never enters the simulation). |
| `GameDate` | `GameDate` | In-game date at the save tick. |
| `Tick` | long | `GameSimulation.CurrentTick`. |
| `CompanyName` | string | Player company name (or empty for map-less scenarios). |
| `Money` | long | Player balance in base currency units. |
| `MapWidth`, `MapDepth` | int | Tiles; 0 for map-less scenarios. |
| `GameVersion` | string | Informational version of the build that wrote it. |
| `FormatVersion` | int | Container version (also in the header). |
| `ContentFingerprint` | ulong | Summary for the "data changed" warning; full identity is in `CNTN`. |
| `Fingerprint` | ulong | State fingerprint at the save tick (lets tools verify a load). |

Validation: name non-empty after trimming; numeric fields non-negative except `Money`.

## SaveKind (enum)

`Manual = 0`, `Autosave = 1`, `QuickSave = 2`.

## SaveThumbnail

256 × 256 RGB24 pixels (196 608 bytes before compression), drawn by `MapOverview.Render` from the map,
the tile layers, town buildings and industries (R14). Colours from `Saves/Data/overview-colors.json`.

## SaveSectionId (readonly struct)

Four ASCII characters packed into a `uint` (`"CORE"`, `"MAP_"`…). Registry and owners:
[contracts/save-sections.md](contracts/save-sections.md).

## SaveWriter / SaveReader (ref-free classes over SaveBuffer)

Explicit little-endian primitives used by every section (R1):

- `Write/Read`: `bool`, `byte`, `int`, `uint`, `long`, `ulong`, `string` (UTF-8, length-prefixed,
  null distinguished from empty), `DeterministicRandom` state (4 × ulong).
- `WriteArray<T>(T[]? array)` / `ReadArray<T>()` for unmanaged `T` (null, length, raw bytes; full array
  length, R1); `ReadInto<T>(Span<T>)` for fixed-size arrays (tile layers) that must keep their length.
- `WriteSpan<T>` for live prefixes where the field's contract is "only `[0, count)` is meaningful" and
  the tail is never read (allowed only when listed as such in the field manifest).
- Dictionary helpers that write entries in enumeration order and read by insertion (R3).
- `ContentMap` access: `reader.Content.Cargo(int savedIndex)` and friends (R10).
- `SaveReader.Fail(string reason)` throws `SaveFormatException` with the section id and offset.

## SaveBuffer / SaveBufferPool

A growable list of pooled 4 MiB segments; `SaveWriter` appends to it without per-write allocation
(spans into the current segment, large arrays split across segments). Released to `SaveBufferPool`
after the file is written; the pool trims to at most one segment set after each save.

## SaveSnapshot

The result of capture (R7): the details, the thumbnail pixels and one `SaveBuffer` per section with its
id and version. Immutable after capture; owned by the write task; `Dispose` returns buffers to the pool.

## ISaveSection (interface, internal)

One implementation per section id, registered in `SaveSections` in restore order.

| Member | Meaning |
|---|---|
| `SaveSectionId Id` | Four-character id. |
| `int Version` | Current version written. |
| `bool IsPresent(GameSimulation)` | False when the system does not exist in this game (map-less scenario); the section is then not written. |
| `void Write(GameSimulation, SaveWriter)` | Capture; main thread; must not allocate once warm. |
| `void Read(GameSimulation, SaveReader, int version)` | Restore into the wired, empty system; applies migrations from `version` to `Version`. |
| `void SetDefaults(GameSimulation)` | Section missing from an older save (FR-021): leave the new-game defaults. |

## Sections in version 1 (restore order)

| Id | Owner | State |
|---|---|---|
| `CORE` | Simulation | Scenario settings (JSON, existing writer), `CurrentTick`, clock start, root random, pending commands (`CommandSerializer` JSON) and the inbox's next submission id, `VehicleFleet` and `TransportNetwork` (prototype, map-less scenarios). |
| `CNTN` | Saves | Content identity: id lists per content table and rules fingerprints (R10). |
| `MAP_` | Maps / Construction | `MapSettings`, `MapStatistics`, sites, the five tile arrays; construction year, companies (identity and balance), `TileObjects`, `UndoRecord`. |
| `ROAD` | Roads / Network | `RoadLayer` arrays; `RoadNetwork` (nodes, edges, numbering, free lists, version) verbatim; `RoadRouter` cache and its eviction order. |
| `RAIL` | Rails | `RailLayer` arrays and `EverUsed`, `RailBlocks`, `RailDepotRegistry`. |
| `TOWN` | Towns | `TownRegistry` arrays, names, random streams, `TownBuildings` and their lookups. |
| `INDY` | Industries | `IndustryFleet` arrays, tile owners, free sites, random stream. |
| `STNS` | Stations | `StationRegistry`: stations, pieces, name counters, coverage grids and dirty flags, groups, ratings, packet pools with free lists, destination buckets and pools, carry, totals, removed centres, reroute cursor, debug hold. |
| `FINA` | Finance | `FinanceSystem` levels and totals, per-company finance (loan, ledger months and years, game-over state), `RunningCosts`. |
| `RVEH` | Vehicles | `RoadVehicleSystem` hot and cold arrays, holds, routes, lanes, link tables, junction holders, route cache, budgets, version stamps, depot carry, notices; `DepotRegistry`; `LineRegistry` with lines and orders. |
| `TRAI` | Rails | `TrainSystem`: trains (consists, steps, reservations, holds), slots, block ownership, link tables, maintenance carry, budgets, counters. |
| `DMND` | Demand | `DestinationTables` (rule tables, cursor), `CargoRouter` schedule, current tables, pending result (`_nextPool`), in-progress `RoutingProblem`s and `LinkCollector` when `_buildStage > 0`. |
| `CAMR` | Client (opaque to Core) | Camera target, distance, yaw, pitch. |

`CATL` stores feature 016 renewal policies and notification state. Older saves receive empty policies. `LSTA` stores feature 017 service histories, line regulation and traffic. See the sections contract.

## ContentMap

Built on load from `CNTN`: per content table an `int[]` from saved index to installed index and a flag
"identity". Missing ids collected into `SaveLoadResult.MissingContent`; any non-empty list refuses
the load.

## SaveLoadResult (record)

| Field | Meaning |
|---|---|
| `Simulation` | The restored game (null on failure). |
| `Details` | The details block. |
| `Status` | `Loaded`, `LoadedWithChangedData`, `Damaged`, `TooNew`, `MissingContent`, `NotASave`, `IoError`. |
| `Problem` | Localisation key and arguments for the message (client shows it). |
| `ExtraSections` | Opaque client sections (`CAMR`). |

Load never throws for bad files; it returns a status (FR-019). Programming errors still throw.

## SaveCatalogEntry (client)

One row of the load screen: file path, `SaveDetails` (or null when damaged), damaged flag, thumbnail
texture handle (created lazily when the row becomes visible).

## AutosaveSettings (client, in `PlayerSettings`)

| Field | Default | Values |
|---|---|---|
| `AutosaveMinutes` | 10 | 0 (off), 5, 10, 15, 30 |
| `AutosaveKeep` | 3 | 1–10 |

`PlayerSettings.CurrentVersion` is bumped; older settings files get the defaults.

## State transitions (client)

```
Playing ──F5 / menu Save──▶ Capturing (main thread, ≤100 ms) ──▶ Writing (background) ──▶ Playing
Playing ──autosave due──▶ Capturing ──▶ Writing … (a due autosave while Writing is skipped)
Menu/Playing ──Load──▶ Confirm (if unsaved progress) ──▶ Loading (background, progress) ──▶ Playing (paused)
Loading ──failure──▶ previous screen + message (running game untouched)
```

"Unsaved progress" means at least one tick or one processed command since the last save or load.
