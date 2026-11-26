# Feature Specification: Road vehicles, depots and lines

**Feature Branch**: `012-road-vehicles`

**Created**: 2026-11-25

**Status**: Draft

**Input**: User description: "Vehicles are how the player earns money. Buses and trucks are bought at depots,
given a line (an ordered list of stops) and drive along the road network, interacting with other traffic.
P1: I build a road depot, buy a bus or truck model available in the current year, and see it leave the
depot. P1: I create a line by choosing stops in order, assign vehicles to it, and they travel stop to stop
loading and unloading cargo. P1: vehicles keep a safe distance from the vehicle ahead, queue at busy stops,
wait at junctions when needed and never drive through each other. P2: I set loading rules per stop (load if
available, full load, unload only, no loading) and the vehicle follows them. P2: vehicles age, become less
reliable, occasionally break down, and visit a depot for servicing at set intervals. P2: I can sell vehicles
at a depot, clone them, and send them to the depot. P3: vehicles find a new route automatically when a road
on their route is removed. Success: 50 000 road vehicles with interactions simulate within the tick budget
(4 ms average); no vehicle ever overlaps another in the same lane (verified by tests and a debug check);
gridlock situations resolve or are reported; no permanent deadlock at simple junctions. Out of scope: rail
(015), vehicle catalog and eras beyond a starter set (016)."

## Context

The game already has everything around vehicles except the vehicles themselves. Feature 007 gave it roads
(eight directions, one-way pieces, bridges, tunnels, motorways) and a routing graph that answers shortest
routes between road junctions and dead ends. Feature 008 grows towns that produce passengers and mail.
Feature 009 runs industries that produce and accept freight. Feature 010 built stations (bus and truck bays
beside a road, drive-through stops on a road, warehouses that group stations) where cargo waits, and
prepared the operations a vehicle uses to load waiting cargo and unload it for delivery. Feature 011 pays
every delivery, charges recurring costs registered for each vehicle, and has a "vehicle purchase" category
in the ledger that nothing fills yet.

The only moving vehicles so far are the synthetic prototype traffic of feature 001, which runs on an
abstract grid in map-less test scenarios (benchmarks and the cross-platform determinism checks) and never
appears in a game with a map. This feature is the first playable loop: the player builds a depot, buys a
bus or a truck, gives it a line between stations, and watches it carry passengers or cargo for money while
sharing the road with the other vehicles.

## Clarifications

### Session 2026-11-25

Given by the roadmap's clarify guidance (docs/SPECKIT_GUIDE.md): two lanes, right-hand traffic, no overtaking in
version 1; junction rule first come, first served with a junction reservation (no roundabouts); starter set of
two buses and three trucks by cargo category.

- Q: How do trucks choose their cargo? → A: Three truck models by cargo category: bulk (coal, iron ore, grain,
  wood), tanker (oil, fuel) and box/van (mail, goods, food, steel, lumber, livestock). One cargo is chosen when
  buying and can be changed free of charge in a depot.
- Q: What does a vehicle do when it breaks down, and can breakdowns be turned off? → A: It keeps driving slowly,
  alternating between half and a quarter of its normal speed, until it reaches a depot (it heads for the nearest
  reachable one to be repaired) or a fixed time (data) has passed, whichever comes first; meanwhile it trails
  smoke (combustion models) or sparks (electric models). No new-game option.
- Q: What happens when vehicles stay blocked for a long time (a circular jam, a queue that never moves)? → A: After
  the stuck limit (data, about 2 in-game days) the player is told; if the vehicle is still blocked after twice
  that time, it turns around where it stands and plans a new route, which breaks the jam.
- Q: How does the player create a line and assign vehicles? → A: From the vehicle window (or a Lines window) "New
  line" starts a mode in which the player clicks stations on the map in order (Esc ends it); the vehicle is
  assigned to the new line. The line is then edited in its window (reorder, remove, loading rule).
- Q: How many vehicles does each stop piece serve at once? → A: A bay has two places (two vehicles loading at
  once, one behind the other); a drive-through stop serves one vehicle per direction. Others queue on the road.

## Scope and deferrals

- **In scope now**: road depots (build, remove, depot window); a starter catalogue of road vehicle models
  in a data file (two buses, three trucks by cargo kind) with availability years; buying, selling,
  cloning and sending vehicles to a depot; lines (an ordered list of stops shared by the vehicles assigned to
  them) with per-stop loading rules; vehicles driving on the player's and towns' roads in lanes, following
  the vehicle ahead, queueing at stops, taking turns at junctions; loading and unloading at stations through
  feature 010's operations, paid through feature 011; purchase and running costs; ageing, reliability,
  breakdowns and servicing; automatic rerouting when roads change; vehicle, line and depot windows; drawing
  vehicles with voxel models oriented along the road and smoothly interpolated; engine sounds through the
  existing audio vehicle voices; a debug option that seeds many vehicles for performance checks.
- **Deferred**: line statistics, timetables and line colours beyond a simple colour (feature 017); the full
  vehicle catalogue with eras, refits across many models, autoreplace and renewal (feature 016); passengers
  and goods wanting specific destinations (feature 013, the cargo already carries a destination field);
  saving and loading (feature 014); overtaking and multi-lane motorways; traffic lights and roundabout
  rules; competitors' vehicles.
- **Out of scope**: rail vehicles (feature 015), ships and aircraft, road vehicles of towns (private cars).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Build a depot and buy a vehicle (Priority: P1)

The player builds a road depot on a free tile beside a road. Opening the depot shows the bus and truck
models available in the current year with their price, capacity, speed, running cost and reliability. The
player buys one; the price is charged, the vehicle appears in the depot and, once it has orders, drives out
onto the road.

**Why this priority**: Without depots and purchases there are no vehicles, and the game cannot be played.

**Independent Test**: In a game with a road, build a depot, buy a bus, give it a line and see it leave the
depot; the money drops by the price and the ledger shows the vehicle purchase.

**Acceptance Scenarios**:

1. **Given** a free flat tile next to a road, **When** the player builds a depot facing that road, **Then**
   the depot is placed, its cost is charged and the road gains a connection into the depot.
2. **Given** a depot, **When** the player opens it, **Then** only models whose availability period includes
   the current year are offered, each with price, capacity and cargo, maximum speed, running cost per year
   and reliability.
3. **Given** enough money, **When** the player buys a model, **Then** the price is booked as a vehicle
   purchase, the vehicle is listed in the depot, and it is registered for running costs.
4. **Given** not enough money (and no infinite-money mode), **When** the player tries to buy, **Then** the
   purchase is refused with a clear reason and nothing changes.
5. **Given** a vehicle in a depot with orders, **When** the player starts it, **Then** it leaves the depot
   onto the road and drives towards its first stop.

---

### User Story 2 - Lines between stations (Priority: P1)

The player creates a line by clicking stations in order, assigns vehicles to it, and the vehicles travel
from stop to stop, load what waits there for them and unload at the other stops, earning money for each
delivery.

**Why this priority**: This is how the game earns money; it closes the loop of features 008 to 011.

**Independent Test**: Two bus stations in two towns joined by a road, a line with both stops, two buses on
it: after some in-game days both stations show passengers picked up and delivered, the company's income
rises by the delivery payments and the buses keep going round.

**Acceptance Scenarios**:

1. **Given** two or more of the company's stations reachable by road, **When** the player creates a line
   with them in order, **Then** the line exists with those stops and can be named; a stop that cannot be
   reached from the previous one is flagged.
2. **Given** a line, **When** the player assigns a vehicle to it, **Then** the vehicle drives to the stops
   in order and, after the last one, starts again from the first.
3. **Given** a vehicle at a stop of a kind it can use (buses at bus stops, trucks at truck stops), **When** it
   stops, **Then** it unloads the cargo the station accepts (delivered and paid) and loads waiting cargo it
   can carry, up to its capacity, with loading taking time in proportion to the amount.
4. **Given** a delivery, **When** it is paid, **Then** the income appears above the vehicle as a floating
   text and is booked under the cargo's income.
5. **Given** a stop the vehicle cannot use (wrong kind, removed), **When** the vehicle reaches its turn,
   **Then** the stop is skipped and the player is told once.

---

### User Story 3 - Vehicles share the road (Priority: P1)

Vehicles drive on the right-hand lane of two-way roads, keep a safe distance from the vehicle ahead in
their lane, slow down behind slower vehicles (no overtaking), queue behind a vehicle that is loading at a
stop, and wait at junctions when another vehicle is crossing. Two vehicles never occupy the same place in
the same lane.

**Why this priority**: Traffic that drives through itself breaks the illusion and makes busy stations
meaningless; queues are what make network design matter.

**Independent Test**: Ten buses on one line with one drive-through stop: they bunch up, queue behind the one
loading and leave one after another; a debug check run every tick finds no overlap in any lane, and the
same game run in parallel and sequentially gives identical results.

**Acceptance Scenarios**:

1. **Given** a faster vehicle behind a slower one in the same lane, **When** it catches up, **Then** it slows
   to the slower vehicle's speed and keeps at least the safe gap.
2. **Given** a vehicle loading at a stop, **When** others of the same line arrive, **Then** they wait in a
   queue behind it and use the stop in arrival order.
3. **Given** two vehicles reaching a junction on crossing paths, **When** both want to enter, **Then** the one
   that asked first goes first (ties broken in a fixed order) and the other waits until the junction is clear.
4. **Given** any game, **When** the debug overlap check is enabled, **Then** it never finds two vehicles in
   one lane closer than their lengths.
5. **Given** a simple junction (a crossing or T of two-way roads) with heavy traffic from every side,
   **When** it runs for an in-game month, **Then** vehicles keep passing (no permanent deadlock).
6. **Given** a vehicle that has not moved for a long time because of a blockage, **When** the blockage lasts
   past the stuck limit, **Then** the player is told which vehicle is stuck and where.

---

### User Story 4 - Loading rules per stop (Priority: P2)

For each stop of a line the player chooses how vehicles load and unload there: load what is available (the
default), wait for a full load, unload only, or no loading.

**Why this priority**: Full loads make freight lines profitable and unload-only stops make one-way freight
flows possible; the line works without them.

**Independent Test**: A truck line from a mine with "full load" at the mine and "unload only" at the power
station: trucks wait at the mine until full and come back empty.

**Acceptance Scenarios**:

1. **Given** "load if available", **When** the vehicle has loaded what was waiting, **Then** it leaves.
2. **Given** "full load", **When** the vehicle is not full, **Then** it keeps waiting and loading as cargo
   arrives, and leaves as soon as it is full.
3. **Given** "unload only", **When** the vehicle stops, **Then** it unloads and loads nothing.
4. **Given** "no loading", **When** the vehicle stops, **Then** it neither loads nor unloads (it only passes
   through the stop).

---

### User Story 5 - Ageing, breakdowns and servicing (Priority: P2)

Every vehicle has an age and a reliability. Reliability falls between services and with age; the lower it
is, the more likely a breakdown. A broken-down vehicle crawls on at reduced speed, trailing smoke or sparks, to the nearest depot for repair. Vehicles visit
a depot for servicing at a set interval, which restores their reliability. Old vehicles past their expected
life are shown as old.

**Why this priority**: Maintenance gives vehicles a lifecycle and makes depots part of network design; the
loop works without it.

**Independent Test**: A bus with a short service interval goes to the nearest reachable depot when due, its
reliability rises back after the visit, and a vehicle whose servicing is turned off breaks down more often
over a year.

**Acceptance Scenarios**:

1. **Given** a vehicle, **When** in-game days pass, **Then** its age grows and its reliability falls a little
   every day, faster once it is past its expected life.
2. **Given** a vehicle due for servicing and a reachable depot near its route, **When** it finishes its
   current stop, **Then** it drives to that depot, is serviced (reliability back to the model's value) and
   resumes its line.
3. **Given** a moving vehicle, **When** a breakdown happens (more often at lower reliability), **Then** it drives
   on at half and a quarter of its speed in turns, trailing smoke or sparks, towards the nearest depot, and recovers
   there or after the breakdown time, whichever is first.
4. **Given** the same game twice, **When** it runs, **Then** breakdowns happen at the same ticks to the same
   vehicles.

---

### User Story 6 - Sell, clone and send to depot (Priority: P2)

The player can send a vehicle to a depot (it goes to the nearest reachable one and stops there), sell a
vehicle that is stopped in a depot (it returns part of its value), and clone a vehicle (buy another of the
same model with the same line, in the same depot).

**Why this priority**: Managing a growing fleet needs these; a first line works without them.

**Independent Test**: Clone a bus twice, send one to the depot, sell it there: the fleet has two buses, the
money changed by twice the price minus the sale value, and the ledger shows both.

**Acceptance Scenarios**:

1. **Given** a running vehicle, **When** the player sends it to the depot, **Then** it drives to the nearest
   reachable depot of the company and stops inside.
2. **Given** a vehicle stopped in a depot, **When** the player sells it, **Then** it disappears, its value
   (falling with age) is booked back in the vehicle purchase row and its running cost stops.
3. **Given** a vehicle, **When** the player clones it, **Then** a new vehicle of the same model (if still
   available) is bought in the same depot with the same line and stop rules.
4. **Given** a vehicle not in a depot, **When** the player tries to sell it, **Then** the sale is refused and
   the player is offered to send it to the depot.

---

### User Story 7 - Rerouting when the network changes (Priority: P3)

When the player builds or removes roads, vehicles whose route used a changed road find a new route at their
next decision; if no route exists any more, they say so and wait.

**Why this priority**: Players rebuild networks all the time; vehicles that drive into a removed road or get
lost would be a bug, but the first loop works on a static network.

**Independent Test**: A bus line with two possible roads between its stops; remove the road the bus uses:
the bus takes the other one; remove that too: the bus reports "no route" and waits; build a road again: it
continues.

**Acceptance Scenarios**:

1. **Given** a vehicle on a route, **When** a road piece of the route is removed, **Then** the vehicle replans
   from its current position and follows the new route.
2. **Given** no route to the next stop, **When** the vehicle needs one, **Then** it stops at a safe place,
   reports "no route" once and tries again periodically.
3. **Given** a shorter road built while vehicles are driving, **When** they next leave a stop or a depot,
   **Then** their new route may use it.
4. **Given** a road removed under a vehicle, **When** the removal applies, **Then** the removal is refused
   while vehicles stand on that road (the player is told), so vehicles never float in the air.

### Edge Cases

- A depot or station with vehicles inside or queued is removed: removal is refused while vehicles occupy
  the tile; a station removed from a line is skipped by its vehicles.
- A line with one stop, or with the same station twice in a row: one stop is allowed (vehicles shuttle to it
  and back); consecutive duplicates are merged.
- Vehicles of the wrong kind on a stop (a bus line with a truck stop): the stop is skipped for that vehicle
  and the player is told once per vehicle and stop.
- A dead-end road: vehicles turn around at the end of a dead end (bays and road ends); they may also turn around at
  a junction when their route goes back the way they came.
- One-way roads and motorway carriageways: vehicles drive only in the allowed direction, in one lane.
- Bridges and tunnels: vehicles follow them; their height follows the deck or the tunnel.
- A road removed in front of a vehicle mid-route: see story 7.
- Thousands of vehicles on one line: the stops queue; vehicles waiting for a stop fill the road behind it;
  queues extending back into a junction keep the junction clear for crossing traffic when possible.
- Full load forever: a vehicle on "full load" at a stop with no supply waits indefinitely (player's choice).
- Infinite-money mode: purchases always succeed and are still listed in the ledger.
- Game over (feature 011): every vehicle command is refused like other commands.
- Map-less test scenarios (benchmarks, determinism goldens) keep the synthetic traffic and their recorded
  fingerprints.

## Requirements *(mandatory)*

### Functional Requirements

**Depots**

- **FR-001**: The player MUST be able to build a road depot on a free, flat, dry tile next to a road the
  company may connect to, with an entrance facing that road; the depot costs money from a data file and has a
  yearly maintenance like other property.
- **FR-002**: Depots MUST be removable by their owner when no vehicle is inside or on its entrance; removal
  refunds part of the cost.
- **FR-003**: The depot window MUST list the vehicles inside, offer the models available this year, and
  allow buying, starting, stopping, selling and cloning.

**Vehicle models**

- **FR-004**: Vehicle models MUST be defined in a validated data file: id, name key, kind (bus or truck),
  the cargo it can carry (passengers; or a set of cargo for trucks), capacity, maximum speed, acceleration,
  length, price, running cost per year, expected life in years, base reliability, first and last year of
  availability, and model colour. Invalid data MUST stop the game at startup with a message naming the file
  and field.
- **FR-005**: The starter set MUST have two bus models (one of them electric) and three truck models covering every freight cargo of
  feature 009 and mail; models available in the start year MUST exist for any start year from 1850.
- **FR-006**: A truck model MUST carry one cargo of its category at a time (bulk: coal, iron ore, grain, wood;
  tanker: oil, fuel; box: mail, goods, food, steel, lumber, livestock), chosen when bought and changeable free of
  charge while stopped in a depot (an emptied hold); a bus carries passengers. Each model also has a power kind
  (combustion or electric) used for breakdown effects.

**Buying, selling, cloning, sending to depot**

- **FR-007**: Buying MUST charge the model's price (inflated by feature 011's cost level) as a vehicle
  purchase, refuse when cash is short (unless infinite money), and register the vehicle's running cost and
  value with the finance system.
- **FR-008**: Selling MUST be possible only for a vehicle stopped in a depot; it MUST book the vehicle's
  current value (price falling linearly over its expected life to a minimum share, from data) as a positive
  amount in the vehicle purchase row of the ledger (net vehicle investment) and remove its running cost.
- **FR-009**: Cloning MUST buy the same model in the depot of the original vehicle (or, for a vehicle on the
  road, the depot it was bought in) with the same line, cargo choice and service interval.
- **FR-010**: Sending a vehicle to the depot MUST make it drive to the nearest reachable depot of the company
  (by route length) and stop there; sending it again cancels the order.

**Lines and orders**

- **FR-011**: A line MUST be an ordered list of one or more stops (stations of the company), with a name and
  a colour, shared by every vehicle assigned to it. The player MUST be able to create, rename, delete a line
  (vehicles on it stop at their next stop), add, remove and reorder stops, and assign or unassign vehicles.
- **FR-012**: Each stop MUST carry a loading rule: load if available (default), full load, unload only, or no
  loading.
- **FR-013**: A vehicle on a line MUST drive to its stops in order, cycling, and use at its stop a station
  piece of the right kind (buses: bus pieces; trucks: truck pieces). A stop it cannot use or reach is skipped
  and reported once.
- **FR-014**: At a stop the vehicle MUST first unload (feature 010 rules; accepted cargo is delivered and paid,
  the rest stays on board) and then load waiting cargo it can carry up to its capacity according to the stop's
  rule. Loading and unloading MUST take time in proportion to the amount moved (rate in data), with a minimum
  stop time.
- **FR-015**: Every delivery's income MUST appear as a floating text above the vehicle that delivered it.

**Driving and interaction**

- **FR-016**: Vehicles MUST drive along the road tiles of their route at a speed limited by the model, the road
  type's speed limit, curves and slopes as defined in data, accelerating and braking smoothly.
- **FR-017**: Two-way roads MUST have one lane per direction (right-hand traffic); one-way roads and motorway
  carriageways one lane in the allowed direction. There is no overtaking.
- **FR-018**: A vehicle MUST never come closer to the vehicle ahead in its lane than a safe gap (data), and
  MUST brake in time to keep it.
- **FR-019**: Junctions (tiles where routes cross or merge) MUST be entered only when the vehicle can reserve
  the junction for its path and has room to leave it; reservations are granted first come, first served, with
  ties broken by a fixed order (lower vehicle id first). A vehicle MUST NOT enter a junction it cannot leave.
- **FR-020**: Each station piece MUST serve a fixed number of vehicles at once (data: two places in a bay, one
  behind the other; one per direction at a drive-through stop); other vehicles MUST queue behind on the road and
  use it in arrival order.
- **FR-021**: Vehicles MUST turn around only at dead ends (bays, depot entrances and road ends), in depots and at
  junctions they hold (leaving by the link they came from); never in the middle of a road, except under the stuck
  rule (FR-022).
- **FR-022**: A vehicle that has not moved for longer than a stuck limit (data, about 2 in-game days) MUST be
  reported to the player once (vehicle and place) and be listed as stuck in its window; if it is still blocked
  after twice the limit, it MUST turn around where it stands (into the opposite lane, when that lane has room) and
  plan a new route.
- **FR-023**: Simple junctions (crossings and T-junctions of two-way roads) MUST not deadlock permanently under
  any traffic pattern of vehicles that can all leave them.

**Ageing and reliability**

- **FR-024**: Vehicles MUST age by in-game days; reliability MUST fall daily by a data-defined amount, faster
  past the expected life, and MUST never go below a minimum (data).
- **FR-025**: Each day a moving vehicle MAY break down with a chance that grows as reliability falls (data). A
  broken-down vehicle keeps driving, alternating between half and a quarter of its normal speed (period in data),
  and heads for the nearest reachable company depot to be repaired; it recovers on reaching the depot or when the
  breakdown time (data) has passed, whichever comes first, then resumes its line with part of its reliability
  restored. While broken down it trails smoke (combustion) or sparks (electric). All chance comes from the game's
  seeded randomness.
- **FR-026**: Each vehicle MUST have a service interval (days, default from data, changeable per vehicle, 0 =
  never); when due, it MUST visit the nearest reachable company depot after its current stop, be serviced
  (reliability back to the model's base) and resume its line. If no depot is reachable, it continues and tries
  again later.

**Rerouting and network changes**

- **FR-027**: Routes MUST be planned on the road network of feature 007 and replanned when the network changes
  or a road on the route disappears, from the vehicle's current position, spread over ticks so that a large
  change does not stall a frame.
- **FR-028**: Road removal, station removal and depot removal MUST be refused while a vehicle stands on an
  affected tile, with a reason the player can read.

**Commands, determinism and performance**

- **FR-029**: Every player action of this feature (build/remove depot, buy, sell, clone, start/stop, send to
  depot, change cargo, service interval, create/edit/delete line, assign) MUST be a recorded command that
  replays identically.
- **FR-030**: The vehicle simulation MUST be deterministic: the same game with the same commands MUST produce
  the same state at every tick, and running the tick in parallel MUST give exactly the same result as running
  it sequentially.
- **FR-031**: All vehicle state MUST be part of the game's fingerprint; games without vehicles, and the
  map-less test scenarios, MUST keep their current fingerprints.
- **FR-032**: A debug check (option and test helper) MUST verify that no two vehicles overlap in a lane and that
  every vehicle is on a road tile of its route.
- **FR-033**: A debug option MUST seed many vehicles on generated lines in a game with a map (client and
  headless), for performance checks and demonstrations.

**Presentation**

- **FR-034**: Vehicles MUST be drawn with voxel models of their model (bus or truck shapes, placeholders
  allowed), coloured, oriented along their travel direction, at the right lane, following slopes, bridges and
  tunnels, and interpolated smoothly between ticks.
- **FR-035**: The player MUST be able to click a vehicle to open its window: model, line, next stop, state
  (driving, loading, waiting, broken down, going to depot, in depot, stuck, no route), load, speed, age,
  reliability, value, profit this year and last year, and buttons to send to depot, sell (in depot), clone,
  start/stop, change line and service interval.
- **FR-036**: A line window MUST show the stops in order with their loading rule and the vehicles on the line,
  and allow editing (reorder, remove, loading rule, rename, delete). "New line" (from the vehicle window or a
  Lines window) and "add stops" MUST start a mode in which clicking the company's stations on the map appends them
  in order and Esc ends it; a vehicle that started the mode is assigned to the new line.
- **FR-037**: Vehicles MUST play the existing engine sounds of feature 019 when near the camera.
- **FR-038**: Every player-facing text MUST be localized in English and Spanish.

### Key Entities

- **Depot**: a company-owned tile beside a road with an entrance direction; holds stopped vehicles; place where
  vehicles are bought, serviced, sold and turned around.
- **Vehicle model**: data-defined kind, cargo, capacity, speed, acceleration, length, price, running cost, life,
  reliability, availability years.
- **Road vehicle**: a bought vehicle: id, owner, model, cargo choice, home depot, line and current stop index,
  cargo hold, position on the road (lane, tile path, distance), speed, state, age, reliability, days since
  service, service interval, value, yearly profits.
- **Line**: owner, name, colour, ordered stops; each stop is a station with a loading rule.
- **Route**: the sequence of road tiles a vehicle follows to its next destination, planned on the road network.
- **Junction reservation**: which vehicle holds a junction tile and for which path, granted in a fixed order.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 50 000 road vehicles driving with interactions on a 4096 × 4096 map simulate in 4 ms or less per
  tick on average (8 ms worst) on the reference machine, and fast forward stays at ×20 or more.
- **SC-002**: The no-overlap check finds no overlap in any lane over 100 000 vehicle-ticks of randomized
  scenarios (tests) and in the seeded 50 000-vehicle game over 10 000 ticks.
- **SC-003**: A crossing and a T-junction fed from every side at full capacity keep vehicles passing for a
  whole in-game month (tests); any vehicle blocked longer than the stuck limit is reported.
- **SC-004**: The same seeded game run with the tick in parallel and sequentially gives identical fingerprints
  at every checkpoint, on every CI platform.
- **SC-005**: A new player can go from an empty road to a bus earning money (depot, two stations, a line, a
  bought bus) in under five minutes, guided only by the interface.
- **SC-006**: A route between two stops 1 000 tiles apart is found within 2 ms and network changes never stall
  the tick by more than the tick budget (replanning is spread over ticks).
- **SC-007**: The vehicle tick and the frame's vehicle drawing allocate no memory in steady state.

## Assumptions

- Lines are shared by their vehicles (editing a line changes every vehicle on it); per-vehicle orders arrive with
  feature 017 if needed.
- Vehicles drive on any road (company or town); depots and stations belong to the company.
- Breakdowns slow the vehicle down instead of stopping it (vehicles behind follow at its speed).
- Vehicle value and sale price fall linearly with age to a minimum share; numbers are in a data file.
- Vehicles report income per delivery; per-vehicle profit is the sum of its deliveries minus its running costs.
- The synthetic prototype traffic stays only in map-less scenarios (benchmarks and determinism goldens); games
  with a map have only the player's vehicles (and seeded ones with the debug option).
- Models of feature 018's pipeline are used; until dedicated bus and truck models exist, generated placeholder
  models are acceptable.
