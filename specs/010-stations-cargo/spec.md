# Feature Specification: Stations and cargo handling

**Feature Branch**: `010-stations-cargo`

**Created**: 2026-11-24

**Status**: Draft

**Input**: User description: "Stations connect the player's network to towns and industries. Cargo
appears at stations from nearby producers, waits, and is loaded onto vehicles; delivered cargo is
accepted by nearby consumers. P1: build bus stops and truck loading bays next to roads; each station
shows its catchment area when placing it, and which cargo it will receive and accept. P1: cargo
produced within the catchment area appears at the station only after a vehicle has started serving
that cargo there, and waits until loaded. P1: vehicles unload cargo that the station accepts and get
paid (feature 011); cargo not accepted stays on board or is transferred if ordered. P2: each station
has a rating per cargo that depends on waiting time, vehicle speed, frequency and amount waiting; a
higher rating means more cargo is delivered to the station. P2: expand stations by building adjacent
pieces that join the same station. P3: a station window with waiting cargo (by destination when
applicable), ratings and recent activity. Success: 5 000 stations update ratings and cargo daily in
under 2 ms total; catchment previews update in real time while moving the placement cursor. Out of
scope: rail stations (015), destination routing logic (013)."

## Context

Feature 006 gave the game construction actions with costs, previews, ownership and a company balance;
feature 007 added tile-level roads and a routing graph; feature 008 founded towns whose buildings
produce passengers and mail every month, accept goods and food, and keep a rating of each company that
decides whether it may build stations in town; feature 009 added data-defined cargo types and
industries that produce cargo into a stock and accept deliveries.

Until now nothing connects producers and consumers: industry stock just piles up and towns' transport
figures are zero. This feature adds stations: the places where cargo from the surroundings gathers,
waits for vehicles and is handed to consumers. Vehicles that actually drive, load and unload arrive in
feature 012 and payments in feature 011; this feature defines exactly how they will hand cargo to and
take cargo from a station, and proves those rules without real vehicles.

## Clarifications

### Session 2026-11-24

- Q: How are bus stops and truck loading bays placed relative to the road? → A: Both forms: a bay on its
  own tile beside a road with one entrance from it, and a drive-through stop on a straight road tile
  (the player's or a town's) that vehicles pass through. Each of the two station types comes in both forms.
- Q: Which cargo does each piece type handle? → A: Bus stops handle passengers only; truck stops handle
  mail and all other cargo; a station with both kinds of piece handles everything. Stations join by
  building pieces next to each other (one station). In addition, the company can build warehouses in three
  sizes (small, medium, very large) next to roads, with an influence radius of 8, 16 and 32 tiles (data
  values): every company station with a piece inside a warehouse's radius forms a group with it that shares
  one waiting stock per cargo, acceptance and ratings, as one virtual station, while each station keeps its
  name and its own catchment; groups of warehouses whose radii overlap merge into one group.
- Q: How is cargo flow shown before vehicles exist (feature 012)? → A: A debug option of the client and the
  headless tool (`--station-service <percent>`) marks every cargo of every station as served and every few
  days loads that percentage of the waiting cargo into a sink (counting as transported for towns); it is
  part of the scenario settings, so recorded sessions replay it.
- Q: What are the catchment radii and the waiting cap? → A: Bus stop radius 3 tiles, truck stop radius 4
  tiles (rail 4 is for feature 015); at most 4 000 units of each cargo wait per station or warehouse group;
  the excess is lost and lowers the rating. All are data values.
- Q: How are new stations named? → A: Road stops: town + type + number ("Fallowstead Bus Stop",
  "Fallowstead Bus Stop 2", localized). Warehouses (and later rail stations): town + place suffix by
  position from the town centre ("Fallowstead Central", "Fallowstead North"...), with a number when
  repeated. The player can rename any station (a recorded command).

## Scope and deferrals

- **In scope now**: two road station types (bus stop, truck stop), each as a bay beside a road or a
  drive-through stop on a straight road, built as construction actions with cost, preview and ownership;
  joining adjacent pieces into one station; warehouses in three sizes that group the stations in their
  influence radius into one virtual station; renaming stations;
  removing pieces; catchment areas with a live preview of the cargo a placement would receive and
  accept; per-station per-cargo waiting cargo, ratings and statistics; daily distribution of
  production from industries and towns to the stations serving it; acceptance of delivered cargo by
  consumers in the catchment; the loading and unloading operations vehicles will use; the station
  window; placeholder station models.
- **Deferred to feature 011**: money paid for deliveries (this feature reports each accepted delivery
  with everything a payment needs: cargo, amount, origin and destination, time in transit).
- **Deferred to feature 012**: road vehicles, their orders and their visits to stations (this feature
  provides the operations a visit uses: start serving, load, unload, transfer).
- **Deferred to feature 013**: choosing destinations for passengers, mail and goods (waiting cargo
  already carries a destination field, "any destination" until then).
- **Deferred to feature 015**: rail stations (the station rules are written so a rail piece type can be
  added as data plus its own placement rule).
- **Deferred to feature 018**: final station models (this feature draws simple placeholders).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Build road stations with a catchment preview (Priority: P1)

The player picks the bus stop or the truck stop tool, in its bay or drive-through form, and moves the
cursor over the map. A placeholder station appears at the cursor, its catchment area is highlighted around it, and a list
shows which cargo the station would receive (produced nearby) and which it would accept (consumed
nearby). Clicking builds it, pays the cost, and the new station gets a name taken from the nearest
town.

**Why this priority**: Stations are the link between the player's roads and the economy; nothing else
in this feature can be seen without them.

**Independent Test**: On a generated map, place a bus stop next to a town road and a truck stop (bay form)
next to an industry; check that the preview highlights the catchment, lists the right cargo, refuses invalid
spots with a clear reason, and that the built stations exist, are owned by the player, cost what the
data says and are named after the nearest town.

**Acceptance Scenarios**:

1. **Given** the bus stop tool and a tile next to a road, **When** the player moves the cursor there,
   **Then** the preview shows the station, its catchment area and the cargo it would receive and accept,
   and it updates every frame as the cursor moves.
2. **Given** a valid placement, **When** the player clicks, **Then** the piece is built, its cost is
   taken from the company balance and the station appears with a name derived from the nearest town.
3. **Given** the drive-through form and a straight road tile (the player's, or a town's while the town
   allows stations), **When** the player builds there, **Then** the stop is built on the road, the road
   keeps working for traffic, and the road cannot be removed while the stop stands.
4. **Given** a bay on a tile with no adjacent road, a drive-through on a curve, junction or bridge end, a sloped or occupied tile, water, or not enough money,
   **When** the player tries to build, **Then** the action is refused with the matching reason and
   nothing changes.
5. **Given** a town whose rating of the company is below the station threshold, **When** the player
   tries to build a station inside that town's zone, **Then** the action is refused with the reason
   "the town refuses".
6. **Given** a station of the player, **When** the player renames it, **Then** the new name is shown
   everywhere and is kept in recorded sessions.
7. **Given** a station of the player, **When** the player uses the station removal tool on one of its
   pieces, **Then** the piece is removed for the data-defined cost, and when the last piece goes the
   station and its waiting cargo disappear.

---

### User Story 2 - Cargo appears at served stations and waits (Priority: P1)

Industries and town buildings inside a station's catchment area produce cargo every day. Once a
vehicle has started serving a cargo at a station (feature 012; tests and a debug option simulate it),
part of that production is moved to the station each day, where it waits until loaded. Cargo nobody
serves stays at the industry, as in feature 009, and town passengers nobody picks up are not
transported.

**Why this priority**: Waiting cargo is what vehicles will carry; without it stations are decoration.

**Independent Test**: Build a truck stop (bay form) next to a coal mine, mark coal as served at the bay through the
same operation vehicles will use, advance days and check that coal appears at the bay in the expected
amounts and is no longer added to the mine's stock; check that an unserved cargo never appears.

**Acceptance Scenarios**:

1. **Given** a station in a coal mine's catchment that nobody serves, **When** days pass, **Then** no
   coal waits at the station and the mine's stock grows as in feature 009.
2. **Given** coal is served at the station, **When** a day passes, **Then** the share of the mine's
   production set by the station's coal rating is moved to the station and the rest stays at the mine.
3. **Given** two stations serving the same cargo of the same producer, **When** a day passes, **Then**
   the moved cargo is shared between them in proportion to their ratings and nothing is created or lost.
4. **Given** a bus stop whose catchment covers town buildings and serves passengers, **When** days pass,
   **Then** passengers appear at the stop from those buildings' monthly passenger production.
5. **Given** a station whose waiting amount of a cargo reached the cap, **When** more arrives, **Then**
   the excess is lost and the station's rating for that cargo is lowered.

---

### User Story 3 - Loading and unloading rules for vehicles (Priority: P1)

Vehicles (feature 012) load waiting cargo at a station and unload it at another. Unloaded cargo that
the destination station accepts is handed to the consumers in its catchment and reported as a
delivery, with what feature 011 needs to pay for it. Cargo the station does not accept stays on board,
unless the vehicle's order says to transfer it, in which case it waits at that station for another
vehicle.

**Why this priority**: These are the operations features 011 and 012 build on; defining and testing
them now keeps cargo correct when vehicles arrive.

**Independent Test**: Without vehicles, load cargo from a station into a test cargo hold, unload it at
a station near a power station and near a town, and check that accepted cargo reaches the consumer and
is reported as a delivery, refused cargo stays in the hold, transferred cargo waits at the second
station with its origin kept, and the total amount of cargo is conserved at every step.

**Acceptance Scenarios**:

1. **Given** coal waiting at a station, **When** a vehicle loads up to its free capacity, **Then** the
   station's waiting coal falls by exactly the loaded amount and the hold carries it with its origin.
2. **Given** a hold with coal, **When** it unloads at a station whose catchment has a power station,
   **Then** the power station receives it through feature 009's delivery, and one delivery report is
   made with cargo, amount, origin, destination and days in transit.
3. **Given** a hold with coal, **When** it unloads at a station that does not accept coal without a
   transfer order, **Then** the coal stays in the hold and nothing is reported.
4. **Given** a transfer order, **When** the hold unloads at a station that does not accept the cargo,
   **Then** the cargo waits at that station keeping its original origin and pickup date.
5. **Given** passengers loaded at a town's bus stop, **When** they are loaded, **Then** the town counts
   them as transported (feature 008 service and growth).

---

### User Story 4 - Station ratings (Priority: P2)

Each station has a rating per cargo it serves. The rating rises when vehicles pick that cargo up often,
when the last vehicle was fast and when little cargo is left waiting; it falls when cargo waits for a
long time or piles up. A higher rating moves a larger share of the producers' output to the station.

**Why this priority**: Ratings reward good service and are what makes frequent, fast lines worth
building; the basic flow works without them at a fixed rating.

**Independent Test**: Feed a station's rating inputs (days since last pickup, last vehicle speed,
amount waiting) through the operations vehicles use and check the daily rating moves toward the value
the data-defined formula gives, within its range, and that the share moved from a producer follows it.

**Acceptance Scenarios**:

1. **Given** a newly served cargo, **When** the rating is read, **Then** it has the data-defined
   initial value.
2. **Given** no pickup for many days, **When** days pass, **Then** the rating falls step by step toward
   the formula's lower value.
3. **Given** frequent pickups by fast vehicles and little waiting cargo, **When** days pass, **Then** the
   rating rises toward the formula's higher value, never above the maximum.
4. **Given** a low rating and a large amount waiting, **When** days pass, **Then** part of the waiting
   cargo is lost, as the data says.

---

### User Story 5 - Expand stations with adjacent pieces (Priority: P2)

Building a piece next to one of the company's stations makes it part of that station: the catchment
grows to cover the new piece, waiting cargo and ratings are shared, and the station keeps its name.

**Why this priority**: Busy places need more than one stop; joining keeps one station per place in the
window and in vehicle orders.

**Independent Test**: Build a piece next to an existing station and check it joins it, that the
catchment is the union of the pieces' catchments, that a piece adjacent to two different stations is
refused, and that removing a piece in the middle never splits cargo between stations.

**Acceptance Scenarios**:

1. **Given** a station, **When** the player builds a piece on a tile next to one of its pieces, **Then**
   the piece joins that station and the catchment covers both pieces.
2. **Given** a placement next to two different stations of the company, **When** the player tries to
   build, **Then** it is refused with the reason "adjacent to two stations".
3. **Given** a station already as large as the data allows, **When** the player adds another piece
   outside that size, **Then** it is refused with the reason "station too spread out".
4. **Given** a joined station, **When** a piece is removed, **Then** the remaining pieces stay one
   station with all its waiting cargo, even if they no longer touch.

---

### User Story 6 - Warehouses group nearby stations (Priority: P2)

The player builds a warehouse (small, medium or very large) next to a road. Every company station with a
piece inside the warehouse's influence radius joins the warehouse's group: cargo arriving at any of those
stations goes into one shared stock per cargo, any of them can load it, cargo unloaded at any of them is
accepted when any member accepts it, and they share one rating per cargo. Each station keeps its own name
and catchment. Warehouses whose radii overlap form one group.

**Why this priority**: Warehouses let the player build hubs that collect cargo from several stations
without vehicles moving it between them; the basic station flow works without them.

**Independent Test**: Build two stations far apart, each next to a different coal mine, and a warehouse
whose radius covers both; mark coal served and check that both mines' coal ends in one shared stock that
either station can load from; build a second overlapping warehouse and check the groups merge; remove a
warehouse and check the group splits without creating or losing cargo.

**Acceptance Scenarios**:

1. **Given** a warehouse and two company stations with pieces inside its radius, **When** cargo arrives at
   either station, **Then** it is added to the group's single stock for that cargo.
2. **Given** a group, **When** a vehicle loads at any member station, **Then** it loads from the shared
   stock.
3. **Given** a group where only one member's catchment accepts coal, **When** coal is unloaded at another
   member, **Then** it is accepted and delivered to that consumer.
4. **Given** two warehouses whose radii overlap, **When** the second is built, **Then** all their stations
   form one group with the stocks added together.
5. **Given** a group, **When** its warehouse is removed, **Then** the stations form the groups that remain
   and the shared stock is divided among them in proportion to their number of stations, nothing lost.
6. **Given** a station outside every warehouse radius, **When** it is read, **Then** it is a group of its
   own with its own stock.

---

### User Story 7 - Station window (Priority: P3)

Clicking a station opens a window with its name, the cargo waiting (by destination when the cargo has
destinations; "any destination" until feature 013), the rating per cargo, and recent activity: cargo
received, picked up and delivered last month, and days since the last pickup per cargo. A button
toggles the catchment highlight on the map.

**Why this priority**: Players need to see why a station works or not, but cargo flows without the
window.

**Independent Test**: Open the window of a station with waiting cargo and ratings and check every
figure matches the simulation; change the language and check labels change but the station's town
name does not.

**Acceptance Scenarios**:

1. **Given** a station, **When** the player clicks one of its pieces, **Then** the station window opens
   with its figures.
2. **Given** the window is open, **When** a day passes, **Then** waiting amounts and ratings update.
3. **Given** the window is open, **When** the player presses the catchment button, **Then** the
   station's catchment is highlighted until pressed again or the window closes.

---

### Edge Cases

- A station with no producer or consumer in its catchment can be built; the preview says it would
  receive and accept nothing.
- An industry that closes (feature 009) stops sending cargo; cargo already waiting stays.
- A town that grows new buildings into a catchment starts contributing the next day.
- A producer covered by stations of several pieces of the same station counts once for that station.
- The road tile a bay connects to, and the road under a drive-through stop, cannot be removed or turned
  into a junction while the station piece stands (the road removal tool reports "a station uses this
  road"); towns never extend their streets through station pieces.
- Cargo is never accepted at the station group it was picked up from; it stays on board.
- The clear tool never removes station pieces; it reports "use the station removal tool".
- Amounts are whole units; when a day's share is not a whole number, the remainder is carried over so
  that no cargo is created or lost by rounding over time.
- Map-less scenarios (replays, benchmarks of earlier features) have no stations and keep their
  fingerprints.
- Removing a station with waiting cargo loses that cargo.

## Requirements *(mandatory)*

### Functional Requirements

**Building and removing**

- **FR-001**: The game MUST offer two road station types, bus stop and truck stop, each in two forms
  (bay and drive-through), defined in a validated data file with cost, removal cost, catchment radius,
  cargo categories handled and placeholder model colours.
- **FR-002**: A bay MUST be placed on a flat, empty, dry land tile that touches a road tile on one of its
  four sides; it faces that road and connects to it, so vehicles enter and leave from the road. A
  drive-through stop MUST be placed on a straight road tile without junction, bridge or tunnel end,
  owned by the company or by a town; the road keeps its links and cannot be removed while the stop
  stands.
- **FR-003**: Building and removing pieces MUST be construction actions with preview, cost, the reasons
  of feature 006, company ownership and recording in sessions.
- **FR-004**: Building inside the zone of a town whose rating of the company is below the station
  threshold (feature 008) MUST be refused with "the town refuses".
- **FR-005**: A new piece next to one of the company's stations MUST join that station; next to two
  different stations it MUST be refused; joining MUST NOT make the station's bounding box larger than
  the data-defined spread.
- **FR-006**: Only the owner MAY remove a piece; removing the last piece removes the station and its
  waiting cargo; the clear tool MUST refuse station pieces.
- **FR-007**: Every new road station MUST be named town + type + number ("Fallowstead Bus Stop 2");
  every warehouse town + place suffix by its position from the town centre (central, north, south, east,
  west...) with a number when repeated; names MUST be shown in the player's language with the town name
  unchanged, and the player MUST be able to rename (names of at most 32 characters) any own station or warehouse with a recorded command.
- **FR-007a**: The company MUST be able to build warehouses in three sizes (small, medium, very large),
  defined in the data file with footprint, cost, removal cost and influence radius (8, 16 and 32 tiles), on
  flat empty land touching a road, under the same town rule as stations.
- **FR-007b**: Every company station with a piece inside a warehouse's influence radius MUST belong to that
  warehouse's group; warehouses whose radii overlap MUST form one group; a station outside every radius is
  a group of its own. A group MUST share one waiting stock per cargo, one rating per cargo and one served
  flag per cargo; it accepts a cargo when any member accepts it; each member keeps its own catchment and
  name.
- **FR-007c**: When groups merge their stocks MUST be added; when a group splits its stock MUST be divided
  among the resulting groups in proportion to their number of stations (remainders to the lowest station
  id), and ratings copied, so no cargo is created or lost.

**Catchment, supply and acceptance**

- **FR-008**: A station's catchment MUST be the union of the squares of the piece type's radius around
  each of its pieces.
- **FR-009**: A station MUST receive a cargo from an industry when any tile of the industry is inside
  its catchment and the industry produces that cargo; and passengers and mail from every town building
  with a footprint tile inside its catchment.
- **FR-010**: A station MUST accept a cargo when the summed acceptance of the tiles in its catchment
  reaches one whole unit: an industry tile of an industry that accepts the cargo counts as a whole unit;
  town buildings count with the acceptance of their type (goods, food, and passengers and mail derived
  from their production as the data says).
- **FR-011**: The placement preview MUST show the catchment and the cargo the placement would receive
  and accept, recomputed whenever the cursor moves to another tile without visible delay.
- **FR-012**: A piece type MUST handle only its categories: bus stops handle passengers; truck stops
  handle mail and every other category; a station handles the union of its pieces' categories.

**Waiting cargo and distribution**

- **FR-013**: A station MUST start receiving a cargo only after it has been marked as served for that
  cargo by a vehicle visit (feature 012) or by the debug service option.
- **FR-014**: Each day, for each producer and cargo, the amount moved to stations MUST be the day's
  production times the best rating among the stations serving it, split between the serving stations
  in proportion to their ratings (up to a data-defined number of stations); the rest stays with the
  producer as in features 008 and 009.
- **FR-015**: Waiting cargo MUST be stored per station and cargo as aggregated amounts grouped by origin
  station and destination (destination "any" until feature 013) and pickup day, never as individual
  units.
- **FR-016**: Waiting cargo per group and cargo MUST be capped by a data value (4 000 units); excess MUST
  be lost and lower that cargo's rating.
- **FR-017**: Fractional daily amounts MUST be carried over so that, over time, the totals moved equal
  the exact shares (no cargo created or lost by rounding).

**Ratings**

- **FR-018**: Each served cargo of a station MUST have a rating from 0 to 1 000 that starts at a
  data-defined value and moves each day toward a target computed from days since the last pickup, the
  speed of the last vehicle that loaded it, the amount waiting, and fixed modifiers, by at most a
  data-defined step per day.
- **FR-019**: With a rating below a data-defined level and a waiting amount above a data-defined level,
  part of the waiting cargo MUST be lost each day as the data says.

**Vehicle operations (used by features 011 and 012)**

- **FR-020**: The game MUST provide operations to mark a cargo as served at a station, to load up to a
  given amount of a cargo into a cargo hold (oldest cargo first), and to unload a hold at a station with
  or without a transfer order.
- **FR-021**: Unloading MUST hand accepted cargo to the consumers in the catchment (industries through
  feature 009's delivery, towns' goods and food acceptance, and passengers and mail), and MUST report
  each accepted delivery with cargo, amount, origin station, destination station, pickup day and
  delivery day for feature 011.
- **FR-022**: Cargo not accepted MUST stay in the hold, or with a transfer order MUST wait at the station
  keeping its origin and pickup day.
- **FR-023**: Every load, unload, transfer and distribution MUST conserve cargo: the amount leaving one
  place equals the amount arriving at another, plus what the rules explicitly lose.
- **FR-024**: Loading passengers or mail at a station MUST be counted as transported for the town each
  unit came from (feature 008 service and growth).

**Window, display and debug**

- **FR-025**: Clicking a station piece MUST open the station window with name, waiting cargo by
  destination, rating per cargo, last month's received, picked-up and delivered amounts, days since the
  last pickup, and a catchment highlight toggle.
- **FR-026**: Station pieces MUST be drawn with simple placeholder models in their type's colours, to be
  replaced in feature 018.
- **FR-027**: The client and the headless tool MUST offer a debug option `--station-service <percent>`
  that marks every cargo of every station as served and every few days loads that percentage of the
  waiting cargo into a sink (counting as transported for towns), so the flow can be seen before feature
  012; it MUST be part of the scenario settings so recorded sessions replay it.
- **FR-028**: All player-facing text MUST be localized in English and Spanish.

**Simulation rules**

- **FR-029**: All station state MUST be deterministic simulation state: whole numbers only, randomness
  from the simulation's own seeded streams, changed only by commands and by the daily and monthly
  updates, identical with parallel and sequential updates, and included in the state fingerprint.
- **FR-030**: Games without stations (and map-less scenarios) MUST keep the fingerprints they had
  before this feature.

### Key Entities

- **Station piece type**: data definition: id, name key, form (bay or drive-through), cost, removal cost,
  catchment radius, cargo categories handled, placeholder colours.
- **Station piece**: one tile of a station: tile, type, facing direction, station it belongs to.
- **Station**: id, owner company, name (town, kind, number, or a custom name), pieces, bounding box,
  catchment, statistics, group.
- **Warehouse**: id, owner company, size type (footprint, cost, influence radius), position, name.
- **Station group**: the stations of overlapping warehouses (or a single station): served cargo, waiting
  cargo per cargo, ratings and rating inputs.
- **Waiting cargo packet**: cargo, amount, origin station, destination (or "any"), pickup day.
- **Cargo hold**: what a vehicle (feature 012) or test carries: packets of cargo with their origin.
- **Delivery report**: cargo, amount, origin and destination stations, pickup and delivery days, the
  consumer that accepted it; consumed by feature 011.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The daily update of 5 000 served stations (with and without warehouse groups) (supply, ratings, losses) takes under 2 ms on the
  reference machine; the monthly statistics update of 5 000 stations takes under 2 ms.
- **SC-002**: Moving the placement cursor updates the catchment preview and cargo lists within the same
  frame; the client keeps 60 FPS on the 4 096-tile map while placing.
- **SC-003**: Over 1 000 simulated days with random loading and unloading, the total amount of each
  cargo produced equals the amount at producers, waiting at stations, in holds, delivered and explicitly
  lost, with no difference.
- **SC-004**: The same seed and commands produce the same stations, waiting cargo and ratings on every
  platform, with parallel and sequential updates.
- **SC-005**: A player can build a station next to an industry and see, in the preview, that it will
  receive the industry's cargo before paying.

## Assumptions

- Stations, waiting cargo and ratings belong to companies; only the player's company exists now.
- Building or removing a station piece cannot be undone with the construction undo (like demolishing
  town buildings in feature 008), because the station's cargo and statistics are not tile data.
- The station threshold of town ratings is feature 008's (rating ≥ 200); building stations does not
  change town ratings.
- Passengers and mail have destinations only from feature 013; until then every packet's destination is
  "any destination" and the window shows one line per cargo.
- Until feature 012 a town's service figure still comes from loading (tests, debug option) or from
  feature 008's `--town-service` option.
- Catchment radii, costs, caps and rating weights are balancing values in a data file and may change
  without changing code.
