# Research: Road vehicles, depots and lines

All decisions below are original designs for TALT. Existing games were only used to understand which mechanics
players expect (depots, shared orders, loading rules, breakdowns); no code, data or text was taken from them.

## R1 — Synthetic traffic and the map-less goldens

**Decision**: Keep `VehicleFleet` and `SyntheticTraffic` unchanged for map-less scenarios only (benchmarks of feature
001, `GoldenFingerprintTests`, `ci/golden-fingerprints.json`, `ci/sessions/windows-reference.json`). Road vehicles are
a new system, `RoadVehicleSystem` (namespace `Talt.Core.Vehicles`), created only for games with a map, next to
stations and finance. Games with a map already have an empty prototype fleet (feature 007), so nothing changes for
them until a depot is built or a vehicle bought.

**Rationale**: The map-less goldens are the cross-platform determinism gate and must not move without a rule change.
The synthetic fleet moves on an abstract graph (`TransportNetwork`), not on road tiles, and has no stations, cargo or
money; turning it into road vehicles would change every golden for no gameplay benefit. The new system hashes
nothing until it holds a depot, line or vehicle, so every existing map golden (construction, roads, towns, stations,
economy) is unchanged too.

**Alternatives considered**: (a) Evolving `VehicleFleet` in place: every golden changes and the abstract-graph
benchmarks lose their meaning. (b) Removing the synthetic traffic: the 001 benchmarks and CI gate would need a map
(4096 generation in CI is 2 s plus town founding), and the windows-reference session would have to be re-recorded.
(c) Feeding synthetic lines with road vehicles: mixes two models of movement.

## R2 — Lanes and positions

**Decision**: A **lane** is one directed road link: `(from tile, direction)` (eight directions, long links for bridges
and tunnels). Lane key = `tile × 8 + direction`. A vehicle's position is its lane and `S`, the distance in units of
its **front** from the centre of the from-tile along the link (0 … link length; dead ends extend it, R5). Two-way
roads give two opposite lanes per link (right-hand traffic is only a drawing offset); one-way links and motorway
carriageways give one lane. Vehicles in one lane are kept in a doubly linked list ordered by `S` (front first): a
derived index rebuilt from the vehicle states, never hashed.

**Rationale**: Directed links are the natural unit of the road layer (link and exit masks), need no extra storage on
the map, make "same lane" exact and make lane changes local (a vehicle leaves the front of one lane and joins the
rear of the next). With no overtaking the order inside a lane never changes, so the list is updated only when vehicles
enter or leave a lane.

**Alternatives considered**: Lanes per contracted-graph edge (fewer lane changes, but positions need a tile walk to
draw and to check tiles under vehicles); per-tile occupancy grids (16 M tiles on a 4096 map); sorting all vehicles by
lane every tick (50 000 keys a tick, ~3 ms).

## R3 — Speeds, following distance and the no-overlap guarantee

**Decision**: Integer kinematics like feature 001 (units, units per tick). Per tick a vehicle chooses its new speed
from the candidates {accelerate (model acceleration), hold, brake by half of D, brake by D} where D is the common
braking deceleration (data), capped by the model's speed, the lane's road speed limit, the breakdown cap and the
obstacle ahead. The obstacle is the nearest of: the rear of the vehicle ahead (same lane or the next lanes along the
path, looked up to a fixed distance), a junction stop line when the vehicle does not hold that junction (R4), the stop
place of its target (R5), a dead-end turning point. Two conditions, both evaluated against the **previous tick's**
positions and speeds:

- hard: `S + v ≤ obstacle − minGap` (always enforced; if no candidate satisfies it, the vehicle takes the largest
  speed that does, down to 0: an "emergency" stop that the tests show never happens in practice);
- soft: `S + v + B(v) ≤ obstacle − minGap + B(v_leader)`, with `B(v)` the exact distance needed to stop from `v`
  braking by D each tick (`k = v / D`, `B = k·v − D·k(k+1)/2`); for static obstacles `v_leader = 0`.

Because every vehicle reads only the previous tick and leaders never move backwards, the hard condition alone proves
that no two vehicles in a lane ever overlap, whatever order the vehicles are updated in. The soft condition is the
safe-distance rule (Gipps-style): a follower can always stop behind its leader even if the leader brakes fully, so
braking stays gentle.

**Rationale**: Reading only the previous state makes the speed phase embarrassingly parallel and exactly equal to a
sequential run. Four candidates avoid a square root per vehicle and give a smooth enough speed profile; the hard rule
makes the overlap guarantee independent of the tuning.

**Alternatives considered**: Updating vehicles front to back with fresh positions (sequential per lane, needs an
ordering across lanes at junctions, not parallel-safe); intelligent-driver models with real numbers (floats are
forbidden in simulation state); solving the braking equation exactly (integer square root per constrained vehicle).

## R4 — Junction reservations

**Decision**: A **junction** is a road tile with three or more links. Its **box** is the area within `junctionRadius`
(data, 0.22 tile) of its centre: on an incoming lane from `S = length − r`, on an outgoing lane up to `S = r`. A vehicle
may cross the stop line only while it **holds** the junction. Rules:

1. A vehicle that is within braking distance of the stop line (or stopped at it) and has room on the lane it will leave
   by (the rear of the last vehicle on that lane at least `r + length + minGap` from its start, previous tick) is a
   **requester** for that junction with key `(waitingSinceTick, vehicleId)`; `waitingSinceTick` is set when it first
   requests and kept while it waits.
2. After the movement phase, each free junction is granted to its smallest key (first come, first served; ties by
   lower vehicle id). One vehicle holds a junction at a time; grants take effect from the next tick.
3. The holder releases the junction when its rear has left the box on the outgoing lane (`S − length ≥ r`).
4. Lanes leaving a junction are entered only through it, so "room to leave" at request time cannot be taken by anyone
   else: a holder can always leave, and a simple junction cannot deadlock (FR-023). Waiting vehicles keep their key,
   so the longest waiting with room goes next (no starvation).

The holder of each junction is stored per vehicle (`HeldJunction` tile, hashed); a tile → holder dictionary is a
derived index.

**Rationale**: One holder per junction is simple, obviously correct and deterministic; data dimensions (box 0.22,
vehicle length 0.45, gap 0.05) guarantee that a vehicle fits between two adjacent junctions (1 − 2·0.22 = 0.56 ≥ 0.5),
so a junction never waits on its neighbour's box. (First drafted with 0.25 and 0.35-tile vehicles; changed during
implementation so the compact voxel models, half a tile long, match the simulated length.) Throughput (about two vehicles a second per junction) is enough for
version 1; letting non-conflicting paths cross together is a later optimisation.

**Alternatives considered**: Per-path conflict matrices (more throughput, many cases for eight directions, harder to
prove deadlock-free); priority roads (not asked for); traffic lights (out of scope).

## R5 — Stops, bays, depots and turning around

**Decision**:

- **Turning around** happens at nodes of the routing graph: at a **dead end** (one link: road ends, bays, depot
  entrances) the incoming lane extends `deadEndExtent` (data, 0.49 tile) past the centre; at that point the vehicle
  moves to the outgoing lane at `S = −extent` (when it has room). At a junction a U-turn is just leaving by the reverse
  link while holding it. Never in the middle of a road, except the stuck rule (R8).
- **Bay**: the stop place is the dead-end turning point. Vehicles of the stop queue behind it; a vehicle is **at the
  stop** when it is stopped, its whole body is inside the bay tile and fewer than `bayPlaces` (data, 2) vehicles of the
  lane are ahead of it inside the tile. Both places load at once; the front one turns around when done, the second
  then drives up to the turning point and turns around.
- **Drive-through stop**: the stop place is the centre of the stop tile on whichever lane the vehicle arrives by (one
  place per direction).
- **Depot**: a dead end on a `TileObjectKind.Depot` tile; a vehicle that reaches the turning point of a depot it is
  heading for goes inside (leaves the lanes). Leaving: the vehicle appears on the outgoing lane at
  `S = −extent + length` when the lane has room.

**Rationale**: Bays and depots are already dead-end nodes of the routing graph (feature 010 designed them so); using
the dead end's turning point as the stop place makes the two bay places fall out of the geometry, and U-turns at nodes
match what the router assumes (it does not track arrival direction).

**Alternatives considered**: A direction-aware router that forbids U-turns at junctions (twice the states, all routes
change); bays with a separate loop path inside the tile (extra geometry for nothing visible).

## R6 — Routes

**Decision**: Routes are lists of `RouteStep`s from feature 007's `RoadRouter` (A*, octile, 4 096-entry cache cleared
on network changes), from the **node ahead** of the vehicle (end of the chain of its current lane) to the goal node.
A bay or depot is itself a node; a drive-through stop T lies inside a chain between nodes P and Q, so the goal is
reached through P (then the chain from P towards T) or Q, whichever gives the shorter total; the vehicle stops when its
lane enters T. Vehicles keep a reference to the cached route array plus an index (no copy); the route's steps are
hashed. Rules while driving:

- On a two-link tile the next lane follows from the road layer itself (geometry).
- At a node with three or more links the next step's `FirstDirection` is used after checking that the step still
  starts at this node and the link still exists; otherwise the vehicle stops at the stop line and asks for an urgent
  route. Dead ends always turn around (geometry).
- Routes are planned again at every departure (stop, depot) — cheap through the cache — so new shorter roads are used
  from the next departure, and urgently when the network invalidates the route.
- Route searches run in the sequential event phase, at most `routeSearchesPerTick` (data, 64) non-cached searches per
  tick in request order; the others wait at their stop line (deterministic: the queue is in tick and vehicle-id order).

**Rationale**: Reuses the tested router and its cache; lazy validation at decision points detects every removal that
matters without scanning routes on each network change, and the budget keeps a big network change from stalling a
tick.

**Alternatives considered**: Re-planning every vehicle on each network version (50 000 searches after each road
piece); storing tile paths per vehicle (memory and allocation for long routes).

## R7 — The tick: read, decide, write

**Decision**: `RoadVehicleSystem.Tick(parallel)` runs after commands and before the day events:

1. **Read and decide (parallel, batches of 2 048 vehicles)**: from the current state arrays (the previous tick), each
   vehicle computes its new speed and position (crossing into the next lanes along its path), whether it requests a
   junction, releases one, arrived at its stop place, reached a dead end, a depot or a missing link, and writes the
   result to its own slot of a second state array (`_next`). Nothing shared is written.
2. **Write lanes (sequential, only changed vehicles)**: swap the arrays, then move the vehicles that changed lane
   between lane lists (an insertion keeps each list sorted by `S` and vehicle id, so the result does not depend on the
   order of processing) and update the tile → holder index of released junctions.
3. **Events (sequential, in slot order)**: junction grants (smallest key per junction), U-turns at dead ends, depot
   entries and exits, arrivals (unload, start loading), loading steps, departures (route), urgent routes within the
   budget, stuck detection and turnarounds. Station loading and unloading (`StationRegistry`, feature 010) and money
   (feature 011) are only touched here.

Day events (ageing, reliability, breakdown rolls, servicing due, running-cost profit statistics) run from the day
handler; breakdown chance is a pure hash of (game seed stream, vehicle id, day number) so no shared random stream is
consumed in parallel.

**Rationale**: This is the plan prompt's read → write design, with determinism by construction: phase 1 only reads
shared state, phases 2–3 are sequential in a fixed order. Tests compare parallel and sequential runs tick by tick.

**Alternatives considered**: Parallel write phase per lane segment (needs ownership of lanes across junctions; the
measured sequential write cost of a few thousand lane changes a tick is about 0.2 ms, so it is not worth the risk).

## R8 — Stuck vehicles and gridlock

**Decision**: Each vehicle counts ticks without moving while it wants to move (not loading, not in a depot, not
waiting for a full load). After `stuckDays` (data, 2 days) a `VehicleStuck` notice is raised once; after twice that,
in the event phase, it turns around where it stands if the opposite lane has room in front of it (`minGap`) and behind
it (the follower on that lane can still stop: the soft rule with the new vehicle as obstacle), releases any junction
request and plans a new route. Turnarounds are processed in slot order, at most one per lane per tick.

**Rationale**: Clarification Q3. Turning around removes the vehicle from the cycle that blocks it, which breaks circular
jams; doing it rarely and sequentially keeps the overlap guarantee.

## R9 — Breakdowns, reliability and servicing

**Decision**: Reliability in per mille. Daily: `reliability −= decayPerDay` (×`oldAgeFactor` past the model's life),
floor `minReliability`. Breakdown chance per day = `(1000 − reliability) × breakdownChanceScale / 1000²` (data) for
moving vehicles, rolled from a hash. A broken-down vehicle's speed cap alternates between ½ and ¼ of its model speed
every `breakdownPulseTicks`; its target becomes the nearest reachable depot of the company (route lengths from the
cache); it recovers on entering the depot (also serviced) or after `breakdownDays`, whichever is first, with
`recoverReliability` added. Servicing: when `daysSinceService ≥ interval` (interval per vehicle, default from data,
0 = never), after its current stop the vehicle heads for the nearest reachable depot, is serviced inside
(reliability back to the model's base, counter reset) and leaves for its next stop. The effect (smoke or sparks by the
model's power kind) is presentation only.

**Rationale**: Clarification Q2; all integers, deterministic, no shared random stream.

## R10 — Data files

**Decision**: `src/Talt.Core/Vehicles/Data/vehicles.json` (embedded, validated by `VehicleRules`, fingerprinted):
units conversion (`kmhPerTileSecond`), common physics (braking, gaps, junction radius, dead-end extent, bay places),
stop timing (minimum dwell, loading interval), stuck and route budgets, reliability and breakdown values, value
depreciation, depot cost/removal/maintenance, default service interval, and the starter models (2 buses, 3 trucks by
cargo category; ids, name keys, cargo, capacity, speed, acceleration, length, price, running cost, life, reliability,
years, power, colour, loading amount). Truck categories map to cargo ids of `cargo.json` and are checked against it.
`finance.json` is unchanged (depot maintenance is booked by the vehicle system as property maintenance), so finance
goldens do not move.

## R11 — Money

**Decision**: Purchase: `Book(VehiclePurchase, −price × costLevel)`; refused when cash is short (not with infinite
money). Sale: `Book(VehiclePurchase, +value)` (net vehicle investment in one ledger row; no new ledger slot, so ledger
hashing is unchanged). Value = price × max(minValuePercent, 100 − age × 100 / life) %; the `RunningCosts` value is
updated monthly (new `RunningCosts.SetValue`). Vehicle profit = sum of payments for its unloads (difference of
`FinanceSystem.TotalPaid` around the unload) − its running cost per day. `MoneyEvent` gains an optional vehicle id so
the client places the floating text above the vehicle (FR-015). Depot maintenance: yearly cost / 12 booked as property
maintenance at each month end.

## R12 — Protection of occupied tiles

**Decision**: `ConstructionState` gets an optional occupancy check (`IVehicleOccupancy`): a construction plan that
removes or changes a road link, a station piece or a depot on a tile currently under a vehicle (its lane's from- or
to-tile) or inside a depot is refused with `vehicle-in-the-way` (`ConstructionCommand.Check`, so previews and commands
agree). The check walks the vehicles once per plan (50 000 vehicles: ~0.1 ms) only when the plan changes road links.

## R13 — Rendering

**Decision**: The client takes poses from `RoadVehicleSystem` (`CopyPoses`: x, z, direction, height hint, model,
state flags) instead of the empty prototype fleet; the existing double-buffered snapshot and `PoseInterpolator` are
reused; a pose change of more than one tile (turnaround, depot exit) is drawn without interpolation. Height: ground
height on normal tiles, deck height on bridges, hidden in tunnels. Models: `tools/Talt.ModelGen` generates
`vehicle:bus-*`/`vehicle:truck-*` voxel models (bus, bulk tipper, tanker, box truck), falling back to the generic
`vehicle` model; colour from the model data. Breakdown smoke/sparks: small instanced boxes rising behind the vehicle
(presentation-only animation from the real-time clock). Depots: `depot` model or placeholder boxes appended to the
station placeholders.

## R14 — Debug seeding and benchmarks

**Decision**: `VehicleSeeder` (Core) turns `--seed-vehicles <n>` into recorded commands after the stations of
`--seed-stations`: enable infinite money (debug games only), one depot near each served station pair, lines between
pairs of seeded stations of the same kind 10–80 tiles apart, and n vehicles spread over the lines; deterministic
order. Benchmarks (`RoadVehicleBenchmarks`): a flat 4096 map with a road grid every 16 tiles, seeded stations and
10 k / 50 k / 100 k vehicles driven for a warm-up, then `Tick` (sequential and parallel); `RouteBenchmarks`: route
query across 1 000 tiles cached and uncached, and the route for a drive-through goal.
