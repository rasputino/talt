# Data model: Stations and cargo handling (feature 010)

All simulation fields are integers (fixed point where noted) and are hashed by
`StationRegistry.AppendTo` when at least one station exists. Types live in `Talt.Core.Stations`.

## StationRules (stations.json, validated at load)

| Field | Meaning |
|-------|---------|
| `pieces[]` | road piece types: `id`, `nameKey`, `kind` (`bus`/`truck`), `form` (`bay`/`driveThrough`), `cost`, `removeCost`, `catchmentRadius`, `categories[]` (cargo categories handled), `model` colours |
| `warehouses[]` | `id` (`small`/`medium`/`large`), `nameKey`, `width`, `depth`, `cost`, `removeCost`, `influenceRadius`, `model` colours |
| `maxSpread` | largest station bounding box side (tiles) |
| `waitingCap` | most units of one cargo waiting per group |
| `maxStationsPerProducer` | groups that share one producer's output |
| `acceptance.passengersFullAt`, `acceptance.mailFullAt` | monthly production that gives 8 eighths |
| `rating.initial`, `rating.base`, `rating.maxStepPerDay`, `rating.overflowPenalty` | rating rules |
| `rating.pickupDays[]`, `rating.speedKmh[]`, `rating.waiting[]` | `[threshold, points]` tables |
| `loss.belowRating`, `loss.aboveWaiting`, `loss.perMilleAtZero` | daily loss of neglected cargo |
| `debug.intervalDays`, `debug.speedKmh` | `--station-service` behaviour |

Validation: unique ids, known categories, radius 1–8, influence 1–64, footprint 1–6, positive costs,
tables sorted, cargo count ≤ 64 (masks).

## Station pieces (SoA, slot = piece index, swap-remove)

| Field | Type | Notes |
|-------|------|-------|
| `_pieceTile` | int | `x + z * width` |
| `_pieceType` | byte | index into `pieces`, or 255 for a warehouse footprint tile |
| `_pieceDir` | byte | bay: direction of its road (Dir8 straight); drive-through: axis (0 = X, 2 = Z) |
| `_pieceStation` | int | station id |
| `_pieceAt` | Dictionary<int,int> | tile → piece slot (not hashed; rebuilt from the arrays) |

## Stations (SoA by slot; ids stable, never reused; `_slotById`)

| Field | Type | Notes |
|-------|------|-------|
| `_id`, `_owner` | int | company id |
| `_kind` | byte | 0 road station, 1 warehouse |
| `_warehouseType` | byte | warehouse size index |
| `_x0,_z0,_x1,_z1` | int | bounding box of pieces (inclusive) |
| `_town`, `_nameKind`, `_nameNumber` | int/byte/int | automatic name; `_nameKind`: bus, truck, warehouse suffix 0–8 |
| `_customName` | string? | renamed (hashed as UTF-16) |
| `_categories` | ushort | union of handled categories (bits) |
| `_radius` | byte | largest catchment radius of its pieces (union of squares per piece) |
| `_group` | int | group slot |
| `_acceptsMask`, `_suppliesMask` | ulong | cargo bits (derived from coverage; not hashed) |

Derived, not hashed: coverage lists, acceptance eighths, producer table, station grid.

## Groups (SoA by slot; key = lowest station id)

Per group: `_groupKey`, `_groupStationCount`. Per group × cargo (`slot * cargoCount + c`):

| Field | Notes |
|-------|-------|
| `_served` | byte, 1 after `MarkServed` |
| `_rating` | 0–1000 |
| `_waitingTotal` | sum of packet amounts |
| `_packetHead`, `_packetTail` | FIFO list in the packet pool |
| `_lastPickupDay` | day number of the last load, or −1 |
| `_lastSpeedKmh` | speed of the last loading vehicle |
| `_received*`, `_pickedUp*`, `_delivered*`, `_lost*` | this month / last month |

## CargoPacket (pool, free list)

`Cargo`, `Amount`, `Origin` (station id), `SourceTown` (−1 unless passengers/mail), `Destination`
(−1 = any, feature 013), `PickupDay` (−1 while waiting fresh), `Next`.

## Supply carry

`Dictionary<long, int>`: key = producer kind (industry 1 / building 2) << 56 | id << 8 | cargo; value
0–999 thousandths. Hashed sorted by key.

## CargoHold (Core class, owned by vehicles in 012 and tests)

Array of `HeldCargo(Cargo, Amount, Origin, SourceTown, Destination, PickupDay)`; `Capacity` in units
per cargo is the caller's concern (Load takes `maxAmount`). Not part of the registry hash (vehicles
hash their holds in 012).

## CargoDelivery (event to feature 011)

`Cargo`, `Amount`, `OriginStation`, `DestinationStation`, `PickupDay`, `DeliveryDay`, `ConsumerKind`
(industry, town), `ConsumerId`, `SourceTown`, `CompanyId`.

## State transitions

- Piece built → station created (named) or joined → groups recomputed → coverage dirty.
- Piece removed → station shrinks; last piece → station and its packets removed (lost) → groups
  recomputed.
- Day: coverage rebuild if dirty → supply → ratings/loss → debug service.
- Month: statistics rotate.
- `MarkServed` → served flag on; `Load` → pickup inputs; `Unload` → deliveries/transfers.
