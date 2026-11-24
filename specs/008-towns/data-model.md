# Data Model: Towns (feature 008)

All simulation fields are integers (Constitution III); types are registered in
`NoFloatingPointStateTests`.

## TownSettings (scenario settings, `Talt.Core.Towns`)

| Field | Type | Rules |
|-------|------|-------|
| NameStyle | enum `TownNameStyle` { Mixed, English, Spanish } | default Mixed |
| Size | enum `TownSize` { Small, Normal, Large } | default Normal; scales target population 50 / 100 / 200 % |
| DebugServicePercent | int | 0–100, default 0; share of each town's generated passengers and mail recorded as transported at month end |

Part of `ScenarioSettings.Towns`; written to recorded sessions as `towns` when the scenario has a map.

## BuildingType (`town-buildings.json`)

| Field | Type | Rules |
|-------|------|-------|
| Id | string | unique, lowercase |
| Category | enum { House, Apartment, Shop, Office, Civic } | |
| Width, Depth | int | 1–2 each |
| Height | int | 1–32 storeys (the client draws 0.6 world units per storey and one window row per storey) |
| Residents | int | ≥ 0 |
| PassengersPerMonth, MailPerMonth | int | ≥ 0 |
| GoodsAcceptance, FoodAcceptance | int | 0–8 (eighths) |
| EraStart, EraEnd | int | EraStart ≤ EraEnd |
| MinTownPopulation | int | ≥ 0 |
| Zones | flags { Centre, Inner, Outer } | at least one |
| Weight | int | 1–1000 |
| Level | int | 1–9; a replacement must have a higher level |
| Model | { wall: "#RRGGBB", roof: "#RRGGBB", roofStyle: pitched/flat, windows: none/rows/grid } | render only |

## TownRules (`towns.json`)

Growth: `radiusBase`, `radiusPerSqrtPopulation` (hundredths), `centreZonePercent`, `innerZonePercent`,
`attemptsPerAction`, `failuresBeforeStreet`, `growthPerMilleOfBuildings`, `replaceChancePercent`,
`replaceMinPopulation`, `gridSpacing`, `gridPatternPercent`, `streetSegmentMin`, `streetSegmentMax`,
`foundingAttemptBudget`.
Sizes: list of `{ weight, minPopulation, maxPopulation }`; `sizeScalePercent` { small, normal, large }.
Ratings: `initial`, `min`, `max`, `bands` (hostile, poor, neutral, good thresholds), `demolishMinimum`,
`stationMinimum`, `buildingPenalty`, `roadPenalty`, `treePenalty`, `maxPenaltyPerAction`,
`zoneMargin`, `recoveryPerMonth`, `serviceTargetSpan`.
Plus `BuildingTypes` and a content fingerprint (hashed with the registry).

## TownNameParts (`town-names.json`)

Per style: `starts`, `middles`, `ends`, `prefixWords`, `suffixWords`, `patterns` (weights for
start+end, start+middle+end, prefix+name, name+suffix). Validated: non-empty lists, unique entries,
letters/spaces/hyphens/apostrophes only, total weights > 0.

## Town (`TownRegistry`, one slot per town, id = slot)

| Field | Type | Notes |
|-------|------|-------|
| Id | int | 0..Count-1, equals `TileOwner.Town(id).Index` |
| CentreX, CentreZ | int | the site |
| SiteIndex | int | index among the map's town sites |
| Name | int (index into name table) | name strings hashed as UTF-8 |
| Pattern | byte { Grid, Organic } | |
| Population | int | sum of residents |
| BuildingCount | int | |
| Passengers/MailProduction | int | totals per month (from buildings) |
| GoodsAcceptance, FoodAcceptance | int | eighths totals |
| GrowthPoints | int | 0–999 carried between months |
| Generated/Transported ThisMonth/LastMonth (passengers, mail) | int ×8 | statistics |
| Ratings[company] | int | 0–1 000 |
| Random | DeterministicRandom | forked from the registry stream by town id |

State transitions: founded (creation) → each month: statistics rotate → ratings move → growth (if
served). Never removed. Population 0 is allowed (all buildings demolished).

## TownBuilding (`TownBuildings`)

| Field | Type | Notes |
|-------|------|-------|
| Id | int | stable, never reused |
| Type | int | index into building types |
| X, Z | int | origin (minimum corner) |
| Town | int | owner town id |
| Year | int | construction year |
| BaseHeight | int | foundation height (highest footprint corner) |

Invariants: footprint tiles have kind `TownBuilding` and owner `Town(town)`; no overlaps; at least one
footprint tile was next to a town road when placed (roads may be removed later by the player).

## Construction additions

- `TileObjectKind.TownBuilding = 2`; `construction.json` `demolishCosts.townBuilding`.
- `ConstructionReasons.TownRefuses = "town-refuses"`.
- `ConstructionPlan.RatingChange` (int, display only; not part of a command's serialized form).
- `ConstructionState.Towns` (attached registry; not hashed by construction state).

## Hashing order

`GameSimulation`: tick, clock, random, network, vehicles, industries, construction (when present), towns
(when present): settings values, rules fingerprint, town arrays (used slots), building arrays, ratings,
name table bytes, registry and per-town streams.
