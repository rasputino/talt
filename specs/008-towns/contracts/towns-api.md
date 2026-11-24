# Contract: Towns API (`Talt.Core.Towns.TownRegistry`)

Read by the client (window, labels, rendering, picking), by construction (rating rules) and later by
feature 010 (service and acceptance). Only the simulation thread calls mutating members.

## Queries

| Member | Meaning |
|--------|---------|
| `int Count` | number of towns (ids `0..Count-1`) |
| `int Version` | bumped by every building or street change (client caches) |
| `string NameOf(int town)` | proper name (not localized) |
| `int PopulationOf(int town)`, `BuildingCountOf(int town)` | |
| `(int X, int Z) CentreOf(int town)` | |
| `int RadiusOf(int town)` | current growth radius in tiles |
| `bool TryGetTown(int town, out TownInfo info)` | snapshot for the window (statistics, acceptance, pattern) |
| `int RatingOf(int town, int companyId)` | 0–1 000 |
| `TownRatingBand BandOf(int rating)` | Hostile, Poor, Neutral, Good, Excellent |
| `bool MayDemolish(int town, int companyId)` | rating ≥ demolish minimum |
| `bool MayBuildStation(int town, int companyId)` | rating ≥ station minimum (feature 010) |
| `bool Accepts(int town, int cargoIndex)` | goods or food eighths ≥ 8 |
| `int TownAt(int x, int z)` | owner town of a building tile, or −1 |
| `bool TryGetBuildingAt(int x, int z, out int buildingId)` | |
| `TownBuildings Buildings` | read-only access: `Count`, `TypeAt`, `XAt`, `ZAt`, `TownAt`, `BaseHeightAt`, `IdAt` |
| `int TownZoneAt(int x, int z)` | town whose zone (radius + margin) contains the tile, nearest centre wins, or −1 |
| `bool TryTakeChangedTiles(List<int> into)` | building tiles changed since the last call (client only, not state) |

## Mutations

| Member | Caller | Effect |
|--------|--------|--------|
| `void AdvanceMonth(GameDate date, bool parallel)` | month event | statistics, ratings, growth |
| `void RecordTransported(int town, int cargoIndex, int amount)` | feature 010 (and the debug service) | adds to this month's transported passengers or mail |
| `void ApplyConstruction(ConstructionPlan plan, int companyId)` | `ConstructionState.Commit` | removes demolished buildings, applies penalties; returns whether buildings were removed (undo cleared) |
| `int PlanRatingChange(ConstructionPlan plan, int companyId)` | planners (preview) | the player's total rating change, display only |

## Events and order

Month event order in `GameSimulation`: industries `AdvanceMonth`, then towns `AdvanceMonth`.
Construction commands apply at the start of a tick, before any event.
