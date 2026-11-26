# Feature Specification: Railways

**Feature Branch**: `015-railways`

**Created**: 2026-11-26

**Status**: Draft

**Input**: User description: "Rail is the backbone of large networks: high capacity, high speed and the most
interesting building challenges (junctions, signals, stations). P1: I build railway tracks by dragging,
including curves, slopes, bridges and tunnels, and junctions with switches. P1: I build rail stations of
configurable length and platforms, and rail depots. P1: I buy locomotives and wagons, compose trains, assign
them to lines and they run between stations, loading and unloading. P1: I place signals that divide track into
blocks; trains never enter an occupied block, so they do not collide. P2: path signals let several trains use
complex junctions and stations safely, reserving a path through them. P2: trains choose free platforms
automatically at stations. P3: I can build electrified track and electric locomotives that require it.
Success: 5 000 trains with signalling simulate within the tick budget; no collisions and no deadlock in
standard layouts (tested with scenario fixtures: single line with passing loops, double track, terminus
station, junctions). Out of scope: track types beyond normal and electrified."

## Context

Road transport is complete: feature 007 gave the game roads as tile-level data with an incrementally updated
routing graph, bridges, tunnels and slopes; feature 010 built stations, warehouses and the cargo operations
vehicles use; feature 011 pays deliveries and charges running costs; feature 012 added road depots, road
vehicles, lines with per-stop loading rules and a vehicle tick that is identical in parallel and in sequence.
Feature 013 (built at the same time) makes passengers and goods want specific destinations and routes them
through stations and lines regardless of the transport mode.

Railways are the second network type. Track is built on tiles like roads, but trains are long, heavy and fast,
cannot steer around each other and need signals to share track. A rail station has platforms of a chosen
length; a train stops along a platform and loads into each of its wagons. This feature delivers the whole rail
loop: track, switches, slopes, bridges, tunnels, stations, depots, trains made of locomotives and wagons,
lines shared with the road-vehicle line system, block and path signals, and electrified track.

## Clarifications

### Session 2026-11-26

- Q: What geometry does track have? → A: Eight directions like roads (grid-aligned and 45° diagonals); a curve
  is a 45° turn between neighbouring tiles; 90° turns within one tile are not allowed.
- Q: Which signal types exist in version 1? → A: Three: block signal, path signal and one-way path signal.
- Q: How are trains composed in the depot? → A: The player buys a locomotive and adds wagons one by one (or
  several at once); wagons come by category like trucks (passengers, mail, bulk, tanker, box), the cargo is
  chosen when buying and can be changed free of charge in a depot while the wagon is empty.
- Q: Can track cross roads at grade (level crossings)? → A: Yes, only straight perpendicular crossings; trains
  have priority: road vehicles wait before the crossing while a train has reserved it or is near, and a train
  never enters it while a road vehicle is on it.
- Q: Where does a train stop at a platform, and what if it is longer than the platform? → A: Only wagons
  standing along the platform load and unload. A train longer than the platform has to move so that its other
  wagons line up with the platform, stopping and starting again each time (a time penalty). The head first stops
  at the far end of the platform; at a through platform the train then pulls forward one platform length at a
  time (when the track ahead is free) until every wagon has been served; at a terminus (no track ahead) the
  wagons that never reach the platform are not served and the player is told once.

## Scope and deferrals

- **In scope now**: rail track (build by dragging, remove), switches at junctions, slopes, rail bridges and
  tunnels; rail stations (platform count and length chosen when building, extendable by building next to an
  existing station); rail depots; a starter catalogue of locomotives and wagons in a data file; buying,
  composing (attach / detach wagons in a depot), selling, cloning and sending trains to a depot; trains on lines
  (the same line and loading-rule system as road vehicles); train movement with acceleration from power,
  tractive effort and mass, slowed by slopes and curves; block signals, path signals and one-way path signals;
  automatic platform choice; electrified track and electric locomotives; ageing, reliability, breakdowns and
  servicing like road vehicles; level crossings where rail crosses a road; rendering of track, switches,
  signals, stations, depots and trains; train sounds through the existing audio layer; a debug option that
  seeds many trains for performance checks.
- **Deferred**: timetables, line statistics and line colours beyond a simple colour (017); the full catalogue
  with eras and autoreplace (016); saving and loading (014); pre-signals, combo signals and other signal types;
  waypoints and per-train orders beyond lines; train reversal in the middle of a track (only at dead ends,
  stations and depots); competitors' trains.
- **Out of scope**: track types beyond normal and electrified (monorail, maglev, narrow gauge), trams.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Build railway track (Priority: P1)

The player chooses the track tool and drags across the map. A preview shows the tiles the track will use, the
cost and any reason it cannot be built; releasing builds it. Track can curve, climb slopes, cross rivers and
valleys on bridges and pass under hills through tunnels. Dragging a new track onto an existing one creates a
junction with switches.

**Why this priority**: Without track there is no railway.

**Independent Test**: In a game with a map, drag a track 50 tiles across uneven ground with a river: the
preview shows tiles and cost, the track is built with the cost charged, a bridge crosses the river, and a second
track dragged from its middle creates a junction.

**Acceptance Scenarios**:

1. **Given** free land, **When** the player drags a track, **Then** the preview shows the path, its cost and
   any blocking tile, and releasing builds it and charges the cost.
2. **Given** a straight track and a new track dragged off its side, **When** it is built, **Then** the tile
   where they meet becomes a junction whose switch lets trains take either branch.
3. **Given** a slope, **When** a track is dragged up it, **Then** the track follows the slope if it is not too
   steep, otherwise the preview says why it cannot be built.
4. **Given** a river or a valley, **When** the player uses the bridge tool between two ends, **Then** a rail
   bridge spans it if its length is allowed; **Given** a hill, **When** the tunnel tool is used, **Then** a rail
   tunnel is dug when both ends are at the same height.
5. **Given** a track crossing a straight road, **When** it is built, **Then** a level crossing is created;
   road vehicles wait while a train is on or near it.
6. **Given** a track the company owns, **When** the player removes it, **Then** it disappears and part of the
   cost is refunded, unless a train stands on it (refused with a reason).

---

### User Story 2 - Rail stations and depots (Priority: P1)

The player builds a rail station by choosing the number of platforms and their length and placing it on flat
land; tracks entering it connect to the platforms. Building next to an existing station extends it. The player
builds a rail depot at the end of a track, where trains are bought and serviced.

**Why this priority**: Stations are where trains earn money; depots are where trains come from.

**Independent Test**: Build a station of 2 platforms by 5 tiles and a depot, connect them with track: the
station covers nearby houses and industries like other stations, and the depot opens its purchase window.

**Acceptance Scenarios**:

1. **Given** flat free land, **When** the player places a station of P platforms by L tiles in a chosen
   orientation, **Then** it is built, charged, joined to an adjacent station of the company if any, and named.
2. **Given** a station, **When** the player places more platforms next to it with the same orientation,
   **Then** they join the same station.
3. **Given** a rail station, **Then** it has a catchment radius like other station types and accepts and
   supplies cargo through the same rules (feature 010), including warehouses grouping it with nearby stations.
4. **Given** the end of a track, **When** the player builds a depot facing it, **Then** the depot is built and
   connected; trains leave and enter through that side.
5. **Given** a station or depot with a train inside, **When** the player removes it, **Then** removal is refused.

---

### User Story 3 - Buy and compose trains, run them on lines (Priority: P1)

In a depot the player buys a locomotive and adds wagons of the cargo they want to carry, forming a train. The
train is assigned to a line made of rail stations (the same line system as road vehicles) and runs between the
stations, loading and unloading in every wagon, earning money for each delivery.

**Why this priority**: This closes the rail loop and is how rail earns money.

**Independent Test**: A depot and two stations in two towns joined by track; buy a locomotive with three
passenger wagons, create a line with both stations, start the train: it drives to the first station, loads
passengers, drives to the second, unloads them (paid) and keeps going round.

**Acceptance Scenarios**:

1. **Given** a depot, **When** the player opens it, **Then** the locomotives and wagons available this year are
   listed with price, capacity and cargo, speed, power, mass, running cost and reliability.
2. **Given** a locomotive in a depot, **When** the player adds wagons, **Then** the train shows its total length,
   mass, capacity per cargo and maximum speed; wagons can be removed again and sold.
3. **Given** a train with a line, **When** the player starts it, **Then** it leaves the depot and drives to the
   stops in order, cycling.
4. **Given** a train at a platform, **When** it stops, **Then** every wagon unloads cargo the station accepts
   (paid, with floating income text) and loads waiting cargo it can carry, according to the stop's loading rule.
5. **Given** a heavy train climbing a slope, **When** its locomotives cannot overcome the slope at full speed,
   **Then** it slows down, and accelerates again on flat track.
6. **Given** a train longer than the platform at a through platform, **When** the wagons along the platform
   have been served, **Then** the train pulls forward one platform length and stops again so the next wagons
   line up, until every wagon has been served (each move costs the stop and start time); at a terminus the
   wagons that cannot reach the platform are not served and the player is told once.

---

### User Story 4 - Block signals (Priority: P1)

The player places signals on track. Signals divide track into blocks; a train may enter a block only if no
other train is in it. Trains stop at red signals and continue when the block ahead is clear.

**Why this priority**: Without signals only one train can use a track network; with them, many trains share it
without ever colliding.

**Independent Test**: A long double-track line with block signals every 10 tiles and 10 trains: trains follow
each other with at least one block between them, never collide, and the same game in parallel and sequentially
gives identical results.

**Acceptance Scenarios**:

1. **Given** a track, **When** the player places a signal, **Then** it appears beside the track facing one
   direction (or both); clicking it again cycles its direction and type.
2. **Given** a train in a block, **When** another train reaches the signal at the entrance of that block,
   **Then** the signal shows red and the second train stops before it.
3. **Given** the block ahead becomes free, **When** the signal turns green, **Then** the waiting train goes on.
4. **Given** any game, **When** the debug safety check runs, **Then** no two trains ever occupy the same track
   tile.
5. **Given** a track section without signals, **Then** it behaves as one block (a whole unsignalled network is
   one block).

---

### User Story 5 - Path signals (Priority: P2)

Path signals let several trains use a junction or a station throat at the same time as long as their paths do
not cross. A train at a path signal reserves the track it needs up to the next signal; another train can pass a
path signal if its path does not touch a reserved tile.

**Why this priority**: Junctions and busy stations need several trains at once; block signals alone make them
bottlenecks. The basic loop works with block signals.

**Independent Test**: A four-platform terminus with path signals at the throat and four trains: two trains enter
and leave on non-crossing paths at the same time; trains with crossing paths wait their turn.

**Acceptance Scenarios**:

1. **Given** a path signal, **When** a train reaches it, **Then** it reserves a path to the next signal (or to
   its platform) if every tile of that path is free and unreserved, and passes; otherwise it waits.
2. **Given** a reserved path, **When** the train has passed a tile, **Then** the tile's reservation is released.
3. **Given** a one-way path signal, **Then** trains may only pass it in its direction; trains coming the other
   way are not allowed on that track.
4. **Given** two trains asking for reservations in the same tick, **Then** the one with the lower id is served
   first, so the result never depends on processing order.

---

### User Story 6 - Automatic platform choice (Priority: P2)

When a train approaches a station with several platforms, it picks a free platform that it can reach; if all are
busy, it waits at the last signal before the station.

**Why this priority**: Stations with several platforms are pointless if trains queue for one; the loop works with
single-platform stations.

**Independent Test**: A three-platform station with three trains on the same line arriving one after another:
each uses a different platform.

**Acceptance Scenarios**:

1. **Given** a station with free and busy platforms, **When** a train plans its route to it, **Then** it chooses
   the free reachable platform with the shortest route.
2. **Given** all platforms busy, **When** the train reaches the last signal before the station, **Then** it waits
   there until a platform is free.

---

### User Story 7 - Electrified track and electric locomotives (Priority: P3)

The player can build electrified track or electrify existing track. Electric locomotives run only on electrified
track; other locomotives run on both.

**Why this priority**: Electric locomotives are faster and more powerful, an upgrade for mature networks; the
loop works without them.

**Independent Test**: Electrify one line: an electric locomotive runs on it; a route that needs a
non-electrified tile is refused for it.

**Acceptance Scenarios**:

1. **Given** normal track, **When** the player uses the electrify tool over it, **Then** it becomes electrified
   (cost charged) and shows overhead wires.
2. **Given** an electric locomotive, **When** it plans a route, **Then** only electrified track is used; with no
   electrified route, it reports "no route" and waits.

### Edge Cases

- A track removed under or ahead of a train: removal is refused while a train stands on or has reserved that
  tile; trains whose route used a removed tile replan.
- A signal removed while a train waits at it: the train re-evaluates the block ahead.
- A loop without signals: the whole loop is one block; two trains on it never move into each other (one waits).
- A dead end: trains turn around at dead ends, stations (terminus) and depots, never in the middle of track.
- A train longer than the track between two signals: it occupies several blocks; each block it touches is
  occupied.
- Diagonal track crossing another diagonal: a crossing is allowed and works as a junction for blocks and
  reservations.
- Level crossings: trains have priority; a road vehicle never stops on the crossing; a train never enters a
  crossing occupied by a road vehicle (it brakes).
- Deadlock: in the standard layouts (single line with passing loops, double track, terminus, junctions) no
  deadlock happens; a train stuck longer than the stuck limit is reported to the player.
- Trains of a road line or road vehicles on a rail line: stops of the wrong kind are skipped and reported once,
  like road vehicles at the wrong kind of stop.
- Infinite money and game over behave as for road vehicles.
- Map-less test scenarios (benchmarks, determinism goldens) keep their recorded fingerprints.

## Requirements *(mandatory)*

### Functional Requirements

**Track**

- **FR-001**: Track MUST be stored per tile, like roads, with eight directions (straight and 45° diagonals) and
  any set of allowed connections through a tile; a path through a tile may turn at most 45°; a tile with more
  than two connections is a junction with a switch.
- **FR-002**: The player MUST be able to build track by dragging, with a preview of tiles, cost and reasons,
  following the construction framework (commands, costs, ownership, undo where the framework supports it).
- **FR-003**: Track MUST follow slopes up to a maximum steepness (data); bridges and tunnels MUST work like
  road bridges and tunnels with lengths and costs from a data file.
- **FR-004**: Track crossing a road MUST make a level crossing where both share the tile, only where a straight
  track crosses a straight road at a right angle. Trains have priority: road vehicles MUST wait before the
  crossing while a train has reserved it or is within a closing distance (data), and a train MUST NOT enter it
  while a road vehicle is on it.
- **FR-005**: Track MUST be removable by its owner, with a partial refund, unless a train stands on it or has
  reserved it.
- **FR-006**: Track types MUST be normal and electrified, defined in a data file (cost, speed limit, whether it
  powers electric locomotives); existing track MUST be convertible to electrified.

**Stations and depots**

- **FR-007**: The player MUST be able to build a rail station with a chosen number of platforms (1 to a data
  limit) and length (1 to a data limit) and orientation, on flat free land; adjacent pieces of the same
  orientation join the station; stations follow feature 010 rules for catchment, acceptance, ratings, names and
  warehouses.
- **FR-008**: The player MUST be able to build a rail depot at a track end, with an entrance side; removal is
  refused while a train is inside or on it.

**Locomotives, wagons and trains**

- **FR-009**: Locomotives and wagons MUST be defined in a validated data file: id, name key, kind (locomotive
  or wagon), power, tractive effort, mass, length, maximum speed, power kind (steam/diesel or electric), cargo
  and capacity (wagons; locomotives may carry passengers or mail), price, running cost, expected life,
  reliability, availability years. Wagons come by category like trucks (passengers, mail, bulk, tanker, box);
  a wagon carries one cargo of its category, chosen when bought and changeable free of charge in a depot while
  empty. Invalid data MUST stop the game at startup with a clear message.
- **FR-010**: A train MUST be a consist: one or more locomotives followed by zero or more wagons, up to a
  maximum length (data); its maximum speed is the lowest of its parts and of the track; its acceleration
  comes from total power and tractive effort against total mass, slope and curves.
- **FR-011**: Buying, selling, cloning, sending to depot, starting and stopping, attaching and detaching
  wagons MUST be possible in a depot with the same money rules as road vehicles.

**Lines and stops**

- **FR-012**: Trains MUST use the same line system as road vehicles (ordered stops with loading rules, shared by
  the vehicles assigned); a rail line's stops are rail stations.
- **FR-013**: At a platform every wagon standing along the platform MUST unload and load through the station
  cargo operations used by road vehicles, so cargo and destinations (feature 013) work the same for every mode.
  The head stops at the far end of the platform. A train longer than the platform MUST, at a through platform,
  pull forward one platform length at a time (only when the track ahead is free) and stop again until every
  wagon has been served; at a terminus the wagons that never reach the platform are not served and the player is
  told once.

**Signals and safety**

- **FR-014**: Signals MUST be placeable on straight and diagonal track tiles (not on junctions), facing one or
  both directions; types: block signal, path signal, one-way path signal.
- **FR-015**: Track between signals MUST form blocks; a block is occupied while any part of any train is in it;
  a train MUST NOT pass a block signal into an occupied block.
- **FR-016**: A train at a path signal MUST reserve every tile of its path to the next signal or its stop before
  passing; reservations are released tile by tile behind the train's tail; two trains' reservations MUST never
  overlap.
- **FR-017**: Requests to enter blocks and reserve paths in the same tick MUST be resolved in train id order so
  the result is independent of parallel processing order.
- **FR-018**: No two trains MUST ever occupy the same track tile; a debug safety check (option and tests) MUST
  verify it.
- **FR-019**: A train MUST brake in time to stop before a red signal or a track end, given its speed and braking
  rate (data).

**Routing**

- **FR-020**: Trains MUST plan routes over the rail network with costs that prefer shorter, faster track and
  penalize red signals and occupied platforms; routes MUST be replanned when the network changes.
- **FR-021**: At a station with several platforms the train MUST choose a free reachable platform at the last
  path signal before it; if none is free it waits.
- **FR-022**: Electric locomotives MUST only route over electrified track.

**Ageing and reliability**

- **FR-023**: Trains MUST age, lose reliability, break down and be serviced like road vehicles (feature 012
  rules and data), with the train's reliability taken from its locomotives.

**Commands, determinism and performance**

- **FR-024**: Every player action of this feature MUST be a recorded command that replays identically.
- **FR-025**: The rail simulation MUST be deterministic; running the tick in parallel MUST give exactly the same
  result as running it sequentially.
- **FR-026**: All rail state MUST be part of the game fingerprint; games without rail, and map-less scenarios,
  MUST keep their fingerprints.
- **FR-027**: A debug option MUST seed many trains on generated lines in a game with a map (client and headless).

**Presentation**

- **FR-028**: Track (straight, diagonal, slopes, junctions, bridges, tunnels, electrified wires), signals with
  their state, stations, depots and trains MUST be drawn; trains with voxel models per locomotive and wagon
  (placeholders allowed), following the track, interpolated smoothly.
- **FR-029**: Train, depot, station and line windows MUST show and edit what road vehicle windows do, plus the
  consist (parts, length, mass, power, capacity per cargo).
- **FR-030**: Trains MUST play sounds through the existing audio layer when near the camera.
- **FR-031**: Every player-facing text MUST be localized in English and Spanish.

### Key Entities

- **Track piece**: per tile: connections over eight directions, type (normal, electrified), slope, bridge or
  tunnel end, owner.
- **Signal**: tile, direction(s), type (block, path, one-way path), state (red/green).
- **Block**: a set of track tiles bounded by signals and track ends; occupied by trains.
- **Reservation**: tiles reserved by a train through path signals.
- **Rail station**: platforms (tile rows) of a station of feature 010.
- **Rail depot**: company tile at a track end.
- **Rolling stock model**: data-defined locomotive or wagon.
- **Train**: id, owner, consist, line and stop index, cargo per wagon, position (head tile path and distance),
  speed, state, age, reliability.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 5 000 trains running on a signalled network on a 4096 × 4096 map simulate within the tick budget
  (4 ms average per tick for all vehicles on the reference machine), the map renders at 60 FPS and fast forward
  stays at ×20 or more.
- **SC-002**: Scenario fixtures — single line with passing loops, double track, terminus station, junctions —
  run for an in-game month with no collision (no two trains on one tile) and no deadlock (every train completes
  its line cycles).
- **SC-003**: The same seeded game run with the rail tick in parallel and sequentially gives identical
  fingerprints at every checkpoint, on every CI platform.
- **SC-004**: A player can go from an empty map to a train earning money (track, two stations, a depot, a line, a
  train) in under ten minutes, guided only by the interface.
- **SC-005**: A rail route between two stations 1 000 tiles apart is found within 2 ms.
- **SC-006**: The train tick and the frame's train drawing allocate no memory in steady state.

## Assumptions

- Trains reverse only at dead ends, terminus platforms and depots.
- Rail lines reuse the road line system (stops, loading rules, shared vehicles).
- Placeholder voxel models for locomotives and wagons are acceptable until dedicated models exist.
- Balancing numbers (costs, speeds, power, braking) live in data files and can be tuned later.
