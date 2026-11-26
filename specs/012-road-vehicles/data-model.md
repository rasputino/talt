# Data model: Road vehicles, depots and lines

All simulation fields are integers (fixed point: 1 tile = 65 536 units, 20 ticks/s, 40 ticks per day). Money in
whole internal units (1 = one US dollar). Everything below is hashed by `RoadVehicleSystem.AppendTo` once anything
exists, except what is marked *derived* or *not state*.

## VehicleModel (data, `vehicles.json`)

| Field | Type | Rule |
|---|---|---|
| id | string | unique, `[a-z0-9-]` |
| nameKey | string | localization key |
| kind | `bus` \| `truck` | bus stops / truck stops |
| cargoCategory | `passengers` \| `bulk` \| `tanker` \| `box` | category → cargo ids in `cargoCategories` |
| capacity | int | 1–200 units |
| speedKmh | int | 10–200 |
| accelerationTilesPerSecond2 (as milli) | int | 100–5 000 (‰ tiles/s²) |
| lengthMilliTiles | int | 150–350 (fits between adjacent junctions, R4) |
| price, runningCostPerYear | long | ≥ 0 |
| lifeYears | int | 1–100 |
| reliabilityPerMille | int | 1–1000 (base, restored by servicing) |
| firstYear, lastYear | int | firstYear ≤ lastYear |
| power | `combustion` \| `electric` | breakdown effect |
| loadPerStep | int | units moved per loading step |
| color | `#RRGGBB` | default paint |

## Depot

`DepotRegistry`: SoA by slot, stable ids (never reused), swap-with-last removal: `id`, `owner`, `tile`, `direction`
(from the depot tile towards the road). Tile object `TileObjectKind.Depot` (4) owned by the company, one-link road piece
on the depot tile plus the reverse link on the road tile (like a bay). Derived: tile → depot id dictionary.

## Line and stops

`LineRegistry`: stable ids, owner, name (null = automatic "Line N"), number, colour index, `List<LineStop>` per line
(`StationId`, `Rule`: 0 LoadIfAvailable, 1 FullLoad, 2 UnloadOnly, 3 NoLoading). Consecutive duplicates are merged on
edit. A line may have 1–64 stops.

## RoadVehicle

Hot state `RoadVehicleState` (packed struct, double buffered `_state` / `_next`):

| Field | Meaning |
|---|---|
| Lane | lane key `tile × 8 + direction`, or −1 inside a depot |
| S | front position along the lane, units (may be negative or beyond the link at dead ends) |
| Speed | units per tick |
| Flags | bit flags: HoldsJunction, Requesting, AtStopPlace, Loading, BrokenDown, Stopped (by player), InDepot, NeedsRoute, UrgentRoute, Stuck, NoRoute, ArrivedEvent, DeadEndEvent, ReleasedEvent, LaneChanged |
| HeldJunction | tile of the held junction or −1 |
| RequestedJunction | tile requested this tick or −1 (phase 1 output) |
| WaitingSince | tick the current junction request started, or −1 |
| StillTicks | ticks without moving while wanting to move |
| CapSpeed | units per tick: model speed (breakdown pulses lower it) |
| Accel, Length | units per tick², units (from the model, cached) |

Cold arrays by slot: `id`, `owner`, `model`, `cargo` (cargo index carried), `homeDepot`, `line` (−1 none), `stopIndex`
(next stop in the line), `target` (kind: Stop / Depot / None; and id), `orderFlags` (ServiceDue, SendToDepot,
BreakdownRepair), `hold` (`CargoHold`), `loadTimer`, `loadedThisStep`, `route` (`RouteStep[]` reference, hashed by
content) and `routeIndex`, `ageDays`, `reliability` (‰), `daysSinceService`, `serviceInterval` (days, 0 never),
`breakdownTicksLeft`, `valueAtPurchase`, `runningHandle` (finance), `profitThisYear`, `profitLastYear`,
`purchaseDay`, `paint` (colour index).

Derived (not hashed): per lane list `ahead`/`behind` slots, lane registry `laneKey → (head, tail)` dictionary, junction
tile → holder slot dictionary, slot ↔ id dictionary.

## States (for the window, derived from flags and target)

`InDepot` → (start with orders) → `Leaving` → `Driving` → `EnteringStop` → `Unloading` → `Loading` → `Driving` …;
side states `GoingToDepot` (service, send, breakdown repair), `BrokenDown` (overlay while driving), `Waiting`
(junction or queue), `Stuck`, `NoRoute`, `Stopped`.

## Notices (not state)

`VehicleNoticeFeed` of fixed size: `(kind: Stuck | NoRoute | SkippedStop | BrokenDown | Serviced, vehicleId, x, z,
stationId)`.

## MoneyEvent (changed)

Gains `VehicleId` (−1 when not a vehicle's).
