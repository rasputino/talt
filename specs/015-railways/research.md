# Research: Railways (015)

Every decision below is original to TALT; existing games were studied only for mechanics (constitution VI).

## R1 — Track storage

- **Decision**: `RailLayer`, dense per tile like `RoadLayer`: link mask (Dir8 bits), type (index + 1), flags (bridge
  end, tunnel portal, passing over/under), long-link span and direction, and a signal byte. Hashed only once a rail
  tile ever existed.
- **Rationale**: the road layer pattern is proven (cache-friendly, cheap to hash, meshed per block); roads and rail
  can share a tile only at level crossings, so two layers are simpler than one combined layer.
- **Alternatives**: a sparse dictionary of rail tiles (slower scans, harder meshing); reusing `RoadLayer` with a type
  flag (roads and rail would fight over link bits at crossings).

## R2 — Switches from link masks

- **Decision**: a train heading `h` (it entered through link `opposite(h)`) may leave by links `h−1`, `h`, `h+1` that
  exist. A tile with three or more links is a junction; a tile with N–S and E–W links is a diamond crossing (no legal
  turn joins them). Planning refuses pieces whose links leave a link without any legal continuation (a 90° kink).
- **Rationale**: matches clarification Q1 (max 45° per tile), needs no per-tile route table, and every junction shape
  is described by its mask.
- **Alternatives**: explicit per-tile connection pairs (more data and more rules to keep consistent).

## R3 — Construction

- **Decision**: `RailPlanner` plans into the shared `ConstructionPlan`; `TileChange` gains `OldRail/NewRail/RailChanged`,
  `TileSnapshot` and `UndoRecord` the rail piece, and `ConstructionState.Commit/ApplyUndo` write it and refresh rail
  blocks. Drags use an A* over (tile, heading) with turns of at most 45° between tiles and ramps only on straight
  links between tiles one step apart (road slope rules), so curves come from the search. Bridges and tunnels reuse the
  road rules' geometry (straight, same height ends, data lengths). Track tiles are `TileObjectKind.Rail`; level
  crossing tiles stay `Road` with a rail piece on them.
- **Rationale**: one commit path keeps money, undo, previews and the occupancy check identical for every network.

## R4 — Ownership invariant

- **Decision**: `owner[tile]` = id of the train whose body covers the tile or that reserved it, 0 otherwise. A train's
  track is a deque of steps (from tile, direction, length) from its tail to the end of its authority; its owned tiles
  are the start tiles of those steps plus the last end tile. Reserving needs `owner == 0`; the tail releases a tile when
  it leaves it. Trains in depots own nothing.
- **Rationale**: no two trains can ever be on the same tile, whatever signals the player builds; collision checks
  reduce to an O(tiles owned) invariant. Diagonal steps cannot cross (the planner forbids crossing diagonals), so tile
  ownership is enough.
- **Alternatives**: continuous distance checks between trains (needs cross-train reads in the move phase).

## R5 — Signals and blocks

- **Decision**: a signal sits on a tile with two opposite links and faces one or both travel directions; a face for
  travel `d` lies on the tile's edge towards `d`. Blocks = components of track tiles joined by links not cut by a
  signal edge (flood fill, rebuilt for the affected components after a track or signal change; derived, not hashed).
  Passing a signal edge (or extending from a place without a signal, a "virtual block signal") reserves the route
  segment up to the next signal tile in travel direction or the stop point:
  - block signal / virtual: allowed when no other train owns any tile of the block entered and every segment tile is
    free;
  - path signal: allowed when every segment tile is free (several trains in one block on disjoint paths);
  - one-way path signal: like a path signal, and travel against its face is forbidden (routing never uses it).
  Requests are served in ascending train id within the sequential phase.
- **Rationale**: clarification Q2; reservations stay tile-level so block and path signals mix safely.

## R6 — Rolling stock and data

- **Decision**: `rail.json`: track types (normal, electrified: cost, removal, speed limit, powers electric), signals
  (cost per type), bridge and tunnel limits and costs, depot cost, physics (gravity, ramp grade, rolling resistance,
  braking, curve speed share, acceleration scale), stops (dwell, load step), and rolling stock: locomotives (power kW,
  tractive effort kN, mass t, length, speed, power kind steam/diesel/electric, optional capacity) and wagons by
  category (passengers, mail, bulk, tanker, box) with capacity, mass, length, speed, price, running cost, life,
  reliability, years. Validated at load with file and field names. Reliability and breakdown tunables come from
  `vehicles.json` (shared with road vehicles).
- **Rationale**: clarification Q3; same category model as trucks.

## R7 — Tick phases

- **Decision**: (1) parallel move over slot batches: speed, advance, release tail tiles (own tiles only), (2)
  sequential by ascending id: reservations, arrivals, loading, depots, routing, stuck checks. Ids kept in a sorted
  array.
- **Rationale**: same guarantee as feature 012: parallel equals sequential by construction.

## R8 — Routing

- **Decision**: A* over (tile, heading) with an octile heuristic scaled by the fastest track speed; when a tile has a
  single legal continuation the search follows it without pushing (corridor skipping). Pooled, stamp-based buffers,
  a node budget per tick shared by trains (data). Electric trains skip unelectrified tiles; one-way path signals are
  never passed against their face. Routes are planned when leaving a stop or depot, after a failed reservation at a
  path signal (at most once per `replanTicks`), and when the network version changed.
- **Alternatives**: a compressed segment graph updated incrementally (faster but much more code; corridor skipping
  gives most of the gain).

## R9 — Signal-aware cost and platform choice

- **Decision**: step cost = length × (1000 / speed limit) + curve penalty; tiles owned by other trains add a large
  penalty, so at a choice point the free branch wins. The goal of a stop route is any platform tile of the target
  station entered in a direction that continues along the platform; the stop point is the platform's far end in travel
  direction. Platforms are chosen when the train plans at the last signal before the station.

## R10 — Rail stations

- **Decision**: a `rail-platform` piece (kind rail, form platform) in `stations.json`. `buildRailStation` places P × L
  tiles of straight track along the chosen axis, each a station piece of one station (joined with adjacent stations of
  the company like road pieces). Catchment, acceptance, ratings, names (place suffix) and warehouses are feature 010's.
- **Rationale**: one station abstraction for every mode, which feature 013 routes cargo through.

## R11 — Stopping and loading

- **Decision**: the head stops at the far end of the platform. A wagon is "along the platform" when its centre lies on
  a platform tile of the stop station. Served wagons are remembered in a mask; at a through platform with unserved
  wagons the train reserves and pulls forward one platform length, stopping again (dwell penalty), until every wagon
  is served; at a terminus unserved wagons are reported once. Each wagon has its own `CargoHold`.

## R12 — Level crossings

- **Decision**: a crossing is a road tile with a straight perpendicular rail piece. Road vehicles treat a crossing
  owned by a train like a held junction and stop before it (`ILevelCrossingGuard` checked in the road events phase);
  a train may reserve a crossing tile only when no road vehicle stands on it or on the two road tiles next to it.

## R13 — Lines

- **Decision**: trains use feature 012's `LineRegistry` (shared instance, same commands to create and edit lines,
  `assignTrainLine` to assign). A stop whose station has no rail platform is skipped and reported once.

## R14 — Client

- **Decision**: `RoadRenderer` is generalized over an `ITileMesher` + change source; `RailMesher` emits ballast,
  sleepers, rails, overhead wire posts, bridge decks and tunnel portals as `RoadVertex` triangles. Trains and signals
  are model instances (placeholders generated per rolling-stock kind; `.vox` files `train:<id>` override them).

## R15 — Fingerprints

- **Decision**: `RailLayer` appends nothing until a rail tile existed; `TrainSystem` nothing until a depot, train or
  signal existed; undo snapshots append the rail piece only when not empty. All existing goldens stay unchanged; a new
  rail replay golden is added.
