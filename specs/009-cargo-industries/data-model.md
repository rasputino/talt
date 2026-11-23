# Data Model: Cargo Types and Industries

All gameplay types live in `Talt.Core.Industries` and `Talt.Core.Content`; the render-only instance
lives in `Talt.Client`. Amounts are whole `int`s in each cargo's own unit (R2); no type holds a
floating-point field.

## Content (immutable after load)

### CargoCategory (enum)

`RawMaterial`, `ProcessedGood`, `Passenger`, `Mail`, `Goods`. Passengers, mail and goods must require a
destination; raw materials and processed goods must not (R11).

### CargoDefinition (record)

| Field | Type | Rules | Meaning |
|-------|------|-------|---------|
| `Id` | `string` | `[a-z0-9-]+`, unique | Stable data id (`coal`, `iron-ore`) |
| `NameKey` | `string` | non-empty | Localization key (`cargo.coal`) |
| `Category` | `CargoCategory` | — | Used by the UI and pricing (011) |
| `UnitKey` | `string` | non-empty | Localization key for the unit (`unit.tonnes`) |
| `DestinationRequired` | `bool` | consistent with `Category` | Destination-free delivery when false |
| `ValueDecayPerDayPerMille` | `int` | 0–1000 | Loss of value per in-game day of travel |
| `MinimumValuePercent` | `int` | 0–100 | Value floor after decay |
| `Color` | `uint` | `0xRRGGBB` | UI/icon colour |
| `VehicleKinds` | `VehicleKind` flags | at least one | Which vehicle kinds can carry it |

`VehicleKind`: `Road = 1`, `Rail = 2`, `Ship = 4`, `Air = 8`.

### IndustryKindDefinition (record)

| Field | Type | Rules | Meaning |
|-------|------|-------|---------|
| `Id` | `string` | unique | `coal-mine`, `steel-mill` |
| `NameKey` | `string` | non-empty | `industry.coal-mine` |
| `FootprintWidth`, `FootprintDepth` | `int` | 1–5, fits the 5×5 free square | Occupied tiles |
| `Model` | `ModelBox[]` | ≥ 1, inside footprint, height 1–12 | Placeholder voxel boxes |
| `Production` | `ProductionEntry[]` | may be empty, unique cargo | Monthly output |
| `Accepts` | `string[]` | unique cargo, may be empty | Cargo the industry takes |
| `Recipes` | `RecipeDefinition[]` | unique input cargo | Automatic processing |
| `SiteKinds` | `IReadOnlyList<SiteKind>` | non-empty, no `Town` | Coast/Forest/Hills/Plain |
| `EraStart`, `EraEnd` | `int` | 1800–9999, start ≤ end | When it may open |
| `Weight` | `int` | 1–1000 | Relative placement probability |
| `Stock` | `StockDefinition[]` | capacity > 0 for every produced/output and recipe-input cargo | Per-cargo cap |
| `AcceptsWithoutRecipe` | derived | accepted cargo with no recipe is consumed | Power station coal |

`ProductionEntry(string CargoId, int PerMonth)` with `PerMonth` 0–1 000 000.
`RecipeDefinition(string InputCargoId, int InputAmount, string OutputCargoId, int OutputAmount)`;
amounts 1–10 000, input ≠ output.
`ModelBox(int X, int Y, int Z, int Width, int Depth, int Height, uint Color)`: X/Z offsets in tiles
relative to the footprint's centre, Y height above the ground in voxels, size in tiles/voxels; every
box must stay inside the footprint's horizontal extent and the kind's maximum height.
`StockDefinition(string CargoId, int Capacity)`.

### EconomyRules (record, `economy` object in `industries.json`)

| Field | Type | Default | Meaning |
|-------|------|---------|---------|
| `InitialOccupancyPercent` | `int` 0–100 | 75 | Sites occupied at game start |
| `DriftPerMille` | `int` 0–200 | 15 | Monthly seeded drift (±) |
| `GrowthPerMille` | `int` 0–500 | 25 | Monthly growth with deliveries |
| `DeclineAfterMonths` | `int` ≥ 0 | 24 | Neglect before decline starts |
| `DeclinePerMille` | `int` 0–500 | 20 | Monthly decline while neglected |
| `CloseAfterMonths` | `int` ≥ DeclineAfterMonths | 60 | Neglect before closing is possible |
| `CloseChancePerMille` | `int` 0–1000 | 8 | Monthly closing roll |
| `CloseAtRatePerMille` | `int` | 700 | Closing requires the rate at or below this |
| `OpeningChancePerMillePerSite` | `int` 0–1000 | 4 | Monthly opening roll per free site |
| `MaxOpeningsPerMonth` | `int` 0–100 | 3 | Cap on new industries per month |
| `MinRatePerMille`, `MaxRatePerMille` | `int` | 300, 1500 | Rate clamp, min < max |

### GameContent (sealed class)

| Member | Meaning |
|--------|---------|
| `CargoCount`, `IndustryKindCount` | Sizes of the catalogues |
| `Cargo(int index)`, `CargoIndex(string id)` | Cargo access and id lookup |
| `IndustryKind(int index)`, `IndustryKind(string id)` | Kind access and id lookup |
| `AllCargo`, `AllIndustryKinds` | Read-only views for tests and tools |
| `Economy` | `EconomyRules` |
| `Fingerprint()` | 64-bit `StateHasher` over all ids and values; independent of JSON formatting |
| `LoadDefault()`, `Default` | Embedded `content/cargo.json` + `content/industries.json`, cached |
| `FromJson(cargoJson, industriesJson)` | Test/modding entry point; validates and throws `InvalidDataException` |

Validation is defined in research R11 and must name the data file and the offending id/field.

## Simulation state

### IndustryFleet (sealed class, `IStateHashable`)

Owns all industry instances and the placement bookkeeping. Count is the number of open industries.

Scalar SoA arrays, indexed by slot:

| Array | Type | Meaning |
|-------|------|---------|
| `_kind` | `int[]` | Index into `GameContent` |
| `_id` | `int[]` | Stable id (never reused) |
| `_x`, `_z` | `int[]` | Centre tile of the footprint |
| `_ratePerMille` | `int[]` | Production condition, 1000 = nominal |
| `_monthsNeglected` | `int[]` | Months since the last accepted delivery |
| `_siteIndex` | `int[]` | Reservation this industry occupies |
| `_deliveredThisMonth` | `int[]` | Sum of accepted cargo for growth |

Per-cargo tables (`index = slot * CargoCount + cargoIndex`):

| Table | Meaning |
|-------|---------|
| `_available` | Produced/processed cargo waiting for transport |
| `_pendingInput` | Recipe input waiting for a whole batch or output space |
| `_acceptedThisMonth`, `_acceptedLastMonth` | Cargo delivered to the industry |
| `_shippedThisMonth`, `_shippedLastMonth` | Cargo taken by transport (feature 010) |

Placement bookkeeping:

| Field | Type | Meaning |
|-------|------|---------|
| `_sites` | `IReadOnlyList<Site>` | Industry sites of the map (reference, no copy) |
| `_siteOccupied` | `bool[]` | Indexed by site |
| `_freeSites` | `int[]`, `_freeCount` | Sites without an industry, swap-removal |
| `_tileOwner` | `Dictionary<int,int>` | Tile key → industry id for picking/construction |
| `_slotById` | `Dictionary<int,int>` | Industry id → slot for the window |
| `Random` | `DeterministicRandom` | Forked industry stream |
| `Version` | `int` | Incremented on placement, opening and closing (client cache) |
| `MapWidth`, `MapDepth` | `int` | Bounds for footprints and tile keys |

Public read API (see contracts): `Count`, `Content`, `SiteCount`, `FreeSiteCount`, `IdAt(slot)`,
`KindAt(slot)`, `XAt`, `ZAt`, `RatePerMilleAt`, `AvailableAt`, `PendingInputAt`,
`AcceptedLastMonthAt`, `ShippedLastMonthAt`, `TryGetIndustry(id, out IndustryInfo)`,
`TryGetIndustryAt(x, z, out int id)`, `IsOccupied(x, z)`, `OpenSlots()` (slot enumeration).

Public operations: `Create(GameContent content, MapData map, int startYear, DeterministicRandom random,
int? initialOccupancyPerMille = null)`, `CreateEmpty`, `AdvanceDay(GameDate)`,
`AdvanceMonth(GameDate)`, `Deliver(id, cargoIndex, amount)`, `Take(id, cargoIndex, amount)`
(for 010), `AppendTo(hasher)`.

### IndustryInfo (read-only record struct, for the window)

`Id`, `X`, `Z`, `KindId`, `NameKey`, `RatePerMille`, `MonthsNeglected`, `Production` (entries),
`Accepts`, `Recipes`, `Available` (cargo + amount), `PendingInput` (cargo + amount), `LastMonth`
(per cargo: accepted, shipped). Built from live arrays on demand; contains no mutable references.

### IndustryDeliveryResult (read-only record struct)

`Outcome` (`Refused`, `Accepted`, `PartiallyAccepted`), `Accepted`, `Refused`, `Consumed`,
`ConvertedCargoId` and `ConvertedAmount` (the output produced by a recipe, if any). Used by tests now
and by feature 010.

### ScenarioSettings (existing record, extended)

Adds `MapSettings? Map = null`. It is optional metadata used to record/replay the map; it does not
change the network or fleet. `With` expressions preserve it. Validation delegates to
`MapSettings.Validate()` when present.

### RecordedSession (existing format, extended)

Writes `"map": "<MapSettings.ToText()>"` inside `settings` when `Settings.Map` is not null, and reads
it back with `MapSettings.TryParse`. Absence means a map-less session (existing files stay valid).
`SessionReplayer` recreates the map through `GameSimulation.Create`, which generates it when
`Settings.Map` is set (R7).

## Relationships and lifecycle

```text
MapSettings --(MapGenerator)--> MapData.Sites --(IndustryFleet.Create)--> Industry instances
GameContent (cargo + kinds + economy) ------/             |
                                                          +-- daily: production into _available
                                                          +-- monthly: stats, rate, growth, close, open
ScenarioSettings + MapData + GameContent --(GameSimulation.Create)--> GameSimulation.Industries
```

State transitions of one industry:

1. **Open** (initial placement or monthly opening): tiles occupied, site marked used, id assigned.
2. **Producing / accepting**: daily production, deliveries, recipes, monthly statistics.
3. **Declining**: after `DeclineAfterMonths` without deliveries, the rate falls each month.
4. **Closed**: a successful closing roll removes the instance (swap-with-last), frees its site and
   tiles and removes it from the id/slot maps; a later industry may open there with a new id and no
   old state.

Invariants:

1. Open industries never share tiles or sites; every footprint lies inside the map and the site's free
   square.
2. `0 ≤ available ≤ capacity` and `0 ≤ pendingInput ≤ capacity` for every cargo.
3. A month's production equals `perMonth * rate / 1000` whole units before caps, independent of how
   days are split.
4. Amounts never become negative; closing and opening happen only on month ends.
5. `Count == 0` implies the fleet contributes nothing to the state fingerprint.

## Client-only types

- `IndustryInstance` (struct, render-only floats): `Position` (base centre), `Extent` (size), `Color`.
  One per model box; `GameSession` rebuilds when `IndustryFleet.Version` changes.
- `PickResult` gains `IndustryId` (`-1` when not an industry) and `PickKind.Industry`; the key is
  `industry:<id>`; `SameObject` compares the id.
- `IndustryPickLayer` uses the kind's footprint and model height for its box and returns the id.

## Files

| File | Change |
|------|--------|
| `src/Talt.Core/Content/Data/cargo.json` | new |
| `src/Talt.Core/Content/Data/industries.json` | new |
| `src/Talt.Core/Content/*.cs` | new definitions and loader |
| `src/Talt.Core/Industries/*.cs` | new fleet, placement, delivery, info |
| `src/Talt.Core/Simulation/GameSimulation.cs` | content, industries, events, Create overload |
| `src/Talt.Core/Scenarios/ScenarioSettings.cs` | optional map |
| `src/Talt.Core/Commands/RecordedSession.cs` | optional map text |
| `src/Talt.Core/Commands/SessionReplayer.cs` | recreate the map through Create |
| `src/Talt.Client/**` | map→simulation order, industry instances, picking, window, strings |
| `tools/Talt.Headless/**` | `--map-text` on `run`, industry count in the summary |
| `benchmarks/Talt.Benchmarks/IndustryBenchmarks.cs` | new |
| `tests/Talt.Core.Tests/Industries/**` | new |
