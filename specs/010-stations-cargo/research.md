# Research: Stations and cargo handling (feature 010)

Decisions for the design. Mechanics were studied from existing transport games only to understand how
stations, catchment and ratings work in general; every rule, formula, name and value below is designed
for TALT.

## R1 — One registry for stations, warehouses and groups

**Decision**: A new Core system `Talt.Core.Stations.StationRegistry`, owned by `GameSimulation` when
the scenario has a map and attached to `ConstructionState` (like towns and industries). It stores
station pieces, stations (road stops and warehouses share one id space, so rename, picking and the
window work the same for both), station groups, waiting cargo and statistics in flat arrays.
Map-less games have no registry, and a registry without stations appends nothing to the fingerprint
(FR-030), so every existing golden stays unchanged until a station is built.

**Rationale**: matches the `IndustryFleet` / `TownRegistry` pattern; one place owns the cross-cutting
cargo rules; stations are rare objects (≤ ~10 000) so small SoA arrays are enough.

**Alternatives**: an ECS entity per station (no benefit at this count); stations inside
`ConstructionState` (mixes tile data and cargo economics).

## R2 — Piece placement: bays and drive-through stops

**Decision**:
- *Bay*: a flat, empty, dry tile beside a road on one of its four straight sides. The tile gets
  `TileObjectKind.Station` (new value 3) owned by the company and a one-link road piece pointing at the
  road (same road type as the neighbour); the neighbour gains the reverse link. Both are ordinary
  `TileChange`s with `RoadChanged`, so `ConstructionState.Commit` updates the routing graph and a bay is
  a dead-end node vehicles (012) can route to. When several sides have roads, the first in N, E, S, W
  order is used (the preview shows which).
- *Drive-through*: a straight road tile (exactly two opposite straight links, no bridge/tunnel end, no
  connector, not a motorway) owned by the company or by a town that allows stations. The tile keeps its
  `Road` object, owner and piece; the station piece is recorded only in the registry. The plan contains
  one unchanged `TileChange` so it is valid and the tile is reported to the client.
- One tool per station type picks the form from the tile under the cursor: a straight road tile → drive-
  through, an empty tile → bay (clarification Q1: both forms, without a form toggle).
- Road protection: `RoadPlanner` refuses to remove, or add links to, a road tile that holds a
  drive-through piece or that a bay links to (`station-road`); town street growth skips such tiles.

**Rationale**: bays need no changes to the road layer format and routing sees them for free;
drive-through pieces reuse the road under them. The protection rule avoids pieces whose road link was
removed from under them.

**Alternatives**: a station layer flag inside `RoadPiece` (changes the road hash and goldens); manual
form toggle (one more button for no gain).

## R3 — Joining and spread

**Decision**: a new road-stop piece joins the company road station that has a piece on one of the
eight neighbouring tiles; two different such stations → refused (`adjacent-two-stations`); the joined
station's bounding box may not exceed `maxSpread` tiles per side (data, 12) → `station-too-spread`.
Warehouses never join road stops by adjacency. Removing a piece never splits a station (FR-005 /
US5-4); the station's bounding box is recomputed from its remaining pieces.

## R4 — Warehouse groups

**Decision**: a group is a connected component of the graph whose nodes are a company's warehouses and
road stations: a road station is linked to a warehouse when one of its pieces lies inside the
warehouse's influence square (Chebyshev distance from the footprint ≤ radius); two warehouses are
linked when their influence squares overlap. A road station linked to no warehouse is a group of its
own; a lone warehouse is a group of its own. Groups are recomputed (union–find in station-id order)
after every station or warehouse change, never per tick.

State per group and cargo: served flag, rating, rating inputs, waiting packets, statistics.
Recomputing maps old groups to new ones:
- merge: waiting packets are concatenated (old group order by lowest station id), served = OR, rating
  and inputs from the old group with the highest rating, statistics summed;
- split: each old group's packets are divided among the new groups holding its stations in proportion
  to the number of its stations each new group got (largest remainders to the new group containing the
  lowest station id), ratings and inputs copied (FR-007c).

**Rationale**: deterministic (fixed iteration order), conserves cargo, cheap (construction time only).

**Alternatives**: warehouses holding a separate stock that stations push to (needs a transfer model,
not what the clarification asked); first-warehouse-wins membership (rejected in clarification).

## R5 — Catchment and coverage tables

**Decision**: each station keeps its catchment as the union of `(2r+1)²` squares around its pieces
(radius from the piece type; warehouses have none). Coverage is precomputed, not scanned daily:
- per station: the industries with a footprint tile in the catchment (with the cargo they produce and
  accept) and the town buildings with a footprint tile in it (each building counted once);
- acceptance per station and cargo in eighths: industry accepting the cargo → 8; town buildings: goods
  and food eighths from `town-buildings.json`; passengers and mail derived from the building's monthly
  production (`stations.json` `acceptance`: eighths = min(8, monthly × 8 / fullAt)). A station accepts
  a cargo at ≥ 8 eighths; a group accepts what any member accepts.
- a producer table in CSR form: one row per producer (industry or town building) that is covered by at
  least one station, with the list of covering stations. Rebuilt only when dirty.

Dirty tracking: station construction marks the station dirty; industries' `Version` change re-derives
industry coverage of all stations through a coarse station grid (16-tile cells); town building changes
are reported to the registry through a new simulation-side changed-tile list in `TownBuildings`
(independent of the client's list), and only stations whose catchment contains a changed tile are
rebuilt, using the same grid. The rebuild runs at the start of the next daily update.

**Rationale**: the daily update must stay under 2 ms for 5 000 stations (SC-001); scanning 100 tiles per
station per day through dictionaries would cost several milliseconds.

## R6 — Daily supply and rounding

**Decision**:
- Industries: `IndustryFleet.AdvanceDay` gains an optional `IProductionSink`. For every daily amount it
  asks the sink how much it takes (`TakeProduction(industryId, cargo, amount)`); the rest goes to the
  industry's stock as in 009 (FR-014). With no sink, or nothing served, behaviour and fingerprints are
  identical to 009.
- Town buildings: each day the registry walks the producer table's town rows and computes each
  building's daily passengers and mail with the same day-spreading rule industries use
  (`IndustryEconomy.DailyAmount`, exact over a month).
- Share: for a producer and cargo with serving groups (served flag set, the group handles the cargo,
  at least one of its stations covers the producer), `moved = (amount × bestRating + carry) / 1000`,
  `carry = remainder`; the carry is kept per producer and cargo in the registry (FR-017). The moved
  amount is split among the best `maxStationsPerProducer` groups (data, 2; ties → lower group key) in
  proportion to their ratings, remainder to the best. Each part is added to the group's packet for
  `(origin station = the covering member with the lowest id, source town, destination any, pickup −1)`.
- Excess over the cap (4 000 per group and cargo) is lost, counted in `lost` statistics and lowers the
  rating by `overflowPenalty`.

**Rationale**: the sink keeps 009's API and stock semantics; per-producer carry makes totals exact; no
randomness is needed for supply.

## R7 — Ratings

**Decision**: integer rating 0–1000 per group and cargo, initial 550 when first served. Each day:
`target = base + pickupPoints(daysSinceLastPickup) + speedPoints(lastSpeedKmh) + waitingPoints(waiting)`
clamped to 0–1000; the rating moves toward the target by at most `maxStepPerDay`. Point tables are in
`stations.json` (thresholds, first match). Loss: when rating < `loss.belowRating` and waiting >
`loss.aboveWaiting`, `waiting × loss.perMilleAtZero × (belowRating − rating) / belowRating / 1000`
units (at least 1) are removed from the oldest packets. Vehicle speed and pickups come from `Load`
(012) or the debug service. No randomness.

**Rationale**: deterministic, tunable in data, reproduces the intended feel (fast, frequent service →
high rating; neglect → falling rating and losses).

## R8 — Waiting cargo packets

**Decision**: a pool of packets (`CargoPacket` struct: cargo, amount, origin station, source town,
destination, pickup day, next) linked per group and cargo in FIFO order, with a free list and a total
per group-cargo. Packets with the same key are merged on arrival (search limited to the list, typically
1–3 packets). `CargoHold` (Core, used by 012 vehicles and tests) stores held packets in a small array;
packets in a hold always have a pickup day (the day they were loaded, or kept from a transfer).

## R9 — Loading and unloading operations

**Decision** (contracts/stations-api.md):
- `MarkServed(stationId, cargo)`: the group starts receiving that cargo (rating initialised).
- `Load(stationId, cargo, maxAmount, hold, speedKmh)`: takes from the group's packets oldest first,
  sets pickup day for fresh packets to today, records last pickup and speed, counts town passengers and
  mail as transported (`TownRegistry.RecordTransported` with the packet's source town), returns the
  amount loaded.
- `Unload(stationId, hold, UnloadMode mode)`: for each held packet whose cargo the group accepts and
  whose origin is not in this group, hands it to consumers: industries in member catchments (unloading
  station first, then members by id; industries by id) through `IndustryFleet.Deliver`; towns for
  passengers, mail, goods and food (accepted whole). Each accepted part produces one
  `CargoDelivery` sent to `IDeliveryListener` (feature 011) and counted in statistics. Not accepted:
  stays in the hold, or with `UnloadMode.Transfer` joins the group's waiting packets with its origin and
  pickup day. Returns a summary (delivered, transferred, kept).
- All operations conserve cargo; a conservation test runs 1 000 days of random operations (SC-003).

## R10 — Debug service (`--station-service`)

**Decision**: `StationSettings(int DebugServicePercent)` in `ScenarioSettings.Stations` (null = default
0), written to recorded sessions as an optional `stations` object only when set (so
`windows-reference.json` is unchanged). Each day every road station's handled cargo that it receives is
marked served; every `debugIntervalDays` (data, 5) each group loads that percentage of each waiting cargo
into a discard hold at `debugSpeedKmh` (data, 60), through `Load`, so ratings and town service react as
with vehicles.

## R11 — Names

**Decision**: a station stores `townId`, a name kind (bus stop, truck stop, warehouse) with a number,
and an optional custom name. Road stops: kind from the first piece, number = 1 + count of the company's
stations of that town and kind ever named (so numbers are unique per town and kind); number 1 is shown
without a digit. Warehouses: place suffix from the position relative to the town centre (central when
within half the town radius, else one of eight compass directions), with a number when the same suffix
repeats. The client formats `station.name.<kind>` / `station.name.warehouse.<suffix>` with the town
name; a custom name is shown as typed. `RenameStationCommand` (≤ 32 characters, trimmed; empty restores
the automatic name).

## R12 — Commands and undo

**Decision**: `BuildStationCommand(companyId, pieceType, x, z)`, `BuildWarehouseCommand(companyId,
size, x, z)`, `RemoveStationCommand(companyId, x, z)` are `ConstructionCommand`s planned by a
`StationPlanner`; `ConstructionCommand` gains a protected virtual `Committed(simulation)` called after
`Commit`, where the registry records the piece. Station commands clear the undo record (like town
demolition in 008), since registry state is not tile data. `RenameStationCommand` is a plain
`ISimulationCommand`. All four are registered in `CommandSerializer`.

## R13 — Client

**Decision**: a third toolbar column with tools `busStop`, `truckStop`, `warehouse` (+ size cycle button,
like the road type) and `removeStation`; previews reuse `PreviewBoxes` plus catchment tiles in the
preview area colour; the cursor label adds "Receives … · Accepts …". Placeholder station boxes are
appended to the industry placeholder instances (same pipeline, no renderer change; 018 replaces them).
`StationPickLayer` turns a picked station tile into `PickKind.Station`; `StationWindow` shows the
figures and a catchment toggle and a rename field (`TextInput`). Localized in English and Spanish.

## R14 — Performance

**Decision**: daily update = rebuild dirty coverage (rare) → supply over the producer table →
per-group rating/loss phase (parallel above 256 groups; touches only that group's slots; equal to
sequential) → debug service. Monthly: rotate statistics. Preallocated arrays, no allocation in steady
state. Benchmarks: `StationBenchmarks` (5 000 served stations on a generated map: daily update, daily
update with warehouse groups, monthly update, catchment preview), numbers in ARCHITECTURE.md.
