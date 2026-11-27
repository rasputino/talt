# Feature Specification: Destination demand model

**Feature Branch**: `013-destination-demand`

**Created**: 2026-11-26

**Status**: Draft

**Input**: User description: "A core pillar of the game: passengers, mail and consumer goods want to go
to specific places. The player's challenge is to build networks that serve real demand. This must
scale to very large maps without slowing down. P1: towns generate passengers and mail with
destinations in other towns; demand between two towns depends on their sizes and distance. P1:
waiting passengers at a station are grouped by destination, and vehicles only pick up cargo that their
line can bring closer to its destination, including transfers between my lines at shared stations.
P1: goods and food produced by industries are wanted by specific towns, not any town. P2: I can see,
for a station or a town, where its passengers and goods want to go and how much of that demand my
network currently serves. P2: when I add or change lines, demand gradually redistributes to use the
new connections. P3: unserved demand is visible as a map overlay to suggest new connections. Success:
on the reference scenario, demand and routing updates fit within the tick budget, amortised over time
(no single tick above 8 ms); cargo is conserved: everything generated is either delivered, waiting, on
board or explicitly lost (expired), verified by tests. Out of scope: individual passenger simulation."

## Context

Feature 008 founded towns whose buildings produce passengers and mail every month and accept goods and
food; feature 009 added cargo types (passengers, mail and goods marked as destination cargo) and
industries; feature 010 added stations and warehouse groups where cargo waits in packets that already
carry a destination field (always "any destination" so far), and the load, unload and transfer
operations; feature 011 pays deliveries by cargo, amount, distance between the pickup and delivery
stations and days in transit; feature 012 added road vehicles that run lines (ordered stops with
rules) and load and unload at each stop.

Today a passenger picked up at a bus stop is delivered at the first other stop whose catchment accepts
passengers, wherever that is: every town is the destination of every passenger. This feature gives
passengers, mail and consumer goods a real destination town and makes the player's network carry them
there, possibly over several lines with transfers at shared stations, and pays them only when they
arrive where they wanted to go. Destination cargo is simulated as aggregated amounts (packets keyed by
destination), never as individual agents (VISION, "Cargo model").

## Clarifications

### Session 2026-11-26

- Q: What happens to destination cargo whose destination the network cannot reach, and how is demand
  shaped? → A: Each passenger, bag of mail or crate of goods wants one destination town. Half of a
  town's passengers and mail (data value 50 %) want to stay in their own town (delivered at any other
  station group of the company whose catchment holds buildings of that town, never the one they left
  from); the other half want other towns, shared in proportion to destination population divided by
  distance (exponents in data; the developer's example 10/8/5/2 tiles → 5.4/6.8/10.8/27 % is the
  distance-only case). The part whose destination the network does not reach does not appear at the
  station and counts as unserved demand. At every transfer the best route is chosen again from that
  station; payment falls with travel time (feature 011), so many slow transfers pay less.
- Q: Does food have destinations? → A: No: food keeps going to any town that accepts it. Destination
  cargo is passengers, mail and goods.
- Q: Does mail have destinations? → A: Yes, like passengers (same 50 % own-town / 50 % other towns
  split), with its own data weights so large towns receive more mail.
- Q: How long may destination cargo wait at a station? → A: 60 days (data value); then it expires and
  counts as lost.
- Q: How is a transfer's payment shown in each vehicle's profit? → A: Per-leg credit: a vehicle that
  leaves cargo at a transfer records an estimated credit for its leg (statistics only, no money); the
  vehicle that delivers records the payment minus the credits already recorded. The company's cash
  gets the single payment at delivery either way.

## Scope and deferrals

- **In scope now**: demand between towns (passengers, mail) and from industries to towns (goods, and
  any other cargo the data marks as destination cargo); destinations given to destination cargo when it
  reaches a station; a network graph of the company's station groups and the connections its lines
  make, kept up to date as lines, stations and vehicles change; routes over that graph, recomputed
  gradually; vehicles loading only cargo their line brings closer to its destination; automatic
  transfers at shared stations; payment only on arrival at the destination; expiry of cargo that waits
  too long; the station, vehicle and town windows showing destinations and demand; an unserved demand
  map overlay; benchmarks and documentation.
- **Raw materials and processed goods** keep the feature 010 rule: any accepting station is a valid
  destination.
- **Mode-agnostic**: the network graph and routes are built from lines and stations, not from roads,
  so trains (feature 015) and later ships and aircraft carry the same destination cargo through the
  same stations by running lines; nothing here depends on the road network.
- **Deferred to feature 014**: saving and loading the new state (it is all part of the fingerprinted
  simulation state, so 014 can save it like the rest).
- **Deferred to feature 017**: detailed line statistics and charts of served demand over time.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Towns generate passengers and mail for other towns (Priority: P1)

Every town's passengers and mail want a destination town: half of them (data value) their own town,
the other half other towns, more towards bigger and closer towns. When passengers or mail from a town
reach one of the player's stations, each unit gets its destination by those shares; units whose
destination the player's network cannot reach from that station do not appear there and count as
unserved demand.

**Why this priority**: Destinations are the core of the feature and of the game's identity (pillar 2);
nothing else in the feature works without them.

**Independent Test**: On a map with a few towns of known sizes and positions, compute the demand table
and check the own-town share and that bigger and closer towns get more demand; serve a bus stop
connected by lines to two other towns and to another stop of its own town and check that the
passengers arriving at it are split between those destinations in the proportion the demand gives and
that the share for unreachable towns stays unserved.

**Acceptance Scenarios**:

1. **Given** towns A, B and C where B is twice as populous as C and both are equally far from A,
   **When** demand is computed, **Then** A's demand for B is larger than its demand for C by the ratio
   the data-defined formula gives, and A's own share is the data-defined 50 %.
2. **Given** towns at 10, 8, 5 and 2 tiles from A with the population exponent set to 0, **When**
   demand is computed, **Then** A's other-town half is split 10.8/13.5/21.6/54 % (5.4/6.8/10.8/27 % of
   all), within rounding.
3. **Given** a bus stop in town A whose lines reach stations in towns B and C and another stop in A,
   **When** passengers arrive at the stop, **Then** every unit has destination A, B or C by the demand
   shares, none has "any destination", and the share for towns the network does not reach is counted
   as unserved and never appears at the stop.
4. **Given** a bus stop whose lines reach no destination, **When** passengers would arrive, **Then**
   none are moved to the stop (they stay with the town, as unserved cargo does today) and all are
   counted as unserved demand.

---

### User Story 2 - Cargo travels to its destination, with transfers (Priority: P1)

Waiting cargo at a station is grouped by destination. A vehicle arriving at a station only loads cargo
whose route continues on the vehicle's line from that station; at a station shared by several of the
player's lines, cargo leaves one line's vehicle and waits for a vehicle of the next line on its route.
Cargo is paid when it reaches a station that serves its destination town, by the distance from where it
was first picked up.

**Why this priority**: Without routing, destinations would only block deliveries; with it the player's
network design (and transfers) matters.

**Independent Test**: Build lines A–B and B–C sharing a station in B; check that passengers from A to
C ride the first line to B, are unloaded there, wait at B grouped under C, ride the second line to C,
and are paid once at C by the A–C distance; check that passengers to B are delivered at B and that a
vehicle of a line that does not lead towards a destination leaves that cargo waiting.

**Acceptance Scenarios**:

1. **Given** passengers waiting at A for B and for C, and a vehicle of a line A–B, **When** it loads at
   A, **Then** it takes passengers whose route goes through B (to B and to C via B) and leaves the rest.
2. **Given** that vehicle arriving at B, **When** it unloads, **Then** passengers for B are delivered and
   paid, passengers for C are unloaded and wait at B for C, and nothing for C is paid yet.
3. **Given** passengers waiting at B for C and a vehicle of line B–C, **When** it reaches C and unloads,
   **Then** they are delivered and paid once, by the distance from A (where they were first picked up)
   and the days since then.
4. **Given** a vehicle carrying passengers whose destination it does not serve and whose route does not
   pass through its next stops, **When** it stops at a station where they can change line, **Then** they
   are unloaded there to wait for the right line; they never ride away from their destination for
   ever.
5. **Given** a line with the "unload and leave empty" (transfer) rule at a stop, **When** a vehicle
   unloads there, **Then** everything it carries waits at that station (kept from feature 012) and
   continues by its route from there.
6. **Given** a station group (warehouse) whose members include stations of several lines, **When**
   cargo changes line inside the group, **Then** it counts as one shared station (feature 010).

---

### User Story 3 - Goods are wanted by specific towns (Priority: P1)

Consumer goods made by factories want to go to a specific town. When goods reach a station from a
factory, each unit gets a destination among the towns the network can reach that accept goods,
weighted by the town's size and its distance, and it is paid only when delivered there.

**Why this priority**: The third destination cargo kind; goods are the most valuable town cargo and
their destinations decide where freight lines are worth building.

**Independent Test**: Serve a factory with a truck stop whose lines reach two goods-accepting towns of
different sizes; check that the goods are split between them by the data-defined weights, that goods
unloaded in a town that is not their destination are not paid, and that raw materials (coal) are
still delivered to any accepting station as before.

**Acceptance Scenarios**:

1. **Given** a factory served by a truck stop whose lines reach towns X (large) and Y (small) that both
   accept goods, **When** goods arrive at the stop, **Then** more of them have destination X than Y, in
   the proportion the data gives.
2. **Given** goods for X unloaded at a station in Y, **When** the station accepts goods, **Then** they
   are not delivered there: they continue towards X (transferred if their route goes on, otherwise they
   stay on board).
3. **Given** coal and other undirected cargo, **When** it is unloaded at any accepting station, **Then**
   it is delivered and paid as in features 010 and 011.

---

### User Story 4 - See where cargo wants to go and how much is served (Priority: P2)

The station window lists waiting cargo per destination town (and the next station on its route); the
vehicle window lists what the vehicle carries per destination; the town window lists the towns its
passengers and mail most want to reach, the demand to each per month and how much of it the player's
network currently carries.

**Why this priority**: Destinations are only a game mechanic if the player can see them; the core flow
works and is testable without the windows.

**Independent Test**: With lines and waiting cargo as in story 2, open the station, vehicle and town
windows and check that the figures match the simulation state (amounts per destination, demand and the
served share).

**Acceptance Scenarios**:

1. **Given** a station with passengers waiting for two towns, **When** the player opens its window,
   **Then** it shows each destination town with its waiting amount and the next station of its route.
2. **Given** a vehicle carrying cargo for several towns, **When** the player opens its window, **Then**
   it shows the amount per destination town.
3. **Given** a town, **When** the player opens its window, **Then** it shows its top destinations with
   monthly demand and the share the player's network served last month, and the same for goods it
   receives.

---

### User Story 5 - Demand redistributes when the network changes (Priority: P2)

When the player adds a line, adds or removes stops, buys or sells vehicles on a line or removes a
station, routes are recomputed and new cargo starts using the new connections within a few in-game
days, without a pause or a long tick. Cargo already waiting or on board whose route changed or vanished
is re-routed; cargo that can no longer reach its destination waits until it expires.

**Why this priority**: Networks change constantly; stale routes would make cargo pile up in wrong
places.

**Independent Test**: Run lines A–B and B–C, then add a direct line A–C; check that within the
data-defined number of days new A→C passengers are routed on the direct line (or split by capacity),
that no single tick exceeds the budget while routes update, and that results are identical with
parallel and sequential ticks.

**Acceptance Scenarios**:

1. **Given** a running network, **When** a new line connects two of its stations, **Then** routes using
   it apply within the data-defined update period, at a known tick that does not depend on thread
   timing.
2. **Given** two routes between the same stations, one of them nearly saturated, **When** routes are
   recomputed, **Then** part of the demand moves to the other route in proportion to free capacity and
   travel time (the data defines how).
3. **Given** a line is deleted, **When** routes are recomputed, **Then** cargo waiting for it is
   re-routed if another route exists, and otherwise waits and expires after the data-defined time,
   counted as lost.

---

### User Story 6 - Unserved demand overlay (Priority: P3)

A map overlay draws lines between pairs of towns with the largest demand the player's network does not
serve, thicker for more demand, so the player can see where a new connection would pay.

**Why this priority**: A planning aid; the game is playable without it.

**Independent Test**: With some towns connected and some not, turn the overlay on and check that it
lists the pairs with the largest unserved demand, ordered by amount, and that pairs the network serves
fully are not drawn.

**Acceptance Scenarios**:

1. **Given** the overlay is on, **When** the map is drawn, **Then** the largest unserved town pairs are
   shown with a thickness or colour by amount, and served pairs fade.
2. **Given** a new line that serves a drawn pair, **When** routes update, **Then** the overlay drops
   or thins that pair.

### Edge Cases

- A town with no other town within the demand range sends all its passengers and mail to itself.
- A station group on none of the company's lines (only the debug service serves it) keeps the feature 010
  behaviour: its cargo has no destination and is delivered anywhere it is accepted.
- A station whose group covers several towns: cargo from each town keeps its own source town; a
  destination town is "served" at a station group whose catchment contains buildings of that town
  that accept the cargo.
- Origin and destination in the same town: own-town cargo is delivered at any other station group
  of the company covering buildings of that town that accept it, never at the group it left from.
- Lines whose vehicles are all stopped or in depots carry no capacity: their connections stay in the
  graph with zero capacity and are avoided when another route exists.
- A station removed while cargo waits there: that cargo is lost as in feature 010; cargo on board that
  was routed through it is re-routed.
- A vehicle with a full hold and nothing to unload: it keeps its cargo; cargo never leaves a vehicle
  except to be delivered or to wait at a station.
- A cargo whose next station is the station the vehicle is at (loops in routes): routes never contain
  loops; a route that would cycle is discarded.
- Company game over: nothing special; existing lines keep running and routing continues (feature 011
  refuses new commands).
- Map-less scenarios (benchmarks, CI goldens) have no towns or stations: the feature does nothing and
  their fingerprints are unchanged.

## Requirements *(mandatory)*

### Functional Requirements

**Demand**

- **FR-001**: The game MUST give every town, per destination cargo of town buildings (passengers,
  mail), a destination table: a data-defined own-town share (default 50 %) and the rest split over
  other towns in proportion to population^a / distance^k (a and k per cargo in data; mail may weigh
  population more so large towns receive more mail).
- **FR-002**: Each town MUST keep at most a data-defined number of other-town destinations (the largest
  weights), so the cost does not grow with the square of the number of towns.
- **FR-003**: The game MUST give every industry producing a destination cargo (goods) a destination
  table over the towns that accept that cargo, by the same population and distance weighting (no
  own-town share).
- **FR-004**: Demand MUST be refreshed monthly (towns grow and change), spread over the month so no
  single tick does all the work.
- **FR-005**: Which cargo types have destinations MUST come from the cargo data (`destinationRequired`);
  every demand parameter MUST come from a validated data file.

**Destinations at stations**

- **FR-006**: When destination cargo moves from a producer to a station group (feature 010 supply),
  the amount MUST be split over the source's destination table with exact integer rounding (carried
  remainders) and no randomness; each unit gets its destination town.
- **FR-007**: The units whose destination is not reachable from that group by the company's network
  (for the own town: no other group of that town reachable) MUST NOT move to the station; they MUST be
  counted as unserved demand of their source and destination.
- **FR-008**: Waiting cargo at a station group MUST be kept per destination town and next station
  group of its route; raw and processed cargo MUST keep "any destination".

**Network graph and routes**

- **FR-009**: The game MUST keep, per company, a graph whose nodes are station groups and whose edges
  are the consecutive stops of its lines (in both stop orders a line visits), with a capacity (units per
  month the line's vehicles carry over that edge) and a travel time.
- **FR-010**: The graph MUST be built only from lines, stops and the vehicles assigned to them, not
  from roads or any other mode-specific data, so any vehicle mode running lines through stations uses
  it unchanged.
- **FR-011**: Routes MUST be recomputed when the graph changes and periodically, split into slices
  that run over several ticks (optionally on worker threads), and their result MUST apply at a tick
  fixed when the job starts, so the simulation state never depends on thread timing.
- **FR-012**: A route assignment MUST split each origin–destination demand over one or more paths,
  preferring shorter travel time and spreading over paths with free capacity (data-defined), and MUST
  give every station group, per destination town, the next station group(s) and the share for each.
- **FR-013**: Routes MUST be loop-free and MUST only use connections of the cargo's company.

**Vehicles**

- **FR-014**: A vehicle loading at a station MUST take only destination cargo whose next station on its
  route is the vehicle's next stop (routes are made of consecutive stops, so cargo riding several stops
  of one line keeps matching at each stop); undirected cargo loads as before.
- **FR-015**: A vehicle unloading at a station MUST deliver destination cargo whose destination town is
  served at that station's group, MUST unload (transfer) cargo whose route continues on another line
  from that group or that its line will not bring closer, and MUST keep the rest on board.
- **FR-016**: The interface between vehicles and stations MUST not depend on the vehicle mode: a vehicle
  gives the station its line (or the list of its upcoming stops) and its hold.
- **FR-017**: Destination cargo delivered at its destination MUST be paid once, by the distance from the
  station where it was first picked up and the days since then (feature 011); transfers MUST not move
  money. A vehicle leaving cargo at a transfer MUST record an estimated credit for its leg in its profit
  statistics, carried with the cargo; the delivering vehicle MUST record the payment minus the credits
  carried (which may be negative).
- **FR-017a**: At every station where cargo waits (after a transfer too) its next station MUST come
  from that station's current routes, so it always takes the best route known from where it is.

**Expiry and conservation**

- **FR-018**: Destination cargo waiting at a station longer than a data-defined number of days MUST
  expire and be counted as lost (default 60 days).
- **FR-019**: Every unit of destination cargo MUST at all times be exactly one of: delivered, waiting,
  on board or lost (cap, neglect, removed station, expired), and totals MUST be kept to verify it.

**Interface**

- **FR-020**: The station window MUST show waiting destination cargo per destination town with the
  next station of its route.
- **FR-021**: The vehicle window MUST show cargo on board per destination town.
- **FR-022**: The town window MUST show the town's top destinations with monthly demand and the share
  served last month, and the goods demand towards it.
- **FR-023**: A map overlay MUST show the town pairs with the largest unserved demand.
- **FR-024**: Every player-facing text MUST be localized in English and Spanish.

**Determinism and performance**

- **FR-025**: All new state MUST be fixed-point or integer, hashed in the fingerprint, free of wall-clock
  time and unseeded randomness, and identical with parallel and sequential ticks.
- **FR-026**: Games where no line exists MUST keep their earlier fingerprints; map-less scenarios MUST be
  unaffected.
- **FR-027**: Demand generation and route updates MUST not allocate in steady state on the tick path.

### Key Entities

- **Destination table**: for a source (town, or industry for goods) and cargo, its own-town share and
  its top destination towns with a share each.
- **Network graph**: per company, station groups as nodes; edges between consecutive line stops with
  capacity and travel time; versioned so routes know when they are stale.
- **Route table**: per company, station group and destination town, up to a few next station groups
  with their shares; the tick at which it applies.
- **Destination packet**: waiting or held cargo with its destination town, next station group, origin
  station, source town and pickup day.
- **Demand statistics**: per town pair (and industry–town), generated, unserved, delivered and lost
  amounts this and last month.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On the reference scenario (feature 012's: 4096 × 4096 map, its towns, bus bays on a
  40-tile lattice — over 5 000 stations — and 50 000 buses on lines) demand and route updates never make a single tick exceed 8 ms and add on average under
  1 ms per tick.
- **SC-002**: Fast forward still reaches at least ×20 and the 4096 map still renders at 60 FPS with
  the feature active.
- **SC-003**: Conservation tests over at least one simulated year show generated = delivered + waiting
  + on board + lost for every destination cargo, to the unit.
- **SC-004**: A new connection is used by newly generated cargo within the data-defined update period
  (default at most 5 in-game days) on the reference scenario.
- **SC-005**: On a three-town, two-line test network, passengers from the first to the third town are
  delivered through the shared station and paid once, every time.
- **SC-006**: Results with parallel and sequential ticks and with background route jobs give the same
  fingerprint at every checked tick.

## Assumptions

- Destinations are towns, not individual buildings or stations: a destination is reached at any of the
  company's station groups whose catchment holds buildings of that town accepting the cargo.
- Demand within a single town is not modelled in this feature.
- Food stays undirected (clarified); destination cargo is passengers, mail and goods, as marked in
  `cargo.json`.
- Payments keep feature 011's formula; only the moment of payment changes (arrival at the destination).
- Companies are independent: one company's lines never carry another's cargo.
- A vehicle's capacity on its line is estimated from its model's capacity and the time it takes to run
  the line (measured in ticks of its last rounds, or estimated from distances until it has run one).
